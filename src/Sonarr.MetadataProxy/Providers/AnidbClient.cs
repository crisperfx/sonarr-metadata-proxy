using System.Collections.Concurrent;
using System.Globalization;
using System.Xml.Linq;
using Sonarr.MetadataProxy.Models.Anidb;
using Sonarr.MetadataProxy.Options;

namespace Sonarr.MetadataProxy.Providers;

public sealed class AnidbClient : IAnidbApi
{
    private const string Endpoint = "http://api.anidb.net:9001/httpapi";

    private readonly HttpClient _http;
    private readonly AnidbRateLimiter _rateLimiter;
    private readonly ILogger<AnidbClient> _logger;
    private readonly string _clientName;
    private readonly string _clientVersion;
    private readonly TimeSpan _cacheTtl;
    private readonly ConcurrentDictionary<int, CachedAnime> _cache = new();

    private sealed record CachedAnime(DateTimeOffset StoredAt, AnidbAnime? Anime);

    public AnidbClient(
        HttpClient http,
        AnidbRateLimiter rateLimiter,
        ProxyOptions options,
        ILogger<AnidbClient> logger)
    {
        _http = http;
        _rateLimiter = rateLimiter;
        _logger = logger;
        _clientName = options.AnidbClientName ?? string.Empty;
        _clientVersion = options.AnidbClientVersion ?? string.Empty;
        _cacheTtl = TimeSpan.FromMinutes(options.CacheTtlMinutes);
    }

    public async Task<AnidbAnime?> GetAnimeAsync(int anidbId, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(anidbId, out var cached) && DateTimeOffset.UtcNow - cached.StoredAt < _cacheTtl)
        {
            return cached.Anime;
        }

        var url = $"{Endpoint}?request=anime&aid={anidbId}&client={Uri.EscapeDataString(_clientName)}&clientver={Uri.EscapeDataString(_clientVersion)}&protover=1";

        await _rateLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);

        const int maxRetries = 2;
        var attempt = 0;

        while (true)
        {
            try
            {
                // ignore: content is XML (gzip is auto-decompressed by the HTTP handler)
                using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var status = (int)response.StatusCode;
                    _logger.LogWarning("AniDB returned {Status} for aid {AnidbId}.", status, anidbId);

                    var isRetryable = status is 429 or >= 500;
                    if (isRetryable && attempt < maxRetries)
                    {
                        attempt++;
                        var delay = TimeSpan.FromSeconds(2 * attempt);
                        _logger.LogInformation("Retrying AniDB request in {Delay}s (attempt {Attempt}/{MaxRetries}).", delay.TotalSeconds, attempt, maxRetries);
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    throw new AnidbApiException($"AniDB API error {status}.");
                }

                var xml = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var anime = AnidbXmlParser.Parse(xml);
                _cache[anidbId] = new CachedAnime(DateTimeOffset.UtcNow, anime);
                return anime;
            }
            catch (AnidbApiException)
            {
                throw;
            }
            catch (HttpRequestException ex)
            {
                throw new AnidbApiException("AniDB request failed.", ex);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new AnidbApiException("AniDB request timed out.", ex);
            }
        }
    }
}

public static class AnidbXmlParser
{
    public static AnidbAnime? Parse(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return null;
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (Exception ex)
        {
            throw new AnidbApiException("AniDB response could not be parsed.", ex);
        }

        var root = document.Root;
        if (root is null)
        {
            return null;
        }

        if (root.Name.LocalName == "error")
        {
            return null;
        }

        var idElement = root.Element("id");
        if (idElement is null || !int.TryParse(idElement.Value.Trim(), out var anidbId) || anidbId <= 0)
        {
            return null;
        }

        var titleElement = root.Element("titles")?
            .Elements("title")
            .FirstOrDefault(element => string.Equals((string?)element.Attribute("type"), "main", StringComparison.OrdinalIgnoreCase));

        var anime = new AnidbAnime
        {
            AnidbId = anidbId,
            Title = titleElement?.Value?.Trim(),
            Type = root.Element("type")?.Value?.Trim(),
            StartDate = root.Element("startdate")?.Value?.Trim(),
            EndDate = root.Element("enddate")?.Value?.Trim(),
            Description = root.Element("description")?.Value?.Trim(),
            Rating = ParseRating(root.Element("rating")?.Element("permanent")?.Value),
            Picture = root.Element("picture")?.Value?.Trim(),
            EpisodeCount = ParseInt(root.Element("episodecount")?.Value) ?? 0,
            Genres = ParseCategories(root),
            Episodes = ParseEpisodes(root)
        };

        return anime;
    }

    private static List<string> ParseCategories(XElement root)
    {
        var names = new List<string>();
        var categories = root.Element("categories")?.Elements("category");
        if (categories is null)
        {
            return names;
        }

        foreach (var category in categories)
        {
            var name = category.Element("name")?.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(name) && !names.Contains(name))
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static List<AnidbEpisode> ParseEpisodes(XElement root)
    {
        var episodes = new List<AnidbEpisode>();
        var episodeNodes = root.Element("episodes")?.Elements("episode");
        if (episodeNodes is null)
        {
            return episodes;
        }

        foreach (var node in episodeNodes)
        {
            var episode = ParseEpisode(node);
            if (episode is not null)
            {
                episodes.Add(episode);
            }
        }

        return episodes;
    }

    private static AnidbEpisode? ParseEpisode(XElement node)
    {
        if (!int.TryParse((string?)node.Attribute("id"), out var episodeId) || episodeId <= 0)
        {
            return null;
        }

        var epno = node.Element("epno");
        var type = ParseInt((string?)epno?.Attribute("type")) ?? 1;
        var number = ParseInt(epno?.Value) ?? 0;
        if (number <= 0)
        {
            return null;
        }

        return new AnidbEpisode
        {
            EpisodeId = episodeId,
            EpisodeNumber = number,
            Type = type,
            Title = node.Element("title")?.Value?.Trim(),
            AirDate = node.Element("airdate")?.Value?.Trim(),
            LengthMinutes = ParseInt(node.Element("length")?.Value),
            Rating = ParseRating(node.Element("rating")?.Value),
            Picture = node.Element("picture")?.Value?.Trim()
        };
    }

    private static double ParseRating(string? value)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    }

    private static int? ParseInt(string? value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }
}