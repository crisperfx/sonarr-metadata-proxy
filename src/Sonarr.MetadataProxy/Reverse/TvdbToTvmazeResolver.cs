using System.Collections.Concurrent;
using Sonarr.MetadataProxy.Models.Tvmaze;
using Sonarr.MetadataProxy.Providers;

namespace Sonarr.MetadataProxy.Reverse;

/// <summary>
/// Resolves a real TVDB id to a TVMaze id. Strategy, most reliable first:
///  1. TVMaze /lookup/shows?thetvdb={id}  (exact, TVMaze itself knows the link)
///  2. TVMaze title search (fuzzy, prefers same premiere year)
/// Successful resolutions are cached. Failures are not, so a retry with a title
/// (or after TVMaze data changed) can still succeed.
/// </summary>
public sealed class TvdbToTvmazeResolver : ITvdbToTvmazeResolver
{
    private readonly ITvmazeApi _tvmaze;
    private readonly ILogger<TvdbToTvmazeResolver> _logger;
    private readonly ConcurrentDictionary<int, int> _found = new();

    public TvdbToTvmazeResolver(ITvmazeApi tvmaze, ILogger<TvdbToTvmazeResolver> logger)
    {
        _tvmaze = tvmaze;
        _logger = logger;
    }

    public async Task<int?> ResolveTvmazeIdAsync(int tvdbId, string? title, int? year, CancellationToken cancellationToken)
    {
        if (_found.TryGetValue(tvdbId, out var cached))
        {
            return cached;
        }

        int? result = null;

        try
        {
            var byTvdb = await _tvmaze.FindByThetvdbAsync(tvdbId, cancellationToken).ConfigureAwait(false);
            result = byTvdb is not null ? byTvdb.Id : null;
        }
        catch (TvmazeApiException ex)
        {
            _logger.LogWarning("TVMaze /lookup (thetvdb) failed for TVDB {TvdbId}: {Message}", tvdbId, ex.Message);
        }

        if (result is not > 0 && !string.IsNullOrWhiteSpace(title))
        {
            try
            {
                var search = await _tvmaze.SearchShowsAsync(title, cancellationToken).ConfigureAwait(false);
                result = Pick(search, title, year);
            }
            catch (TvmazeApiException ex)
            {
                _logger.LogWarning("TVMaze title search failed for TVDB {TvdbId}: {Message}", tvdbId, ex.Message);
            }
        }

        if (result is > 0)
        {
            _found[tvdbId] = result.Value;
            _logger.LogInformation("Mapping resolved: TVDB {TvdbId} -> TVMaze {TvmazeId}.", tvdbId, result);
        }

        return result;
    }

    private static int? Pick(IReadOnlyList<TvmazeShow> shows, string? title, int? year)
    {
        if (shows is null || shows.Count == 0)
        {
            return null;
        }

        var normalizedTitle = Normalize(title);
        var best = shows
            .Select(show => new { show.Id, Score = Score(show, normalizedTitle, year) })
            .OrderByDescending(x => x.Score)
            .First();

        return best.Score > 0 ? best.Id : null;
    }

    private static int Score(TvmazeShow show, string? normalizedTitle, int? year)
    {
        var score = 1;

        if (!string.IsNullOrEmpty(normalizedTitle))
        {
            var name = Normalize(show.Name);
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

        if (year is > 0 && show.Premiered is { Length: >= 4 } &&
            int.TryParse(show.Premiered.AsSpan(0, 4), out var releaseYear) &&
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