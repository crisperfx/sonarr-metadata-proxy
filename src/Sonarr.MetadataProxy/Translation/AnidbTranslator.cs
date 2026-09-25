using Sonarr.MetadataProxy.Contracts.SkyHook;
using Sonarr.MetadataProxy.Models.Anidb;
using Sonarr.MetadataProxy.Models.Metadata;
using Sonarr.MetadataProxy.Services;

namespace Sonarr.MetadataProxy.Translation;

public sealed class AnidbTranslator
{
    private const string MainImageBase = "https://cdn.anidb.net/images/main/";

    public const int TypeRegularEpisode = 1;
    public const int TypeSpecial = 2;

    public SeriesMetadata ToSeries(AnidbAnime anime)
    {
        var regularCount = anime.Episodes.Count(episode => episode.Type == TypeRegularEpisode);

        return new SeriesMetadata
        {
            ProviderId = anime.AnidbId.ToString(),
            Title = !string.IsNullOrWhiteSpace(anime.Title) ? anime.Title : string.Empty,
            Overview = anime.Description,
            FirstAirDate = NormalizeDate(anime.StartDate),
            LastAirDate = NormalizeDate(anime.EndDate),
            Status = MapStatus(anime),
            RuntimeMinutes = FirstRuntime(anime),
            VoteAverage = anime.Rating,
            VoteCount = 0,
            Genres = anime.Genres,
            PosterPath = PictureUrl(anime.Picture),
            ExternalIds = new ExternalIdSet(null, null, null),
            Seasons = regularCount > 0
                ? new List<SeasonSummaryMetadata>
                {
                    new() { SeasonNumber = 1, EpisodeCount = regularCount, AirDate = NormalizeDate(anime.StartDate), PosterPath = PictureUrl(anime.Picture) }
                }
                : new List<SeasonSummaryMetadata>()
        };
    }

    public EpisodeMetadata ToEpisode(AnidbEpisode episode)
    {
        return new EpisodeMetadata
        {
            EpisodeNumber = episode.EpisodeNumber,
            AbsoluteEpisodeNumber = episode.Type == TypeRegularEpisode ? episode.EpisodeNumber : null,
            Title = episode.Title,
            AirDate = NormalizeDate(episode.AirDate),
            RuntimeMinutes = episode.LengthMinutes ?? 0,
            ImageUrl = PictureUrl(episode.Picture),
            VoteAverage = episode.Rating,
            VoteCount = 0,
            EpisodeType = MapEpisodeType(episode.Type)
        };
    }

    public ShowResource ToSearchResult(AnidbAnime anime, int tvdbId)
    {
        var title = !string.IsNullOrWhiteSpace(anime.Title) ? anime.Title : "Unknown";

        return new ShowResource
        {
            TvdbId = tvdbId,
            Title = title,
            Overview = anime.Description,
            Slug = Slugify(title, tvdbId),
            FirstAired = NormalizeDate(anime.StartDate),
            LastAired = NormalizeDate(anime.EndDate),
            AnidbId = anime.AnidbId,
            Status = MapStatus(anime),
            Runtime = FirstRuntime(anime),
            Genres = anime.Genres,
            Images = PictureUrl(anime.Picture) is { } posterUrl
                ? new List<ImageResource> { new() { CoverType = "poster", Url = posterUrl } }
                : new List<ImageResource>(),
            LastUpdated = DateTime.UtcNow,
            Rating = new RatingResource
            {
                Count = 0,
                Value = ToDecimal(anime.Rating)
            }
        };
    }

    public ShowResource ToSearchResult(AnidbTitleHit hit, int tvdbId)
    {
        return new ShowResource
        {
            TvdbId = tvdbId,
            Title = hit.Title,
            Slug = Slugify(hit.Title, tvdbId),
            AnidbId = hit.Aid,
            LastUpdated = DateTime.UtcNow,
            Images = new List<ImageResource>(),
            Overview = string.Empty,
            FirstAired = null,
            LastAired = null,
            Status = "ended",
            Runtime = 0,
            Genres = new List<string>(),
            Rating = new RatingResource { Count = 0, Value = 0m }
        };
    }

    private static int? FirstRuntime(AnidbAnime anime)
    {
        var regular = anime.Episodes.FirstOrDefault(episode => episode.Type == TypeRegularEpisode);
        return regular?.LengthMinutes;
    }

    private static string? MapStatus(AnidbAnime anime)
    {
        if (!string.IsNullOrWhiteSpace(anime.EndDate))
        {
            return "ended";
        }

        if (!string.IsNullOrWhiteSpace(anime.StartDate))
        {
            return "continuing";
        }

        return null;
    }

    private static string? MapEpisodeType(int type)
    {
        return type switch
        {
            TypeRegularEpisode => "standard",
            TypeSpecial => "special",
            _ => null
        };
    }

    private static string? PictureUrl(string? picture)
    {
        if (string.IsNullOrWhiteSpace(picture))
        {
            return null;
        }

        return picture.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? picture
            : MainImageBase + picture;
    }

    public static string? NormalizeDate(string? date)
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
            out _)
            ? date
            : null;
    }

    private static string Slugify(string title, int tvdbId)
    {
        var slug = System.Text.RegularExpressions.Regex
            .Replace((title ?? string.Empty).ToLowerInvariant().Trim(), "[^a-z0-9]+", "-")
            .Trim('-');

        return string.IsNullOrWhiteSpace(slug) ? tvdbId.ToString() : slug;
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
}