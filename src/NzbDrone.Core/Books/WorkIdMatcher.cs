namespace NzbDrone.Core.Books
{
    public static class WorkIdMatcher
    {
        public static bool WorkIdMatches(Book left, Book right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            return BookIdentity.MatchesByProviderIdIntersection(left, right);
        }

        /// <summary>
        /// Work-level match only (Hardcover/Goodreads/OpenLibrary work IDs).
        /// Excludes edition-level IDs (ASIN/AudibleASIN, GB edition IDs, etc) so callers don't accidentally treat two
        /// different editions as the "same work" for cross-format operations (delete, colocate, etc).
        /// </summary>
        public static bool WorkProviderIdMatches(Book left, Book right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            var leftIds = BookIdentity.GetStableWorkProviderIdentityTokens(left);
            var rightIds = BookIdentity.GetStableWorkProviderIdentityTokens(right);

            if (leftIds.Count == 0 || rightIds.Count == 0)
            {
                return false;
            }

            return leftIds.Overlaps(rightIds);
        }

        /// <summary>
        /// True when two rows are the same work, tolerating catalogue duplicates that
        /// carry no work IDs at all.
        ///
        /// The metadata server can hold the same book twice: one row fully identified,
        /// its duplicate with no work IDs. WorkProviderIdMatches bails as soon as either
        /// side has no IDs, so a completed download grabbed against one row and matched
        /// to the other was rejected as a mismatch and never imported.
        ///
        /// Falling back to author + title is only safe when work IDs are ABSENT, never
        /// when they are present and different - two known-but-different works must stay
        /// separate.
        /// </summary>
        public static bool SameWorkOrUnidentifiedDuplicate(Book left, Book right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            if (WorkProviderIdMatches(left, right))
            {
                return true;
            }

            var leftIds = BookIdentity.GetStableWorkProviderIdentityTokens(left);
            var rightIds = BookIdentity.GetStableWorkProviderIdentityTokens(right);

            // Both sides identified and not intersecting: genuinely different works.
            if (leftIds.Count > 0 && rightIds.Count > 0)
            {
                return false;
            }

            if (left.MediaType != right.MediaType)
            {
                return false;
            }

            var leftTitle = NormalizeTitle(left.Title);
            var rightTitle = NormalizeTitle(right.Title);
            if (string.IsNullOrWhiteSpace(leftTitle) || string.IsNullOrWhiteSpace(rightTitle))
            {
                return false;
            }

            if (leftTitle == rightTitle)
            {
                return true;
            }

            return IsSubtitleVariantOf(left.Title, right.Title) ||
                   IsSubtitleVariantOf(right.Title, left.Title);
        }

        // Subtitles disagreeing between duplicate rows is common: the same audiobook is
        // held as "The Eye of the World" and "The Eye of the World: Book One of The Wheel
        // of Time". Accept that ONLY when the shorter title is the longer one's title
        // proper - the text before its first colon - and the subtitle does not advertise
        // a compilation. "Muddle Earth" / "Muddle Earth Too" has no colon and is
        // correctly refused; "Mercy Watson: #1-2" and "Tilly Trotter: An Omnibus" are
        // refused by the digit and keyword guards below.
        private static readonly string[] CompilationMarkers =
        {
            "omnibus", "collection", "box set", "boxed set", "anthology", "volume", "vol", "complete"
        };

        private static bool IsSubtitleVariantOf(string shortTitle, string longTitle)
        {
            if (string.IsNullOrWhiteSpace(shortTitle) || string.IsNullOrWhiteSpace(longTitle))
            {
                return false;
            }

            var colon = longTitle.IndexOf(':');
            if (colon <= 0 || colon >= longTitle.Length - 1)
            {
                return false;
            }

            var titleProper = NormalizeTitle(longTitle.Substring(0, colon));
            if (string.IsNullOrWhiteSpace(titleProper) || titleProper != NormalizeTitle(shortTitle))
            {
                return false;
            }

            var subtitle = longTitle.Substring(colon + 1);
            foreach (var ch in subtitle)
            {
                // A number in the subtitle usually means a range or volume, i.e. a
                // compilation rather than this single work.
                if (char.IsDigit(ch))
                {
                    return false;
                }
            }

            var normalizedSubtitle = NormalizeTitle(subtitle);
            foreach (var marker in CompilationMarkers)
            {
                if (normalizedSubtitle == marker ||
                    normalizedSubtitle.StartsWith(marker + " ", System.StringComparison.Ordinal) ||
                    normalizedSubtitle.EndsWith(" " + marker, System.StringComparison.Ordinal) ||
                    normalizedSubtitle.Contains(" " + marker + " ", System.StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static string NormalizeTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return string.Empty;
            }

            var builder = new System.Text.StringBuilder(title.Length);
            foreach (var ch in title)
            {
                // Apostrophes are dropped rather than turned into separators so
                // "Philosopher's" and "Philosophers" normalise the same way.
                if (ch == '\u0027' || ch == '\u2019' || ch == '\u2018' || ch == '\u02bc')
                {
                    continue;
                }

                if (char.IsLetterOrDigit(ch))
                {
                    builder.Append(char.ToLowerInvariant(ch));
                }
                else if (builder.Length > 0 && builder[builder.Length - 1] != ' ')
                {
                    builder.Append(' ');
                }
            }

            return builder.ToString().Trim();
        }

        /// <summary>
        /// Cross-format-safe matcher for "same work" grouping.
        /// Same-format comparisons may use broader edition/work identity, but audiobook↔ebook
        /// comparisons must only use work-level provider IDs to avoid linking unrelated editions.
        /// </summary>
        public static bool CrossFormatSafeMatches(Book left, Book right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            if (left.MediaType != right.MediaType)
            {
                return WorkProviderIdMatches(left, right);
            }

            return WorkIdMatches(left, right);
        }
    }
}
