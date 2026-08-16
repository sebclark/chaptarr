using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NLog;
using NUnit.Framework;
using NzbDrone.Common;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.RootFolders;

namespace Chaptarr.Core.Test.MediaFiles
{
    [TestFixture]
    [Platform(Exclude = "Win", Reason = "Tests use Unix paths")]
    public class BookFileMovingServiceTransferFixture
    {
        private class ThrowingProxy<T> : DispatchProxy where T : class
        {
            protected override object Invoke(MethodInfo targetMethod, object[] args)
            {
                throw new NotImplementedException($"Test proxy does not implement {typeof(T).Name}.{targetMethod?.Name}");
            }
        }

        private class BuildFileNamesProxy : DispatchProxy
        {
            public Func<Author, Edition, BookFile, NamingConfig, string> BookFileNameFactory { get; set; }

            protected override object Invoke(MethodInfo targetMethod, object[] args)
            {
                if (targetMethod?.Name == nameof(IBuildFileNames.BuildBookFileName))
                {
                    return BookFileNameFactory?.Invoke(args?[0] as Author, args?[1] as Edition, args?[2] as BookFile, args?[3] as NamingConfig);
                }

                throw new NotImplementedException($"Test proxy does not implement {typeof(IBuildFileNames).Name}.{targetMethod?.Name}");
            }
        }

        private class DiskProviderProxy : DispatchProxy
        {
            public HashSet<string> ExistingFolders { get; } = new(PathEqualityComparer.Instance);
            public HashSet<string> ExistingFiles { get; } = new(PathEqualityComparer.Instance);
            public Dictionary<string, long> FileSizes { get; } = new(PathEqualityComparer.Instance);

            protected override object Invoke(MethodInfo targetMethod, object[] args)
            {
                if (targetMethod?.Name == nameof(IDiskProvider.FolderExists) && args?[0] is string folderPath)
                {
                    return ExistingFolders.Contains(folderPath);
                }

                if (targetMethod?.Name == nameof(IDiskProvider.FileExists) && args?[0] is string filePath)
                {
                    return ExistingFiles.Contains(filePath);
                }

                if (targetMethod?.Name == nameof(IDiskProvider.FolderWritable))
                {
                    return true;
                }

                if (targetMethod?.Name == nameof(IDiskProvider.GetFileSize) && args?[0] is string sizePath)
                {
                    return FileSizes.TryGetValue(sizePath, out var size) ? size : 0L;
                }

                if (targetMethod?.Name == nameof(IDiskProvider.RemoveEmptySubfolders))
                {
                    return null;
                }

                throw new NotImplementedException($"Test proxy does not implement {typeof(IDiskProvider).Name}.{targetMethod?.Name}");
            }
        }

        private class RecordingDiskTransferProxy : DispatchProxy
        {
            public List<(string Source, string Destination)> Transfers { get; } = new();

            protected override object Invoke(MethodInfo targetMethod, object[] args)
            {
                if (targetMethod?.Name == nameof(IDiskTransferService.TransferFile) &&
                    args?.Length >= 3 &&
                    args[0] is string source &&
                    args[1] is string destination)
                {
                    Transfers.Add((source, destination));
                    return TransferMode.Move;
                }

                throw new NotImplementedException($"Test proxy does not implement {typeof(IDiskTransferService).Name}.{targetMethod?.Name}");
            }
        }

        private class RecordingRecycleBinProxy : DispatchProxy
        {
            public List<string> DeletedFiles { get; } = new();

            protected override object Invoke(MethodInfo targetMethod, object[] args)
            {
                if (targetMethod?.Name == nameof(IRecycleBinProvider.DeleteFile) && args?[0] is string path)
                {
                    DeletedFiles.Add(path);
                    return null;
                }

                throw new NotImplementedException($"Test proxy does not implement {typeof(IRecycleBinProvider).Name}.{targetMethod?.Name}");
            }
        }

        private class RecordingUpdateBookFileProxy : DispatchProxy
        {
            public int ChangeFileDateCalls { get; private set; }

            protected override object Invoke(MethodInfo targetMethod, object[] args)
            {
                if (targetMethod?.Name == nameof(IUpdateBookFileService.ChangeFileDateForFile))
                {
                    ChangeFileDateCalls++;
                    return null;
                }

                throw new NotImplementedException($"Test proxy does not implement {typeof(IUpdateBookFileService).Name}.{targetMethod?.Name}");
            }
        }

