using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Messaging;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;

namespace Chaptarr.Core.Test.MediaFiles
{
    [TestFixture]
    public class MediaFileServiceAuthorDeletedFixture
    {
        private sealed class RecordingEventAggregator : IEventAggregator
        {
            public readonly List<IEvent> Events = new List<IEvent>();

            public void PublishEvent<TEvent>(TEvent @event)
                where TEvent : class, IEvent
            {
                Events.Add(@event);
            }
        }

        private sealed class RecordingIngestQueueRepository : IIngestQueueRepository
        {
            public readonly List<string> PurgedPrefixes = new List<string>();

            public void BeginSession(int commandId) => throw new NotImplementedException();
            public void InsertBatch(List<IngestQueueItem> items) => throw new NotImplementedException();
            public List<IngestQueueItem> GetQueuedItems(int limit = 100) => throw new NotImplementedException();
            public List<IngestQueueItem> GetQueuedItemsUnderPath(string pathPrefix, int limit = 100, int afterId = 0) => throw new NotImplementedException();
            public int GetActiveCountUnderPath(string pathPrefix) => throw new NotImplementedException();
            public List<IngestQueueStatusCount> GetActiveStatusCountsUnderPath(string pathPrefix) => throw new NotImplementedException();
            public List<IngestQueueItem> GetActiveItemsUnderPath(string pathPrefix, int limit = 20) => throw new NotImplementedException();
            public List<IngestQueueItem> GetActiveItems(int limit = 1000, int afterId = 0) => throw new NotImplementedException();
            public List<IngestQueueItem> GetActiveItemsForSweepUnderPath(string pathPrefix, int limit = 1000, int afterId = 0) => throw new NotImplementedException();

            public int RecoverStaleInProgress(string pathPrefix, int staleMinutes = 10) => throw new NotImplementedException();
            public int RecoverInProgressUpdatedBefore(string pathPrefix, long updatedBefore, string error = null) => throw new NotImplementedException();
            public bool TryClaimItem(int id, out IngestQueueItem item) => throw new NotImplementedException();
            public List<IngestQueueItem> TryClaimUnit(string folderPath) => throw new NotImplementedException();
            public void UpdateStatus(int id, string status, string error = null) => throw new NotImplementedException();
            public void UpdateBatchTagsJson(IEnumerable<(int Id, string TagsJson)> items) => throw new NotImplementedException();
            public void UpdateBatchTagsAndDuration(IEnumerable<(int Id, string TagsJson, int? DurationSeconds)> items) => throw new NotImplementedException();
            public void UpdateBatchStatus(List<int> ids, string status) => throw new NotImplementedException();
            public void RequeueInProgress(List<int> ids, string error = null) => throw new NotImplementedException();
            public int GetQueueCount() => throw new NotImplementedException();
            public int RequeueFailedOrUnmappedUnderPath(string pathPrefix) => throw new NotImplementedException();
            public int RequeueFailedPaths(IEnumerable<string> paths) => throw new NotImplementedException();
            public int PurgeUnderPath(string pathPrefix)
            {
                PurgedPrefixes.Add(pathPrefix);
                return 1;
            }

            public int PurgePaths(IEnumerable<string> paths) => throw new NotImplementedException();
            public void PurgeOldCompleted(int daysToKeep = 14) => throw new NotImplementedException();
            public void RecordImportResult(int queueItemId, string path, ImportOutcome outcome, int? bookId = null, int? authorId = null, string quality = null, string errorMessage = null) => throw new NotImplementedException();
            public void CompleteItemWithResult(int queueItemId, string path, ImportOutcome outcome, int? bookId = null, int? authorId = null, string quality = null, string errorMessage = null, string statusError = null) => throw new NotImplementedException();
            public List<ImportResult> GetImportResults(int? commandId = null) => throw new NotImplementedException();
        }

        private class MediaFileRepositoryProxy : DispatchProxy
        {
            public List<BookFile> DeletedMany { get; } = new List<BookFile>();
            public Func<string, IEnumerable<BookFile>> GetFilesWithBasePathHandler { get; set; }
            public Func<int, IEnumerable<BookFile>> GetFilesByAuthorHandler { get; set; }

            protected override object Invoke(MethodInfo targetMethod, object[] args)
            {
                switch (targetMethod?.Name)
                {
                    case nameof(IMediaFileRepository.GetFilesWithBasePath):
                        return (GetFilesWithBasePathHandler?.Invoke((string)args[0]) ?? Enumerable.Empty<BookFile>()).ToList();

                    case nameof(IMediaFileRepository.GetFilesByAuthor):
                        return (GetFilesByAuthorHandler?.Invoke((int)args[0]) ?? Enumerable.Empty<BookFile>()).ToList();

                    case nameof(IMediaFileRepository.DeleteMany):
                        DeletedMany.Clear();
                        DeletedMany.AddRange((IEnumerable<BookFile>)args[0]);
                        return null;

                    case nameof(IMediaFileRepository.Get):
                        if (args?.Length == 1 && args[0] is IEnumerable<int> ids)
                        {
                            // Hydration for delete events: echo minimal mapped files back.
                            return ids.Select(id => new BookFile { Id = id, EditionId = 100 + id, Path = $"/audiobooks/A/B/{id}.m4b" });
                        }

                        break;
                }

                throw new NotImplementedException($"Test proxy does not implement {typeof(IMediaFileRepository).Name}.{targetMethod?.Name}");
            }
        }

