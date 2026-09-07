using Chaptarr.Http;

namespace Chaptarr.Api.V1.BookFiles
{
    /// <summary>
    /// Unmapped files page plus the library-wide file total.
    /// TotalRecords counts FOLDERS because paging is folder-aligned, so the header
    /// needs a separate count to report files without loading them all.
    /// </summary>
    public class UnmappedFilesPagingResource : PagingResource<BookFileResource>
    {
        public UnmappedFilesPagingResource()
        {
        }

        public UnmappedFilesPagingResource(PagingRequestResource requestResource)
            : base(requestResource)
        {
        }

        public int TotalFiles { get; set; }
    }
}
