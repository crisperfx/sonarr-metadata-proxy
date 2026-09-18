using Microsoft.Extensions.Configuration;
using Sonarr.MetadataProxy.Options;
using Xunit;

namespace Sonarr.MetadataProxy.Tests;

public class ProxyOptionsTests
{
    [Fact]
    public void CorsAllowedOrigins_EmptyWhenUnset()
    {
        var options = ProxyOptions.FromConfiguration(BuildConfig(new Dictionary<string, string?>()));

        Assert.Empty(options.CorsAllowedOrigins);
    }

    [Fact]
    public void CorsAllowedOrigins_ParsesCommaSeparatedList()
    {
        var options = ProxyOptions.FromConfiguration(BuildConfig(new Dictionary<string, string?>
        {
            ["CORS_ALLOWED_ORIGINS"] = "http://192.168.0.142:8989, https://sonarr.local:8989"
        }));

        Assert.Equal(new[] { "http://192.168.0.142:8989", "https://sonarr.local:8989" }, options.CorsAllowedOrigins);
    }

    [Fact]
    public void CorsAllowedOrigins_TrimsBlankEntries()
    {
        var options = ProxyOptions.FromConfiguration(BuildConfig(new Dictionary<string, string?>
        {
            ["CORS_ALLOWED_ORIGINS"] = "  , http://localhost:8989 ,,"
        }));

        Assert.Equal(new[] { "http://localhost:8989" }, options.CorsAllowedOrigins);
    }

    private static IConfiguration BuildConfig(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}