        private static Author TestAuthor()
        {
            return new Author
            {
                Id = 7,
                Name = "Deleted Author",
                Path = "/audiobooks/Deleted Author",
                AudiobookPath = "/audiobooks/Deleted Author",
                EbookPath = "/ebooks/Deleted Author"
            };
        }

        [Test]
        public void should_not_delete_book_file_rows_when_files_were_kept()
        {
            var repo = DispatchProxy.Create<IMediaFileRepository, MediaFileRepositoryProxy>();
            var repoProxy = (MediaFileRepositoryProxy)(object)repo;
            var sut = new MediaFileService(repo, new RecordingEventAggregator(), new RecordingIngestQueueRepository(), LogManager.GetLogger("test"));

            sut.Handle(new AuthorDeletedEvent(TestAuthor(), deleteFiles: false, addImportListExclusion: false));

            Assert.That(repoProxy.DeletedMany, Is.Empty);
        }

        [Test]
        public void should_delete_orphaned_rows_under_author_paths_when_files_were_deleted()
        {
            var repo = DispatchProxy.Create<IMediaFileRepository, MediaFileRepositoryProxy>();
            var repoProxy = (MediaFileRepositoryProxy)(object)repo;

            // One unmapped row surviving under the audiobook path (the #42 scenario)
            // and one still linked row reachable via the author id.
            var unmapped = new BookFile { Id = 41, EditionId = 0, Path = "/audiobooks/Deleted Author/Book/01.mp3" };
            var linked = new BookFile { Id = 42, EditionId = 142, Path = "/ebooks/Deleted Author/Book/book.epub" };

            repoProxy.GetFilesWithBasePathHandler = path =>
                path.StartsWith("/audiobooks") ? new[] { unmapped } : Enumerable.Empty<BookFile>();
            repoProxy.GetFilesByAuthorHandler = _ => new[] { linked };

            var events = new RecordingEventAggregator();
            var sut = new MediaFileService(repo, events, new RecordingIngestQueueRepository(), LogManager.GetLogger("test"));

            sut.Handle(new AuthorDeletedEvent(TestAuthor(), deleteFiles: true, addImportListExclusion: false));

            Assert.That(repoProxy.DeletedMany.Select(f => f.Id), Is.EquivalentTo(new[] { 41, 42 }));
            Assert.That(events.Events.OfType<BookFileDeletedEvent>().Count(), Is.EqualTo(1), "only the mapped file should raise a delete event");
        }

        [Test]
        public void should_not_delete_retained_book_files()
        {
            var repo = DispatchProxy.Create<IMediaFileRepository, MediaFileRepositoryProxy>();
            var repoProxy = (MediaFileRepositoryProxy)(object)repo;

            var kept = new BookFile { Id = 51, EditionId = 0, Path = "/audiobooks/Deleted Author/Kept/01.mp3" };
            var removed = new BookFile { Id = 52, EditionId = 0, Path = "/audiobooks/Deleted Author/Removed/01.mp3" };

            repoProxy.GetFilesWithBasePathHandler = path =>
                path.StartsWith("/audiobooks") ? new[] { kept, removed } : Enumerable.Empty<BookFile>();
            repoProxy.GetFilesByAuthorHandler = _ => Enumerable.Empty<BookFile>();

            var sut = new MediaFileService(repo, new RecordingEventAggregator(), new RecordingIngestQueueRepository(), LogManager.GetLogger("test"));

            sut.Handle(new AuthorDeletedEvent(TestAuthor(), deleteFiles: true, addImportListExclusion: false, preserveRetainedFileHistory: true, retainedBookFileIds: new[] { 51 }));

            Assert.That(repoProxy.DeletedMany.Select(f => f.Id), Is.EquivalentTo(new[] { 52 }));
        }

        [Test]
        public void should_deduplicate_rows_found_via_both_path_and_author()
        {
            var repo = DispatchProxy.Create<IMediaFileRepository, MediaFileRepositoryProxy>();
            var repoProxy = (MediaFileRepositoryProxy)(object)repo;

            var file = new BookFile { Id = 61, EditionId = 0, Path = "/audiobooks/Deleted Author/Book/01.mp3" };

            repoProxy.GetFilesWithBasePathHandler = path =>
                path.StartsWith("/audiobooks") ? new[] { file } : Enumerable.Empty<BookFile>();
            repoProxy.GetFilesByAuthorHandler = _ => new[] { file };

            var sut = new MediaFileService(repo, new RecordingEventAggregator(), new RecordingIngestQueueRepository(), LogManager.GetLogger("test"));

            sut.Handle(new AuthorDeletedEvent(TestAuthor(), deleteFiles: true, addImportListExclusion: false));

            Assert.That(repoProxy.DeletedMany.Count, Is.EqualTo(1));
        }
    }
}
