namespace Sonarr.MetadataProxy.Options;

public sealed class ProxyOptions
{
    public string MetadataSource { get; init; } = "tmdb";
    public string? TmdbApiKey { get; init; }
    public string? TmdbApiToken { get; init; }
    public bool HasTmdbAuth => !string.IsNullOrWhiteSpace(TmdbApiKey) || !string.IsNullOrWhiteSpace(TmdbApiToken);
    public string TmdBLanguage { get; init; } = "en-US";
    public bool EnableTvdbFallback { get; init; } = true;
    public int Port { get; init; } = 9697;
    public string LogLevel { get; init; } = "Information";
    public string DataDir { get; init; } = "data";
    public int CacheTtlMinutes { get; init; } = 1440;
    public int SearchResultLimit { get; init; } = 10;
    public bool SkipTls { get; init; }
    public string SkyhookBaseUrl { get; init; } = "https://skyhook.sonarr.tv";
    public string SkyhookResolverUrl { get; init; } = "https://cloudflare-dns.com/dns-query";
    public string AniListDatamapDir { get; init; } = "datamaps";
    public string? AnidbClientName { get; init; }
    public string? AnidbClientVersion { get; init; }
    public bool HasAnidbClient => !string.IsNullOrWhiteSpace(AnidbClientName) && !string.IsNullOrWhiteSpace(AnidbClientVersion);
    public IReadOnlyList<string> CorsAllowedOrigins { get; init; } = Array.Empty<string>();

    public static ProxyOptions FromConfiguration(IConfiguration cfg)
    {
        return new ProxyOptions
        {
            MetadataSource = NonEmpty(cfg["METADATA_SOURCE"], "tmdb"),
            TmdbApiKey = TrimToNull(cfg["TMDB_API_KEY"]),
            TmdbApiToken = TrimToNull(cfg["TMDB_API_TOKEN"]),
            TmdBLanguage = NonEmpty(cfg["TMDb_LANGUAGE"], "en-US"),
            EnableTvdbFallback = ParseBool(cfg["ENABLE_TVDB_FALLBACK"], true),
            Port = ParseInt(cfg["PORT"], 9697),
            LogLevel = NonEmpty(cfg["LOG_LEVEL"], "Information"),
            DataDir = NonEmpty(cfg["DATA_DIR"], "data"),
            CacheTtlMinutes = ParseInt(cfg["CACHE_TTL_MINUTES"], 1440),
            SearchResultLimit = ParseInt(cfg["SEARCH_RESULT_LIMIT"], 10),
            SkipTls = ParseBool(cfg["SKIP_TLS"], false),
            SkyhookBaseUrl = NonEmpty(cfg["SKYHOOK_BASE_URL"], "https://skyhook.sonarr.tv"),
            SkyhookResolverUrl = NonEmpty(cfg["SKYHOOK_RESOLVER_URL"], "https://cloudflare-dns.com/dns-query"),
            AniListDatamapDir = NonEmpty(cfg["ANILIST_DATAMAP_DIR"], "datamaps"),
            AnidbClientName = TrimToNull(cfg["ANIDB_CLIENT"]),
            AnidbClientVersion = TrimToNull(cfg["ANIDB_CLIENT_VERSION"]),
            CorsAllowedOrigins = ParseList(cfg["CORS_ALLOWED_ORIGINS"])
        };
    }

    private static string? TrimToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string NonEmpty(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static int ParseInt(string? value, int fallback)
    {
        return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
    }

    private static bool ParseBool(string? value, bool fallback)
    {
        return bool.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static IReadOnlyList<string> ParseList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}