using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sonarr.MetadataProxy.Mapping;
using Sonarr.MetadataProxy.Options;
using Sonarr.MetadataProxy.Providers;
using Sonarr.MetadataProxy.Services;
using Sonarr.MetadataProxy.Tests.Infrastructure;
using Sonarr.MetadataProxy.Translation;
using Xunit;

namespace Sonarr.MetadataProxy.Tests;

public class MetadataRequestHandlerTests
{
    private static readonly IServiceProvider TestServiceProvider = CreateTestServiceProvider();

    private readonly string _dataDir;
    private readonly FakeTmdbApi _tmdb;
    private readonly FakeTvdbResolver _resolver;
    private readonly FakeSkyHookPassthrough _passthrough;

    public MetadataRequestHandlerTests()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), "metadataproxy-tests", Guid.NewGuid().ToString("N"));
        _tmdb = new FakeTmdbApi();
        _resolver = new FakeTvdbResolver();
        _passthrough = new FakeSkyHookPassthrough();
    }

    [Fact]
    public async Task Search_Title_UsesActiveProviderNotPassthrough()
    {
        var handler = CreateHandler(fallbackEnabled: true);
        _tmdb.SearchResults = new List<Sonarr.MetadataProxy.Models.Tmdb.TmdbTvSearchResult>
        {
            new() { Id = 1396, Name = "Breaking Bad" }
        };
        _tmdb.Details = TestData.BreakingBadDetails();

        var result = await handler.SearchAsync("breaking bad", CancellationToken.None);

        Assert.Null(_passthrough.LastSearchTerm);
        var (status, _) = await ExecuteAsync(result);
        Assert.Equal(StatusCodes.Status200OK, status);
    }

    [Fact]
    public async Task Search_EmptyResults_FallsThroughToTvdb()
    {
        var handler = CreateHandler(fallbackEnabled: true);

        await handler.SearchAsync("totally nonexistent show", CancellationToken.None);

        Assert.Equal("totally nonexistent show", _passthrough.LastSearchTerm);
    }

    [Fact]
    public async Task Search_EmptyResults_WithFallbackDisabled_DoesNotCallPassthrough()
    {
        var handler = CreateHandler(fallbackEnabled: false);

        var result = await handler.SearchAsync("totally nonexistent show", CancellationToken.None);

        Assert.Null(_passthrough.LastSearchTerm);
        var (status, _) = await ExecuteAsync(result);
        Assert.Equal(StatusCodes.Status200OK, status);
    }

    [Fact]
    public async Task Search_AniListTerm_FallsThroughToTvdb()
    {
        var handler = CreateHandler(fallbackEnabled: true);

        await handler.SearchAsync("anilist:1535", CancellationToken.None);

        Assert.Equal("anilist:1535", _passthrough.LastSearchTerm);
    }

    [Fact]
    public async Task Search_TvdbTextTerm_ForcesTvdbSearchWithUnprefixedTerm()
    {
        var handler = CreateHandler(fallbackEnabled: true);
        _tmdb.SearchResults = new List<Sonarr.MetadataProxy.Models.Tmdb.TmdbTvSearchResult>
        {
            new() { Id = 1396, Name = "Breaking Bad" }
        };

        await handler.SearchAsync("tvdb:breaking bad", CancellationToken.None);

        Assert.Equal("breaking bad", _passthrough.LastSearchTerm);
        Assert.Equal(0, _tmdb.SearchCallCount);
    }

    [Fact]
    public async Task Search_TmdbTextTerm_DoesNotFallThroughToTvdbOnEmptyResults()
    {
        var handler = CreateHandler(fallbackEnabled: true);

        var result = await handler.SearchAsync("tmdb:totally nonexistent show", CancellationToken.None);

        Assert.Null(_passthrough.LastSearchTerm);
        var (status, _) = await ExecuteAsync(result);
        Assert.Equal(StatusCodes.Status200OK, status);
    }

    [Fact]
    public async Task Search_TmdbTextTerm_UsesActiveProvider()
    {
        var handler = CreateHandler(fallbackEnabled: true);
        _tmdb.SearchResults = new List<Sonarr.MetadataProxy.Models.Tmdb.TmdbTvSearchResult>
        {
            new() { Id = 1396, Name = "Breaking Bad" }
        };
        _tmdb.Details = TestData.BreakingBadDetails();

        var result = await handler.SearchAsync("tmdb:breaking bad", CancellationToken.None);

        Assert.True(_tmdb.SearchCallCount > 0);
        Assert.Null(_passthrough.LastSearchTerm);
        var (status, _) = await ExecuteAsync(result);
        Assert.Equal(StatusCodes.Status200OK, status);
    }

    [Fact]
    public async Task Search_MalTerm_FallsThroughToTvdb()
    {
        var handler = CreateHandler(fallbackEnabled: true);

        await handler.SearchAsync("mal:1535", CancellationToken.None);

        Assert.Equal("mal:1535", _passthrough.LastSearchTerm);
    }

    [Fact]
    public async Task Search_TmdbApiFailure_FallsThroughToTvdb()
    {
        var handler = CreateHandler(fallbackEnabled: true);
        _tmdb.Exception = new TmdbApiException("TMDB is down", 503);

        await handler.SearchAsync("breaking bad", CancellationToken.None);

        Assert.Equal("breaking bad", _passthrough.LastSearchTerm);
    }

    [Fact]
    public async Task Search_UnsupportedProviderSource_FallsThroughToTvdb()
    {
        var handler = CreateHandler(fallbackEnabled: true, source: "anilist");

        await handler.SearchAsync("some anime", CancellationToken.None);

        Assert.Equal("some anime", _passthrough.LastSearchTerm);
    }

    [Fact]
    public async Task Show_SyntheticTvdbId_UsesTmdbDetails()
    {
        var handler = CreateHandler(fallbackEnabled: true);
        _tmdb.Details = TestData.BreakingBadDetails();
        _tmdb.Seasons[1] = TestData.SeasonOneEpisodes();
        _tmdb.Seasons[2] = TestData.SeasonTwoEpisodes();

        var result = await handler.ShowAsync(1000001396, CancellationToken.None);

        Assert.Equal(0, _resolver.CallCount);
        Assert.Equal(0, _passthrough.ShowCallCount);
        Assert.True(_tmdb.DetailsCallCount > 0);
        var (status, _) = await ExecuteAsync(result);
        Assert.Equal(StatusCodes.Status200OK, status);
    }

    [Fact]
    public async Task Show_RealTvdbWithMapping_UsesTmdbAndRegistersMapping()
    {
        var handler = CreateHandler(fallbackEnabled: true);
        _resolver.Map[81189] = 1396;
        _tmdb.Details = TestData.BreakingBadDetails();
        _tmdb.Seasons[1] = TestData.SeasonOneEpisodes();

        var first = await handler.ShowAsync(81189, CancellationToken.None);
        Assert.Equal(StatusCodes.Status200OK, (await ExecuteAsync(first)).Status);

        var second = await handler.ShowAsync(81189, CancellationToken.None);
        Assert.Equal(StatusCodes.Status200OK, (await ExecuteAsync(second)).Status);

        Assert.Equal(1, _resolver.CallCount);
    }

    [Fact]
    public async Task Show_UnknownTvdb_FallsThroughToTvdbWhenEnabled()
    {
        var handler = CreateHandler(fallbackEnabled: true);

        var result = await handler.ShowAsync(999999, CancellationToken.None);

        Assert.Equal(1, _passthrough.ShowCallCount);
        var (status, body) = await ExecuteAsync(result);
        Assert.Equal(200, status);
        Assert.Contains("fallback", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Show_UnknownTvdb_ReturnsNotFoundWhenFallbackDisabled()
    {
        var handler = CreateHandler(fallbackEnabled: false);

        var result = await handler.ShowAsync(999999, CancellationToken.None);

        Assert.Equal(0, _passthrough.ShowCallCount);
        var (status, _) = await ExecuteAsync(result);
        Assert.Equal(StatusCodes.Status404NotFound, status);
    }

    [Fact]
    public async Task Show_TmdbApiFailure_FallsThroughToTvdb()
    {
        var handler = CreateHandler(fallbackEnabled: true);
        _resolver.Map[81189] = 1396;
        _tmdb.Exception = new TmdbApiException("TMDB is down", 503);

        var result = await handler.ShowAsync(81189, CancellationToken.None);

        Assert.Equal(1, _passthrough.ShowCallCount);
        var (status, _) = await ExecuteAsync(result);
        Assert.Equal(200, status);
    }

    [Fact]
    public async Task Show_SyntheticId_WithFallbackDisabled_TmdbApiFailure_ReturnsNotFound()
    {
        var handler = CreateHandler(fallbackEnabled: false);
        _tmdb.Exception = new TmdbApiException("TMDB is down", 503);

        var result = await handler.ShowAsync(1000001396, CancellationToken.None);

        Assert.Equal(0, _passthrough.ShowCallCount);
        var (status, _) = await ExecuteAsync(result);
        Assert.Equal(StatusCodes.Status404NotFound, status);
    }

    [Fact]
    public async Task Show_OverrideTvdb_ForcesPassthroughEvenWhenMappingExists()
    {
        var (handler, mapping) = CreateHandlerWithMapping(fallbackEnabled: true);
        mapping.SetOverride(TestData.BreakingBadTvdbId, MappingStore.SourceTvdb);
        _resolver.Map[TestData.BreakingBadTvdbId] = TestData.BreakingBadTmdbId;
        _tmdb.Details = TestData.BreakingBadDetails();

        var result = await handler.ShowAsync(TestData.BreakingBadTvdbId, CancellationToken.None);

        Assert.Equal(1, _passthrough.ShowCallCount);
        Assert.Equal(0, _resolver.CallCount);
        var (status, body) = await ExecuteAsync(result);
        Assert.Equal(200, status);
        Assert.Contains("fallback", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Show_OverrideTmdb_WithMapping_UsesTmdbNotPassthrough()
    {
        var (handler, mapping) = CreateHandlerWithMapping(fallbackEnabled: true);
        mapping.SetOverride(TestData.BreakingBadTvdbId, MappingStore.SourceTmdb);
        mapping.RegisterSeries(TestData.BreakingBadTvdbId, TestData.BreakingBadTmdbId);
        _tmdb.Details = TestData.BreakingBadDetails();
        _tmdb.Seasons[1] = TestData.SeasonOneEpisodes();

        var result = await handler.ShowAsync(TestData.BreakingBadTvdbId, CancellationToken.None);

        Assert.Equal(0, _passthrough.ShowCallCount);
        Assert.Equal(0, _resolver.CallCount);
        var (status, _) = await ExecuteAsync(result);
        Assert.Equal(StatusCodes.Status200OK, status);
    }

    [Fact]
    public async Task Show_OverrideTmdb_NoMapping_FallsThroughToTvdbWhenEnabled()
    {
        var (handler, mapping) = CreateHandlerWithMapping(fallbackEnabled: true);
        mapping.SetOverride(999999, MappingStore.SourceTmdb);

        var result = await handler.ShowAsync(999999, CancellationToken.None);

        Assert.Equal(1, _passthrough.ShowCallCount);
        Assert.Equal(1, _resolver.CallCount);
        var (status, _) = await ExecuteAsync(result);
        Assert.Equal(200, status);
    }

    [Fact]
    public async Task Show_OverrideTmdb_NoMapping_ReturnsNotFoundWhenFallbackDisabled()
    {
        var (handler, mapping) = CreateHandlerWithMapping(fallbackEnabled: false);
        mapping.SetOverride(999999, MappingStore.SourceTmdb);

        var result = await handler.ShowAsync(999999, CancellationToken.None);

        Assert.Equal(0, _passthrough.ShowCallCount);
        var (status, _) = await ExecuteAsync(result);
        Assert.Equal(StatusCodes.Status404NotFound, status);
    }

    [Fact]
    public async Task Show_OverrideTmdb_MetadataSourceTvdb_StillUsesTmdb()
    {
        var (handler, mapping) = CreateHandlerWithMapping(fallbackEnabled: true, source: "tvdb");
        mapping.SetOverride(TestData.BreakingBadTvdbId, MappingStore.SourceTmdb);
        mapping.RegisterSeries(TestData.BreakingBadTvdbId, TestData.BreakingBadTmdbId);
        _tmdb.Details = TestData.BreakingBadDetails();
        _tmdb.Seasons[1] = TestData.SeasonOneEpisodes();

        var result = await handler.ShowAsync(TestData.BreakingBadTvdbId, CancellationToken.None);

        Assert.Equal(0, _passthrough.ShowCallCount);
        Assert.Equal(0, _resolver.CallCount);
        var (status, _) = await ExecuteAsync(result);
        Assert.Equal(StatusCodes.Status200OK, status);
    }

    [Fact]
    public async Task Show_NoOverride_MetadataSourceTvdb_FallsThroughToPassthrough()
    {
        var (handler, mapping) = CreateHandlerWithMapping(fallbackEnabled: true, source: "tvdb");
        mapping.RegisterSeries(TestData.BreakingBadTvdbId, TestData.BreakingBadTmdbId);

        var result = await handler.ShowAsync(TestData.BreakingBadTvdbId, CancellationToken.None);

        Assert.Equal(1, _passthrough.ShowCallCount);
        var (status, _) = await ExecuteAsync(result);
        Assert.Equal(200, status);
    }

    [Fact]
    public async Task Search_TmdbSearchSource_MetadataSourceTvdb_UsesTmdb()
    {
        var (handler, mapping) = CreateHandlerWithMapping(fallbackEnabled: true, source: "tvdb");
        mapping.SetDefaultSearchSource(MappingStore.SourceTmdb);
        _tmdb.SearchResults = new List<Sonarr.MetadataProxy.Models.Tmdb.TmdbTvSearchResult>
        {
            new() { Id = 1396, Name = "Breaking Bad" }
        };

        var result = await handler.SearchAsync("breaking bad", CancellationToken.None);

        Assert.Null(_passthrough.LastSearchTerm);
        var (status, _) = await ExecuteAsync(result);
        Assert.Equal(StatusCodes.Status200OK, status);
    }

    [Fact]
    public async Task Show_OverrideRemoved_RestoresDefaultResolution()
    {
        var (handler, mapping) = CreateHandlerWithMapping(fallbackEnabled: true);
        mapping.SetOverride(TestData.BreakingBadTvdbId, MappingStore.SourceTvdb);
        mapping.RemoveOverride(TestData.BreakingBadTvdbId);
        _resolver.Map[TestData.BreakingBadTvdbId] = TestData.BreakingBadTmdbId;
        _tmdb.Details = TestData.BreakingBadDetails();
        _tmdb.Seasons[1] = TestData.SeasonOneEpisodes();

        var result = await handler.ShowAsync(TestData.BreakingBadTvdbId, CancellationToken.None);

        Assert.Equal(0, _passthrough.ShowCallCount);
        var (status, _) = await ExecuteAsync(result);
        Assert.Equal(StatusCodes.Status200OK, status);
    }

    [Fact]
    public async Task Show_RealTvdb_NoMapping_WithTvdbSearchSourcePreference_UsesTvdbPassthrough()
    {
        var (handler, mapping) = CreateHandlerWithMapping(fallbackEnabled: true);
        mapping.SetDefaultSearchSource(MappingStore.SourceTvdb);
        _resolver.Map[81189] = 1396;
        _tmdb.Details = TestData.BreakingBadDetails();
        _tmdb.Seasons[1] = TestData.SeasonOneEpisodes();

        var result = await handler.ShowAsync(81189, CancellationToken.None);

        Assert.Equal(1, _passthrough.ShowCallCount);
        Assert.Equal(0, _tmdb.DetailsCallCount);
        Assert.Equal(0, _resolver.CallCount);
        Assert.Equal(MappingStore.SourceTvdb, mapping.GetOverride(81189));
    }

    [Fact]
    public async Task Show_RealTvdb_NoMapping_WithDefaultPreference_ReverseMapsToTmdb()
    {
        var (handler, mapping) = CreateHandlerWithMapping(fallbackEnabled: true);
        mapping.SetDefaultSearchSource("");
        _resolver.Map[81189] = 1396;
        _tmdb.Details = TestData.BreakingBadDetails();
        _tmdb.Seasons[1] = TestData.SeasonOneEpisodes();

        var result = await handler.ShowAsync(81189, CancellationToken.None);

        Assert.Equal(0, _passthrough.ShowCallCount);
        Assert.True(_tmdb.DetailsCallCount > 0);
        Assert.Equal(1, _resolver.CallCount);
        var (status, _) = await ExecuteAsync(result);
        Assert.Equal(StatusCodes.Status200OK, status);
    }

    [Fact]
    public async Task Show_RealTvdb_ExistingMapping_WithTvdbSearchSourcePreference_KeepsTmdbMapping()
    {
        var (handler, mapping) = CreateHandlerWithMapping(fallbackEnabled: true);
        mapping.RegisterSeries(81189, 1396);
        mapping.SetDefaultSearchSource(MappingStore.SourceTvdb);
        _tmdb.Details = TestData.BreakingBadDetails();
        _tmdb.Seasons[1] = TestData.SeasonOneEpisodes();

        var result = await handler.ShowAsync(81189, CancellationToken.None);

        Assert.Equal(0, _passthrough.ShowCallCount);
        Assert.True(_tmdb.DetailsCallCount > 0);
    }

    [Fact]
    public async Task Search_Title_WithTvdbSearchSourcePreference_ForcesTvdbPassthrough()
    {
        var (handler, mapping) = CreateHandlerWithMapping(fallbackEnabled: true);
        mapping.SetDefaultSearchSource(MappingStore.SourceTvdb);
        _tmdb.SearchResults = new List<Sonarr.MetadataProxy.Models.Tmdb.TmdbTvSearchResult>
        {
            new() { Id = 1396, Name = "Breaking Bad" }
        };

        var result = await handler.SearchAsync("breaking bad", CancellationToken.None);

        Assert.Equal("breaking bad", _passthrough.LastSearchTerm);
        Assert.Equal(0, _tmdb.SearchCallCount);
        var (status, _) = await ExecuteAsync(result);
        Assert.Equal(StatusCodes.Status200OK, status);
    }

    [Fact]
    public async Task Search_Title_WithTmdbSearchSourcePreference_UsesActiveProvider()
    {
        var (handler, mapping) = CreateHandlerWithMapping(fallbackEnabled: true);
        mapping.SetDefaultSearchSource(MappingStore.SourceTmdb);
        _tmdb.SearchResults = new List<Sonarr.MetadataProxy.Models.Tmdb.TmdbTvSearchResult>
        {
            new() { Id = 1396, Name = "Breaking Bad" }
        };
        _tmdb.Details = TestData.BreakingBadDetails();

        var result = await handler.SearchAsync("breaking bad", CancellationToken.None);

        Assert.True(_tmdb.SearchCallCount > 0);
        Assert.Null(_passthrough.LastSearchTerm);
        var (status, _) = await ExecuteAsync(result);
        Assert.Equal(StatusCodes.Status200OK, status);
    }

    [Fact]
    public async Task Search_Title_WithTvdbSearchSourcePreference_EmptyResultsStaysOnTvdb()
    {
        var (handler, mapping) = CreateHandlerWithMapping(fallbackEnabled: true);
        mapping.SetDefaultSearchSource(MappingStore.SourceTvdb);

        var result = await handler.SearchAsync("totally nonexistent show", CancellationToken.None);

        Assert.Equal("totally nonexistent show", _passthrough.LastSearchTerm);
        var (status, _) = await ExecuteAsync(result);
        Assert.Equal(StatusCodes.Status200OK, status);
    }

    [Fact]
    public async Task Search_ExplicitTvdbPrefix_OverridesSearchSourcePreference()
    {
        var (handler, mapping) = CreateHandlerWithMapping(fallbackEnabled: true);
        mapping.SetDefaultSearchSource(MappingStore.SourceTmdb);
        _tmdb.SearchResults = new List<Sonarr.MetadataProxy.Models.Tmdb.TmdbTvSearchResult>
        {
            new() { Id = 1396, Name = "Breaking Bad" }
        };

        await handler.SearchAsync("tvdb:breaking bad", CancellationToken.None);

        Assert.Equal("breaking bad", _passthrough.LastSearchTerm);
        Assert.Equal(0, _tmdb.SearchCallCount);
    }

    private MetadataRequestHandler CreateHandler(bool fallbackEnabled, string source = "tmdb")
    {
        return CreateHandlerWithMapping(fallbackEnabled, source).Handler;
    }

    private (MetadataRequestHandler Handler, MappingStore Mapping) CreateHandlerWithMapping(
        bool fallbackEnabled,
        string source = "tmdb",
        AniListSearchService? aniList = null,
        MalSearchService? mal = null)
    {
        var options = new ProxyOptions
        {
            MetadataSource = source,
            TmdbApiKey = "test-key",
            EnableTvdbFallback = fallbackEnabled,
            DataDir = _dataDir
        };

        var mapping = new MappingStore(options, NullLogger<MappingStore>.Instance);
        var translator = new SkyHookTranslator(mapping, NullLogger<SkyHookTranslator>.Instance);
        var providerSource = new DummyServiceProvider(_tmdb, options);
        var activeProvider = MetadataProviderRegistry.Create(source, providerSource);
        var tmdbProvider = new TmdbMetadataProvider(_tmdb, options, NullLogger<TmdbMetadataProvider>.Instance);

        var handler = new MetadataRequestHandler(
            options,
            mapping,
            _resolver,
            _passthrough,
            translator,
            aniList,
            mal,
            null,
            null,
            activeProvider,
            tmdbProvider,
            NullLogger<MetadataRequestHandler>.Instance);

        return (handler, mapping);
    }

    private static async Task<(int Status, string Body)> ExecuteAsync(IResult result)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.RequestServices = TestServiceProvider;
        await result.ExecuteAsync(context);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync();
        return (context.Response.StatusCode, body);
    }

    private static IServiceProvider CreateTestServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IOptions<JsonOptions>>(
            new OptionsWrapper<JsonOptions>(new JsonOptions()));
        return services.BuildServiceProvider();
    }

    private sealed class DummyServiceProvider : IServiceProvider
    {
        private readonly FakeTmdbApi _tmdb;
        private readonly ProxyOptions _options;

        public DummyServiceProvider(FakeTmdbApi tmdb, ProxyOptions options)
        {
            _tmdb = tmdb;
            _options = options;
        }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(ITmdbApi))
            {
                return _tmdb;
            }

            if (serviceType == typeof(TmdbMetadataProvider))
            {
                return new TmdbMetadataProvider(_tmdb, _options, NullLogger<TmdbMetadataProvider>.Instance);
            }

            return null;
        }
    }
}