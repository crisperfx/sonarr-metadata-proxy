using Microsoft.Extensions.Logging.Abstractions;
using Sonarr.MetadataProxy.Models.Metadata;
using Sonarr.MetadataProxy.Models.Tmdb;
using Sonarr.MetadataProxy.Options;
using Sonarr.MetadataProxy.Providers;
using Sonarr.MetadataProxy.Tests.Infrastructure;
using Xunit;

namespace Sonarr.MetadataProxy.Tests;

public class TmdbMetadataProviderTests
{
    private readonly FakeTmdbApi _api;
    private readonly TmdbMetadataProvider _provider;

    public TmdbMetadataProviderTests()
    {
        _api = new FakeTmdbApi();
        _provider = new TmdbMetadataProvider(
            _api,
            new ProxyOptions { MetadataSource = "tmdb", TmdbApiKey = "test-key", SearchResultLimit = 10 },
            NullLogger<TmdbMetadataProvider>.Instance);
    }

    [Fact]
    public async Task Search_MapsTmdbSearchResultsToSeriesMetadata()
    {
        _api.SearchResults = new List<TmdbTvSearchResult> { TestData.BreakingBadSearchResult() };
        _api.Details = TestData.BreakingBadDetails();

        var results = await _provider.Search("breaking bad", CancellationToken.None);

        var series = Assert.Single(results);
        Assert.Equal("1396", series.ProviderId);
        Assert.Equal("Breaking Bad", series.Title);
        Assert.Equal("2008-01-20", series.FirstAirDate);
        Assert.Equal("ended", series.Status);
        Assert.Equal("AMC", series.Network);
        Assert.Equal("TV-MA", series.ContentRating);
        Assert.Equal(2, series.Genres.Count);
        Assert.Equal(2, series.Seasons.Count);
        Assert.Equal(2, series.Actors.Count);
        Assert.Equal(81189, series.ExternalIds.TvdbId);
        Assert.Equal("tt0903747", series.ExternalIds.ImdbId);
        Assert.Equal(1396, series.ExternalIds.TmdbId);
    }

    [Fact]
    public async Task Search_OrdersByVoteCountAndLimits()
    {
        _api.SearchResults = new List<TmdbTvSearchResult>
        {
            CreateSearchResult(1, "one", 10),
            CreateSearchResult(2, "two", 5),
            CreateSearchResult(3, "three", 50),
            CreateSearchResult(4, "four", 1)
        };
        _api.DetailsById[1] = CreateDetails(1, "one");
        _api.DetailsById[2] = CreateDetails(2, "two");
        _api.DetailsById[3] = CreateDetails(3, "three");
        _api.DetailsById[4] = CreateDetails(4, "four");

        var results = await _provider.Search("test", CancellationToken.None);

        Assert.Equal(4, results.Count);
        Assert.Equal("three", results[0].Title);
        Assert.Equal(new[] { "3", "1", "2", "4" }, results.Select(series => series.ProviderId));
    }

    [Fact]
    public async Task GetSeries_MapsExternalIds()
    {
        _api.Details = TestData.BreakingBadDetails();

        var series = await _provider.GetSeries("1396", CancellationToken.None);

        Assert.Equal(81189, series.ExternalIds.TvdbId);
        Assert.Equal("tt0903747", series.ExternalIds.ImdbId);
        Assert.Equal(1396, series.ExternalIds.TmdbId);
    }

    [Fact]
    public async Task GetSeasons_MapsEpisodes()
    {
        _api.Details = TestData.BreakingBadDetails();
        _api.Seasons[1] = TestData.SeasonOneEpisodes();
        _api.Seasons[2] = TestData.SeasonTwoEpisodes();

        var seasons = await _provider.GetSeasons("1396", CancellationToken.None);

        Assert.Equal(2, seasons.Count);
        Assert.Equal(2, seasons[0].Episodes.Count);
        Assert.Equal("Pilot", seasons[0].Episodes[0].Title);
        Assert.Equal("2008-01-20", seasons[0].Episodes[0].AirDate);
        Assert.Null(seasons[0].Episodes[0].AbsoluteEpisodeNumber);
    }

    [Fact]
    public async Task Search_WithoutTmdbCredentials_ThrowsControlledError()
    {
        var provider = new TmdbMetadataProvider(
            _api,
            new ProxyOptions { MetadataSource = "tmdb", TmdbApiKey = null, TmdbApiToken = null },
            NullLogger<TmdbMetadataProvider>.Instance);

        var exception = await Assert.ThrowsAsync<TmdbApiException>(() => provider.Search("test", CancellationToken.None));

        Assert.Contains("credentials", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Search_ThrowsOnApiFailure()
    {
        _api.Exception = new TmdbApiException("TMDB is down", 503);

        await Assert.ThrowsAsync<TmdbApiException>(() => _provider.Search("breaking bad", CancellationToken.None));
    }

    private static TmdbTvSearchResult CreateSearchResult(int id, string name, int votes)
    {
        return new TmdbTvSearchResult { Id = id, Name = name, VoteCount = votes };
    }

    private static TmdbTvDetails CreateDetails(int id, string name)
    {
        return new TmdbTvDetails { Id = id, Name = name, OriginalName = name };
    }
}