using Microsoft.AspNetCore.Http;

namespace Sonarr.MetadataProxy.Services;

public sealed class ImageUrlRewriter
{
    public const string ProxyPath = "/api/images/remote";

    private static readonly string[] AllowedHostSuffixes =
    {
        "tvmaze.com",
        "thetvdb.com",
        "tmdb.org",
        "anidb.net",
        "myanimelist.net",
        "anilist.co",
        "kitsu.io"
    };

    private readonly IHttpContextAccessor _accessor;

    public ImageUrlRewriter(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    public string? Rewrite(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return url;
        }

        if (uri.AbsolutePath.Contains("/api/images/", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        if (!IsAllowedHost(uri.Host))
        {
            return url;
        }

        var request = _accessor.HttpContext?.Request;
        if (request is null)
        {
            return url;
        }

        return $"{request.Scheme}://{request.Host}{ProxyPath}?url={Uri.EscapeDataString(url)}";
    }

    public static bool IsAllowedHost(string host)
    {
        foreach (var suffix in AllowedHostSuffixes)
        {
            if (host.Equals(suffix, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
