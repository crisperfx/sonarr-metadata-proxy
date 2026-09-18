using Sonarr.MetadataProxy.Models.Metadata;
using Sonarr.MetadataProxy.Models.Tmdb;
using Sonarr.MetadataProxy.Options;

namespace Sonarr.MetadataProxy.Providers;

public sealed class TmdbMetadataProvider : IMetadataProvider
{
    private readonly ITmdbApi _api;
    private readonly ProxyOptions _options;
    private readonly ILogger<TmdbMetadataProvider> _logger;

    public TmdbMetadataProvider(ITmdbApi api, ProxyOptions options, ILogger<TmdbMetadataProvider> logger)
    {
        _api = api;
        _options = options;
        _logger = logger;
    }

    public string Name => "tmdb";

    public async Task<IReadOnlyList<SeriesMetadata>> Search(string query, CancellationToken cancellationToken)
    {
        EnsureTmdbCredentials();
        var results = await _api.SearchTvAsync(query, cancellationToken).ConfigureAwait(false);
        return await BuildSearchResults(results, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SeriesMetadata>> SearchByImdbId(string imdbId, CancellationToken cancellationToken)
    {
        EnsureTmdbCredentials();
        var results = await _api.FindByImdbAsync(imdbId, cancellationToken).ConfigureAwait(false);
        return await BuildSearchResults(results, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SeriesMetadata>> SearchById(string providerId, CancellationToken cancellationToken)
    {
        EnsureTmdbCredentials();
        var details = await FetchDetails(providerId, cancellationToken).ConfigureAwait(false);
        if (details is null)
        {
            return Array.Empty<SeriesMetadata>();
        }

        return new[] { MapSeries(details) };
    }

    public async Task<SeriesMetadata> GetSeries(string providerId, CancellationToken cancellationToken)
    {
        EnsureTmdbCredentials();
        var details = await FetchDetails(providerId, cancellationToken).ConfigureAwait(false);
        return details is null ? throw new InvalidOperationException($"TMDB series {providerId} not found.") : MapSeries(details);
    }

    public async Task<IReadOnlyList<SeasonMetadata>> GetSeasons(string providerId, CancellationToken cancellationToken)
    {
        EnsureTmdbCredentials();
        if (!int.TryParse(providerId, out var tmdbId))
        {
            return Array.Empty<SeasonMetadata>();
        }

        var details = await FetchDetails(providerId, cancellationToken).ConfigureAwait(false);
        if (details is null)
        {
            return Array.Empty<SeasonMetadata>();
        }

        var seasonNumbers = details.Seasons
            .Select(season => season.SeasonNumber)
            .Where(number => number >= 0)
            .Distinct()
            .OrderBy(number => number)
            .ToList();

        var seasons = new List<SeasonMetadata>();
        foreach (var chunk in Chunk(seasonNumbers, 4))
        {
            var fetched = await Task.WhenAll(
                chunk.Select(async seasonNumber =>
                {
                    var episodes = await _api.GetSeasonEpisodesAsync(tmdbId, seasonNumber, cancellationToken).ConfigureAwait(false);
                    return new SeasonMetadata
                    {
                        SeasonNumber = seasonNumber,
                        Episodes = episodes.Select(MapEpisode).ToList()
                    };
                })).ConfigureAwait(false);

            seasons.AddRange(fetched);
        }

        return seasons;
    }

    private async Task<IReadOnlyList<SeriesMetadata>> BuildSearchResults(
        IReadOnlyList<TmdbTvSearchResult> results,
        CancellationToken cancellationToken)
    {
        var projected = results
            .OrderByDescending(r => r.VoteCount)
            .Take(_options.SearchResultLimit)
            .ToList();

        var series = new List<SeriesMetadata>();
        foreach (var chunk in Chunk(projected, 4))
        {
            var fetched = await Task.WhenAll(
                chunk.Select(async result =>
                {
                    var details = await FetchDetails(result.Id.ToString(), cancellationToken).ConfigureAwait(false);
                    return details is not null ? MapSeries(details) : MapSeries(result);
                })).ConfigureAwait(false);

            series.AddRange(fetched);
        }

        return series;
    }

    private async Task<TmdbTvDetails?> FetchDetails(string providerId, CancellationToken cancellationToken)
    {
        if (!int.TryParse(providerId, out var tmdbId))
        {
            return null;
        }

        return await _api.GetTvDetailsAsync(tmdbId, cancellationToken).ConfigureAwait(false);
    }

    private static SeriesMetadata MapSeries(TmdbTvSearchResult result)
    {
        return new SeriesMetadata
        {
            ProviderId = result.Id.ToString(),
            Title = !string.IsNullOrWhiteSpace(result.Name) ? result.Name : string.Empty,
            Overview = result.Overview,
            OriginalTitle = result.OriginalName,
            FirstAirDate = NormalizeDate(result.FirstAirDate),
            OriginalCountryCode = result.OriginCountry.FirstOrDefault(),
            OriginalLanguageCode = result.OriginalLanguage,
            VoteAverage = result.VoteAverage,
            VoteCount = result.VoteCount,
            PosterPath = result.PosterPath,
            BackdropPath = result.BackdropPath,
            ExternalIds = new ExternalIdSet(null, null, result.Id)
        };
    }

    private static SeriesMetadata MapSeries(TmdbTvDetails details)
    {
        var rating = PickContentRating(details);

        return new SeriesMetadata
        {
            ProviderId = details.Id.ToString(),
            Title = !string.IsNullOrWhiteSpace(details.Name) ? details.Name : string.Empty,
            Overview = details.Overview,
            OriginalTitle = details.OriginalName,
            FirstAirDate = NormalizeDate(details.FirstAirDate),
            LastAirDate = NormalizeDate(details.LastAirDate),
            Status = MapStatus(details.Status),
            RuntimeMinutes = details.EpisodeRunTime.FirstOrDefault(runtime => runtime > 0) is > 0 and var runtime ? runtime : null,
            Network = details.Networks.FirstOrDefault()?.Name,
            OriginalCountryCode = details.OriginCountry.FirstOrDefault(),
            OriginalLanguageCode = details.OriginalLanguage,
            ContentRating = rating,
            VoteAverage = details.VoteAverage,
            VoteCount = details.VoteCount,
            Genres = details.Genres.Where(genre => !string.IsNullOrWhiteSpace(genre.Name)).Select(genre => genre.Name!).ToList(),
            PosterPath = details.PosterPath,
            BackdropPath = details.BackdropPath,
            ExternalIds = new ExternalIdSet(
                details.ExternalIds?.TvdbId,
                details.ExternalIds?.ImdbId,
                details.Id),
            Seasons = details.Seasons
                .Where(season => season.SeasonNumber >= 0)
                .OrderBy(season => season.SeasonNumber)
                .Select(season => new SeasonSummaryMetadata
                {
                    SeasonNumber = season.SeasonNumber,
                    EpisodeCount = season.EpisodeCount,
                    AirDate = NormalizeDate(season.AirDate),
                    PosterPath = season.PosterPath
                })
                .ToList(),
            Actors = details.Credits?.Cast
                .Where(cast => cast.Order < 20)
                .Select(cast => new ActorMetadata
                {
                    Name = cast.Name,
                    Character = cast.Character,
                    ImageUrl = BuildProfileImageUrl(cast.ProfilePath)
                })
                .ToList() ?? new List<ActorMetadata>()
        };
    }

    private static EpisodeMetadata MapEpisode(TmdbEpisode episode)
    {
        return new EpisodeMetadata
        {
            EpisodeNumber = episode.EpisodeNumber,
            AbsoluteEpisodeNumber = episode.AbsoluteNumber,
            Title = episode.Name,
            Overview = episode.Overview,
            AirDate = NormalizeDate(episode.AirDate),
            RuntimeMinutes = episode.Runtime,
            ImageUrl = BuildStillImageUrl(episode.StillPath),
            VoteAverage = episode.VoteAverage,
            VoteCount = episode.VoteCount,
            EpisodeType = episode.EpisodeType
        };
    }

    private static string? MapStatus(string? tmdbStatus)
    {
        return (tmdbStatus ?? string.Empty).ToLowerInvariant() switch
        {
            "ended" or "canceled" => "ended",
            "planned" => "upcoming",
            _ => "continuing"
        };
    }

    private static string? PickContentRating(TmdbTvDetails details)
    {
        var result = details.ContentRatings?.Results
            .FirstOrDefault(rating => string.Equals(rating.Iso3166_1, "US", StringComparison.OrdinalIgnoreCase))
            ?? details.ContentRatings?.Results.FirstOrDefault();

        return string.IsNullOrWhiteSpace(result?.Rating) ? null : result.Rating!.ToUpperInvariant();
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

    private static string? BuildProfileImageUrl(string? path)
    {
        return string.IsNullOrWhiteSpace(path) ? null : "https://image.tmdb.org/t/p/w185" + path;
    }

    private static string? BuildStillImageUrl(string? path)
    {
        return string.IsNullOrWhiteSpace(path) ? null : "https://image.tmdb.org/t/p/w500" + path;
    }

    private static IEnumerable<List<T>> Chunk<T>(List<T> source, int size)
    {
        for (var i = 0; i < source.Count; i += size)
        {
            yield return source.GetRange(i, Math.Min(size, source.Count - i));
        }
    }

    private void EnsureTmdbCredentials()
    {
        if (!_options.HasTmdbAuth)
        {
            throw new TmdbApiException("TMDB credentials are not configured.", 401);
        }
    }
}