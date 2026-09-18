using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sonarr.MetadataProxy.Models.Tmdb;
using Sonarr.MetadataProxy.Options;
using Sonarr.MetadataProxy.Passthrough;
using Sonarr.MetadataProxy.Providers;
using Sonarr.MetadataProxy.Reverse;
using Sonarr.MetadataProxy.Tests.Infrastructure;
using Xunit;

namespace Sonarr.MetadataProxy.Tests;

public class SkyHookApiIntegrationTests
{
    private readonly string _dataDir;

    public SkyHookApiIntegrationTests()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), "metadataproxy-it", Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public async Task Search_ReturnsSonarrCompatibleJsonArray()
    {
        var tmdb = new FakeTmdbApi
        {
            SearchResults = new List<TmdbTvSearchResult> { TestData.BreakingBadSearchResult() },
            Details = TestData.BreakingBadDetails()
        };

        using var factory = CreateFactory(tmdb);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/v1/tvdb/search/en/?term=breaking+bad");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var array = document.RootElement.EnumerateArray().ToList();
        var first = Assert.Single(array);

        Assert.Equal(TestData.BreakingBadTvdbId, first.GetProperty("tvdbId").GetInt32());
        Assert.Equal(TestData.BreakingBadTmdbId, first.GetProperty("tmdbId").GetInt32());
        Assert.Equal("Breaking Bad", first.GetProperty("title").GetString());
        Assert.Equal("ended", first.GetProperty("status").GetString());
        Assert.Equal("2008-01-20", first.GetProperty("firstAired").GetString());
        Assert.Equal("tt0903747", first.GetProperty("imdbId").GetString());
        Assert.True(first.TryGetProperty("seasons", out var seasons) && seasons.ValueKind == JsonValueKind.Array);
        Assert.True(first.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Array);
        Assert.True(first.TryGetProperty("rating", out var rating) && rating.GetProperty("count").GetInt32() == 12345);
    }

    [Fact]
    public async Task Show_BySyntheticTvdbId_ReturnsFullSeriesWithEpisodes()
    {
        var tmdb = new FakeTmdbApi
        {
            Details = TestData.BreakingBadDetails(),
            Seasons =
            {
                [1] = TestData.SeasonOneEpisodes(),
                [2] = TestData.SeasonTwoEpisodes()
            }
        };

        using var factory = CreateFactory(tmdb);
        using var client = factory.CreateClient();

        var syntheticId = 1000001396;
        using var response = await client.GetAsync($"/v1/tvdb/shows/en/{syntheticId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        Assert.Equal(syntheticId, root.GetProperty("tvdbId").GetInt32());
        Assert.Equal(TestData.BreakingBadTmdbId, root.GetProperty("tmdbId").GetInt32());
        Assert.Equal(3, root.GetProperty("episodes").GetArrayLength());
    }

    [Fact]
    public async Task Show_BySyntheticTvdbId_EpisodeIdsAreStableAcrossCalls()
    {
        var tmdb = new FakeTmdbApi
        {
            Details = TestData.BreakingBadDetails(),
            Seasons = { [1] = TestData.SeasonOneEpisodes() }
        };

        using var factory = CreateFactory(tmdb);
        using var client = factory.CreateClient();

        var body1 = JsonDocument.Parse(await client.GetStringAsync("/v1/tvdb/shows/en/1000001396"));
        var body2 = JsonDocument.Parse(await client.GetStringAsync("/v1/tvdb/shows/en/1000001396"));

        var id1 = body1.RootElement.GetProperty("episodes")[0].GetProperty("tvdbId").GetInt32();
        var id2 = body2.RootElement.GetProperty("episodes")[0].GetProperty("tvdbId").GetInt32();

        Assert.Equal(id1, id2);
        Assert.True(id1 >= 180_000_000);
    }

    [Fact]
    public async Task Search_ByTmdbId_ReturnsSingleResult()
    {
        var tmdb = new FakeTmdbApi
        {
            SearchResults = new List<TmdbTvSearchResult> { TestData.BreakingBadSearchResult() },
            Details = TestData.BreakingBadDetails()
        };

        using var factory = CreateFactory(tmdb);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/v1/tvdb/search/en/?term=tmdb%3A1396");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var first = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(TestData.BreakingBadTvdbId, first.GetProperty("tvdbId").GetInt32());
    }

    [Fact]
    public async Task Search_ByImdbId_ReturnsSingleResult()
    {
        var tmdb = new FakeTmdbApi
        {
            SearchResults = new List<TmdbTvSearchResult> { TestData.BreakingBadSearchResult() },
            Details = TestData.BreakingBadDetails()
        };

        using var factory = CreateFactory(tmdb);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/v1/tvdb/search/en/?term=imdb%3Att0903747");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var first = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal("Breaking Bad", first.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Search_AniListTerm_FallsBackToTvdbAndReturnsItsRawResponse()
    {
        var passthrough = new FakeSkyHookPassthrough
        {
            SearchResponse = new Sonarr.MetadataProxy.Passthrough.ProxyResponse(200, "application/json", "[]")
        };

        using var factory = CreateFactory(new FakeTmdbApi(), passthrough: passthrough);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/v1/tvdb/search/en/?term=anilist%3A1535");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("[]", body);
        Assert.Equal("anilist:1535", passthrough.LastSearchTerm);
    }

    [Fact]
    public async Task Show_UnknownTvdbWithoutMapping_PassesThroughToTvdbWhenFallbackEnabled()
    {
        var passthrough = new FakeSkyHookPassthrough
        {
            ShowResponse = new Sonarr.MetadataProxy.Passthrough.ProxyResponse(
                200,
                "application/json",
                "{\"tvdbId\":81189,\"title\":\"from-tvdb\",\"episodes\":[]}")
        };

        using var factory = CreateFactory(new FakeTmdbApi(), passthrough: passthrough);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/v1/tvdb/shows/en/81189");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("from-tvdb", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Show_UnknownTvdbWithoutMapping_Returns404WhenFallbackDisabled()
    {
        using var factory = CreateFactory(new FakeTmdbApi(), fallbackEnabled: false);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/v1/tvdb/shows/en/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Show_RealTvdbWithReverseMapping_ReturnsTranslatedSeries()
    {
        var tmdb = new FakeTmdbApi
        {
            Details = TestData.BreakingBadDetails(),
            Seasons = { [1] = TestData.SeasonOneEpisodes() }
        };
        var resolver = new FakeTvdbResolver { Map = { [81189] = 1396 } };

        using var factory = CreateFactory(tmdb, resolver: resolver);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/v1/tvdb/shows/en/81189");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        Assert.Equal("Breaking Bad", document.RootElement.GetProperty("title").GetString());
        Assert.Equal(1, resolver.CallCount);
    }

    [Fact]
    public async Task Show_TmdbApiFailure_FallsBackToTvdb()
    {
        var tmdb = new FakeTmdbApi
        {
            Details = TestData.BreakingBadDetails(),
            Exception = new TmdbApiException("TMDB is down", 503)
        };
        var resolver = new FakeTvdbResolver { Map = { [81189] = 1396 } };
        var passthrough = new FakeSkyHookPassthrough
        {
            ShowResponse = new Sonarr.MetadataProxy.Passthrough.ProxyResponse(503, "application/json", "{\"error\":\"backend down\"}")
        };

        using var factory = CreateFactory(tmdb, resolver: resolver, passthrough: passthrough);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/v1/tvdb/shows/en/81189");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("backend down", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Search_TmdbApiFailure_FallsBackToTvdb()
    {
        var tmdb = new FakeTmdbApi { Exception = new TmdbApiException("TMDB is down", 503) };
        var passthrough = new FakeSkyHookPassthrough
        {
            SearchResponse = new Sonarr.MetadataProxy.Passthrough.ProxyResponse(503, "application/json", "{\"error\":\"backend down\"}")
        };

        using var factory = CreateFactory(tmdb, passthrough: passthrough);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/v1/tvdb/search/en/?term=breaking+bad");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("backend down", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Show_TmdbApiFailure_WithFallbackDisabled_ReturnsNotFound()
    {
        var tmdb = new FakeTmdbApi
        {
            Details = TestData.BreakingBadDetails(),
            Exception = new TmdbApiException("TMDB is down", 503)
        };
        var resolver = new FakeTvdbResolver { Map = { [81189] = 1396 } };

        using var factory = CreateFactory(tmdb, resolver: resolver, fallbackEnabled: false);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/v1/tvdb/shows/en/81189");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private WebApplicationFactory<Program> CreateFactory(
        FakeTmdbApi tmdb,
        FakeTvdbResolver? resolver = null,
        FakeSkyHookPassthrough? passthrough = null,
        bool fallbackEnabled = true)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, cfg) =>
                {
                    cfg.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["METADATA_SOURCE"] = "tmdb",
                        ["TMDB_API_KEY"] = "test-key",
                        ["ENABLE_TVDB_FALLBACK"] = fallbackEnabled ? "true" : "false",
                        ["DATA_DIR"] = _dataDir,
                        ["SKIP_TLS"] = "true",
                        ["PORT"] = "9697",
                        ["LOG_LEVEL"] = "Warning"
                    });
                });

                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<ProxyOptions>();
                    services.AddSingleton(new ProxyOptions
                    {
                        MetadataSource = "tmdb",
                        TmdbApiKey = "test-key",
                        EnableTvdbFallback = fallbackEnabled,
                        DataDir = _dataDir,
                        SkipTls = true,
                        Port = 9697,
                        LogLevel = "Warning"
                    });

                    services.RemoveAll<ITmdbApi>();
                    services.RemoveAll<ITvdbToTmdbResolver>();
                    services.RemoveAll<ISkyHookPassthrough>();
                    services.AddSingleton<ITmdbApi>(tmdb);
                    services.AddSingleton<ITvdbToTmdbResolver>(resolver ?? new FakeTvdbResolver());
                    services.AddSingleton<ISkyHookPassthrough>(passthrough ?? new FakeSkyHookPassthrough());
                });
            });
    }
}