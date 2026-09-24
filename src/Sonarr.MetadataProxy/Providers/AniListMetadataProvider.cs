using Sonarr.MetadataProxy.Models.AniList;
using Sonarr.MetadataProxy.Models.Mal;
using Sonarr.MetadataProxy.Models.Metadata;
using Sonarr.MetadataProxy.Options;
using Sonarr.MetadataProxy.Reverse;

namespace Sonarr.MetadataProxy.Providers;

public sealed class AniListMetadataProvider : IMetadataProvider
{
    private readonly IAniListApi _api;
    private readonly IMalApi _malApi;
    private readonly AniListTvdbMap _map;
    private readonly ProxyOptions _options;
    private readonly ILogger<AniListMetadataProvider> _logger;

    public AniListMetadataProvider(
        IAniListApi api,
        IMalApi malApi,
        AniListTvdbMap map,
        ProxyOptions options,
        ILogger<AniListMetadataProvider> logger)
    {
        _api = api;
        _malApi = malApi;
        _map = map;
        _options = options;
        _logger = logger;
    }

    public string Name => "anilist";

    public async Task<IReadOnlyList<SeriesMetadata>> Search(string query, CancellationToken cancellationToken)
    {
        var results = await _api.SearchAsync(query, cancellationToken).ConfigureAwait(false);
        return results
            .OrderByDescending(m => m.AverageScore)
            .Take(_options.SearchResultLimit)
            .Select(m => MapSeries(m, m.Id))
            .ToList();
    }

    public async Task<IReadOnlyList<SeriesMetadata>> SearchById(string providerId, CancellationToken cancellationToken)
    {
        if (!int.TryParse(providerId, out var anilistId))
        {
            return Array.Empty<SeriesMetadata>();
        }

        var media = await _api.GetByIdAsync(anilistId, cancellationToken).ConfigureAwait(false);
        return media is null ? Array.Empty<SeriesMetadata>() : new[] { MapSeries(media, media.Id) };
    }

    public Task<IReadOnlyList<SeriesMetadata>> SearchByImdbId(string imdbId, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<SeriesMetadata>>(Array.Empty<SeriesMetadata>());
    }

    public async Task<SeriesMetadata> GetSeries(string providerId, CancellationToken cancellationToken)
    {
        if (!int.TryParse(providerId, out var anilistId))
        {
            throw new InvalidOperationException($"AniList series {providerId} not found.");
        }

        var media = await _api.GetByIdAsync(anilistId, cancellationToken).ConfigureAwait(false);
        return media is null ? throw new InvalidOperationException($"AniList series {providerId} not found.") : MapSeries(media, media.Id);
    }

    public async Task<SeriesMetadata> GetSeriesWithTvdbId(string providerId, int tvdbId, CancellationToken cancellationToken)
    {
        if (!int.TryParse(providerId, out var anilistId))
        {
            throw new InvalidOperationException($"AniList series {providerId} not found.");
        }

        var media = await _api.GetByIdAsync(anilistId, cancellationToken).ConfigureAwait(false);
        return media is null ? throw new InvalidOperationException($"AniList series {providerId} not found.") : MapSeries(media, media.Id, tvdbId);
    }

