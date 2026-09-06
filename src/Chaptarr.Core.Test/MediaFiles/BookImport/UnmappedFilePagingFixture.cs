using System.Linq;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.BookImport.Services;

namespace Chaptarr.Core.Test.MediaFiles.BookImport
{
    [TestFixture]
    public class UnmappedFilePagingFixture
    {
        // The unmapped endpoint loaded every unmapped file - rows, resources and
        // per-file metadata - to serve one screen, and fell over with an
        // OutOfMemoryException on a library with 82,000 of them. Paging has to be by
        // FOLDER, not by file: import units are built per folder, so a page boundary
        // through the middle of one would hand the UI a half unit.
        private static UnmappedFileIdentifier File(int id, string path)
        {
            return new UnmappedFileIdentifier { Id = id, Path = path };
        }

        [Test]
        public void should_return_only_the_requested_page_of_folders()
        {
            var files = new[]
            {
                File(1, @"/audiobooks/A/Book One/01.mp3"),
                File(2, @"/audiobooks/A/Book One/02.mp3"),
                File(3, @"/audiobooks/B/Book Two/01.mp3"),
                File(4, @"/audiobooks/C/Book Three/01.mp3")
            };

            var page = UnmappedFilePaging.SelectFolderPage(files, 1, 2);

            Assert.That(page.TotalFolders, Is.EqualTo(3));
            Assert.That(page.FileIds, Is.EquivalentTo(new[] { 1, 2, 3 }));
        }

        [Test]
        public void should_never_split_a_folder_across_pages()
        {
            var files = new[]
            {
                File(1, @"/audiobooks/A/Book One/01.mp3"),
                File(2, @"/audiobooks/A/Book One/02.mp3"),
                File(3, @"/audiobooks/A/Book One/03.mp3"),
                File(4, @"/audiobooks/B/Book Two/01.mp3")
            };

            var first = UnmappedFilePaging.SelectFolderPage(files, 1, 1);
            var second = UnmappedFilePaging.SelectFolderPage(files, 2, 1);

            Assert.That(first.FileIds, Is.EquivalentTo(new[] { 1, 2, 3 }));
            Assert.That(second.FileIds, Is.EquivalentTo(new[] { 4 }));
            Assert.That(first.FileIds.Intersect(second.FileIds), Is.Empty);
        }

        [Test]
        public void should_return_empty_page_past_the_end()
        {
            var files = new[] { File(1, @"/audiobooks/A/Book One/01.mp3") };

            var page = UnmappedFilePaging.SelectFolderPage(files, 5, 10);

            Assert.That(page.TotalFolders, Is.EqualTo(1));
            Assert.That(page.FileIds, Is.Empty);
        }
    }
}
