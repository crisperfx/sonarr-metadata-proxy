using System.Collections.Concurrent;
using Sonarr.MetadataProxy.Options;

namespace Sonarr.MetadataProxy.Providers;

public sealed class AnidbImageProxy
{
    internal const string MainImageBase = "https://cdn.anidb.net/images/main/";

    private readonly HttpClient _http;
    private readonly ILogger<AnidbImageProxy> _logger;
    private readonly TimeSpan _cacheTtl;
    private readonly ConcurrentDictionary<string, CachedImage> _cache = new();

    private sealed record CachedImage(DateTimeOffset StoredAt, byte[] Bytes, string ContentType);

    public AnidbImageProxy(HttpClient http, ProxyOptions options, ILogger<AnidbImageProxy> logger)
    {
        _http = http;
        _logger = logger;
        _cacheTtl = TimeSpan.FromMinutes(options.CacheTtlMinutes);
    }

    public async Task<IResult> GetImageAsync(string picture, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(picture) || picture.Contains(".."))
        {
            return Results.NotFound();
        }

        if (_cache.TryGetValue(picture, out var cached) && DateTimeOffset.UtcNow - cached.StoredAt < _cacheTtl)
        {
            return Results.File(cached.Bytes, cached.ContentType, enableRangeProcessing: true);
        }

        var url = MainImageBase + picture;
        try
        {
            using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("AniDB image proxy: {RemoteStatus} for {Picture}.", (int)response.StatusCode, picture);
                return Results.StatusCode((int)response.StatusCode);
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
            _cache[picture] = new CachedImage(DateTimeOffset.UtcNow, bytes, contentType);
            return Results.File(bytes, contentType, enableRangeProcessing: true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AniDB image proxy failed for {Picture}.", picture);
            return Results.StatusCode(503);
        }
    }
}