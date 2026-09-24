using Microsoft.Extensions.Logging.Abstractions;
using Sonarr.MetadataProxy.Mapping;
using Sonarr.MetadataProxy.Models.Metadata;
using Sonarr.MetadataProxy.Options;
using Sonarr.MetadataProxy.Translation;
using Xunit;

namespace Sonarr.MetadataProxy.Tests;

public class SkyHookTranslatorTests : IDisposable
{
    private readonly string _dataDir;
    private readonly SkyHookTranslator _translator;

    public SkyHookTranslatorTests()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), "metadataproxy-tests", Guid.NewGuid().ToString("N"));
        var mapping = new MappingStore(
            new ProxyOptions { DataDir = _dataDir },
            NullLogger<MappingStore>.Instance);
        _translator = new SkyHookTranslator(mapping, NullLogger<SkyHookTranslator>.Instance);
    }

    [Fact]
    public void ToFullSeries_UsesRealTvdbIdWhenPresent()
    {
        var metadata = SeriesWithExternalIds(81189, "tt0903747");

        var show = _translator.ToFullSeries(metadata, Array.Empty<SeasonMetadata>());

        Assert.Equal(81189, show.TvdbId);
        Assert.Equal(1396, show.TmdbId);
        Assert.Equal("tt0903747", show.ImdbId);
    }

    [Fact]
    public void ToFullSeries_SynthesizesTvdbIdWhenNoMappingExists()
    {
        var metadata = SeriesWithExternalIds(null, null);

        var show = _translator.ToFullSeries(metadata, Array.Empty<SeasonMetadata>());

        Assert.Equal(1000001396, show.TvdbId);
        Assert.True(SyntheticIds.IsSyntheticSeries(show.TvdbId));
        Assert.Equal(1396, SyntheticIds.TryDecomposeSeries(show.TvdbId));
    }

    [Fact]
    public void ToFullSeries_MapsSeasonsAndEpisodesWithStableIds()
    {
        var metadata = SeriesWithExternalIds(null, null);
        var seasons = new List<SeasonMetadata>
        {
            new()
            {
                SeasonNumber = 1,
                Episodes = new List<EpisodeMetadata>
                {
                    new() { EpisodeNumber = 1, Title = "Pilot", Overview = "x", AirDate = "2008-01-20", RuntimeMinutes = 58 },
                    new() { EpisodeNumber = 2, Title = "Second", AirDate = "2008-01-27" }
                }
            }
        };

        var show = _translator.ToFullSeries(metadata, seasons);

        Assert.Equal(2, show.Episodes.Count);
        Assert.All(show.Episodes, episode => Assert.True(episode.TvdbId >= SyntheticIds.MinEpisodeSynthetic));
        Assert.NotEqual(show.Episodes[0].TvdbId, show.Episodes[1].TvdbId);
        Assert.Equal(1, show.Episodes[0].SeasonNumber);
        Assert.Equal(1, show.Episodes[0].EpisodeNumber);
        Assert.Equal("Pilot", show.Episodes[0].Title);
        Assert.Equal("2008-01-20", show.Episodes[0].AirDate);
        Assert.Equal(58, show.Episodes[0].Runtime);
        Assert.Equal(show.TvdbId, show.Episodes[0].TvdbShowId);
    }

    [Fact]
    public void ToSearchResult_ContainsNoEpisodes()
    {
        var metadata = SeriesWithExternalIds(81189, "tt0903747");

        var show = _translator.ToSearchResult(metadata);

        Assert.Empty(show.Episodes);
        Assert.Equal("Breaking Bad", show.Title);
        Assert.Equal("ended", show.Status);
        Assert.Equal("AMC", show.Network);
        Assert.Equal(2, show.Genres.Count);
        Assert.Equal(2, show.Images.Count);
        Assert.Equal("poster", show.Images[0].CoverType);
        Assert.Equal("fanart", show.Images[1].CoverType);
        Assert.Contains("/t/p/w1280/", show.Images[1].Url);
        var posterFile = show.Images[0].Url!.Substring(show.Images[0].Url!.LastIndexOf("/", StringComparison.Ordinal));
        Assert.EndsWith(posterFile, show.Images[1].Url!);
        Assert.Equal(2, show.Seasons.Count);
        Assert.Equal("TV-MA", show.ContentRating);
        Assert.NotNull(show.Slug);
    }

    private static SeriesMetadata SeriesWithExternalIds(int? tvdbId, string? imdbId)
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
            ExternalIds = new ExternalIdSet(tvdbId, imdbId, 1396),
            Seasons =
            {
                new SeasonSummaryMetadata { SeasonNumber = 1, EpisodeCount = 7, PosterPath = "/s1.jpg" },
                new SeasonSummaryMetadata { SeasonNumber = 2, EpisodeCount = 13, PosterPath = "/s2.jpg" }
            }
        };
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