using Microsoft.Extensions.Logging.Abstractions;
using Sonarr.MetadataProxy.Models.Anidb;
using Sonarr.MetadataProxy.Providers;
using Sonarr.MetadataProxy.Tests.Infrastructure;
using Sonarr.MetadataProxy.Translation;
using Xunit;

namespace Sonarr.MetadataProxy.Tests;

public class AnidbMetadataProviderTests
{
    private readonly FakeAnidbApi _api;
    private readonly AnidbMetadataProvider _provider;

    public AnidbMetadataProviderTests()
    {
        _api = new FakeAnidbApi();
        _provider = new AnidbMetadataProvider(_api, new AnidbTranslator(), NullLogger<AnidbMetadataProvider>.Instance);
    }

    [Fact]
    public void Name_IsAnidb()
    {
        Assert.Equal("anidb", _provider.Name);
    }

    [Fact]
    public async Task GetSeries_ReturnsMappedSeries()
    {
        _api.ById[TestData.DeathNoteAnidbId] = TestData.DeathNoteAnime();

        var series = await _provider.GetSeries(TestData.DeathNoteAnidbId.ToString(), CancellationToken.None);

        Assert.Equal(TestData.DeathNoteAnidbId.ToString(), series.ProviderId);
        Assert.Equal("Death Note", series.Title);
    }

    [Fact]
    public async Task SearchById_ReturnsSeries()
    {
        _api.ById[TestData.DeathNoteAnidbId] = TestData.DeathNoteAnime();

        var results = await _provider.SearchById(TestData.DeathNoteAnidbId.ToString(), CancellationToken.None);

        Assert.Single(results);
    }

    [Fact]
    public async Task GetSeasons_GroupsSpecialsIntoSeasonZero()
    {
        _api.ById[TestData.DeathNoteAnidbId] = TestData.DeathNoteAnime();

        var seasons = await _provider.GetSeasons(TestData.DeathNoteAnidbId.ToString(), CancellationToken.None);

        var specials = Assert.Single(seasons, s => s.SeasonNumber == 0);
        Assert.Single(specials.Episodes);
        Assert.Equal("Directives", specials.Episodes[0].Title);

        var regular = Assert.Single(seasons, s => s.SeasonNumber == 1);
        Assert.Equal(2, regular.Episodes.Count);
        Assert.All(regular.Episodes, e => Assert.Equal("standard", e.EpisodeType));
    }

    [Fact]
    public async Task GetSeries_UnknownIdThrows()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _provider.GetSeries("999999", CancellationToken.None));
    }

    [Fact]
    public async Task Search_ThrowsNotSupported()
    {
        await Assert.ThrowsAsync<NotSupportedException>(
            () => _provider.Search("death note", CancellationToken.None));
    }

    [Fact]
    public async Task GetSeries_ApiShowsError_ThrowsAnidbApiException()
    {
        _api.Exception = new AnidbApiException("down");

        await Assert.ThrowsAsync<AnidbApiException>(
            () => _provider.GetSeries(TestData.DeathNoteAnidbId.ToString(), CancellationToken.None));
    }
}