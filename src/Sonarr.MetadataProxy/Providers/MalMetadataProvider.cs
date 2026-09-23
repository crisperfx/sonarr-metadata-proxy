using Sonarr.MetadataProxy.Models.Mal;
using Sonarr.MetadataProxy.Models.Metadata;
using Sonarr.MetadataProxy.Options;

namespace Sonarr.MetadataProxy.Providers;

public sealed class MalMetadataProvider : IMetadataProvider
{
    private readonly IMalApi _api;
    private readonly ProxyOptions _options;
    private readonly ILogger<MalMetadataProvider> _logger;

    public MalMetadataProvider(IMalApi api, ProxyOptions options, ILogger<MalMetadataProvider> logger)
    {
        _api = api;
        _options = options;
        _logger = logger;
    }

    public string Name => "mal";

    public async Task<IReadOnlyList<SeriesMetadata>> Search(string query, CancellationToken cancellationToken)
    {
        var results = await _api.SearchAsync(query, cancellationToken).ConfigureAwait(false);
        return await BuildSearchResults(results, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SeriesMetadata>> SearchById(string providerId, CancellationToken cancellationToken)
    {
        if (!int.TryParse(providerId, out var malId))
        {
            return Array.Empty<SeriesMetadata>();
        }

        var details = await _api.GetSeriesDetailsAsync(malId, cancellationToken).ConfigureAwait(false);
        if (details is null)
        {
            return Array.Empty<SeriesMetadata>();
        }

        return new[] { MapSeries(details) };
    }

    public async Task<IReadOnlyList<SeriesMetadata>> SearchByImdbId(string imdbId, CancellationToken cancellationToken)
    {
        return Array.Empty<SeriesMetadata>();
    }

    public async Task<SeriesMetadata> GetSeries(string providerId, CancellationToken cancellationToken)
    {
        if (!int.TryParse(providerId, out var malId))
        {
            throw new InvalidOperationException($"MAL series {providerId} not found.");
        }

        var details = await _api.GetSeriesDetailsAsync(malId, cancellationToken).ConfigureAwait(false);
        return details is null ? throw new InvalidOperationException($"MAL series {providerId} not found.") : MapSeries(details);
    }

    public async Task<IReadOnlyList<SeasonMetadata>> GetSeasons(string providerId, CancellationToken cancellationToken)
    {
        if (!int.TryParse(providerId, out var malId))
        {
            return Array.Empty<SeasonMetadata>();
        }

        var details = await _api.GetSeriesDetailsAsync(malId, cancellationToken).ConfigureAwait(false);
        if (details is null)
        {
            return Array.Empty<SeasonMetadata>();
        }

        var episodes = await _api.GetEpisodesAsync(malId, cancellationToken).ConfigureAwait(false);

        var seasons = episodes
            .Where(e => e.Number > 0)
            .GroupBy(e => e.Number)
            .Select(g => new SeasonMetadata
            {
                SeasonNumber = g.Key,
                Episodes = g.Select(MapEpisode).ToList()
            })
            .OrderBy(s => s.SeasonNumber)
            .ToList();

        return seasons;
    }

    private async Task<IReadOnlyList<SeriesMetadata>> BuildSearchResults(
        IReadOnlyList<MalAnime> results,
        CancellationToken cancellationToken)
    {
        var projected = results
            .OrderByDescending(r => r.ScoreCount ?? 0)
            .Take(_options.SearchResultLimit)
            .ToList();

        var series = new List<SeriesMetadata>();
        foreach (var chunk in Chunk(projected, 4))
        {
            var fetched = await Task.WhenAll(
                chunk.Select(async result =>
                {
                    var details = await _api.GetSeriesDetailsAsync(result.Id, cancellationToken).ConfigureAwait(false);
                    return details is not null ? MapSeries(details) : MapSeries(result);
                })).ConfigureAwait(false);

            series.AddRange(fetched);
        }

        return series;
    }

    private static SeriesMetadata MapSeries(MalAnime result)
    {
        return new SeriesMetadata
        {
            ProviderId = result.Id.ToString(),
            Title = !string.IsNullOrWhiteSpace(result.Title) ? result.Title : string.Empty,
            Overview = result.Synopsis,
            OriginalTitle = result.TitleJapanese,
            FirstAirDate = NormalizeDate(result.FirstAirDate),
            OriginalCountryCode = "JP",
            OriginalLanguageCode = "ja",
            VoteAverage = result.Score,
            VoteCount = result.ScoreCount,
            PosterPath = result.PosterUrl,
            BackdropPath = null,
            ExternalIds = new ExternalIdSet(null, null, result.Id)
        };
    }

    private static SeriesMetadata MapSeries(MalAnimeDetails details)
    {
        var seasons = details.Seasons
            .Where(s => s.Number >= 0)
            .OrderBy(s => s.Number)
            .Select(s => new SeasonSummaryMetadata
            {
                SeasonNumber = s.Number,
                EpisodeCount = s.EpisodeCount,
                AirDate = NormalizeDate(s.AirDate),
                PosterPath = s.PosterUrl
            })
            .ToList();

        var actors = details.Cast
            .Where(c => !string.IsNullOrWhiteSpace(c.Name))
            .Take(20)
            .Select(c => new ActorMetadata
            {
                Name = c.Name!,
                Character = c.Character,
                ImageUrl = c.ImageUrl
            })
            .ToList();

        return new SeriesMetadata
        {
            ProviderId = details.Id.ToString(),
            Title = !string.IsNullOrWhiteSpace(details.Title) ? details.Title : string.Empty,
            Overview = details.Synopsis,
            OriginalTitle = details.TitleJapanese,
            FirstAirDate = NormalizeDate(details.FirstAirDate),
            LastAirDate = NormalizeDate(details.LastAirDate),
            Status = MapStatus(details.Status),
            RuntimeMinutes = details.DurationMinutes,
            Network = details.Studios.FirstOrDefault(),
            OriginalCountryCode = "JP",
            OriginalLanguageCode = "ja",
            VoteAverage = details.Score,
            VoteCount = details.ScoreCount,
            Genres = details.Genres,
            PosterPath = details.PosterUrl,
            BackdropPath = null,
            ExternalIds = new ExternalIdSet(
                details.ExternalIds?.TvdbId,
                details.ExternalIds?.ImdbId,
                details.Id),
            Seasons = seasons,
            Actors = actors
        };
    }

    private static EpisodeMetadata MapEpisode(MalEpisode episode)
    {
        return new EpisodeMetadata
        {
            EpisodeNumber = episode.Number,
            AbsoluteEpisodeNumber = episode.AbsoluteNumber,
            Title = episode.Title,
            Overview = episode.Overview,
            AirDate = NormalizeDate(episode.AirDate),
            RuntimeMinutes = episode.RuntimeMinutes,
            ImageUrl = episode.StillUrl,
            VoteAverage = episode.Score,
            VoteCount = episode.VoteCount,
            EpisodeType = episode.EpisodeType
        };
    }

    private static string? MapStatus(string? malStatus)
    {
        return (malStatus ?? string.Empty).ToLowerInvariant() switch
        {
            "finished_airing" or "completed" => "ended",
            "not_yet_aired" => "upcoming",
            "currently_airing" => "continuing",
            _ => "continuing"
        };
    }

    private static string? NormalizeDate(string? date)
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

    private static IEnumerable<List<T>> Chunk<T>(List<T> source, int size)
    {
        for (var i = 0; i < source.Count; i += size)
        {
            yield return source.GetRange(i, Math.Min(size, source.Count - i));
        }
    }
}