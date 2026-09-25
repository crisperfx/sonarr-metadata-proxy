using Sonarr.MetadataProxy.Contracts.SkyHook;
using Sonarr.MetadataProxy.Mapping;
using Sonarr.MetadataProxy.Models.Metadata;
using Sonarr.MetadataProxy.Models.Tvmaze;

namespace Sonarr.MetadataProxy.Translation;

public sealed class TvmazeTranslator
{
    public SeriesMetadata ToSeries(TvmazeShow show)
    {
        var countryCode = show.Network?.Country?.Code ?? show.WebChannel?.Country?.Code;

        return new SeriesMetadata
        {
            ProviderId = show.Id.ToString(),
            Title = !string.IsNullOrWhiteSpace(show.Name) ? show.Name : string.Empty,
            Overview = StripHtml(show.Summary),
            FirstAirDate = NormalizeDate(show.Premiered),
            LastAirDate = NormalizeDate(show.Ended),
            Status = MapStatus(show.Status),
            RuntimeMinutes = show.Runtime,
            Network = show.Network?.Name ?? show.WebChannel?.Name,
            OriginalCountryCode = countryCode,
            OriginalLanguageCode = MapLanguage(show.Language),
            VoteAverage = show.Rating?.Average ?? 0,
            VoteCount = 0,
            Genres = show.Genres.Where(genre => !string.IsNullOrWhiteSpace(genre)).ToList(),
            PosterPath = PosterOf(show),
            ExternalIds = new ExternalIdSet(EffectiveTvdbId(show), show.Externals?.Imdb, null),
            Seasons = MapSeasons(show),
            Actors = MapActors(show)
        };
    }

    public EpisodeMetadata ToEpisode(TvmazeEpisode episode)
    {
        return new EpisodeMetadata
        {
            EpisodeNumber = episode.Number ?? 0,
            Title = episode.Name,
            Overview = StripHtml(episode.Summary),
            AirDate = NormalizeDate(episode.AirDate),
            RuntimeMinutes = episode.Runtime,
            ImageUrl = episode.Image?.Original ?? episode.Image?.Medium,
            VoteAverage = episode.Rating?.Average ?? 0,
            VoteCount = 0,
            EpisodeType = episode.Type
        };
    }

    public ShowResource ToSearchResult(TvmazeShow show, int tvdbId)
    {
        var title = !string.IsNullOrWhiteSpace(show.Name) ? show.Name : "Unknown";

        return new ShowResource
        {
            TvdbId = tvdbId,
            Title = title,
            Overview = StripHtml(show.Summary),
            Slug = Slugify(title, tvdbId),
            OriginalCountry = show.Network?.Country?.Code ?? show.WebChannel?.Country?.Code,
            OriginalLanguage = MapLanguage(show.Language),
            FirstAired = NormalizeDate(show.Premiered),
            LastAired = NormalizeDate(show.Ended),
            TvRageId = show.Externals?.TvRage,
            TvMazeId = show.Id,
            ImdbId = show.Externals?.Imdb,
            Status = MapStatus(show.Status),
            Runtime = show.Runtime,
            Network = show.Network?.Name ?? show.WebChannel?.Name,
            Genres = show.Genres.Where(genre => !string.IsNullOrWhiteSpace(genre)).ToList(),
            LastUpdated = DateTime.UtcNow,
            Rating = new RatingResource
            {
                Count = 0,
                Value = ToDecimal(show.Rating?.Average ?? 0)
            }
        };
    }

    private static int EffectiveTvdbId(TvmazeShow show)
    {
        if (show.Externals?.TheTvdb is > 0)
        {
            return show.Externals.TheTvdb.Value;
        }

        return show.Id > 0 ? SyntheticIds.TvmazeSeriesId(show.Id) : 0;
    }

    private static List<SeasonSummaryMetadata> MapSeasons(TvmazeShow show)
    {
        var seasons = new List<SeasonSummaryMetadata>();
        foreach (var season in show.Embedded?.Seasons ?? new List<TvmazeSeason>())
        {
            if (season.Number is not (>= 0))
            {
                continue;
            }

            seasons.Add(new SeasonSummaryMetadata
            {
                SeasonNumber = season.Number.Value,
                EpisodeCount = season.EpisodeOrder ?? 0,
                AirDate = NormalizeDate(season.PremiereDate),
                PosterPath = season.Image?.Original ?? season.Image?.Medium
            });
        }

        return seasons;
    }

    private static List<ActorMetadata> MapActors(TvmazeShow show)
    {
        return (show.Embedded?.Cast ?? new List<TvmazeCast>())
            .Take(20)
            .Select(cast => new ActorMetadata
            {
                Name = cast.Person?.Name,
                Character = cast.Character?.Name,
                ImageUrl = cast.Person?.Image?.Original ?? cast.Person?.Image?.Medium
            })
            .ToList();
    }

    private static string? PosterOf(TvmazeShow show)
    {
        return show.Image?.Original ?? show.Image?.Medium;
    }

    public static string? MapStatus(string? status)
    {
        return (status ?? string.Empty).ToLowerInvariant() switch
        {
            "running" or "in production" => "continuing",
            "ended" or "canceled" or "cancelled" => "ended",
            "to be determined" or "in development" or "planned" => "upcoming",
            _ => null
        };
    }

    public static string? MapLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        return language.ToLowerInvariant() switch
        {
            "english" => "en",
            "japanese" => "ja",
            "french" => "fr",
            "german" => "de",
            "spanish" => "es",
            "italian" => "it",
            "portuguese" => "pt",
            "dutch" => "nl",
            "flemish" => "nl",
            "korean" => "ko",
            "chinese" or "mandarin" or "cantonese" => "zh",
            "russian" => "ru",
            "polish" => "pl",
            "swedish" => "sv",
            "norwegian" => "no",
            "danish" => "da",
            "finnish" => "fi",
            "turkish" => "tr",
            "arabic" => "ar",
            "hindi" => "hi",
            "thai" => "th",
            "vietnamese" => "vi",
            "indonesian" => "id",
            "malay" => "ms",
            "hungarian" => "hu",
            "czech" => "cs",
            "slovak" => "sk",
            "greek" => "el",
            "hebrew" => "he",
            "icelandic" => "is",
            "ukrainian" => "uk",
            "romanian" => "ro",
            "croatian" or "serbian" => "hr",
            "bulgarian" => "bg",
            "latin american spanish" or "castilian spanish" => "es",
            _ => null
        };
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

    public static string? StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var stripped = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
        return System.Net.WebUtility.HtmlDecode(stripped).Trim();
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