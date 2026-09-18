using Sonarr.MetadataProxy.Terms;
using Xunit;

namespace Sonarr.MetadataProxy.Tests;

public class TermClassifierTests
{
    [Theory]
    [InlineData("Breaking Bad", TermKind.Title, "Breaking Bad")]
    [InlineData("breaking bad", TermKind.Title, "breaking bad")]
    [InlineData("tvdb:81189", TermKind.TvdbId, "81189")]
    [InlineData("tvdb: 81189", TermKind.TvdbId, "81189")]
    [InlineData("tvdbid:81189", TermKind.TvdbId, "81189")]
    [InlineData("tvdb:0", TermKind.Title, "tvdb:0")]
    [InlineData("tvdb:abc", TermKind.Title, "tvdb:abc")]
    [InlineData("tmdb:1396", TermKind.TmdbId, "1396")]
    [InlineData("tmdb: 1399", TermKind.TmdbId, "1399")]
    [InlineData("imdb:tt0903747", TermKind.ImdbId, "tt0903747")]
    [InlineData("imdb:  tt0903747", TermKind.ImdbId, "tt0903747")]
    [InlineData("mal:1535", TermKind.MalId, "1535")]
    [InlineData("anilist:1535", TermKind.AniListId, "1535")]
    [InlineData("", TermKind.Title, "")]
    [InlineData("   ", TermKind.Title, "")]
    public void Classify_ReturnsExpected(string raw, TermKind expectedKind, string expectedValue)
    {
        var result = TermClassifier.Classify(raw);

        Assert.Equal(expectedKind, result.Kind);
        Assert.Equal(expectedValue, result.Value);
    }

    [Fact]
    public void Classify_TrimsLeadingAndTrailingWhitespace()
    {
        var result = TermClassifier.Classify("  breaking bad  ");

        Assert.Equal(TermKind.Title, result.Kind);
        Assert.Equal("breaking bad", result.Value);
    }
}