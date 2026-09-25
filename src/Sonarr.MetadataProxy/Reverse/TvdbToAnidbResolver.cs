using System.Collections.Concurrent;
using Sonarr.MetadataProxy.Services;

namespace Sonarr.MetadataProxy.Reverse;

public interface ITvdbToAnidbResolver
{
    Task<int?> ResolveAnidbIdAsync(int tvdbId, string? title, int? year, CancellationToken cancellationToken);
}

public sealed class TvdbToAnidbResolver : ITvdbToAnidbResolver
{
    private readonly AniListTvdbMap _map;
    private readonly AnidbTitleList _titles;
    private readonly ILogger<TvdbToAnidbResolver> _logger;
    private readonly ConcurrentDictionary<int, int?> _cache = new();

    public TvdbToAnidbResolver(
        AniListTvdbMap map,
        AnidbTitleList titles,
        ILogger<TvdbToAnidbResolver> logger)
    {
        _map = map;
        _titles = titles;
        _logger = logger;
    }

    public async Task<int?> ResolveAnidbIdAsync(int tvdbId, string? title, int? year, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(tvdbId, out var cached))
        {
            return cached;
        }

        var fromMap = _map.TryGetAnidbIdByTvdb(tvdbId);
        if (fromMap is > 0)
        {
            _cache[tvdbId] = fromMap;
            return fromMap;
        }

        if (!string.IsNullOrWhiteSpace(title))
        {
            try
            {
                await _titles.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
                if (_titles.HasIndex)
                {
                    var match = _titles.Search(title).FirstOrDefault();
                    if (match?.Aid > 0)
                    {
                        _logger.LogInformation(
                            "TVDB {TvdbId} resolved to AniDB {Aid} via title match '{Title}'.",
                            tvdbId, match.Aid, match.Title);
                        _cache[tvdbId] = match.Aid;
                        return match.Aid;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Title-based AniDB resolution failed for TVDB {TvdbId}.", tvdbId);
            }
        }

        _cache[tvdbId] = null;
        return null;
    }
}