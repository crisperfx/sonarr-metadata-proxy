using Microsoft.Extensions.Logging.Abstractions;
using Sonarr.MetadataProxy.Models.AniList;
using Sonarr.MetadataProxy.Models.Mal;
using Sonarr.MetadataProxy.Options;
using Sonarr.MetadataProxy.Providers;
using Sonarr.MetadataProxy.Reverse;
using Sonarr.MetadataProxy.Tests.Infrastructure;
using Xunit;

namespace Sonarr.MetadataProxy.Tests;

public class AniListMetadataProviderTests
{
    private readonly FakeAniListApi _api;
    private readonly FakeMalApi _malApi;
    private readonly AniListMetadataProvider _provider;

    public AniListMetadataProviderTests()
    {
        _api = new FakeAniListApi();
        _malApi = new FakeMalApi();
        _provider = new AniListMetadataProvider(
            _api,
            _malApi,
            new AniListTvdbMap(new ProxyOptions { DataDir = Path.GetTempPath() }, NullLogger<AniListTvdbMap>.Instance),
            new ProxyOptions { MetadataSource = "anilist", SearchResultLimit = 10 },
            NullLogger<AniListMetadataProvider>.Instance);
    }

    [Fact]
    public async Task Search_MapsAniListMediaToSeriesMetadata()
    {
        _api.SearchResults = new List<AniListMedia> { TestData.DeathNote() };

        var results = await _provider.Search("death note", CancellationToken.None);

        var series = Assert.Single(results);
        Assert.Equal("1535", series.ProviderId);
        Assert.Equal("Death Note", series.Title);
        Assert.Equal("2006-10-04", series.FirstAirDate);
        Assert.Equal("2007-06-27", series.LastAirDate);
        Assert.Equal("ended", series.Status);
        Assert.Equal("Madhouse", series.Network);
        Assert.Equal("JP", series.OriginalCountryCode);
        Assert.Equal("ja", series.OriginalLanguageCode);
        Assert.Equal(7.7, series.VoteAverage);
        Assert.Equal(2, series.Genres.Count);
        Assert.Equal("https://s4.anilist.co/file/anilistcdn/media/anime/banner/1535-1.jpg", series.BackdropPath);
        Assert.Equal(1535, series.ExternalIds.TmdbId);
        Assert.Null(series.ExternalIds.TvdbId);
        Assert.Null(series.ExternalIds.ImdbId);
    }

    [Fact]
    public async Task Search_OrdersByAverageScoreAndLimits()
    {
        _api.SearchResults = new List<AniListMedia>
        {
            CreateMedia(1, "one", 50),
            CreateMedia(2, "two", 90),
            CreateMedia(3, "three", 70)
        };

        var results = await _provider.Search("test", CancellationToken.None);

        Assert.Equal(3, results.Count);
        Assert.Equal(new[] { "2", "3", "1" }, results.Select(series => series.ProviderId));
    }

    [Fact]
    public async Task SearchById_MapsMedia()
    {
        _api.ById[1535] = TestData.DeathNote();

        var results = await _provider.SearchById("1535", CancellationToken.None);

        var series = Assert.Single(results);
        Assert.Equal("1535", series.ProviderId);
    }

