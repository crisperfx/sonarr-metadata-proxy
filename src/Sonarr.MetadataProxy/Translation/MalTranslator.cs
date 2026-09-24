using Sonarr.MetadataProxy.Contracts.SkyHook;
using Sonarr.MetadataProxy.Models.Mal;

namespace Sonarr.MetadataProxy.Translation;

public sealed class MalTranslator
{
    public ShowResource ToSearchResult(MalAnime anime, int tvdbId)
    {
        var title = Title(anime);
        var status = MapStatus(anime.Status);

        var show = new ShowResource
        {
            TvdbId = tvdbId,
            Title = title,
            Overview = anime.Synopsis,
            Slug = Slugify(title, tvdbId),
            OriginalCountry = "JP",
            OriginalLanguage = "ja",
            FirstAired = anime.FirstAirDate,
            LastAired = anime.LastAirDate,
            Status = status,
            Runtime = anime.DurationMinutes,
            Network = anime.Studio,
            Genres = anime.Genres,
            LastUpdated = DateTime.UtcNow,
            Rating = new RatingResource
            {
                Count = anime.ScoreCount ?? 0,
                Value = (decimal)(anime.Score ?? 0)
            }
        };

        show.MalIds.Add(anime.Id);

        foreach (var alternative in MediaTitles(anime))
        {
            if (!string.Equals(alternative, title, StringComparison.OrdinalIgnoreCase))
            {
                show.AlternativeTitles.Add(new AlternativeTitleResource { Title = alternative });
            }
        }

        if (!string.IsNullOrWhiteSpace(anime.PosterUrl))
        {
            show.Images.Add(new ImageResource { CoverType = "poster", Url = anime.PosterUrl });
        }

        return show;
    }

    private static string Title(MalAnime anime)
    {
        return anime.TitleEnglish ?? anime.Title ?? anime.TitleJapanese ?? "Unknown";
    }

    private static IEnumerable<string> MediaTitles(MalAnime anime)
    {
        return new[] { anime.Title, anime.TitleEnglish, anime.TitleJapanese }
            .Concat(anime.Synonyms)
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .Cast<string>();
    }

    private static string? MapStatus(string? status)
    {
        return status?.ToUpperInvariant() switch
        {
            "FINISHED AIRING" => "ended",
            "CANCELLED" => "ended",
            "CURRENTLY AIRING" => "continuing",
            "NOT YET AIRED" => "upcoming",
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