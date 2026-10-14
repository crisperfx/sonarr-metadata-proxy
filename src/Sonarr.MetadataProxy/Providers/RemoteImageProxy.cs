using System.Collections.Concurrent;
using System.Net.Http;
using Sonarr.MetadataProxy.Options;
using Sonarr.MetadataProxy.Services;

namespace Sonarr.MetadataProxy.Providers;

public sealed class RemoteImageProxy
{
    private readonly HttpClient _http;
    private readonly ILogger<RemoteImageProxy> _logger;
    private readonly TimeSpan _cacheTtl;
    private readonly ConcurrentDictionary<string, CachedImage> _cache = new();

    private sealed record CachedImage(DateTimeOffset StoredAt, byte[] Bytes, string ContentType);

    public RemoteImageProxy(HttpClient http, ProxyOptions options, ILogger<RemoteImageProxy> logger)
    {
        _http = http;
        _logger = logger;
        _cacheTtl = TimeSpan.FromMinutes(options.CacheTtlMinutes);
    }

    public async Task<IResult> GetImageAsync(string pictureUrl, CancellationToken ct)
    {
        if (!Uri.TryCreate(pictureUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !ImageUrlRewriter.IsAllowedHost(uri.Host))
        {
            return Results.NotFound();
        }

        if (_cache.TryGetValue(pictureUrl, out var cached) && DateTimeOffset.UtcNow - cached.StoredAt < _cacheTtl)
        {
            return Results.File(cached.Bytes, cached.ContentType, enableRangeProcessing: true);
        }

        try
        {
            using var response = await _http.GetAsync(pictureUrl, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Image proxy: {RemoteStatus} for {Url}", (int)response.StatusCode, pictureUrl);
                return Results.StatusCode((int)response.StatusCode);
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
            _cache[pictureUrl] = new CachedImage(DateTimeOffset.UtcNow, bytes, contentType);

            return Results.File(bytes, contentType, enableRangeProcessing: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Image proxy failed for {Url}", pictureUrl);
            return Results.StatusCode(503);
        }
    }
}
