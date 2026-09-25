using Microsoft.Extensions.Logging.Abstractions;
using Sonarr.MetadataProxy.Mapping;
using Sonarr.MetadataProxy.Models.Anidb;
using Sonarr.MetadataProxy.Options;
using Sonarr.MetadataProxy.Reverse;
using Sonarr.MetadataProxy.Services;
using Sonarr.MetadataProxy.Tests.Infrastructure;
using Sonarr.MetadataProxy.Translation;
using Xunit;

namespace Sonarr.MetadataProxy.Tests;

public class AnidbSearchServiceTests : IDisposable
{
    private readonly string _dataDir;
    private readonly FakeAnidbApi _api;
    private readonly MappingStore _mapping;
    private readonly AniListTvdbMap _map;
    private readonly AnidbSearchService _service;

    public AnidbSearchServiceTests()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), "metadataproxy-anidb", Guid.NewGuid().ToString("N"));
        _api = new FakeAnidbApi();
        _mapping = new MappingStore(new ProxyOptions { DataDir = _dataDir }, NullLogger<MappingStore>.Instance);
        WriteAniListFixtures(_dataDir);
        _map = new AniListTvdbMap(
            new ProxyOptions { AniListDatamapDir = Path.Combine(_dataDir, "datamaps"), DataDir = _dataDir },
            NullLogger<AniListTvdbMap>.Instance);
        TestData.WriteTitleDumpFile(_dataDir, "2993|1|en|Death Note", "80|1|en|Shin Sekai Yori");

        var options = new ProxyOptions
        {
            DataDir = _dataDir,
            AnidbClientName = "test-client",
            AnidbClientVersion = "1"
        };
        _service = new AnidbSearchService(
            _api,
            new AnidbTitleList(options, new HttpClient(), NullLogger<AnidbTitleList>.Instance),
            new AnidbTranslator(),
            _mapping,
            _map,
            options,
            NullLogger<AnidbSearchService>.Instance);
    }

    [Fact]
    public void Configured_RequiresClientNameAndVersion()
    {
        var configured = new ProxyOptions { AnidbClientName = "x", AnidbClientVersion = "1" };
        var unconfigured = new ProxyOptions();

        Assert.True(configured.HasAnidbClient);
        Assert.False(unconfigured.HasAnidbClient);
    }

    [Fact]
    public async Task SearchAsync_TitleHit_UsesRealTvdbIdAndRegistersMapping()
    {
        var results = await _service.SearchAsync("death note", CancellationToken.None);

        var first = Assert.Single(results!);
        Assert.Equal(TestData.DeathNoteTvdbId, first.TvdbId);
        Assert.Equal(TestData.DeathNoteAnidbId, first.AnidbId);
        Assert.False(first.NoTVDBMapping);
        Assert.Empty(first.Images);
        Assert.Equal(TestData.DeathNoteAnidbId, _mapping.TryGetAnidbIdByTvdb(TestData.DeathNoteTvdbId));
    }

    [Fact]
    public async Task SearchAsync_WithoutRealTvdbMapping_UsesSyntheticIdAndSetsNoTvdbMapping()
    {
        var results = await _service.SearchAsync("shin sekai", CancellationToken.None);

        var first = Assert.Single(results!);
        Assert.Equal(SyntheticIds.AnidbSeriesId(80), first.TvdbId);
        Assert.True(first.NoTVDBMapping);
        Assert.Equal(80, _mapping.TryGetAnidbIdByTvdb(SyntheticIds.AnidbSeriesId(80)));
    }

    [Fact]
    public async Task SearchAsync_WhenNotConfigured_ReturnsNull()
    {
        var service = new AnidbSearchService(
            _api,
            new AnidbTitleList(new ProxyOptions { DataDir = _dataDir }, new HttpClient(), NullLogger<AnidbTitleList>.Instance),
            new AnidbTranslator(),
            _mapping,
            _map,
            new ProxyOptions { DataDir = _dataDir },
            NullLogger<AnidbSearchService>.Instance);

        var results = await service.SearchAsync("death note", CancellationToken.None);

        Assert.Null(results);
    }

    [Fact]
    public async Task SearchByAnidbIdAsync_ReturnsDecoratedShowWithPoster()
    {
        _api.ById[TestData.DeathNoteAnidbId] = TestData.DeathNoteAnime();

        var results = await _service.SearchByAnidbIdAsync(TestData.DeathNoteAnidbId, CancellationToken.None);

        var first = Assert.Single(results!);
        Assert.Equal(TestData.DeathNoteTvdbId, first.TvdbId);
        Assert.Equal(TestData.DeathNoteAnidbId, first.AnidbId);
        Assert.Equal("Death Note", first.Title);
        Assert.Single(first.Images);
        Assert.StartsWith("https://cdn.anidb.net/images/main/", first.Images[0].Url);
    }

    [Fact]
    public async Task SearchAsync_CachesTitleSearchResults()
    {
        var first = await _service.SearchAsync("death note", CancellationToken.None);
        var second = await _service.SearchAsync("death note", CancellationToken.None);

        Assert.Same(first, second);
    }

    [Fact]
    public async Task SearchAsync_ThrowsAnidbApiException_ReturnsNullForTvdbFallback()
    {
        _api.Exception = new AnidbApiException("down");

        var results = await _service.SearchByAnidbIdAsync(TestData.DeathNoteAnidbId, CancellationToken.None);

        Assert.Null(results);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dataDir, true);
        }
        catch
        {
        }
    }

    private static void WriteAniListFixtures(string dataDir)
    {
        var dir = Path.Combine(dataDir, "datamaps");
        Directory.CreateDirectory(dir);

        File.WriteAllText(Path.Combine(dir, "anime.json"), """
        [
          { "name": "Death Note", "idAL": 1535, "idAniDB": 2993, "idMal": 1535 },
          { "name": "Shin Sekai Yori", "idAL": 80, "idAniDB": 80, "idMal": 80 }
        ]
        """);

        File.WriteAllText(Path.Combine(dir, "anime-list-full.xml"), """
        <anime-list>
          <anime anidbid="2993" tvdbid="81356" defaulttvdbseason="1" episodeoffset="" lastupdate="1700000000" />
        </anime-list>
        """);
    }
}