using Sonarr.MetadataProxy.Models.Mal;

namespace Sonarr.MetadataProxy.Providers;

public interface IMalApi
{
    Task<IReadOnlyList<MalAnime>> SearchAsync(string query, CancellationToken cancellationToken);

    Task<MalAnime?> GetByIdAsync(int malId, CancellationToken cancellationToken);

    Task<MalPictures?> GetPicturesAsync(int malId, CancellationToken cancellationToken);
}

public sealed class MalPictures
{
    public List<string> Posters { get; init; } = new();
    public List<string> Backgrounds { get; init; } = new();
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