        private class NoOpFileMutationSafetyProxy : DispatchProxy
        {
            protected override object Invoke(MethodInfo targetMethod, object[] args)
            {
                if (targetMethod?.Name == nameof(IFileMutationSafetyService.PrepareImportDestination) ||
                    targetMethod?.Name == nameof(IFileMutationSafetyService.EnsureMutableFile))
                {
                    return null;
                }

                throw new NotImplementedException($"Test proxy does not implement {typeof(IFileMutationSafetyService).Name}.{targetMethod?.Name}");
            }
        }

        private class NoOpMediaFileAttributeProxy : DispatchProxy
        {
            protected override object Invoke(MethodInfo targetMethod, object[] args)
            {
                if (targetMethod?.Name == nameof(IMediaFileAttributeService.SetFilePermissions) ||
                    targetMethod?.Name == nameof(IMediaFileAttributeService.SetFolderPermissions) ||
                    targetMethod?.Name == nameof(IMediaFileAttributeService.SetFolderLastWriteTime))
                {
                    return null;
                }

                throw new NotImplementedException($"Test proxy does not implement {typeof(IMediaFileAttributeService).Name}.{targetMethod?.Name}");
            }
        }

        private class RootFolderWatchingServiceProxy : DispatchProxy
        {
            protected override object Invoke(MethodInfo targetMethod, object[] args)
            {
                if (targetMethod?.Name == nameof(IRootFolderWatchingService.ReportFileSystemChangeBeginning))
                {
                    return null;
                }

                throw new NotImplementedException($"Test proxy does not implement {typeof(IRootFolderWatchingService).Name}.{targetMethod?.Name}");
            }
        }

        private class NamingConfigServiceProxy : DispatchProxy
        {
            protected override object Invoke(MethodInfo targetMethod, object[] args)
            {
                if (targetMethod?.Name == nameof(INamingConfigService.GetConfig))
                {
                    return NamingConfig.Default;
                }

                throw new NotImplementedException($"Test proxy does not implement {typeof(INamingConfigService).Name}.{targetMethod?.Name}");
            }
        }

        private class NonColocatingPlannerProxy : DispatchProxy
        {
            protected override object Invoke(MethodInfo targetMethod, object[] args)
            {
                if (targetMethod?.Name == nameof(IEbookColocationPlanner.Plan))
                {
                    return EbookColocationPlan.Skipped(EbookColocationSkipReason.NotEbook);
                }

                throw new NotImplementedException($"Test proxy does not implement {typeof(IEbookColocationPlanner).Name}.{targetMethod?.Name}");
            }
        }

        private const string RootFolder = "/library";
        private const string AuthorFolder = "/library/Test Author";
        private const string BookFolder = "/library/Test Author/Book";
        private const string DestinationPath = "/library/Test Author/Book/file.mp3";
        private const string SourcePath = "/downloads/file.mp3";

        private DiskProviderProxy _diskProxy;
        private RecordingDiskTransferProxy _transferProxy;
        private RecordingRecycleBinProxy _recycleProxy;
        private RecordingUpdateBookFileProxy _updateProxy;
        private BookFileMovingService _service;

