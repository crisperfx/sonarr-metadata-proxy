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
        return BuildBase(metadata, null);
    }

    public ShowResource ToFullSeries(SeriesMetadata metadata, IReadOnlyList<SeasonMetadata> seasons, int? overrideTvdbId = null)
    {
        var show = BuildBase(metadata, overrideTvdbId);

        foreach (var season in seasons.OrderBy(season => season.SeasonNumber))
        {
            foreach (var episode in season.Episodes)
            {
                show.Episodes.Add(BuildEpisode(show.TvdbId, season.SeasonNumber, episode));
            }
        }

        return show;
    }

    private ShowResource BuildBase(SeriesMetadata metadata, int? overrideTvdbId)
    {
        var tmdbId = int.Parse(metadata.ProviderId);
        var effectiveTvdbId = overrideTvdbId ?? metadata.ExternalIds.TvdbId ?? SyntheticIds.SeriesId(tmdbId);

        if (metadata.ExternalIds.TvdbId.HasValue)
        {
            _mapping.RegisterSeries(metadata.ExternalIds.TvdbId.Value, tmdbId);
            _logger.LogInformation("TVDB mapping found for TMDB {TmdbId}: TVDB {TvdbId}.", tmdbId, metadata.ExternalIds.TvdbId.Value);
        }
        else
        {
            _logger.LogInformation(
                "No TVDB mapping exists for TMDB {TmdbId}. Using synthetic TVDB id {TvdbId}.",
                tmdbId,
                effectiveTvdbId);
        }

        var show = new ShowResource
        {
            TvdbId = effectiveTvdbId,
            Title = metadata.Title,
            Overview = metadata.Overview,
            Slug = Slugify(metadata.Title, effectiveTvdbId),
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
        AddImage(show.Images, "fanart", metadata.BackdropPath, BackdropWidth);

        show.Seasons.AddRange(metadata.Seasons.Select(season => new SeasonResource
        {
            SeasonNumber = season.SeasonNumber,
            Images = BuildSeasonImages(season)
        }));

        return show;
    }

    private EpisodeResource BuildEpisode(int seriesTvdbId, int seasonNumber, EpisodeMetadata episode)
    {
        var episodeTvdbId = _mapping.EpisodeTvdbId(seriesTvdbId, seasonNumber, episode.EpisodeNumber);

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

    private static decimal ToDecimal(double value)
    {
        try
        {
            return (decimal)value;
        }
        catch (OverflowException)
        {
            return 0m;
        }
    }

    private static string Slugify(string title, int tvdbId)
    {
        var slug = System.Text.RegularExpressions.Regex
            .Replace((title ?? string.Empty).ToLowerInvariant().Trim(), "[^a-z0-9]+", "-")
            .Trim('-');

        return string.IsNullOrWhiteSpace(slug) ? tvdbId.ToString() : slug;
    }
}