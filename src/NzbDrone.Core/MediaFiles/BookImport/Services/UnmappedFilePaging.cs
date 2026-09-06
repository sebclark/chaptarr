using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NzbDrone.Core.MediaFiles.BookImport.Services
{
    /// <summary>
    /// Lightweight identity for an unmapped file. The unmapped endpoint only needs the
    /// path to decide which files belong on a page, so paging reads these instead of
    /// full BookFile rows - the per-file metadata is what made serving the page
    /// expensive enough to exhaust the process.
    /// </summary>
    public sealed class UnmappedFileIdentifier
    {
        public int Id { get; set; }
        public string Path { get; set; }
    }

    public sealed class UnmappedFilePage
    {
        public int TotalFolders { get; set; }
        public IReadOnlyList<int> FileIds { get; set; } = Array.Empty<int>();
    }

    /// <summary>
    /// Folder-aligned paging for the unmapped-files API.
    /// </summary>
    public static class UnmappedFilePaging
    {
        public static UnmappedFilePage SelectFolderPage(
            IReadOnlyCollection<UnmappedFileIdentifier> files,
            int page,
            int pageSize)
        {
            if (files == null || files.Count == 0 || pageSize <= 0)
            {
                return new UnmappedFilePage();
            }

            var requestedPage = page < 1 ? 1 : page;

            // Group by folder and page over the FOLDERS. Import units are built per
            // folder, so paging by file would let a boundary fall inside a unit and
            // hand the caller a fragment of one.
            var folders = files
                .Where(file => file != null && !string.IsNullOrWhiteSpace(file.Path))
                .GroupBy(file => GetDirectory(file.Path), StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var ids = folders
                .Skip((requestedPage - 1) * pageSize)
                .Take(pageSize)
                .SelectMany(group => group.Select(file => file.Id))
                .ToList();

            return new UnmappedFilePage
            {
                TotalFolders = folders.Count,
                FileIds = ids
            };
        }

        private static string GetDirectory(string path)
        {
            try
            {
                return System.IO.Path.GetDirectoryName(path) ?? string.Empty;
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
        }
    }
}
