using System.Collections.Concurrent;
using Sonarr.MetadataProxy.Models.Tmdb;
using Sonarr.MetadataProxy.Providers;

namespace Sonarr.MetadataProxy.Reverse;

/// <summary>
/// Resolves a real TVDB id to a TMDB id. Strategy, most reliable first:
///  1. TMDB /find/{tvdbId}?external_source=tvdb_id  (exact, TMDB itself knows the link)
///  2. TMDB title search (fuzzy, prefers same first-air year)
///  3. Wikidata reverse lookup (last resort)
/// Successful resolutions are cached. Failures are not, so a retry with a title
/// (or after TMDB data changed) can still succeed.
/// </summary>
public sealed class TvdbToTmdbResolver : ITvdbToTmdbResolver
{
    private readonly ITmdbApi _tmdb;
    private readonly WikidataTvdbResolver _wikidata;
    private readonly ILogger<TvdbToTmdbResolver> _logger;
    private readonly ConcurrentDictionary<int, int> _found = new();

    public TvdbToTmdbResolver(ITmdbApi tmdb, WikidataTvdbResolver wikidata, ILogger<TvdbToTmdbResolver> logger)
    {
        _tmdb = tmdb;
        _wikidata = wikidata;
        _logger = logger;
    }

    public async Task<int?> ResolveTmdbIdAsync(int tvdbId, string? title, int? year, CancellationToken cancellationToken)
    {
        if (_found.TryGetValue(tvdbId, out var cached))
        {
            return cached;
        }

        int? result = null;

        try
        {
            var byTvdb = await _tmdb.FindByTvdbAsync(tvdbId, cancellationToken).ConfigureAwait(false);
            result = Pick(byTvdb, title, year);
        }
        catch (TmdbApiException ex)
        {
            _logger.LogWarning("TMDB /find lookup failed for TVDB {TvdbId}: {Message}", tvdbId, ex.Message);
        }

        if (result is not > 0 && !string.IsNullOrWhiteSpace(title))
        {
            try
            {
                var search = await _tmdb.SearchTvAsync(title, cancellationToken).ConfigureAwait(false);
                result = Pick(search, title, year);
            }
            catch (TmdbApiException ex)
            {
                _logger.LogWarning("TMDB title search failed for TVDB {TvdbId}: {Message}", tvdbId, ex.Message);
            }
        }

        if (result is not > 0)
        {
            result = await _wikidata.ResolveTmdbIdAsync(tvdbId, title, year, cancellationToken).ConfigureAwait(false);
        }

        if (result is > 0)
        {
            _found[tvdbId] = result.Value;
            _logger.LogInformation("Mapping resolved: TVDB {TvdbId} -> TMDB {TmdbId}.", tvdbId, result);
        }

        return result;
    }

    private static int? Pick(IReadOnlyList<TmdbTvSearchResult>? results, string? title, int? year)
    {
        if (results is null || results.Count == 0)
        {
            return null;
        }

        var normalizedTitle = Normalize(title);
        var best = results
            .Select(r => new { r.Id, Score = Score(r, normalizedTitle, year) })
            .OrderByDescending(x => x.Score)
            .First();

        return best.Score > 0 ? best.Id : null;
    }

    private static int Score(TmdbTvSearchResult result, string? normalizedTitle, int? year)
    {
        var score = 1;

        if (!string.IsNullOrEmpty(normalizedTitle))
        {
            var name = Normalize(result.Name);
            if (string.Equals(name, normalizedTitle, StringComparison.Ordinal))
            {
                score += 100;
            }
            else if (name is not null && name.Contains(normalizedTitle, StringComparison.Ordinal))
            {
                score += 40;
            }
            else if (name is not null && normalizedTitle.Contains(name, StringComparison.Ordinal))
            {
                score += 20;
            }
        }

        if (year is > 0 && result.FirstAirDate is { Length: >= 4 } &&
            int.TryParse(result.FirstAirDate.AsSpan(0, 4), out var releaseYear) &&
            releaseYear == year.Value)
        {
            score += 30;
        }

        return score;
    }

    private static string? Normalize(string? value)
    {
        return value?.Trim().ToLowerInvariant();
    }
}