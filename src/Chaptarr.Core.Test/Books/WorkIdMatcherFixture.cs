using NUnit.Framework;
using NzbDrone.Core.Books;

namespace Chaptarr.Core.Test.Books
{
    [TestFixture]
    public class WorkIdMatcherFixture
    {
        [Test]
        public void work_id_matches_should_not_promote_shared_asin_over_work_ids()
        {
            var audiobook = new Book
            {
                MediaType = BookMediaType.Audiobook,
                HardcoverBookId = "hc:work-1",
                ASIN = "B00SHAREDASIN"
            };

            var ebook = new Book
            {
                MediaType = BookMediaType.Ebook,
                HardcoverBookId = "hc:work-2",
                ASIN = "B00SHAREDASIN"
            };

            Assert.That(WorkIdMatcher.WorkIdMatches(audiobook, ebook), Is.False);
            Assert.That(WorkIdMatcher.WorkProviderIdMatches(audiobook, ebook), Is.False);
            Assert.That(WorkIdMatcher.CrossFormatSafeMatches(audiobook, ebook), Is.False);
        }

        [Test]
        public void cross_format_safe_matches_should_still_allow_same_format_edition_matches()
        {
            var firstAudiobook = new Book
            {
                MediaType = BookMediaType.Audiobook,
                ASIN = "B00SHAREDASIN"
            };

            var secondAudiobook = new Book
            {
                MediaType = BookMediaType.Audiobook,
                ASIN = "B00SHAREDASIN"
            };

            Assert.That(WorkIdMatcher.WorkIdMatches(firstAudiobook, secondAudiobook), Is.True);
            Assert.That(WorkIdMatcher.CrossFormatSafeMatches(firstAudiobook, secondAudiobook), Is.True);
        }

        [Test]
        public void work_id_matches_should_not_bridge_asin_only_row_to_work_id_row()
        {
            var workBacked = new Book
            {
                MediaType = BookMediaType.Audiobook,
                HardcoverBookId = "hc:work-1",
                ASIN = "B00SHAREDASIN"
            };

            var asinOnly = new Book
            {
                MediaType = BookMediaType.Audiobook,
                ASIN = "B00SHAREDASIN"
            };

            Assert.That(WorkIdMatcher.WorkIdMatches(workBacked, asinOnly), Is.False);
            Assert.That(WorkIdMatcher.CrossFormatSafeMatches(workBacked, asinOnly), Is.False);
        }

        [Test]
        public void work_id_matches_should_not_use_edition_ids_across_media_types()
        {
            var audiobook = new Book
            {
                MediaType = BookMediaType.Audiobook,
                ASIN = "B00SHAREDASIN"
            };

            var ebook = new Book
            {
                MediaType = BookMediaType.Ebook,
                ASIN = "B00SHAREDASIN"
            };

            Assert.That(WorkIdMatcher.WorkIdMatches(audiobook, ebook), Is.False);
            Assert.That(WorkIdMatcher.CrossFormatSafeMatches(audiobook, ebook), Is.False);
        }

        [Test]
        public void work_provider_matches_should_ignore_base_book_id()
        {
            var first = new Book
            {
                MediaType = BookMediaType.Audiobook,
                BaseBookId = "hc:work-1"
            };

            var second = new Book
            {
                MediaType = BookMediaType.Ebook,
                BaseBookId = "hc:work-1"
            };

            Assert.That(WorkIdMatcher.WorkProviderIdMatches(first, second), Is.False);
            Assert.That(WorkIdMatcher.CrossFormatSafeMatches(first, second), Is.False);
        }
        // The metadata server holds March Upcountry twice: 248079 with gr/hc work IDs
        // and 248123 with none. The download was grabbed against the identified row and
        // matched to the bare duplicate, so the import was rejected as a mismatch.
        [Test]
        public void should_treat_unidentified_duplicate_row_as_the_same_work()
        {
            var identified = new Book
            {
                Id = 248079,
                Title = "March Upcountry",
                MediaType = BookMediaType.Audiobook,
                HardcoverBookId = "hc:446298",
                GoodreadsWorkId = "gr:26063"
            };

            var bareDuplicate = new Book
            {
                Id = 248123,
                Title = "March Upcountry",
                MediaType = BookMediaType.Audiobook
            };

            Assert.That(WorkIdMatcher.WorkProviderIdMatches(identified, bareDuplicate), Is.False,
                "work-ID matching cannot succeed when one row carries none");
            Assert.That(WorkIdMatcher.SameWorkOrUnidentifiedDuplicate(identified, bareDuplicate), Is.True);
        }

