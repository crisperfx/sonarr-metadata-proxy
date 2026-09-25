using Microsoft.Extensions.Logging.Abstractions;
using Sonarr.MetadataProxy.Mapping;
using Sonarr.MetadataProxy.Models.Tvmaze;
using Sonarr.MetadataProxy.Options;
using Sonarr.MetadataProxy.Services;
using Sonarr.MetadataProxy.Tests.Infrastructure;
using Sonarr.MetadataProxy.Translation;
using Xunit;

namespace Sonarr.MetadataProxy.Tests;

public class TvmazeSearchServiceTests : IDisposable
{
    private readonly string _dataDir;
    private readonly FakeTvmazeApi _api;
    private readonly MappingStore _mapping;
    private readonly TvmazeSearchService _service;

    public TvmazeSearchServiceTests()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), "metadataproxy-tests", Guid.NewGuid().ToString("N"));
        _api = new FakeTvmazeApi();
        _mapping = new MappingStore(new ProxyOptions { DataDir = _dataDir }, NullLogger<MappingStore>.Instance);
        _service = new TvmazeSearchService(
            _api,
            new TvmazeTranslator(),
            _mapping,
            new ProxyOptions { CacheTtlMinutes = 1440 },
            NullLogger<TvmazeSearchService>.Instance);
    }

    [Fact]
    public void IsConfigured_IsAlwaysTrue()
    {
        Assert.True(_service.IsConfigured);
    }

    [Fact]
    public async Task SearchAsync_RegistersRealTvdbMapping()
    {
        _api.SearchResults = TestData.BreakingBadTvmazeSearchHit();

        var results = await _service.SearchAsync("breaking bad", CancellationToken.None);

        var first = Assert.Single(results!);
        Assert.Equal(TestData.BreakingBadTvdbId, first.TvdbId);
        Assert.Equal(TestData.BreakingBadTvmazeId, first.TvMazeId);
        Assert.False(first.NoTVDBMapping);
        Assert.Equal(TestData.BreakingBadTvmazeId, _mapping.TryGetTvmazeIdByTvdb(TestData.BreakingBadTvdbId));
    }

    [Fact]
    public async Task SearchAsync_WithoutRealTvdbExternal_UsesSyntheticIdAndSetsNoTvdbMapping()
    {
        _api.SearchResults = new List<TvmazeShow> { TestData.BlackMirrorNoTvdbExternal() };

        var results = await _service.SearchAsync("black mirror", CancellationToken.None);

        var first = Assert.Single(results!);
        Assert.Equal(SyntheticIds.TvmazeSeriesId(96), first.TvdbId);
        Assert.Equal(96, first.TvMazeId);
        Assert.True(first.NoTVDBMapping);
        Assert.Equal(96, _mapping.TryGetTvmazeIdByTvdb(SyntheticIds.TvmazeSeriesId(96)));
    }

    [Fact]
    public async Task SearchByTvmazeIdAsync_ReturnsShow()
    {
        _api.ById[TestData.BreakingBadTvmazeId] = TestData.BreakingBadTvmaze();

        var results = await _service.SearchByTvmazeIdAsync(TestData.BreakingBadTvmazeId, CancellationToken.None);

        var first = Assert.Single(results!);
        Assert.Equal(TestData.BreakingBadTvdbId, first.TvdbId);
        Assert.Equal(TestData.BreakingBadTvmazeId, first.TvMazeId);
    }

    [Fact]
    public async Task SearchByImdbIdAsync_ReturnsShow()
    {
        _api.ById[TestData.BreakingBadTvmazeId] = TestData.BreakingBadTvmaze();

        var results = await _service.SearchByImdbIdAsync("tt0903747", CancellationToken.None);

        var first = Assert.Single(results!);
        Assert.Equal(TestData.BreakingBadTvdbId, first.TvdbId);
    }

    [Fact]
    public async Task SearchAsync_ApiFailure_ReturnsNullForTvdbFallback()
    {
        _api.Exception = new TvmazeApiException("down");

        var results = await _service.SearchAsync("anything", CancellationToken.None);

        Assert.Null(results);
    }

    [Fact]
    public async Task SearchAsync_CachesResultsWithinTtl()
    {
        _api.SearchResults = TestData.BreakingBadTvmazeSearchHit();

        _ = await _service.SearchAsync("breaking bad", CancellationToken.None);
        _ = await _service.SearchAsync("breaking bad", CancellationToken.None);

        Assert.Equal(1, _api.SearchCallCount);
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