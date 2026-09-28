namespace Sonarr.MetadataProxy.Infrastructure;

public static class LogSanitizer
{
    public static string Sanitize(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input ?? string.Empty;
        }
        return input.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
    }
}