using Sonarr.MetadataProxy.Models.Metadata;

namespace Sonarr.MetadataProxy.Providers;

public interface IMetadataProvider
{
    string Name { get; }

    Task<IReadOnlyList<SeriesMetadata>> Search(string query, CancellationToken cancellationToken);

    Task<IReadOnlyList<SeriesMetadata>> SearchById(string providerId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SeriesMetadata>> SearchByImdbId(string imdbId, CancellationToken cancellationToken);

    Task<SeriesMetadata> GetSeries(string providerId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SeasonMetadata>> GetSeasons(string providerId, CancellationToken cancellationToken);
}