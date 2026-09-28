using Microsoft.Extensions.Logging.Abstractions;
using Sonarr.MetadataProxy.Mapping;
using Sonarr.MetadataProxy.Models.Tvmaze;
using Sonarr.MetadataProxy.Options;
using Sonarr.MetadataProxy.Providers;
using Sonarr.MetadataProxy.Tests.Infrastructure;
using Sonarr.MetadataProxy.Translation;
using Xunit;

namespace Sonarr.MetadataProxy.Tests;

public class TvmazeMetadataProviderTests
{
    private readonly FakeTvmazeApi _api;
    private readonly TvmazeMetadataProvider _provider;

    public TvmazeMetadataProviderTests()
    {
        _api = new FakeTvmazeApi();
        _provider = new TvmazeMetadataProvider(
            _api,
            new ProxyOptions { MetadataSource = "tvmaze", SearchResultLimit = 10 },
            new TvmazeTranslator(),
            NullLogger<TvmazeMetadataProvider>.Instance);
    }

    [Fact]
    public void Name_IsTvmaze()
    {
        Assert.Equal("tvmaze", _provider.Name);
    }

    [Fact]
    public async Task Search_MapsTvmazeShowsToSeriesMetadata()
    {
        _api.SearchResults = new List<TvmazeShow> { TestData.BreakingBadTvmaze() };

        var results = await _provider.Search("breaking bad", CancellationToken.None);

        var series = Assert.Single(results);
        Assert.Equal("169", series.ProviderId);
        Assert.Equal("Breaking Bad", series.Title);
        Assert.Equal("2008-01-20", series.FirstAirDate);
        Assert.Equal("ended", series.Status);
        Assert.Equal("AMC", series.Network);
        Assert.Equal(TestData.BreakingBadTvdbId, series.ExternalIds.TvdbId);
        Assert.Equal("tt0903747", series.ExternalIds.ImdbId);
    }

    [Fact]
    public async Task Search_WithoutRealTvdbExternal_UsesSyntheticTvdbId()
    {
        _api.SearchResults = new List<TvmazeShow> { TestData.BlackMirrorNoTvdbExternal() };

        var results = await _provider.Search("black mirror", CancellationToken.None);

        var series = Assert.Single(results);
        Assert.Equal(SyntheticIds.TvmazeSeriesId(96), series.ExternalIds.TvdbId);
    }

    [Fact]
    public async Task GetSeries_MapsShowById()
    {
        _api.ById[TestData.BreakingBadTvmazeId] = TestData.BreakingBadTvmaze();

        var series = await _provider.GetSeries("169", CancellationToken.None);

        Assert.Equal("169", series.ProviderId);
        Assert.Equal(TestData.BreakingBadTvdbId, series.ExternalIds.TvdbId);
        Assert.Equal("tt0903747", series.ExternalIds.ImdbId);
    }

    [Fact]
    public async Task GetSeries_ThrowsWhenShowMissing()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _provider.GetSeries("169", CancellationToken.None));
    }

    [Fact]
    public async Task GetSeasons_GroupsEpisodesBySeason()
    {
        var episodes = TestData.BreakingBadTvmazeEpisodes();
        episodes.Add(new TvmazeEpisode
        {
            Id = 2000,
            Name = "Seven Thirty-Seven",
            Season = 2,
            Number = 1,
            Type = "regular",
            AirDate = "2009-03-08"
        });
        _api.EpisodesById[TestData.BreakingBadTvmazeId] = episodes;

        var seasons = await _provider.GetSeasons("169", CancellationToken.None);

        Assert.Equal(2, seasons.Count);
        Assert.Equal(1, seasons[0].SeasonNumber);
        Assert.Equal(2, seasons[0].Episodes.Count);
        Assert.Equal("Pilot", seasons[0].Episodes[0].Title);
        Assert.Equal(2, seasons[1].SeasonNumber);
        Assert.Equal("Seven Thirty-Seven", seasons[1].Episodes[0].Title);
    }

    [Fact]
    public async Task GetSeasons_IgnoresNegativeSeasonNumbers()
    {
        _api.EpisodesById[TestData.BreakingBadTvmazeId] = new List<TvmazeEpisode>
        {
            new() { Id = 1, Name = "Specials", Season = -1, Number = 0, Type = "special" },
            new() { Id = 2, Name = "Pilot", Season = 1, Number = 1, Type = "regular" }
        };

        var seasons = await _provider.GetSeasons("169", CancellationToken.None);

        var season = Assert.Single(seasons);
        Assert.Equal(1, season.SeasonNumber);
    }

    [Fact]
    public async Task Search_ThrowsOnApiFailure()
    {
        _api.Exception = new TvmazeApiException("TVMaze is down");

        await Assert.ThrowsAsync<TvmazeApiException>(() => _provider.Search("breaking bad", CancellationToken.None));
    }
}