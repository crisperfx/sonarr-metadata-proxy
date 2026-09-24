using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Sonarr.MetadataProxy.Providers;
using Xunit;

namespace Sonarr.MetadataProxy.Tests;

public class MalClientTests
{
    [Fact]
    public async Task SearchAsync_ParsesMusicAnime()
    {
        var client = new MalClient(
            new HttpClient(new StubHandler(JikanSearchResponse)),
            NullLogger<MalClient>.Instance,
            new NoopRateLimiter());

        var results = await client.SearchAsync("death note", CancellationToken.None);

        var first = Assert.Single(results);
        Assert.Equal(1535, first.Id);
        Assert.Equal("Death Note", first.Title);
        Assert.Equal("Death Note", first.TitleEnglish);
        Assert.Equal("\u30c7\u30b9\u30ce\u30fc\u30c8", first.TitleJapanese);
        Assert.Equal("Finished Airing", first.Status);
        Assert.Equal("2006-10-04", first.FirstAirDate);
        Assert.Equal("2007-06-27", first.LastAirDate);
        Assert.Equal(37, first.Episodes);
        Assert.Equal(23, first.DurationMinutes);
        Assert.Equal(8.6, first.Score);
        Assert.Equal(12345, first.ScoreCount);
        Assert.Equal("Madhouse", first.Studio);
        Assert.Contains("Mystery", first.Genres);
        Assert.Equal("https://cdn.myanimelist.net/images/anime/9/9453l.jpg", first.PosterUrl);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNullForMissingAnime()
    {
        var client = new MalClient(
            new HttpClient(new StubHandler("""{"data":null}""")),
            NullLogger<MalClient>.Instance,
            new NoopRateLimiter());

        var result = await client.GetByIdAsync(123456789, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNullForNotFoundStatus()
    {
        var client = new MalClient(
            new HttpClient(new NotFoundHandler()),
            NullLogger<MalClient>.Instance,
            new NoopRateLimiter());

        var result = await client.GetByIdAsync(999999, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task SearchAsync_ErrorStatus_ThrowsMalApiException()
    {
        var client = new MalClient(
            new HttpClient(new ErrorHandler()),
            NullLogger<MalClient>.Instance,
            new NoopRateLimiter());

        await Assert.ThrowsAsync<MalApiException>(() => client.SearchAsync("x", CancellationToken.None));
    }

    [Fact]
    public async Task SearchAsync_Duration_ParsesHoursAndMinutes()
    {
        var json = """
        {
          "data": [
            {
              "mal_id": 21,
              "title": "One Piece",
              "status": "Currently Airing",
              "duration": "24 min per ep",
              "airing": true,
              "aired": { "from": "1999-10-20T00:00:00+00:00", "to": null },
              "score": 8.76,
              "scored_by": 2288905,
              "synopsis": "Pirates rule the seas.",
              "images": { "jpg": { "large_image_url": "op.jpg" } },
              "genres": [{ "name": "Action" }],
              "studios": [{ "name": "Toei Animation" }],
              "type": "TV"
            },
            {
              "mal_id": 32,
              "title": "Neon Genesis Evangelion",
              "status": "Finished Airing",
              "duration": "1 hr 24 min per ep",
              "aired": { "from": "1995-10-04T00:00:00+00:00", "to": "1996-03-27T00:00:00+00:00" },
              "score": 8.35,
              "scored_by": 1000,
              "images": { "jpg": { "large_image_url": "eva.jpg" } },
              "genres": [{ "name": "Mecha" }],
              "studios": [{ "name": "Gainax" }],
              "type": "TV"
            }
          ]
        }
        """;

        var client = new MalClient(
            new HttpClient(new StubHandler(json)),
            NullLogger<MalClient>.Instance,
            new NoopRateLimiter());

        var results = await client.SearchAsync("one piece", CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.Equal("Currently Airing", results[0].Status);
        Assert.Equal(24, results[0].DurationMinutes);
        Assert.Equal("1999-10-20", results[0].FirstAirDate);
        Assert.Null(results[0].LastAirDate);
        Assert.Equal(84, results[1].DurationMinutes);
    }

    [Fact]
    public async Task SearchAsync_NoDataArray_ReturnsEmptyList()
    {
        var client = new MalClient(
            new HttpClient(new StubHandler("""{"data":[]}""")),
            NullLogger<MalClient>.Instance,
            new NoopRateLimiter());

        var results = await client.SearchAsync("zzz", CancellationToken.None);

        Assert.Empty(results);
    }

    [Fact]
    public async Task GetEpisodesAsync_ParsesMalIdAndAired()
    {
        var json = """
        {
          "data": [
            {
              "mal_id": 62,
              "title": "The First Line of Defense?",
              "aired": "2001-03-21T01:00:00+01:00",
              "duration": 1477,
              "score": 4.17,
              "synopsis": "As the Straw Hats ride down Reverse Mountain..."
            }
          ]
        }
        """;

        var client = new MalClient(
            new HttpClient(new StubHandler(json)),
            NullLogger<MalClient>.Instance,
            new NoopRateLimiter());

        var episodes = await client.GetEpisodesAsync(21, CancellationToken.None);

        var first = Assert.Single(episodes);
        Assert.Equal(0, first.Number);
        Assert.Equal(62, first.AbsoluteNumber);
        Assert.Null(first.SeasonNumber);
        Assert.Equal("2001-03-21", first.AirDate);
    }

    [Fact]
    public async Task GetEpisodesAsync_ParsesSeasonNumber()
    {
        var json = """
        {
          "data": [
            {
              "mal_id": 1,
              "season": 2,
              "episode_number": 3,
              "title": "S2E3"
            }
          ]
        }
        """;

        var client = new MalClient(
            new HttpClient(new StubHandler(json)),
            NullLogger<MalClient>.Instance,
            new NoopRateLimiter());

        var episodes = await client.GetEpisodesAsync(1, CancellationToken.None);

        var first = Assert.Single(episodes);
        Assert.Equal(3, first.Number);
        Assert.Equal(2, first.SeasonNumber);
    }

    private const string JikanSearchResponse = """
    {
      "data": [
        {
          "mal_id": 1535,
          "url": "https://myanimelist.net/anime/1535/Death_Note",
          "images": {
            "jpg": { "large_image_url": "https://cdn.myanimelist.net/images/anime/9/9453l.jpg" }
          },
          "title": "Death Note",
          "title_english": "Death Note",
          "title_japanese": "デスノート",
          "title_synonyms": ["Death Note (2006)"],
          "type": "TV",
          "episodes": 37,
          "status": "Finished Airing",
          "airing": false,
          "aired": {
            "from": "2006-10-04T00:00:00+00:00",
            "to": "2007-06-27T00:00:00+00:00"
          },
          "duration": "23 min per ep",
          "rating": "R - 17+",
          "score": 8.6,
          "scored_by": 12345,
          "synopsis": "A high school student finds a notebook that kills anyone whose name is written in it.",
          "season": "fall",
          "year": 2006,
          "studios": [{ "mal_id": 7, "type": "anime", "name": "Madhouse" }],
          "genres": [{ "mal_id": 7, "type": "anime", "name": "Mystery" }, { "mal_id": 40, "type": "anime", "name": "Psychological" }]
        }
      ]
    }
    """;

    private sealed class NoopRateLimiter : TenraiRateLimiter
    {
        public override Task WaitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _json;

        public StubHandler(string json)
        {
            _json = json;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_json, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    private sealed class NotFoundHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class ErrorHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        }
    }
}