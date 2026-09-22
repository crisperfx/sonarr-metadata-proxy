using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
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
    private static readonly ConcurrentQueue<string> CapturedLogs = new();
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

    [Fact]
    public async Task SetSearchSource_ThenPlainTermSearch_RoutesToTvdbPassthrough()
    {
        var passthrough = new FakeSkyHookPassthrough
        {
            SearchResponse = new Sonarr.MetadataProxy.Passthrough.ProxyResponse(200, "application/json", "[]")
        };

        using var factory = CreateFactory(new FakeTmdbApi(), passthrough: passthrough);
        using var client = factory.CreateClient();

        using var setResponse = await client.PostAsJsonAsync("/api/overrides/searchsource", new { source = "tvdb" });
        Assert.True(HttpStatusCode.OK == setResponse.StatusCode, LogDump());

        using var searchResponse = await client.GetAsync("/v1/tvdb/search/en/?term=breaking+bad");

        Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);
        Assert.Equal("breaking bad", passthrough.LastSearchTerm);
    }

    [Fact]
    public async Task SetSearchSource_ThenPlainTermSearch_DoesNotQueryTmdb()
    {
        var tmdb = new FakeTmdbApi
        {
            SearchResults = new List<TmdbTvSearchResult> { TestData.BreakingBadSearchResult() },
            Details = TestData.BreakingBadDetails()
        };

        using var factory = CreateFactory(tmdb);
        using var client = factory.CreateClient();

        using var setResponse = await client.PostAsJsonAsync("/api/overrides/searchsource", new { source = "tvdb" });
        Assert.True(HttpStatusCode.OK == setResponse.StatusCode, LogDump());

        await client.GetAsync("/v1/tvdb/search/en/?term=breaking+bad");

        Assert.Equal(0, tmdb.SearchCallCount);
    }

    [Fact]
    public async Task SearchSource_GetAndSet_RoundTrips()
    {
        using var factory = CreateFactory(new FakeTmdbApi());
        using var client = factory.CreateClient();

        var get = await client.GetAsync("/api/overrides/searchsource");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var body = await get.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        Assert.Equal("", document.RootElement.GetProperty("source").GetString());

        using var post = await client.PostAsJsonAsync("/api/overrides/searchsource", new { source = "tmdb" });
        Assert.True(HttpStatusCode.OK == post.StatusCode, LogDump());

        var get2 = await client.GetAsync("/api/overrides/searchsource");
        var body2 = await get2.Content.ReadAsStringAsync();
        using var document2 = JsonDocument.Parse(body2);
        Assert.Equal("tmdb", document2.RootElement.GetProperty("source").GetString());
    }

    [Fact]
    public async Task SearchSource_InvalidValue_ReturnsBadRequest()
    {
        using var factory = CreateFactory(new FakeTmdbApi());
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/overrides/searchsource", new { source = "bogus" });

        Assert.True(HttpStatusCode.BadRequest == response.StatusCode, LogDump());
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

                builder.ConfigureLogging(logging =>
                {
                    logging.AddProvider(new CaptureLoggerProvider(CapturedLogs));
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

    private static string LogDump()
    {
        return string.Join(Environment.NewLine, CapturedLogs.ToArray());
    }

    private sealed class CaptureLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _messages;

        public CaptureLoggerProvider(ConcurrentQueue<string> messages)
        {
            _messages = messages;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new CaptureLogger(_messages, categoryName);
        }

        public void Dispose()
        {
        }

        private sealed class CaptureLogger : ILogger
        {
            private readonly ConcurrentQueue<string> _messages;
            private readonly string _category;

            public CaptureLogger(ConcurrentQueue<string> messages, string category)
            {
                _messages = messages;
                _category = category;
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                var text = formatter(state, exception);
                if (exception is not null)
                {
                    text += Environment.NewLine + exception;
                }

                _messages.Enqueue($"[{DateTime.UtcNow:HH:mm:ss.fff}] {logLevel} {_category}: {text}");
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                return null;
            }
        }
    }
}