using Sonarr.MetadataProxy.Contracts.SkyHook;
using Sonarr.MetadataProxy.Mapping;
using Sonarr.MetadataProxy.Models.Metadata;

namespace Sonarr.MetadataProxy.Translation;

public sealed class SkyHookTranslator
{
    private const int PosterWidth = 342;
    private const int BackdropWidth = 1280;

    private readonly MappingStore _mapping;
    private readonly ILogger<SkyHookTranslator> _logger;

    public SkyHookTranslator(MappingStore mapping, ILogger<SkyHookTranslator> logger)
    {
        _mapping = mapping;
        _logger = logger;
    }

    public ShowResource ToSearchResult(SeriesMetadata metadata)
    {
        var show = BuildBase(metadata, null);
        show.SeasonTypes = BuildSeasonTypes(show, metadata);
        return show;
    }

    public ShowResource ToFullSeries(
        SeriesMetadata metadata,
        IReadOnlyList<SeasonMetadata> seasons,
        int? overrideTvdbId = null,
        IReadOnlyDictionary<int, (int Season, int Episode)>? episodeIdSource = null)
    {
        var show = BuildBase(metadata, overrideTvdbId);

        foreach (var season in seasons.OrderBy(season => season.SeasonNumber))
        {
            foreach (var episode in season.Episodes)
            {
                var idSource = episode.SourceEpisodeId.HasValue &&
                               episodeIdSource is not null &&
                               episodeIdSource.TryGetValue(episode.SourceEpisodeId.Value, out var official)
                    ? official
                    : (Season: season.SeasonNumber, Episode: episode.EpisodeNumber);

                show.Episodes.Add(BuildEpisode(show.TvdbId, season.SeasonNumber, episode, idSource));
            }
        }

        show.SeasonTypes = BuildSeasonTypes(show, metadata);

        return show;
    }

    private static List<SeasonTypeResource> BuildSeasonTypes(ShowResource show, SeriesMetadata metadata)
    {
        var result = new List<SeasonTypeResource> { BuildOfficialSeasonType(show, metadata) };
        result.AddRange(metadata.SeasonTypes.Select(seasonType => new SeasonTypeResource
        {
            Type = seasonType.Type,
            Name = seasonType.Name,
            SeasonNumbers = seasonType.SeasonNumbers.ToList(),
            EpisodeCount = seasonType.EpisodeCount
        }));

        return result;
    }

    private static SeasonTypeResource BuildOfficialSeasonType(ShowResource show, SeriesMetadata metadata)
    {
        var summaries = metadata.Seasons.Where(season => season.SeasonNumber > 0).ToList();
        List<int> seasonNumbers;
        int episodeCount;

        if (summaries.Count > 0)
        {
            seasonNumbers = summaries.Select(season => season.SeasonNumber).Distinct().OrderBy(number => number).ToList();
            episodeCount = summaries.Sum(season => season.EpisodeCount);
        }
        else
        {
            var regular = show.Episodes.Where(episode => episode.SeasonNumber > 0).ToList();
            if (regular.Count > 0)
            {
                seasonNumbers = regular.Select(episode => episode.SeasonNumber).Distinct().OrderBy(number => number).ToList();
                episodeCount = regular.Count;
            }
            else
            {
                seasonNumbers = show.Seasons.Where(season => season.SeasonNumber > 0).Select(season => season.SeasonNumber).Distinct().OrderBy(number => number).ToList();
                episodeCount = 0;
            }
        }

        return new SeasonTypeResource
        {
            Type = "official",
            Name = "Aired Order",
            SeasonNumbers = seasonNumbers,
            EpisodeCount = episodeCount
        };
    }

private ShowResource BuildBase(SeriesMetadata metadata, int? overrideTvdbId)
    {
        // Priority for TVDB ID:
        // 1. overrideTvdbId (from per-series override)
        // 2. metadata.ExternalIds.TvdbId (from TMDB external_ids)
        // 3. Synthetic ID from TMDB ID (from ProviderId or ExternalIds.TmdbId)
        // 4. 0 (fallback)

        int? effectiveTvdbId = overrideTvdbId ?? metadata.ExternalIds.TvdbId;

        // Determine TMDB ID for synthetic ID generation
        // Only use ProviderId as TMDB ID if it's actually a TMDB ID (from TMDB provider)
        int? tmdbId = metadata.ExternalIds.TmdbId;
        if (!tmdbId.HasValue && metadata.Source == "tmdb" && int.TryParse(metadata.ProviderId, out var parsedProviderId))
        {
            tmdbId = parsedProviderId;
        }

        // If no TVDB ID yet, synthesize from TMDB ID
        if (!effectiveTvdbId.HasValue && tmdbId.HasValue)
        {
            effectiveTvdbId = SyntheticIds.SeriesId(tmdbId.Value);
        }

        var finalTvdbId = effectiveTvdbId ?? 0;

        // Register mapping if we have both TVDB and TMDB
        if (metadata.ExternalIds.TvdbId.HasValue && tmdbId.HasValue)
        {
            _mapping.RegisterSeries(metadata.ExternalIds.TvdbId.Value, tmdbId.Value);
            _logger.LogInformation("TVDB mapping found for TMDB {TmdbId}: TVDB {TvdbId}.", tmdbId, metadata.ExternalIds.TvdbId.Value);
        }
        else if (finalTvdbId > 0 && tmdbId.HasValue && !metadata.ExternalIds.TvdbId.HasValue)
        {
            _logger.LogInformation(
                "No TVDB mapping exists for TMDB {TmdbId}. Using synthetic TVDB id {TvdbId}.",
                tmdbId,
                finalTvdbId);
        }
        else if (finalTvdbId == 0)
        {
            _logger.LogWarning("No TVDB ID available for series {Title}.", metadata.Title);
        }

        var show = new ShowResource
        {
            TvdbId = finalTvdbId,
            Title = metadata.Title,
            Overview = metadata.Overview,
            Slug = _mapping.GetOrAddSeriesSlug(finalTvdbId, () => Slugify(metadata.Title, finalTvdbId)),
            OriginalCountry = metadata.OriginalCountryCode,
            OriginalLanguage = metadata.OriginalLanguageCode,
            FirstAired = metadata.FirstAirDate,
            LastAired = metadata.LastAirDate,
            TmdbId = tmdbId,
            ImdbId = metadata.ExternalIds.ImdbId,
            Status = metadata.Status,
            Runtime = metadata.RuntimeMinutes,
            Network = metadata.Network,
            Genres = metadata.Genres,
            ContentRating = metadata.ContentRating,
            LastUpdated = DateTime.UtcNow,
            Rating = new RatingResource
            {
                Count = metadata.VoteCount,
                Value = ToDecimal(metadata.VoteAverage)
            }
        };

        if (!string.IsNullOrWhiteSpace(metadata.OriginalTitle) &&
            !string.Equals(metadata.OriginalTitle, metadata.Title, StringComparison.OrdinalIgnoreCase))
        {
            show.AlternativeTitles.Add(new AlternativeTitleResource { Title = metadata.OriginalTitle });
        }

        show.Actors.AddRange(metadata.Actors.Select(actor => new ActorResource
        {
            Name = actor.Name,
            Character = actor.Character,
            Image = actor.ImageUrl
        }));

        AddImage(show.Images, "poster", metadata.PosterPath, PosterWidth);
        AddImage(show.Images, "fanart", metadata.BackdropPath ?? metadata.PosterPath, BackdropWidth);

        show.Seasons.AddRange(metadata.Seasons.Select(season => new SeasonResource
        {
            SeasonNumber = season.SeasonNumber,
            Images = BuildSeasonImages(season)
        }));

        return show;
    }

