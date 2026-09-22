using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Sonarr.MetadataProxy.Providers;
using Xunit;

namespace Sonarr.MetadataProxy.Tests;

public class AniListClientTests
{
    [Fact]
    public async Task SearchAsync_ParsesMediaAfterTheResponseStreamIsClosed()
    {
        var client = new AniListClient(
            new HttpClient(new StubHandler(GraphQlSearchResponse)),
            NullLogger<AniListClient>.Instance);

        var results = await client.SearchAsync("one piece", CancellationToken.None);

        var first = Assert.Single(results);
        Assert.Equal(21, first.Id);
        Assert.Equal(21, first.IdMal);
        Assert.Equal("ONE PIECE", first.TitleEnglish);
        Assert.Equal("One Piece", first.TitleRomaji);
        Assert.Equal("FINISHED", first.Status);
        Assert.Equal("1999-10-20", first.FirstAirDate);
        Assert.Equal("JP", first.CountryOfOrigin);
        Assert.Equal("Toei Animation", first.Studio);
        Assert.Contains("Action", first.Genres);
        Assert.Equal(27, first.Episodes?.GetValueOrDefault()); // fixture only, not the real show's count
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNullForMissingMedia()
    {
        var client = new AniListClient(
            new HttpClient(new StubHandler("""{"data":{}}""")),
            NullLogger<AniListClient>.Instance);

        var result = await client.GetByIdAsync(123456789, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task SearchAsync_GraphQlError_ThrowsAniListApiException()
    {
        var client = new AniListClient(
            new HttpClient(new StubHandler("""{"errors":[{"message":"boom"}]}""")),
            NullLogger<AniListClient>.Instance);

        await Assert.ThrowsAsync<AniListApiException>(() => client.SearchAsync("x", CancellationToken.None));
    }

    private const string GraphQlSearchResponse = """
    {
      "data": {
        "Page": {
          "media": [
            {
              "id": 21,
              "idMal": 21,
              "title": { "romaji": "One Piece", "english": "ONE PIECE", "native": "ワンピース" },
              "synonyms": ["One Piece (2006)"],
              "format": "TV",
              "episodes": 27,
              "duration": 24,
              "status": "FINISHED",
              "startDate": { "year": 1999, "month": 10, "day": 20 },
              "endDate": { "year": 2000, "month": 12, "day": 10 },
              "averageScore": 80,
              "description": "A pirate sets out to become the Pirate King.",
              "coverImage": { "extraLarge": "x.jpg", "large": "l.jpg", "medium": "m.jpg" },
              "bannerImage": "b.jpg",
              "genres": ["Action", "Adventure"],
              "countryOfOrigin": "JP",
              "studios": { "nodes": [{ "name": "Toei Animation" }] }
            }
          ]
        }
      }
    }
    """;

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
}