    public async Task<IReadOnlyList<SeasonMetadata>> GetSeasons(string providerId, CancellationToken cancellationToken)
    {
        if (!int.TryParse(providerId, out var anilistId))
        {
            return Array.Empty<SeasonMetadata>();
        }

        var media = await _api.GetByIdAsync(anilistId, cancellationToken).ConfigureAwait(false);
        if (media is null)
        {
            return Array.Empty<SeasonMetadata>();
        }

        var malId = media.IdMal;
        if (malId is not > 0)
        {
            _logger.LogWarning("AniList {AniListId} has no MAL id; cannot fetch episodes.", anilistId);
            return Array.Empty<SeasonMetadata>();
        }

        var episodes = await _malApi.GetEpisodesAsync(malId.Value, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("AniList {AniListId} (MAL {MalId}) episodes: count={Count}, sample Number={Num}, AbsoluteNumber={AbsNum}, SeasonNumber={SeasonNum}",
            anilistId, malId.Value, episodes.Count,
            episodes.FirstOrDefault()?.Number,
            episodes.FirstOrDefault()?.AbsoluteNumber,
            episodes.FirstOrDefault()?.SeasonNumber);

        var hasSeasonNumbers = episodes.Any(e => e.SeasonNumber is > 0);
        List<SeasonMetadata> seasons;
        if (hasSeasonNumbers)
        {
            seasons = episodes
                .Where(e => e.SeasonNumber is > 0)
                .GroupBy(e => e.SeasonNumber!.Value)
                .Select(g => new SeasonMetadata
                {
                    SeasonNumber = g.Key,
                    Episodes = g.OrderBy(e => e.AbsoluteNumber ?? e.Number).Select(MapEpisode).ToList()
                })
                .OrderBy(s => s.SeasonNumber)
                .ToList();
        }
        else
        {
            var allEpisodes = episodes
                .Where(e => e.AbsoluteNumber.HasValue || e.Number > 0)
                .OrderBy(e => e.AbsoluteNumber ?? e.Number)
                .Select((e, idx) => MapEpisodeWithAdjustedNumbers(e, idx + 1))
                .ToList();

            seasons = new List<SeasonMetadata>
            {
                new SeasonMetadata
                {
                    SeasonNumber = 1,
                    Episodes = allEpisodes
                }
            };
        }

        _logger.LogInformation("AniList {AniListId} seasons: {SeasonCount}", anilistId, seasons.Count);
        return seasons;
    }

    private EpisodeMetadata MapEpisodeWithAdjustedNumbers(MalEpisode episode, int episodeNumber)
    {
        var baseMapped = MapEpisode(episode);
        return new EpisodeMetadata
        {
            EpisodeNumber = episodeNumber,
            AbsoluteEpisodeNumber = episode.AbsoluteNumber ?? episodeNumber,
            Title = baseMapped.Title,
            Overview = baseMapped.Overview,
            AirDate = baseMapped.AirDate,
            RuntimeMinutes = baseMapped.RuntimeMinutes,
            ImageUrl = baseMapped.ImageUrl,
            VoteAverage = baseMapped.VoteAverage,
            VoteCount = baseMapped.VoteCount,
            EpisodeType = baseMapped.EpisodeType
        };
    }

    private SeriesMetadata MapSeries(AniListMedia media, int anilistId, int? tvdbId = null)
    {
        var resolvedTvdbId = tvdbId ?? _map.TryGetTvdbId(anilistId);

        return new SeriesMetadata
        {
            ProviderId = anilistId.ToString(),
            Title = !string.IsNullOrWhiteSpace(media.TitleEnglish) ? media.TitleEnglish : !string.IsNullOrWhiteSpace(media.TitleRomaji) ? media.TitleRomaji : media.TitleNative ?? string.Empty,
            Overview = media.Description,
            OriginalTitle = media.TitleNative ?? media.TitleRomaji,
            FirstAirDate = NormalizeDate(media.FirstAirDate),
            LastAirDate = NormalizeDate(media.LastAirDate),
            Status = MapStatus(media.Status),
            RuntimeMinutes = media.DurationMinutes ?? 0,
            Network = media.Studio,
            OriginalCountryCode = NormalizeCountry(media.CountryOfOrigin),
            OriginalLanguageCode = NormalizeLanguage(media.CountryOfOrigin),
            VoteAverage = (double)Math.Round((decimal)media.AverageScore / 10m, 1),
            VoteCount = 0,
            Genres = media.Genres,
            PosterPath = media.PosterUrl,
            BackdropPath = media.BannerUrl,
            ExternalIds = new ExternalIdSet(resolvedTvdbId, null, anilistId),
            Actors = media.Cast
                .Where(c => !string.IsNullOrWhiteSpace(c.Name))
                .Take(20)
                .Select(c => new ActorMetadata
                {
                    Name = c.Name!,
                    Character = c.Character,
                    ImageUrl = c.ImageUrl
                })
                .ToList()
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
            RuntimeMinutes = episode.RuntimeMinutes ?? 0,
            ImageUrl = episode.StillUrl,
            VoteAverage = episode.Score ?? 0,
            VoteCount = episode.VoteCount ?? 0,
            EpisodeType = episode.EpisodeType
        };
    }

    private static string? MapStatus(string? anilistStatus)
    {
        return (anilistStatus ?? string.Empty).ToUpperInvariant() switch
        {
            "FINISHED" or "CANCELLED" => "ended",
            "NOT_YET_RELEASED" => "upcoming",
            "RELEASING" or "HIATUS" => "continuing",
            _ => null
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
}