        [Test]
        public void should_not_merge_two_identified_works_that_differ()
        {
            var first = new Book
            {
                Title = "March Upcountry",
                MediaType = BookMediaType.Audiobook,
                HardcoverBookId = "hc:446298"
            };

            var second = new Book
            {
                Title = "March Upcountry",
                MediaType = BookMediaType.Audiobook,
                HardcoverBookId = "hc:999999"
            };

            Assert.That(WorkIdMatcher.SameWorkOrUnidentifiedDuplicate(first, second), Is.False,
                "both sides identified and disagreeing must stay separate");
        }

        [Test]
        public void should_not_match_unidentified_rows_with_different_titles()
        {
            var left = new Book { Title = "March Upcountry", MediaType = BookMediaType.Audiobook };
            var right = new Book { Title = "March to the Sea", MediaType = BookMediaType.Audiobook };

            Assert.That(WorkIdMatcher.SameWorkOrUnidentifiedDuplicate(left, right), Is.False);
        }

        [Test]
        public void should_ignore_apostrophe_style_when_comparing_duplicate_titles()
        {
            var curly = new Book
            {
                Title = "Harry Potter and the Philosopher’s Stone",
                MediaType = BookMediaType.Audiobook,
                HardcoverBookId = "hc:123"
            };
            var straight = new Book
            {
                Title = "Harry Potter and the Philosopher's Stone",
                MediaType = BookMediaType.Audiobook
            };

            Assert.That(WorkIdMatcher.SameWorkOrUnidentifiedDuplicate(curly, straight), Is.True);
        }

        [Test]
        public void should_not_match_unidentified_rows_across_media_types()
        {
            var audio = new Book { Title = "March Upcountry", MediaType = BookMediaType.Audiobook };
            var ebook = new Book { Title = "March Upcountry", MediaType = BookMediaType.Ebook };

            Assert.That(WorkIdMatcher.SameWorkOrUnidentifiedDuplicate(audio, ebook), Is.False);
        }

        private static Book Identified(string title) => new Book
        {
            Title = title,
            MediaType = BookMediaType.Audiobook,
            HardcoverBookId = "hc:77104"
        };

        private static Book Bare(string title) => new Book
        {
            Title = title,
            MediaType = BookMediaType.Audiobook
        };

        // Live case: grabbed BookId 360735 "The Eye of the World" (hc:77104), import
        // matched BookId 47550 "The Eye of the World: Book One of The Wheel of Time"
        // which carries no work IDs. Same audiobook, rejected as a mismatch.
        [Test]
        public void should_accept_subtitle_variant_of_the_same_work()
        {
            var grabbed = Identified("The Eye of the World");
            var matched = Bare("The Eye of the World: Book One of The Wheel of Time");

            Assert.That(WorkIdMatcher.SameWorkOrUnidentifiedDuplicate(grabbed, matched), Is.True);
            Assert.That(WorkIdMatcher.SameWorkOrUnidentifiedDuplicate(matched, grabbed), Is.True,
                "the comparison must not depend on argument order");
        }

        [TestCase("Mercy Watson", "Mercy Watson: #1-2", TestName = "subtitle_variant_rejects_numbered_range")]
        [TestCase("Tilly Trotter", "Tilly Trotter: An Omnibus", TestName = "subtitle_variant_rejects_omnibus")]
        [TestCase("The Great Hunt", "The Great Hunt: The Graphic Novel: Volume One", TestName = "subtitle_variant_rejects_volume")]
        [TestCase("Discworld", "Discworld: The Complete Collection", TestName = "subtitle_variant_rejects_collection")]
        public void should_reject_compilation_subtitles(string shortTitle, string longTitle)
        {
            Assert.That(WorkIdMatcher.SameWorkOrUnidentifiedDuplicate(Identified(shortTitle), Bare(longTitle)),
                Is.False);
        }

        [TestCase("Muddle Earth", "Muddle Earth Too", TestName = "subtitle_variant_rejects_sequel_without_colon")]
        [TestCase("Blame It on the Shame", "Blame It on the Shame Part 2", TestName = "subtitle_variant_rejects_part_two")]
        [TestCase("The Naughtiest Girl", "The Naughtiest Girl Again", TestName = "subtitle_variant_rejects_again")]
        public void should_reject_sequels_that_merely_append_words(string shortTitle, string longTitle)
        {
            Assert.That(WorkIdMatcher.SameWorkOrUnidentifiedDuplicate(Identified(shortTitle), Bare(longTitle)),
                Is.False, "no colon means the extra words are a different book, not a subtitle");
        }

        [Test]
        public void should_not_accept_subtitle_variant_when_both_sides_are_identified()
        {
            var first = Identified("The Eye of the World");
            var second = new Book
            {
                Title = "The Eye of the World: Book One of The Wheel of Time",
                MediaType = BookMediaType.Audiobook,
                HardcoverBookId = "hc:999999"
            };

            Assert.That(WorkIdMatcher.SameWorkOrUnidentifiedDuplicate(first, second), Is.False,
                "two known and disagreeing works must stay separate whatever the titles say");
        }

    }
}
