namespace Sonarr.MetadataProxy.Models.Tvmaze;

public sealed class TvmazeApiException : Exception
{
    public TvmazeApiException(string message)
        : base(message)
    {
    }

    public TvmazeApiException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}