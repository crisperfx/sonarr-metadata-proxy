using Sonarr.MetadataProxy.Terms;
using Xunit;

namespace Sonarr.MetadataProxy.Tests;

public class TermClassifierTests
{
    [Theory]
    [InlineData("Breaking Bad", TermKind.Title, "Breaking Bad")]
    [InlineData("breaking bad", TermKind.Title, "breaking bad")]
    [InlineData("tvdbid:81189", TermKind.TvdbId, "81189")]
    [InlineData("tvdbid: 81189", TermKind.TvdbId, "81189")]
    [InlineData("tvdb:81189", TermKind.Title, "tvdb:81189")]
    [InlineData("tvdb: 81189", TermKind.Title, "tvdb: 81189")]
    [InlineData("tvdb:0", TermKind.Title, "tvdb:0")]
    [InlineData("tvdb:abc", TermKind.Title, "tvdb:abc")]
    [InlineData("tvdb: breaking bad", TermKind.Title, "tvdb: breaking bad")]
    [InlineData("tmdb:1396", TermKind.Title, "tmdb:1396")]
    [InlineData("tmdb: 1399", TermKind.Title, "tmdb: 1399")]
    [InlineData("tmdb:0", TermKind.Title, "tmdb:0")]
    [InlineData("tmdb:abc", TermKind.Title, "tmdb:abc")]
    [InlineData("tmdb: breaking bad", TermKind.Title, "tmdb: breaking bad")]
    [InlineData("imdb:tt0903747", TermKind.ImdbId, "tt0903747")]
    [InlineData("imdb:  tt0903747", TermKind.ImdbId, "tt0903747")]
    [InlineData("mal:1535", TermKind.Title, "mal:1535")]
    [InlineData("anilist:1535", TermKind.Title, "anilist:1535")]
    [InlineData("tvmaze:169", TermKind.Title, "tvmaze:169")]
    [InlineData("tvmaze: 169", TermKind.Title, "tvmaze: 169")]
    [InlineData("tvmaze:0", TermKind.Title, "tvmaze:0")]
    [InlineData("tvmaze:abc", TermKind.Title, "tvmaze:abc")]
    [InlineData("tvmaze: breaking bad", TermKind.Title, "tvmaze: breaking bad")]
    [InlineData("", TermKind.Title, "")]
    [InlineData("   ", TermKind.Title, "")]
    [InlineData("81189", TermKind.TvdbId, "81189")]
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