    [Fact]
    public async Task SearchById_UnknownId_ReturnsEmpty()
    {
        var results = await _provider.SearchById("999999", CancellationToken.None);

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchById_NonNumeric_ReturnsEmpty()
    {
        var results = await _provider.SearchById("abc", CancellationToken.None);

        Assert.Empty(results);
    }

    [Fact]
    public async Task GetSeries_MapsCast_AndTakesTwenty()
    {
        _api.ById[1535] = new AniListMedia
        {
            Id = 1535,
            IdMal = 1535,
            TitleRomaji = "Death Note",
            Cast = Enumerable.Range(1, 25).Select(i => new AniListCast
            {
                Name = $"Actor {i}",
                Character = $"Character {i}",
                ImageUrl = $"https://img{i}.example.com/x.jpg"
            }).ToList()
        };

        var series = await _provider.GetSeries("1535", CancellationToken.None);

        Assert.Equal(20, series.Actors.Count);
        Assert.Equal("Actor 1", series.Actors[0].Name);
        Assert.Equal("Character 1", series.Actors[0].Character);
        Assert.Equal("https://img1.example.com/x.jpg", series.Actors[0].ImageUrl);
    }

    [Fact]
    public async Task GetSeries_WithoutCast_ReturnsEmptyActors()
    {
        _api.ById[1535] = TestData.DeathNote();

        var series = await _provider.GetSeries("1535", CancellationToken.None);

        Assert.Empty(series.Actors);
    }

    [Fact]
    public async Task GetSeries_UnknownId_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _provider.GetSeries("999999", CancellationToken.None));
    }

    [Fact]
    public async Task GetSeriesWithTvdbId_ForcesTvdbId()
    {
        _api.ById[1535] = TestData.DeathNote();

        var series = await _provider.GetSeriesWithTvdbId("1535", 81189, CancellationToken.None);

        Assert.Equal(81189, series.ExternalIds.TvdbId);
        Assert.Equal(1535, series.ExternalIds.TmdbId);
    }

    [Fact]
    public async Task GetSeasons_WithSeasonNumbers_GroupsBySeason()
    {
        _api.ById[1535] = TestData.DeathNote();
        _malApi.EpisodesById[1535] = new List<MalEpisode>
        {
            CreateEpisode(1, 1, 1),
            CreateEpisode(2, 2, 1),
            CreateEpisode(3, 3, 2),
            CreateEpisode(4, 4, 2)
        };

        var seasons = await _provider.GetSeasons("1535", CancellationToken.None);

        Assert.Equal(2, seasons.Count);
        Assert.Equal(1, seasons[0].SeasonNumber);
        Assert.Equal(2, seasons[0].Episodes.Count);
        Assert.Equal(2, seasons[1].SeasonNumber);
        Assert.Equal(2, seasons[1].Episodes.Count);
        Assert.Equal("Episode 1", seasons[0].Episodes[0].Title);
        Assert.Equal("2006-10-04", seasons[0].Episodes[0].AirDate);
    }

    [Fact]
    public async Task GetSeasons_WithoutSeasonNumbers_FlattensToSingleContinuousSeason()
    {
        _api.ById[1535] = TestData.DeathNote();
        _malApi.EpisodesById[1535] = new List<MalEpisode>
        {
            CreateEpisode(1, 1, null),
            CreateEpisode(2, 2, null),
            CreateEpisode(3, 3, null)
        };

        var seasons = await _provider.GetSeasons("1535", CancellationToken.None);

        var season = Assert.Single(seasons);
        Assert.Equal(1, season.SeasonNumber);
        Assert.Equal(3, season.Episodes.Count);
        Assert.Equal(new[] { 1, 2, 3 }, season.Episodes.Select(episode => episode.EpisodeNumber));
        Assert.Equal(new[] { 1, 2, 3 }, season.Episodes.Select(episode => episode.AbsoluteEpisodeNumber!.Value));
    }

    [Fact]
    public async Task GetSeasons_WithoutMalId_ReturnsEmpty()
    {
        _api.ById[1535] = new AniListMedia
        {
            Id = 1535,
            TitleRomaji = "No Mal"
        };

        var seasons = await _provider.GetSeasons("1535", CancellationToken.None);

        Assert.Empty(seasons);
    }

    [Fact]
    public async Task GetSeasons_UnknownId_ReturnsEmpty()
    {
        var seasons = await _provider.GetSeasons("999999", CancellationToken.None);

        Assert.Empty(seasons);
    }

    private static AniListMedia CreateMedia(int id, string title, double score)
    {
        return new AniListMedia
        {
            Id = id,
            TitleRomaji = title,
            AverageScore = score
        };
    }

    private static MalEpisode CreateEpisode(int number, int absoluteNumber, int? seasonNumber)
    {
        return new MalEpisode
        {
            Number = number,
            AbsoluteNumber = absoluteNumber,
            SeasonNumber = seasonNumber,
            Title = $"Episode {number}",
            AirDate = "2006-10-04"
        };
    }
}