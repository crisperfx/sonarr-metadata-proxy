using Sonarr.MetadataProxy.Models.Metadata;

namespace Sonarr.MetadataProxy.Providers;

public sealed class NotImplementedProvider : IMetadataProvider
{
    public NotImplementedProvider(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public Task<IReadOnlyList<SeriesMetadata>> Search(string query, CancellationToken cancellationToken)
    {
        throw new NotSupportedException($"{Name} is not implemented yet.");
    }

    public Task<IReadOnlyList<SeriesMetadata>> SearchById(string providerId, CancellationToken cancellationToken)
    {
        throw new NotSupportedException($"{Name} is not implemented yet.");
    }

    public Task<IReadOnlyList<SeriesMetadata>> SearchByImdbId(string imdbId, CancellationToken cancellationToken)
    {
        throw new NotSupportedException($"{Name} is not implemented yet.");
    }

    public Task<SeriesMetadata> GetSeries(string providerId, CancellationToken cancellationToken)
    {
        throw new NotSupportedException($"{Name} is not implemented yet.");
    }

    public Task<IReadOnlyList<SeasonMetadata>> GetSeasons(string providerId, CancellationToken cancellationToken)
    {
        throw new NotSupportedException($"{Name} is not implemented yet.");
    }
}