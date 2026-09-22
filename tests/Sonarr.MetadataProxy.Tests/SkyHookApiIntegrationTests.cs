using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sonarr.MetadataProxy.Models.AniList;
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
    public async Task Search_AniListSearchSourcePreference_ReturnsMappedResults()
    {
        WriteAniListFixtures();
        var aniList = new FakeAniListApi { SearchResults = new List<AniListMedia> { TestData.DeathNote() } };

        using var factory = CreateFactory(new FakeTmdbApi(), aniList: aniList);
        using var client = factory.CreateClient();

        using var set = await client.PostAsJsonAsync("/api/overrides/searchsource", new { source = "anilist" });
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);

        using var response = await client.GetAsync("/v1/tvdb/search/en/?term=death+note");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var first = Assert.Single(document.RootElement.EnumerateArray());

        Assert.Equal(81356, first.GetProperty("tvdbId").GetInt32());
        Assert.Equal("Death Note", first.GetProperty("title").GetString());
        Assert.Equal("ended", first.GetProperty("status").GetString());
        Assert.Equal(1, aniList.SearchCallCount);

        var anilistIds = first.GetProperty("aniListIds").EnumerateArray().Select(x => x.GetInt32()).ToList();
        Assert.Contains(1535, anilistIds);

        var malIds = first.GetProperty("malIds").EnumerateArray().Select(x => x.GetInt32()).ToList();
        Assert.Contains(1535, malIds);
    }

    [Fact]
    public async Task Search_AniListDuplicateTvdbMappings_DeduplicatesResults()
    {
        WriteAniListFixtures();
        var duplicate = new AniListMedia
        {
            Id = 9000,
            IdMal = 9000,
            TitleEnglish = "Death Note (Special)"
        };
        var aniList = new FakeAniListApi
        {
            SearchResults = new List<AniListMedia> { TestData.DeathNote(), duplicate }
        };

        using var factory = CreateFactory(new FakeTmdbApi(), aniList: aniList);
        using var client = factory.CreateClient();
        using var set = await client.PostAsJsonAsync("/api/overrides/searchsource", new { source = "anilist" });
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);

        using var response = await client.GetAsync("/v1/tvdb/search/en/?term=death+note");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var first = Assert.Single(document.RootElement.EnumerateArray());

        Assert.Equal(81356, first.GetProperty("tvdbId").GetInt32());
        Assert.Equal("Death Note", first.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Search_AniListIdTerm_ReturnsMappedResult()
    {
        WriteAniListFixtures();
        var aniList = new FakeAniListApi
        {
            ById = { [1535] = TestData.DeathNote() }
        };

        using var factory = CreateFactory(new FakeTmdbApi(), aniList: aniList);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/v1/tvdb/search/en/?term=anilist%3A1535");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var first = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(81356, first.GetProperty("tvdbId").GetInt32());
    }

    [Fact]
    public async Task Search_MalIdTerm_ReturnsMappedResult()
    {
        WriteAniListFixtures();
        var aniList = new FakeAniListApi
        {
            ById = { [1535] = TestData.DeathNote() }
        };

        using var factory = CreateFactory(new FakeTmdbApi(), aniList: aniList);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/v1/tvdb/search/en/?term=mal%3A1535");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var first = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(81356, first.GetProperty("tvdbId").GetInt32());
    }

    [Fact]
    public async Task Search_AniListApiFailure_FallsBackToTvdb()
    {
        WriteAniListFixtures();
        var aniList = new FakeAniListApi
        {
            SearchResults = new List<AniListMedia> { TestData.DeathNote() },
            Exception = new AniListApiException("AniList is down", new Exception("boom"))
        };
        var passthrough = new FakeSkyHookPassthrough
        {
            SearchResponse = new Sonarr.MetadataProxy.Passthrough.ProxyResponse(200, "application/json", "[]")
        };

        using var factory = CreateFactory(new FakeTmdbApi(), aniList: aniList, passthrough: passthrough);
        using var client = factory.CreateClient();

        using var set = await client.PostAsJsonAsync("/api/overrides/searchsource", new { source = "anilist" });
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);

        using var response = await client.GetAsync("/v1/tvdb/search/en/?term=death+note");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("[]", body);
        Assert.Equal("death note", passthrough.LastSearchTerm);
    }

    [Fact]
    public async Task Search_AniListResultWithoutMapping_FallsBackToTvdb()
    {
        WriteAniListFixtures();
        var unmapped = new AniListMedia
        {
            Id = 701,
            IdMal = 701,
            TitleEnglish = "No TVDB link here"
        };
        var aniList = new FakeAniListApi { SearchResults = new List<AniListMedia> { unmapped } };
        var passthrough = new FakeSkyHookPassthrough
        {
            SearchResponse = new Sonarr.MetadataProxy.Passthrough.ProxyResponse(200, "application/json", "[]")
        };

        using var factory = CreateFactory(new FakeTmdbApi(), aniList: aniList, passthrough: passthrough);
        using var client = factory.CreateClient();
        using var set = await client.PostAsJsonAsync("/api/overrides/searchsource", new { source = "anilist" });

        using var response = await client.GetAsync("/v1/tvdb/search/en/?term=unmapped");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("[]", body);
        Assert.Equal("unmapped", passthrough.LastSearchTerm);
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
        Assert.Equal(HttpStatusCode.OK, setResponse.StatusCode);

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
        Assert.Equal(HttpStatusCode.OK, setResponse.StatusCode);

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
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);

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

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("anilist")]
    [InlineData("singleseason")]
    public async Task Override_SecondarySources_AreAcceptedAndListed(string source)
    {
        using var factory = CreateFactory(new FakeTmdbApi());
        using var client = factory.CreateClient();

        using var post = await client.PostAsJsonAsync("/api/overrides", new { tvdbId = 81189, source });
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);

        var overrides = await client.GetStringAsync("/api/overrides");
        using var document = JsonDocument.Parse(overrides);
        var entries = document.RootElement.EnumerateArray()
            .Select(e => (tvdbId: e.GetProperty("tvdbId").GetInt32(),
                          source: e.GetProperty("source").GetString()))
            .ToList();
        Assert.Contains((81189, source), entries);
    }

    [Fact]
    public async Task Show_SingleSeasonOverride_FlattensMappedSeasonsIntoOne()
    {
        var tmdb = new FakeTmdbApi
        {
            DetailsById = { [TestData.BreakingBadTmdbId] = TestData.BreakingBadDetails() },
            Seasons =
            {
                [1] = TestData.SeasonOneEpisodes(),
                [2] = TestData.SeasonTwoEpisodes()
            }
        };
        var resolver = new FakeTvdbResolver { Map = { [TestData.BreakingBadTvdbId] = TestData.BreakingBadTmdbId } };

        using var factory = CreateFactory(tmdb, resolver: resolver);
        using var client = factory.CreateClient();

        using var set = await client.PostAsJsonAsync("/api/overrides", new { tvdbId = 81189, source = "singleseason" });
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);

        var body = await client.GetStringAsync("/v1/tvdb/shows/en/81189");
        using var document = JsonDocument.Parse(body);

        var episodes = document.RootElement.GetProperty("episodes").EnumerateArray().ToList();
        Assert.Equal(3, episodes.Count);
        Assert.All(episodes, episode => Assert.Equal(1, episode.GetProperty("seasonNumber").GetInt32()));
        Assert.Equal(new[] { 1, 2, 3 }, episodes.Select(e => e.GetProperty("episodeNumber").GetInt32()));
        Assert.Equal(new[] { 1, 2, 3 }, episodes.Select(e => e.GetProperty("absoluteEpisodeNumber").GetInt32()));

        var seasons = document.RootElement.GetProperty("seasons").EnumerateArray().ToList();
        Assert.Equal(new[] { 1 }, seasons.Select(season => season.GetProperty("seasonNumber").GetInt32()));
    }

    [Fact]
    public async Task Show_SingleSeasonOverride_FlattensPassthroughEpisodesIntoOne()
    {
        var passthrough = new FakeSkyHookPassthrough
        {
            ShowResponse = new Sonarr.MetadataProxy.Passthrough.ProxyResponse(200, "application/json", """
            {
              "tvdbId": 81189,
              "title": "One Piece",
              "seasons": [],
              "episodes": [
                { "tvdbId": 1, "seasonNumber": 0, "episodeNumber": 1, "title": "Spec", "airDateUtc": "1998-07-26T14:15:00Z" },
                { "tvdbId": 2, "seasonNumber": 1, "episodeNumber": 1, "title": "Ep1", "airDateUtc": "1999-10-20T14:15:00Z", "absoluteEpisodeNumber": 1 },
                { "tvdbId": 3, "seasonNumber": 2, "episodeNumber": 1, "title": "Ep50", "airDateUtc": "1999-03-13T14:15:00Z", "absoluteEpisodeNumber": 50 },
                { "tvdbId": 4, "seasonNumber": 2, "episodeNumber": 2, "title": "Ep51", "airDateUtc": "1999-03-20T14:15:00Z", "absoluteEpisodeNumber": 51 }
              ]
            }
            """)
        };

        using var factory = CreateFactory(new FakeTmdbApi(), passthrough: passthrough);
        using var client = factory.CreateClient();

        using var set = await client.PostAsJsonAsync("/api/overrides", new { tvdbId = 81189, source = "singleseason" });
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);

        var body = await client.GetStringAsync("/v1/tvdb/shows/en/81189");
        using var document = JsonDocument.Parse(body);

        var episodes = document.RootElement.GetProperty("episodes").EnumerateArray().ToList();
        Assert.Equal(4, episodes.Count);
        Assert.Equal(0, episodes[0].GetProperty("seasonNumber").GetInt32());

        var regular = episodes.Skip(1).ToList();
        Assert.All(regular, e => Assert.Equal(1, e.GetProperty("seasonNumber").GetInt32()));
        Assert.Equal(new[] { 1, 2, 3 }, regular.Select(e => e.GetProperty("episodeNumber").GetInt32()));
        Assert.Equal(new[] { 1, 50, 51 }, regular.Select(e => e.GetProperty("absoluteEpisodeNumber").GetInt32()));
    }

    [Fact]
    public async Task Show_RealTvdb_NoMapping_WithTvdbSearchSourcePreference_SkipsReverseMapping()
    {
        var resolver = new FakeTvdbResolver { Map = { [81189] = 1396 } };
        var passthrough = new FakeSkyHookPassthrough
        {
            ShowResponse = new Sonarr.MetadataProxy.Passthrough.ProxyResponse(
                200,
                "application/json",
                "{\"tvdbId\":81189,\"title\":\"from-tvdb\"}")
        };

        using var factory = CreateFactory(new FakeTmdbApi(), resolver: resolver, passthrough: passthrough);
        using var client = factory.CreateClient();

        using var set = await client.PostAsJsonAsync("/api/overrides/searchsource", new { source = "tvdb" });
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);

        var body = await client.GetStringAsync("/v1/tvdb/shows/en/81189");

        Assert.Contains("from-tvdb", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, resolver.CallCount);

        var overrides = await client.GetStringAsync("/api/overrides");
        using var document = JsonDocument.Parse(overrides);
        var entries = document.RootElement.EnumerateArray()
            .Select(e => (tvdbId: e.GetProperty("tvdbId").GetInt32(),
                          source: e.GetProperty("source").GetString()))
            .ToList();
        Assert.Contains((81189, "tvdb"), entries);
    }

    private void WriteAniListFixtures()
    {
        var dir = Path.Combine(_dataDir, "datamaps");
        Directory.CreateDirectory(dir);

        File.WriteAllText(Path.Combine(dir, "anime.json"), """
        [
          { "name": "Death Note", "name_cn": "", "name_jp": "", "idAL": 1535, "idAniDB": 2993, "idMal": 1535 },
          { "name": "Death Note (Special)", "name_cn": "", "name_jp": "", "idAL": 9000, "idAniDB": 2993, "idMal": 9000 }
        ]
        """);

        File.WriteAllText(Path.Combine(dir, "anime-list-full.xml"), """
        <anime-list>
          <anime anidbid="2993" tvdbid="81356" defaulttvdbseason="1" episodeoffset="" lastupdate="1700000000" />
        </anime-list>
        """);
    }

    private WebApplicationFactory<Program> CreateFactory(
        FakeTmdbApi tmdb,
        FakeTvdbResolver? resolver = null,
        FakeSkyHookPassthrough? passthrough = null,
        FakeAniListApi? aniList = null,
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
                    services.RemoveAll<IAniListApi>();
                    services.AddSingleton<ITmdbApi>(tmdb);
                    services.AddSingleton<ITvdbToTmdbResolver>(resolver ?? new FakeTvdbResolver());
                    services.AddSingleton<ISkyHookPassthrough>(passthrough ?? new FakeSkyHookPassthrough());
                    services.AddSingleton<IAniListApi>(aniList ?? new FakeAniListApi());
                });
            });
    }
}