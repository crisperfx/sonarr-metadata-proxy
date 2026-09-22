using Sonarr.MetadataProxy.Models.AniList;

namespace Sonarr.MetadataProxy.Providers;

public interface IAniListApi
{
    Task<IReadOnlyList<AniListMedia>> SearchAsync(string query, CancellationToken cancellationToken);

    Task<AniListMedia?> GetByIdAsync(int anilistId, CancellationToken cancellationToken);
}

public sealed class AniListApiException : Exception
{
    public AniListApiException(string message) : base(message)
    {
    }

    public AniListApiException(string message, Exception innerException) : base(message, innerException)
    {
    }
}