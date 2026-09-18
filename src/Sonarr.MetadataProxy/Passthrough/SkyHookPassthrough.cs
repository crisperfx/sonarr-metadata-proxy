using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using Sonarr.MetadataProxy.Options;

namespace Sonarr.MetadataProxy.Passthrough;

public sealed record ProxyResponse(int StatusCode, string ContentType, string Body);

public interface ISkyHookPassthrough
{
    Task<ProxyResponse> SearchAsync(string rawTerm, CancellationToken cancellationToken);

    Task<ProxyResponse> ShowAsync(int tvdbId, CancellationToken cancellationToken);
}

public sealed class SkyHookPassthrough : ISkyHookPassthrough
{
    private readonly ILogger<SkyHookPassthrough> _logger;
    private readonly HttpClient _http;

    public SkyHookPassthrough(ProxyOptions options, RuntimeDnsResolver dns, ILogger<SkyHookPassthrough> logger)
    {
        _logger = logger;

        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectCallback = async (context, cancellationToken) =>
            {
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                var address = await dns.ResolveAsync(context.DnsEndPoint.Host, cancellationToken).ConfigureAwait(false)
                              ?? throw new HttpRequestException($"Could not resolve {context.DnsEndPoint.Host}.");
                await socket.ConnectAsync(address, context.DnsEndPoint.Port, cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
        };

        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri(options.SkyhookBaseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };

        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Sonarr.MetadataProxy/0.1 (TVDB fallback)");
    }

    public async Task<ProxyResponse> SearchAsync(string rawTerm, CancellationToken cancellationToken)
    {
        var term = Uri.EscapeDataString(rawTerm);
        return await GetAsync($"v1/tvdb/search/en/?term={term}", cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProxyResponse> ShowAsync(int tvdbId, CancellationToken cancellationToken)
    {
        return await GetAsync($"v1/tvdb/shows/en/{tvdbId}", cancellationToken).ConfigureAwait(false);
    }

    private async Task<ProxyResponse> GetAsync(string requestUri, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";
            return new ProxyResponse((int)response.StatusCode, contentType, body);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(ex, "TVDB fallback backend request failed for {Path}.", requestUri);
            return new ProxyResponse(503, "application/json",
                "{\"error\":\"TVDB fallback backend unreachable\"}");
        }
    }
}