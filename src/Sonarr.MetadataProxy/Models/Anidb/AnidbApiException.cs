namespace Sonarr.MetadataProxy.Models.Anidb;

public sealed class AnidbApiException : Exception
{
    public AnidbApiException(string message)
        : base(message)
    {
    }

    public AnidbApiException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}