using Sonarr.MetadataProxy.Providers;

namespace Sonarr.MetadataProxy.Reverse;

public interface ITvdbToTvmazeResolver
{
    Task<int?> ResolveTvmazeIdAsync(int tvdbId, string? title, int? year, CancellationToken cancellationToken);
}