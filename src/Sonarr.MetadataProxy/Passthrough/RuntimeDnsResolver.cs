using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using Sonarr.MetadataProxy.Options;
using Sonarr.MetadataProxy.Tls;

namespace Sonarr.MetadataProxy.Passthrough;

/// <summary>
/// Resolves the real address of the (intercepted) SkyHook hostname via DNS-over-HTTPS.
/// Docker's embedded DNS answers for the network alias <c>skyhook.sonarr.tv</c> and would
/// otherwise point the TVDB passthrough back at the proxy itself (infinite loop). A
/// hardcoded extra_hosts entry is fragile because the address is Cloudflare anycast and
/// can change, so we resolve it at runtime against a fixed DoH endpoint instead.
/// </summary>
public sealed class RuntimeDnsResolver
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly ProxyOptions _options;
    private readonly ILogger<RuntimeDnsResolver> _logger;
    private readonly HttpClient _http;
    private readonly ConcurrentDictionary<string, CachedRecord> _cache = new();

    public RuntimeDnsResolver(ProxyOptions options, ILogger<RuntimeDnsResolver> logger)
    {
        _options = options;
        _logger = logger;

        _http = new HttpClient
        {
            BaseAddress = new Uri(_options.SkyhookResolverUrl),
            Timeout = TimeSpan.FromSeconds(5)
        };
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/dns-json"));
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SonarrMetadataProxy/0.1 (DNS resolver)");
    }

    /// <summary>
    /// Returns a usable A record for <paramref name="host"/>, or <c>null</c> when it cannot be
    /// determined. For the intercepted hostname the system resolver is deliberately NOT used as
    /// a fallback (Docker DNS would answer with the network alias and create a self-loop).
    /// </summary>
    public async Task<IPAddress?> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(host, out var cached) && cached.Expires > DateTime.UtcNow)
        {
            return cached.Address;
        }

        var address = await ResolveViaDoHAsync(host, cancellationToken).ConfigureAwait(false);
        address ??= await ResolveViaSystemDnsAsync(host, cancellationToken).ConfigureAwait(false);

        if (address is null)
        {
            _logger.LogWarning("Could not resolve {Host}; the TVDB passthrough will fail (503).", host);
            return null;
        }

        _cache[host] = new CachedRecord(address, DateTime.UtcNow.Add(CacheTtl));
        _logger.LogInformation("Resolved {Host} to {Address}.", host, address);
        return address;
    }

    private async Task<IPAddress?> ResolveViaDoHAsync(string host, CancellationToken cancellationToken)
    {
        try
        {
            var query = Uri.EscapeDataString(host);
            using var response = await _http.GetAsync($"?name={query}&type=A", cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("DNS resolver returned {(int)StatusCode} for {Host}.", (int)response.StatusCode, host);
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var address = ParseFirstAddress(body);
            if (address is null)
            {
                _logger.LogWarning("DNS resolver returned no A record for {Host}.", host);
            }

            return address;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogError(ex, "DNS-over-HTTPS resolution failed for {Host}.", host);
            return null;
        }
    }

    private async Task<IPAddress?> ResolveViaSystemDnsAsync(string host, CancellationToken cancellationToken)
    {
        if (string.Equals(host, CertificateProvider.HostName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
            return addresses.FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork);
        }
        catch (Exception ex) when (ex is SocketException or System.Net.Http.HttpRequestException)
        {
            _logger.LogWarning(ex, "System DNS resolution failed for {Host}.", host);
            return null;
        }
    }

    private static IPAddress? ParseFirstAddress(string dnsJson)
    {
        using var document = JsonDocument.Parse(dnsJson);
        if (!document.RootElement.TryGetProperty("Answer", out var answer))
        {
            return null;
        }

        foreach (var record in answer.EnumerateArray())
        {
            if (record.TryGetProperty("type", out var type) && type.GetInt32() == 1 &&
                record.TryGetProperty("data", out var data) &&
                IPAddress.TryParse(data.GetString(), out var address))
            {
                return address;
            }
        }

        return null;
    }

    private sealed record CachedRecord(IPAddress Address, DateTime Expires);
}