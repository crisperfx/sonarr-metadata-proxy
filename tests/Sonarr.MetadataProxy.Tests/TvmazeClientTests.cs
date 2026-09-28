using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Sonarr.MetadataProxy.Models.Tvmaze;
using Sonarr.MetadataProxy.Providers;
using Xunit;

namespace Sonarr.MetadataProxy.Tests;

public class TvmazeClientTests
{
    [Fact]
    public async Task SearchShowsAsync_ParsesSearchHits()
    {
        var client = new TvmazeClient(
            new HttpClient(new StubHandler(SearchShowsResponse)),
            new NoopRateLimiter(),
            NullLogger<TvmazeClient>.Instance);

        var shows = await client.SearchShowsAsync("breaking bad", CancellationToken.None);

        var show = Assert.Single(shows);
        Assert.Equal(169, show.Id);
        Assert.Equal("Breaking Bad", show.Name);
        Assert.Equal("Ended", show.Status);
        Assert.Equal("2008-01-20", show.Premiered);
        Assert.Equal("AMC", show.Network!.Name);
        Assert.Equal("US", show.Network.Country!.Code);
        Assert.Equal(81189, show.Externals!.TheTvdb);
        Assert.Equal("tt0903747", show.Externals.Imdb);
    }

    [Fact]
    public async Task SearchShowsAsync_EmptyHits_ReturnsEmptyList()
    {
        var client = new TvmazeClient(
            new HttpClient(new StubHandler("[]")),
            new NoopRateLimiter(),
            NullLogger<TvmazeClient>.Instance);

        var shows = await client.SearchShowsAsync("zzz", CancellationToken.None);

        Assert.Empty(shows);
    }

    [Fact]
    public async Task GetShowAsync_ParsesEmbeddedSeasonsAndCast()
    {
        var client = new TvmazeClient(
            new HttpClient(new StubHandler(GetShowResponse)),
            new NoopRateLimiter(),
            NullLogger<TvmazeClient>.Instance);

        var show = await client.GetShowAsync(169, CancellationToken.None);

        Assert.NotNull(show);
        Assert.Single(show!.Embedded!.Seasons);
        Assert.Equal(1, show.Embedded.Seasons[0].Number);
        Assert.Equal("Bryan Cranston", show.Embedded.Cast[0].Person!.Name);
        Assert.Equal("Walter White", show.Embedded.Cast[0].Character!.Name);
    }

    [Fact]
    public async Task GetShowAsync_ReturnsNullForNotFound()
    {
        var client = new TvmazeClient(
            new HttpClient(new NotFoundHandler()),
            new NoopRateLimiter(),
            NullLogger<TvmazeClient>.Instance);

        var show = await client.GetShowAsync(999999, CancellationToken.None);

        Assert.Null(show);
    }

    [Fact]
    public async Task GetEpisodesAsync_ParsesEpisodeFields()
    {
        var client = new TvmazeClient(
            new HttpClient(new StubHandler(GetEpisodesResponse)),
            new NoopRateLimiter(),
            NullLogger<TvmazeClient>.Instance);

        var episodes = await client.GetEpisodesAsync(169, CancellationToken.None);

        var episode = Assert.Single(episodes);
        Assert.Equal("Pilot", episode.Name);
        Assert.Equal(1, episode.Season);
        Assert.Equal(1, episode.Number);
        Assert.Equal("2008-01-20", episode.AirDate);
        Assert.Equal("regular", episode.Type);
    }

    [Fact]
    public async Task FindByImdbAsync_SendsLookupQuery()
    {
        var handler = new RecordingHandler(GetShowResponse);
        var client = new TvmazeClient(
            new HttpClient(handler),
            new NoopRateLimiter(),
            NullLogger<TvmazeClient>.Instance);

        var show = await client.FindByImdbAsync("tt0903747", CancellationToken.None);

        Assert.NotNull(show);
        Assert.Contains("lookup/shows?imdb=tt0903747", handler.LastRequestUri);
    }

    [Fact]
    public async Task SearchShowsAsync_ErrorStatus_ThrowsTvmazeApiException()
    {
        var client = new TvmazeClient(
            new HttpClient(new ErrorHandler()),
            new NoopRateLimiter(),
            NullLogger<TvmazeClient>.Instance);

        await Assert.ThrowsAsync<TvmazeApiException>(() => client.SearchShowsAsync("x", CancellationToken.None));
    }

    private const string SearchShowsResponse = """
    [
      {
        "score": 32.5,
        "show": {
          "id": 169,
          "name": "Breaking Bad",
          "type": "Scripted",
          "language": "English",
          "genres": ["Drama", "Crime", "Thriller"],
          "status": "Ended",
          "runtime": 60,
          "premiered": "2008-01-20",
          "ended": "2013-09-29",
          "summary": "<p>A chemistry teacher turns to manufacturing meth.</p>",
          "rating": { "average": 8.6 },
          "network": { "id": 1, "name": "AMC", "country": { "name": "United States", "code": "US" } },
          "externals": { "imdb": "tt0903747", "thetvdb": 81189, "tvrage": 18164 },
          "image": { "medium": "/m.jpg", "original": "/o.jpg" }
        }
      }
    ]
    """;

    private const string GetShowResponse = """
    {
      "id": 169,
      "name": "Breaking Bad",
      "premiered": "2008-01-20",
      "network": { "id": 1, "name": "AMC", "country": { "name": "United States", "code": "US" } },
      "externals": { "imdb": "tt0903747", "thetvdb": 81189 },
      "_embedded": {
        "seasons": [
          { "id": 1, "number": 1, "name": "Season 1", "episodeOrder": 7, "premiereDate": "2008-01-20" }
        ],
        "cast": [
          { "person": { "id": 1, "name": "Bryan Cranston", "image": { "medium": "/c.jpg", "original": "/co.jpg" } }, "character": { "id": 9, "name": "Walter White" } }
        ]
      }
    }
    """;

    private const string GetEpisodesResponse = """
    [
      { "id": 11336, "name": "Pilot", "season": 1, "number": 1, "type": "regular", "airdate": "2008-01-20", "runtime": 60 }
    ]
    """;

    private sealed class NoopRateLimiter : TvmazeRateLimiter
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

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _json;

        public string? LastRequestUri { get; private set; }

        public RecordingHandler(string json)
        {
            _json = json;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri?.ToString();
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