using TelegramDownloader.Models.Library;
using TelegramDownloader.Services.Library;

namespace Test
{
    /// <summary>
    /// How provider hits are scored against a parsed name. The thresholds
    /// decide whether a file lands in the library, in the review queue or
    /// stays unmatched.
    /// </summary>
    public class LibraryMatcherTest
    {
        private static ProviderCandidate Candidate(string title, int? year, string? original = null) =>
            new() { Provider = "tmdb", ProviderId = title, Kind = LibraryKind.Movie, Title = title, OriginalTitle = original, Year = year };

        [Test]
        public void ExactTitleAndYearIsAMatch()
        {
            var parsed = new ParsedName { Title = "Interstellar", Year = 2014 };
            var score = LibraryMatcher.Score(parsed, Candidate("Interstellar", 2014));
            Assert.That(score, Is.GreaterThanOrEqualTo(LibraryMatcher.MatchedThreshold));
        }

        [Test]
        public void LocalizedTitleMatchesThroughTheOriginalTitle()
        {
            var parsed = new ParsedName { Title = "The Godfather", Year = 1972 };
            var score = LibraryMatcher.Score(parsed, Candidate("El padrino", 1972, "The Godfather"));
            Assert.That(score, Is.GreaterThanOrEqualTo(LibraryMatcher.MatchedThreshold));
        }

        [Test]
        public void WrongYearDragsTheScoreIntoReview()
        {
            var parsed = new ParsedName { Title = "Dune", Year = 2021 };
            var score = LibraryMatcher.Score(parsed, Candidate("Dune", 1984));
            Assert.That(LibraryMatcher.StatusFor(score), Is.Not.EqualTo(LibraryFileStatus.Matched));
        }

        [Test]
        public void ASequelIsNotTheOriginal()
        {
            var parsed = new ParsedName { Title = "Avatar", Year = 2009 };
            var sequel = LibraryMatcher.Score(parsed, Candidate("Avatar: The Way of Water", 2022));
            var original = LibraryMatcher.Score(parsed, Candidate("Avatar", 2009));
            Assert.That(original, Is.GreaterThan(sequel));
            Assert.That(LibraryMatcher.StatusFor(sequel), Is.Not.EqualTo(LibraryFileStatus.Matched));
        }

        [Test]
        public void UnrelatedTitleIsUnmatched()
        {
            var parsed = new ParsedName { Title = "Interstellar", Year = 2014 };
            var score = LibraryMatcher.Score(parsed, Candidate("Gravity", 2013));
            Assert.That(LibraryMatcher.StatusFor(score), Is.EqualTo(LibraryFileStatus.Unmatched));
        }

        [Test]
        public void BestPicksTheHighestScoreAndBreaksTiesByProviderOrder()
        {
            var parsed = new ParsedName { Title = "Dune", Year = 2021 };
            var best = LibraryMatcher.Best(parsed, new[]
            {
                Candidate("Dune", 1984),
                Candidate("Dune", 2021),
                Candidate("Dune: Part Two", 2024)
            });
            Assert.That(best, Is.Not.Null);
            Assert.That(best!.Candidate.Year, Is.EqualTo(2021));
            Assert.That(best.Status, Is.EqualTo(LibraryFileStatus.Matched));
        }

        [Test]
        public void NoCandidatesMeansNoMatch()
        {
            Assert.That(LibraryMatcher.Best(new ParsedName { Title = "X" }, Array.Empty<ProviderCandidate>()), Is.Null);
        }

        [Test]
        public void MissingYearStillMatchesAnExactTitleButLower()
        {
            var parsed = new ParsedName { Title = "Interstellar" };
            var without = LibraryMatcher.Score(parsed, Candidate("Interstellar", 2014));
            var with = LibraryMatcher.Score(new ParsedName { Title = "Interstellar", Year = 2014 }, Candidate("Interstellar", 2014));
            Assert.That(without, Is.GreaterThanOrEqualTo(LibraryMatcher.MatchedThreshold));
            Assert.That(with, Is.GreaterThan(without));
        }
    }
}
