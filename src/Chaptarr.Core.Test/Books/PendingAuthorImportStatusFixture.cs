using NUnit.Framework;
using NzbDrone.Core.Books;

namespace Chaptarr.Core.Test.Books
{
    [TestFixture]
    public class PendingAuthorImportStatusFixture
    {
        // A row created without per-media root folders requests neither audiobook nor
        // ebook. NotRequested satisfied both the "done" and "successful" tests, so the
        // row reported Succeeded having imported nothing: 1,022 rows sat at Succeeded
        // while producing no authors at all.
        [Test]
        public void row_requesting_no_media_should_not_report_success()
        {
            var pending = new PendingAuthorImport
            {
                AudiobookStatus = PendingImportStatus.NotRequested,
                EbookStatus = PendingImportStatus.NotRequested
            };

            pending.UpdateOverallStatus();

            Assert.That(pending.OverallStatus, Is.EqualTo(PendingImportStatus.Failed));
        }

        [Test]
        public void audiobook_only_request_should_still_succeed()
        {
            var pending = new PendingAuthorImport
            {
                AudiobookStatus = PendingImportStatus.Succeeded,
                EbookStatus = PendingImportStatus.NotRequested
            };

            pending.UpdateOverallStatus();

            Assert.That(pending.OverallStatus, Is.EqualTo(PendingImportStatus.Succeeded));
        }

        [Test]
        public void ebook_only_request_should_still_succeed()
        {
            var pending = new PendingAuthorImport
            {
                AudiobookStatus = PendingImportStatus.NotRequested,
                EbookStatus = PendingImportStatus.Succeeded
            };

            pending.UpdateOverallStatus();

            Assert.That(pending.OverallStatus, Is.EqualTo(PendingImportStatus.Succeeded));
        }

        [Test]
        public void one_side_failing_should_report_partial_success()
        {
            var pending = new PendingAuthorImport
            {
                AudiobookStatus = PendingImportStatus.Succeeded,
                EbookStatus = PendingImportStatus.Failed
            };

            pending.UpdateOverallStatus();

            Assert.That(pending.OverallStatus, Is.EqualTo(PendingImportStatus.PartialSuccess));
        }

        [Test]
        public void both_sides_failing_should_report_failure()
        {
            var pending = new PendingAuthorImport
            {
                AudiobookStatus = PendingImportStatus.Failed,
                EbookStatus = PendingImportStatus.Failed
            };

            pending.UpdateOverallStatus();

            Assert.That(pending.OverallStatus, Is.EqualTo(PendingImportStatus.Failed));
        }
    }
}
