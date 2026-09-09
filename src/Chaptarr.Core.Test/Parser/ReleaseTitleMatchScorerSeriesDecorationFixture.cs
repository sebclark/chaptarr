using System.Collections.Generic;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Parser;

namespace Chaptarr.Core.Test.Parser
{
    [TestFixture]
    public class ReleaseTitleMatchScorerSeriesDecorationFixture
    {
        // A duplicate catalogue row that carries the series in its title rejected every
        // release for the book it names. Searching Jeff Kinney's "Old School: Diary of a
        // Wimpy Kid (BK10)" hid three approved-elsewhere releases behind
        // "Title/Author mismatch", while the plain "Old School" row accepted the same
        // three. The series decoration sits on either side of the colon depending on the
        // row, so both sides have to be offered as candidates.
        private static Author Kinney => new Author { Name = "Jeff Kinney" };

        [Test]
        public void should_match_when_series_decoration_follows_the_title()
        {
            var book = new Book
            {
                Title = "Old School: Diary of a Wimpy Kid (BK10)",
                Author = Kinney,
                SeriesName = "Diary of a Wimpy Kid",
                SeriesPosition = "10",
                Editions = new List<Edition>
                {
                    new Edition { Id = 1, Title = "Old School: Diary of a Wimpy Kid (BK10)", Monitored = true }
                }
            };

            var result = ReleaseTitleMatchScorer.FindBestMatch(
                "Jeff Kinney - Old School (2015) MP3",
                "Jeff Kinney",
                new[] { book },
                "Jeff Kinney",
                new[] { book });

            Assert.That(result, Is.Not.Null);
            Assert.That(result.IsMatch, Is.True);
        }

        [Test]
        public void should_match_when_series_decoration_precedes_the_title()
        {
            var book = new Book
            {
                Title = "Diary of a Wimpy Kid: The Getaway (Book 12)",
                Author = Kinney,
                SeriesName = "Diary of a Wimpy Kid",
                SeriesPosition = "12",
                Editions = new List<Edition>
                {
                    new Edition { Id = 1, Title = "Diary of a Wimpy Kid: The Getaway (Book 12)", Monitored = true }
                }
            };

            var result = ReleaseTitleMatchScorer.FindBestMatch(
                "Jeff Kinney - The Getaway MP3",
                "Jeff Kinney",
                new[] { book },
                "Jeff Kinney",
                new[] { book });

            Assert.That(result, Is.Not.Null);
            Assert.That(result.IsMatch, Is.True);
        }

        [Test]
        public void should_not_match_a_different_book_in_the_same_series()
        {
            // The whole point of the guard: offering both sides of the decoration must not
            // let book 12's release satisfy book 10.
            var target = new Book
            {
                Title = "Old School: Diary of a Wimpy Kid (BK10)",
                Author = Kinney,
                SeriesName = "Diary of a Wimpy Kid",
                SeriesPosition = "10",
                Editions = new List<Edition>
                {
                    new Edition { Id = 1, Title = "Old School: Diary of a Wimpy Kid (BK10)", Monitored = true }
                }
            };
            var sibling = new Book
            {
                Title = "Diary of a Wimpy Kid: The Getaway (Book 12)",
                Author = Kinney,
                SeriesName = "Diary of a Wimpy Kid",
                SeriesPosition = "12",
                Editions = new List<Edition>
                {
                    new Edition { Id = 2, Title = "Diary of a Wimpy Kid: The Getaway (Book 12)", Monitored = true }
                }
            };

            var result = ReleaseTitleMatchScorer.FindBestMatch(
                "Jeff Kinney - The Getaway MP3",
                "Jeff Kinney",
                new[] { target },
                "Jeff Kinney",
                new[] { target, sibling });

            Assert.That(result == null || !result.IsMatch, Is.True,
                "The Getaway must not be accepted as a match for Old School");
        }
    }
}
