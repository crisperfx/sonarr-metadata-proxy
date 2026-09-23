using Microsoft.Extensions.Logging.Abstractions;
using Sonarr.MetadataProxy.Mapping;
using Sonarr.MetadataProxy.Options;
using Xunit;

namespace Sonarr.MetadataProxy.Tests;

public class MappingStoreTests : IDisposable
{
    private readonly string _dataDir;

    public MappingStoreTests()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), "metadataproxy-tests", Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public void RegisterAniListId_StoresAniListBindingForTvdbId()
    {
        var store = CreateStore();

        store.RegisterAniListId(81356, 1535);

        Assert.Equal(1535, store.TryGetAniListIdByTvdb(81356));
    }

    [Fact]
    public void RegisterAniListId_AcceptsSyntheticTvdbIds()
    {
        var store = CreateStore();
        var synthetic = SyntheticIds.SeriesId(1396);

        store.RegisterAniListId(synthetic, 1535);

        Assert.Equal(1535, store.TryGetAniListIdByTvdb(synthetic));
    }

    [Fact]
    public void AniListBindings_PersistAcrossStoreInstances()
    {
        var dir = _dataDir;
        CreateStore(dir).RegisterAniListId(81356, 1535);

        var reloaded = CreateStore(dir);

        Assert.Equal(1535, reloaded.TryGetAniListIdByTvdb(81356));
    }

    [Fact]
    public void RegisterSeries_StoresRealTvdbToTmdbMapping()
    {
        var store = CreateStore();

        store.RegisterSeries(81189, 1396);

        Assert.Equal(1396, store.TryResolveSeriesTmdb(81189));
    }

    [Fact]
    public void RegisterSeries_IgnoresSyntheticTvdbIds()
    {
        var store = CreateStore();
        var synthetic = SyntheticIds.SeriesId(1396);

        store.RegisterSeries(synthetic, 1396);

        Assert.Null(store.TryResolveSeriesTmdb(synthetic));
    }

    [Fact]
    public void EpisodeTvdbId_IsStableForSameEpisode()
    {
        var store = CreateStore();
        var seriesId = SyntheticIds.SeriesId(1396);

        var first = store.EpisodeTvdbId(seriesId, 1, 1);
        var second = store.EpisodeTvdbId(seriesId, 1, 1);

        Assert.Equal(first, second);
    }

    [Fact]
    public void EpisodeTvdbId_IsUniqueAcrossEpisodes()
    {
        var store = CreateStore();
        var seriesId = SyntheticIds.SeriesId(1396);

        var id1 = store.EpisodeTvdbId(seriesId, 1, 1);
        var id2 = store.EpisodeTvdbId(seriesId, 1, 2);
        var id3 = store.EpisodeTvdbId(seriesId, 2, 1);

        Assert.NotEqual(id1, id2);
        Assert.NotEqual(id1, id3);
        Assert.NotEqual(id2, id3);
    }

    [Fact]
    public void Mappings_PersistAcrossStoreInstances()
    {
        var dir = _dataDir;
        var store1 = CreateStore(dir);
        var seriesId = SyntheticIds.SeriesId(1396);
        store1.RegisterSeries(81189, 1396);
        var episodeId = store1.EpisodeTvdbId(seriesId, 1, 1);

        var store2 = CreateStore(dir);

        Assert.Equal(1396, store2.TryResolveSeriesTmdb(81189));
        Assert.Equal(episodeId, store2.EpisodeTvdbId(seriesId, 1, 1));
    }

    [Fact]
    public void MissingMapping_ReturnsNull()
    {
        var store = CreateStore();

        Assert.Null(store.TryResolveSeriesTmdb(999999));
    }

    [Fact]
    public void Override_NullByDefault()
    {
        var store = CreateStore();

        Assert.Null(store.GetOverride(81189));
    }

    [Theory]
    [InlineData(MappingStore.SourceTmdb)]
    [InlineData(MappingStore.SourceTvdb)]
    [InlineData(MappingStore.SourceAniList)]
    public void Override_SetStoresSource(string source)
    {
        var store = CreateStore();

        store.SetOverride(81189, source);

        Assert.Equal(source, store.GetOverride(81189));
    }

    [Fact]
    public void Override_AcceptsSyntheticIds()
    {
        var store = CreateStore();
        var synthetic = SyntheticIds.SeriesId(1396);

        store.SetOverride(synthetic, MappingStore.SourceTvdb);

        Assert.Equal(MappingStore.SourceTvdb, store.GetOverride(synthetic));
    }

    [Fact]
    public void Override_Removed_ReturnsNull()
    {
        var store = CreateStore();
        store.SetOverride(81189, MappingStore.SourceTvdb);

        var removed = store.RemoveOverride(81189);

        Assert.True(removed);
        Assert.Null(store.GetOverride(81189));
    }

    [Fact]
    public void Override_Removed_AlsoClearsAssociations()
    {
        var store = CreateStore();
        store.RegisterSeries(81189, 1396);
        store.RegisterAniListId(81189, 12345);
        store.SetOverride(81189, MappingStore.SourceTmdb);

        var removed = store.RemoveOverride(81189);

        Assert.True(removed);
        Assert.Null(store.GetOverride(81189));
        Assert.Null(store.TryResolveSeriesTmdb(81189));
        Assert.Null(store.TryGetAniListIdByTvdb(81189));
    }

    [Fact]
    public void Override_PersistsAcrossStoreInstances()
    {
        var dir = _dataDir;
        var store1 = CreateStore(dir);
        store1.SetOverride(81189, MappingStore.SourceTvdb);

        var store2 = CreateStore(dir);

        Assert.Equal(MappingStore.SourceTvdb, store2.GetOverride(81189));
    }

    [Fact]
    public void SearchSource_EmptyByDefault()
    {
        var store = CreateStore();

        Assert.Equal("", store.GetDefaultSearchSource());
    }

    [Theory]
    [InlineData("")]
    [InlineData(MappingStore.SourceTmdb)]
    [InlineData(MappingStore.SourceTvdb)]
    [InlineData(MappingStore.SourceAniList)]
    public void SearchSource_SetStoresValue(string source)
    {
        var store = CreateStore();

        store.SetDefaultSearchSource(source);

        Assert.Equal(source, store.GetDefaultSearchSource());
    }

    [Fact]
    public void SearchSource_NormalizesInput()
    {
        var store = CreateStore();

        store.SetDefaultSearchSource("  TVDB ");

        Assert.Equal(MappingStore.SourceTvdb, store.GetDefaultSearchSource());
    }

    [Theory]
    [InlineData("bogus")]
    [InlineData("tmdb:")]
    public void SearchSource_RejectsInvalidValue(string source)
    {
        var store = CreateStore();

        Assert.Throws<ArgumentException>(() => store.SetDefaultSearchSource(source));
    }

    [Fact]
    public void SearchSource_PersistsAcrossStoreInstances()
    {
        var dir = _dataDir;
        var store1 = CreateStore(dir);
        store1.SetDefaultSearchSource(MappingStore.SourceTmdb);

        var store2 = CreateStore(dir);

        Assert.Equal(MappingStore.SourceTmdb, store2.GetDefaultSearchSource());
    }

    private MappingStore CreateStore(string? dir = null)
    {
        var dataDir = dir ?? _dataDir;
        return new MappingStore(new ProxyOptions { DataDir = dataDir }, NullLogger<MappingStore>.Instance);
    }

    [Fact]
    public void LegacyFile_IsMigratedIntoMappingsSubfolder()
    {
        var legacy = Path.Combine(_dataDir, "mappings.json");
        Directory.CreateDirectory(_dataDir);
        File.WriteAllText(
            legacy,
            """
            {
              "SeriesReal": { "81189": 1396 },
              "Episodes": { "81189:1:1": 2000000000 },
              "EpisodeSequences": { "81189": 1 },
              "Overrides": { "81189": "tmdb" }
            }
            """);

        var store = new MappingStore(new ProxyOptions { DataDir = _dataDir }, NullLogger<MappingStore>.Instance);

        Assert.Equal(1396, store.TryResolveSeriesTmdb(81189));
        Assert.Equal(2000000000, store.EpisodeTvdbId(81189, 1, 1));
        Assert.Equal(MappingStore.SourceTmdb, store.GetOverride(81189));
        Assert.False(File.Exists(legacy), "Legacy file should be moved, not copied.");
        Assert.True(File.Exists(Path.Combine(_dataDir, "mappings", "mappings.json")));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dataDir))
            {
                Directory.Delete(_dataDir, true);
            }
        }
        catch
        {
        }
    }
}