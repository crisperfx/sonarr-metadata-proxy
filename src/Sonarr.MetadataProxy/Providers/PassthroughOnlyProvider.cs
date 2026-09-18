using Sonarr.MetadataProxy.Models.Metadata;

namespace Sonarr.MetadataProxy.Providers;

public sealed class PassthroughOnlyProvider : IMetadataProvider
{
    public string Name => "tvdb";

    public Task<IReadOnlyList<SeriesMetadata>> Search(string query, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<SeriesMetadata>>(Array.Empty<SeriesMetadata>());
    }

    public Task<IReadOnlyList<SeriesMetadata>> SearchById(string providerId, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<SeriesMetadata>>(Array.Empty<SeriesMetadata>());
    }

    public Task<IReadOnlyList<SeriesMetadata>> SearchByImdbId(string imdbId, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<SeriesMetadata>>(Array.Empty<SeriesMetadata>());
    }

    public Task<SeriesMetadata> GetSeries(string providerId, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Passthrough-only provider cannot resolve series metadata.");
    }

    public Task<IReadOnlyList<SeasonMetadata>> GetSeasons(string providerId, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<SeasonMetadata>>(Array.Empty<SeasonMetadata>());
    }
}