    private EpisodeResource BuildEpisode(int seriesTvdbId, int seasonNumber, EpisodeMetadata episode, (int Season, int Episode) idSource)
    {
        var episodeTvdbId = _mapping.EpisodeTvdbId(seriesTvdbId, idSource.Season, idSource.Episode);

        return new EpisodeResource
        {
            TvdbId = episodeTvdbId,
            TvdbShowId = seriesTvdbId,
            SeasonNumber = seasonNumber,
            EpisodeNumber = episode.EpisodeNumber,
            AbsoluteEpisodeNumber = episode.AbsoluteEpisodeNumber,
            Title = episode.Title,
            Overview = episode.Overview,
            AirDate = episode.AirDate,
            AirDateUtc = ParseAirDateUtc(episode.AirDate),
            Runtime = episode.RuntimeMinutes,
            Image = episode.ImageUrl,
            Rating = new RatingResource
            {
                Count = episode.VoteCount,
                Value = ToDecimal(episode.VoteAverage)
            }
        };
    }

    private static DateTime? ParseAirDateUtc(string? date)
    {
        if (string.IsNullOrWhiteSpace(date))
        {
            return null;
        }

        return DateTime.TryParseExact(
            date,
            "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out var parsed)
            ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
            : null;
    }

    private static List<ImageResource> BuildSeasonImages(SeasonSummaryMetadata season)
    {
        var images = new List<ImageResource>();
        AddImage(images, "poster", season.PosterPath, PosterWidth);
        return images;
    }

    private static void AddImage(List<ImageResource> images, string coverType, string? path, int width)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var url = path.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? path
            : $"https://image.tmdb.org/t/p/w{width}{path}";

        images.Add(new ImageResource
        {
            CoverType = coverType,
            Url = url
        });
    }

    public static decimal ToDecimal(double value)
    {
        try
        {
            return decimal.Round((decimal)value, 1, MidpointRounding.AwayFromZero);
        }
        catch (OverflowException)
        {
            return 0m;
        }
    }

    public static string Slugify(string title, int tvdbId, int? year = null)
    {
        var slug = System.Text.RegularExpressions.Regex
            .Replace((title ?? string.Empty).ToLowerInvariant().Trim(), "[^a-z0-9]+", "-")
            .Trim('-');

        if (string.IsNullOrWhiteSpace(slug))
        {
            return tvdbId.ToString();
        }

        return year is > 0 ? $"{slug}-{year.Value}" : slug;
    }

    public static int? ParseYear(string? date)
    {
        if (string.IsNullOrWhiteSpace(date) || date.Length < 4)
        {
            return null;
        }

        return int.TryParse(date.AsSpan(0, 4), out var year) ? year : null;
    }
}