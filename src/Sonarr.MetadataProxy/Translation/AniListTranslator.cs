using Sonarr.MetadataProxy.Contracts.SkyHook;
using Sonarr.MetadataProxy.Models.AniList;

namespace Sonarr.MetadataProxy.Translation;

public sealed class AniListTranslator
{
    public ShowResource ToSearchResult(AniListMedia media, int tvdbId)
    {
        var title = Title(media);
        var status = MapStatus(media.Status);

        var show = new ShowResource
        {
            TvdbId = tvdbId,
            Title = title,
            Overview = media.Description,
            Slug = Slugify(title, tvdbId),
            OriginalCountry = NormalizeCountry(media.CountryOfOrigin),
            OriginalLanguage = NormalizeLanguage(media.CountryOfOrigin),
            FirstAired = media.FirstAirDate,
            LastAired = media.LastAirDate,
            Status = status,
            Runtime = media.DurationMinutes,
            Network = media.Studio,
            Genres = media.Genres,
            LastUpdated = DateTime.UtcNow,
            Rating = new RatingResource
            {
                Count = 0,
                Value = Math.Round((decimal)media.AverageScore / 10m, 1)
            }
        };

        if (media.IdMal is > 0)
        {
            show.MalIds.Add(media.IdMal.Value);
        }

        show.AniListIds.Add(media.Id);

        foreach (var alternative in MediaTitles(media))
        {
            if (!string.Equals(alternative, title, StringComparison.OrdinalIgnoreCase))
            {
                show.AlternativeTitles.Add(new AlternativeTitleResource { Title = alternative });
            }
        }

        if (!string.IsNullOrWhiteSpace(media.PosterUrl))
        {
            show.Images.Add(new ImageResource { CoverType = "poster", Url = media.PosterUrl });
            show.Images.Add(new ImageResource { CoverType = "fanart", Url = !string.IsNullOrWhiteSpace(media.BannerUrl) ? media.BannerUrl : media.PosterUrl });
        }
        else if (!string.IsNullOrWhiteSpace(media.BannerUrl))
        {
            show.Images.Add(new ImageResource { CoverType = "fanart", Url = media.BannerUrl });
        }

        return show;
    }

    private static string Title(AniListMedia media)
    {
        return media.TitleEnglish ?? media.TitleRomaji ?? media.TitleNative ?? "Unknown";
    }

    private static IEnumerable<string> MediaTitles(AniListMedia media)
    {
        return new[] { media.TitleRomaji, media.TitleEnglish, media.TitleNative }
            .Concat(media.AlternativeTitles)
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .Cast<string>();
    }

    private static string? MapStatus(string? status)
    {
        return status?.ToUpperInvariant() switch
        {
            "FINISHED" => "ended",
            "CANCELLED" => "ended",
            "RELEASING" => "continuing",
            "HIATUS" => "continuing",
            "NOT_YET_RELEASED" => "upcoming",
            _ => null
        };
    }

    private static string? NormalizeCountry(string? country)
    {
        return string.IsNullOrWhiteSpace(country) ? null : country.ToUpperInvariant();
    }

    private static string? NormalizeLanguage(string? country)
    {
        return country switch
        {
            "JP" => "ja",
            "KR" => "ko",
            "CN" => "zh",
            "TW" => "zh",
            "TH" => "th",
            _ => null
        };
    }

    private static string Slugify(string title, int tvdbId)
    {
        var slug = System.Text.RegularExpressions.Regex
            .Replace((title ?? string.Empty).ToLowerInvariant().Trim(), "[^a-z0-9]+", "-")
            .Trim('-');

        return string.IsNullOrWhiteSpace(slug) ? tvdbId.ToString() : slug;
    }
}