        [SetUp]
        public void Setup()
        {
            var fileNameBuilder = DispatchProxy.Create<IBuildFileNames, BuildFileNamesProxy>();
            ((BuildFileNamesProxy)(object)fileNameBuilder).BookFileNameFactory = (_, _, _, _) => Path.Combine("Book", "file");

            var diskProvider = DispatchProxy.Create<IDiskProvider, DiskProviderProxy>();
            _diskProxy = (DiskProviderProxy)(object)diskProvider;
            _diskProxy.ExistingFolders.Add(RootFolder);
            _diskProxy.ExistingFolders.Add(AuthorFolder);
            _diskProxy.ExistingFolders.Add(BookFolder);
            _diskProxy.ExistingFiles.Add(SourcePath);
            _diskProxy.FileSizes[SourcePath] = 1000;

            var diskTransferService = DispatchProxy.Create<IDiskTransferService, RecordingDiskTransferProxy>();
            _transferProxy = (RecordingDiskTransferProxy)(object)diskTransferService;

            var recycleBinProvider = DispatchProxy.Create<IRecycleBinProvider, RecordingRecycleBinProxy>();
            _recycleProxy = (RecordingRecycleBinProxy)(object)recycleBinProvider;

            var updateBookFileService = DispatchProxy.Create<IUpdateBookFileService, RecordingUpdateBookFileProxy>();
            _updateProxy = (RecordingUpdateBookFileProxy)(object)updateBookFileService;

            _service = new BookFileMovingService(
                DispatchProxy.Create<IEditionService, ThrowingProxy<IEditionService>>(),
                updateBookFileService,
                fileNameBuilder,
                DispatchProxy.Create<IBuildAuthorPaths, ThrowingProxy<IBuildAuthorPaths>>(),
                DispatchProxy.Create<INamingConfigService, NamingConfigServiceProxy>(),
                DispatchProxy.Create<IEbookColocationPlanner, NonColocatingPlannerProxy>(),
                diskTransferService,
                diskProvider,
                recycleBinProvider,
                DispatchProxy.Create<IRootFolderWatchingService, RootFolderWatchingServiceProxy>(),
                DispatchProxy.Create<IMediaFileAttributeService, NoOpMediaFileAttributeProxy>(),
                DispatchProxy.Create<IEventAggregator, ThrowingProxy<IEventAggregator>>(),
                DispatchProxy.Create<IConfigService, ThrowingProxy<IConfigService>>(),
                DispatchProxy.Create<IFileMutationSafetyService, NoOpFileMutationSafetyProxy>(),
                LogManager.GetCurrentClassLogger());
        }

        private (BookFile BookFile, NzbDrone.Core.Parser.Model.LocalBook LocalBook) CreateImport()
        {
            var author = new Author
            {
                Id = 1,
                Name = "Test Author",
                AudiobookRootFolderPath = RootFolder,
                AudiobookPath = AuthorFolder
            };
            var book = new Book { Id = 42, AuthorId = author.Id, Author = author };
            var edition = new Edition { Id = 7, BookId = book.Id, Book = book };
            var bookFile = new BookFile
            {
                Path = SourcePath,
                EditionId = edition.Id,
                Edition = edition,
                Quality = new QualityModel(Quality.MP3),
                MediaType = "audiobook"
            };
            var localBook = new NzbDrone.Core.Parser.Model.LocalBook
            {
                Path = SourcePath,
                Author = author,
                Book = book,
                Edition = edition,
                Quality = bookFile.Quality
            };

            return (bookFile, localBook);
        }

        [Test]
        public void should_adopt_existing_destination_when_identical_size()
        {
            _diskProxy.ExistingFiles.Add(DestinationPath);
            _diskProxy.FileSizes[DestinationPath] = 1000;

            var (bookFile, localBook) = CreateImport();

            var result = _service.MoveBookFile(bookFile, localBook);

            Assert.That(result.Path, Is.EqualTo(DestinationPath));
            Assert.That(_transferProxy.Transfers, Is.Empty, "identical existing file should be adopted, not transferred over");
            Assert.That(_recycleProxy.DeletedFiles, Is.Empty, "identical existing file should not be recycled");
            Assert.That(_updateProxy.ChangeFileDateCalls, Is.EqualTo(1), "adopted file should still get its dates fixed up");
        }

        [Test]
        public void should_recycle_existing_destination_and_transfer_when_size_differs()
        {
            _diskProxy.ExistingFiles.Add(DestinationPath);
            _diskProxy.FileSizes[DestinationPath] = 500;

            var (bookFile, localBook) = CreateImport();

            var result = _service.MoveBookFile(bookFile, localBook);

            Assert.That(_recycleProxy.DeletedFiles, Is.EqualTo(new[] { DestinationPath }), "differing existing file should be recycled first");
            Assert.That(_transferProxy.Transfers, Is.EqualTo(new[] { (SourcePath, DestinationPath) }), "download should be transferred after the recycle");
            Assert.That(result.Path, Is.EqualTo(DestinationPath));
            Assert.That(_updateProxy.ChangeFileDateCalls, Is.EqualTo(1));
        }

        [Test]
        public void should_transfer_normally_when_destination_does_not_exist()
        {
            var (bookFile, localBook) = CreateImport();

            var result = _service.MoveBookFile(bookFile, localBook);

            Assert.That(_transferProxy.Transfers, Is.EqualTo(new[] { (SourcePath, DestinationPath) }));
            Assert.That(_recycleProxy.DeletedFiles, Is.Empty);
            Assert.That(result.Path, Is.EqualTo(DestinationPath));
        }
    }
}
