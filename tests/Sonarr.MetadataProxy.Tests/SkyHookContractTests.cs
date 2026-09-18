using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Sonarr.MetadataProxy.Contracts.SkyHook;
using Sonarr.MetadataProxy.Mapping;
using Sonarr.MetadataProxy.Models.Metadata;
using Sonarr.MetadataProxy.Options;
using Sonarr.MetadataProxy.Translation;
using Xunit;

namespace Sonarr.MetadataProxy.Tests;

public class SkyHookContractTests : IDisposable
{
    private readonly string _dataDir;
    private readonly SkyHookTranslator _translator;

    public SkyHookContractTests()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), "metadataproxy-tests", Guid.NewGuid().ToString("N"));
        var mapping = new MappingStore(
            new ProxyOptions { DataDir = _dataDir },
            NullLogger<MappingStore>.Instance);
        _translator = new SkyHookTranslator(mapping, NullLogger<SkyHookTranslator>.Instance);
    }

    [Fact]
    public void FullSeries_ContainsAllFieldsSonarrReads()
    {
        var metadata = BuildMetadata();
        var seasons = new List<SeasonMetadata>
        {
            new()
            {
                SeasonNumber = 1,
                Episodes = new List<EpisodeMetadata>
                {
                    new() { EpisodeNumber = 1, Title = "Pilot", AirDate = "2008-01-20", RuntimeMinutes = 58, Overview = "x", VoteCount = 100, VoteAverage = 8.5 }
                }
            }
        };

        var show = _translator.ToFullSeries(metadata, seasons);
        var json = Serialize(show);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(JsonValueKind.Number, root.GetProperty("tvdbId").ValueKind);
        Assert.Equal(81189, root.GetProperty("tvdbId").GetInt32());
        Assert.Equal("Breaking Bad", root.GetProperty("title").GetString());
        Assert.Equal("2008-01-20", root.GetProperty("firstAired").GetString());
        Assert.Equal("2013-09-29", root.GetProperty("lastAired").GetString());
        Assert.Equal("ended", root.GetProperty("status").GetString());
        Assert.Equal(47, root.GetProperty("runtime").GetInt32());
        Assert.Equal("AMC", root.GetProperty("network").GetString());
        Assert.Equal("TV-MA", root.GetProperty("contentRating").GetString());
        Assert.Equal(1396, root.GetProperty("tmdbId").GetInt32());
        Assert.Equal("tt0903747", root.GetProperty("imdbId").GetString());

        Assert.Equal(JsonValueKind.Array, root.GetProperty("genres").ValueKind);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("actors").ValueKind);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("images").ValueKind);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("seasons").ValueKind);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("episodes").ValueKind);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("malIds").ValueKind);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("aniListIds").ValueKind);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("alternativeTitles").ValueKind);

        var rating = root.GetProperty("rating");
        Assert.True(rating.TryGetProperty("value", out var value));
        Assert.Equal(JsonValueKind.Number, value.ValueKind);
        Assert.Equal(12345, rating.GetProperty("count").GetInt32());

        var season = root.GetProperty("seasons").EnumerateArray().First();
        Assert.Equal(1, season.GetProperty("seasonNumber").GetInt32());
        Assert.Equal(JsonValueKind.Array, season.GetProperty("images").ValueKind);

        var episode = root.GetProperty("episodes").EnumerateArray().First();
        Assert.Equal(JsonValueKind.Number, episode.GetProperty("tvdbId").ValueKind);
        Assert.Equal(1, episode.GetProperty("seasonNumber").GetInt32());
        Assert.Equal(1, episode.GetProperty("episodeNumber").GetInt32());
        Assert.Equal("Pilot", episode.GetProperty("title").GetString());
        Assert.Equal("2008-01-20", episode.GetProperty("airDate").GetString());
    }

    [Fact]
    public void SearchResult_IsJsonArrayCompatibleWithSkyHookSearch()
    {
        var translated = _translator.ToSearchResult(BuildMetadata());
        var json = Serialize(translated);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("Breaking Bad", root.GetProperty("title").GetString());
        Assert.True(root.TryGetProperty("episodes", out _));
    }

    [Fact]
    public void EpisodeDates_FollowSonarrsDateParsingContract()
    {
        var metadata = BuildMetadata();
        var seasons = new List<SeasonMetadata>
        {
            new()
            {
                SeasonNumber = 3,
                Episodes = new List<EpisodeMetadata>
                {
                    new() { EpisodeNumber = 1, Title = "First", AirDate = "2010-03-21" },
                    new() { EpisodeNumber = 2, Title = "Unaired", AirDate = null },
                    new() { EpisodeNumber = 3, Title = "Birim", AirDate = "2010-03-28" }
                }
            }
        };

        var show = _translator.ToFullSeries(metadata, seasons);
        var json = Serialize(show);
        using var document = JsonDocument.Parse(json);

        var episodes = document.RootElement.GetProperty("episodes").EnumerateArray().ToList();
        Assert.Equal("2010-03-21", episodes[0].GetProperty("airDate").GetString());
        Assert.False(episodes[1].TryGetProperty("airDate", out _));
        Assert.Equal("2010-03-28", episodes[2].GetProperty("airDate").GetString());
    }

    private static SeriesMetadata BuildMetadata()
    {
        return new SeriesMetadata
        {
            ProviderId = "1396",
            Title = "Breaking Bad",
            Overview = "A chemistry teacher diagnosed with cancer turns to crime.",
            OriginalTitle = "Breaking Bad",
            FirstAirDate = "2008-01-20",
            LastAirDate = "2013-09-29",
            Status = "ended",
            RuntimeMinutes = 47,
            Network = "AMC",
            OriginalCountryCode = "US",
            OriginalLanguageCode = "en",
            ContentRating = "TV-MA",
            VoteAverage = 8.9,
            VoteCount = 12345,
            Genres = { "Drama", "Crime" },
            PosterPath = "/ggFHVNu6YYI5L9pCfOacjizRGt.jpg",
            ExternalIds = new ExternalIdSet(81189, "tt0903747", 1396),
            Actors =
            {
                new ActorMetadata { Name = "Bryan Cranston", Character = "Walter White", ImageUrl = "https://image.tmdb.org/t/p/w185/cb.jpeg" }
            },
            Seasons =
            {
                new SeasonSummaryMetadata { SeasonNumber = 1, EpisodeCount = 7, PosterPath = "/s1.jpg" },
                new SeasonSummaryMetadata { SeasonNumber = 2, EpisodeCount = 13, PosterPath = "/s2.jpg" }
            }
        };
    }

    private static string Serialize(ShowResource show)
    {
        return JsonSerializer.Serialize(show, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
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