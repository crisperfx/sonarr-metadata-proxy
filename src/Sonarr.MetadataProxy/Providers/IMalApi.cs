using Sonarr.MetadataProxy.Models.Mal;

namespace Sonarr.MetadataProxy.Providers;

public interface IMalApi
{
    Task<IReadOnlyList<MalAnime>> SearchAsync(string query, CancellationToken cancellationToken);

    Task<MalAnime?> GetByIdAsync(int malId, CancellationToken cancellationToken);
}

public sealed class MalApiException : Exception
{
    public MalApiException(string message) : base(message)
    {
    }

    public MalApiException(string message, Exception innerException) : base(message, innerException)
    {
    }
}