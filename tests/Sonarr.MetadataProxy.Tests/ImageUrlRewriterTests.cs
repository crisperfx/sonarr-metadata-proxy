using Microsoft.AspNetCore.Http;
using Sonarr.MetadataProxy.Services;
using Xunit;

namespace Sonarr.MetadataProxy.Tests;

public class ImageUrlRewriterTests
{
    private static ImageUrlRewriter Rewriter(string? host = "skyhook.sonarr.tv", string scheme = "https")
    {
        var accessor = new HttpContextAccessor();
        if (host is not null)
        {
            var context = new DefaultHttpContext();
            context.Request.Scheme = scheme;
            context.Request.Host = new HostString(host);
            accessor.HttpContext = context;
        }

        return new ImageUrlRewriter(accessor);
    }

    [Fact]
    public void Rewrite_ReturnsRequestDerivedProxyUrl_ForAllowedHost()
    {
        var rewriter = Rewriter();
        var url = "https://static.tvmaze.com/uploads/images/original_untouched/1/poster.jpg";

        var rewritten = rewriter.Rewrite(url);

        Assert.Equal(
            "https://skyhook.sonarr.tv/api/images/remote?url=" + Uri.EscapeDataString(url),
            rewritten);
    }

    [Fact]
    public void Rewrite_UsesRequestSchemeAndHost()
    {
        var rewriter = Rewriter("192.168.1.50:9697", "http");
        var url = "https://image.tmdb.org/t/p/w342/abc.jpg";

        var rewritten = rewriter.Rewrite(url);

        Assert.Equal(
            "http://192.168.1.50:9697/api/images/remote?url=" + Uri.EscapeDataString(url),
            rewritten);
    }

    [Fact]
    public void Rewrite_LeavesUrl_WhenNoRequestContext()
    {
        var rewriter = Rewriter(host: null);
        var url = "https://static.tvmaze.com/uploads/images/original_untouched/1/poster.jpg";

        Assert.Equal(url, rewriter.Rewrite(url));
    }

    [Fact]
    public void Rewrite_LeavesUrl_ForDisallowedHost()
    {
        var rewriter = Rewriter();
        var url = "https://example.com/poster.jpg";

        Assert.Equal(url, rewriter.Rewrite(url));
    }

    [Fact]
    public void Rewrite_LeavesUrl_WhenAlreadyProxied()
    {
        var rewriter = Rewriter();
        var url = "https://skyhook.sonarr.tv/api/images/anidb/1";

        Assert.Equal(url, rewriter.Rewrite(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rewrite_LeavesBlankUrl(string? url)
    {
        Assert.Equal(url, Rewriter().Rewrite(url));
    }

    [Theory]
    [InlineData("static.tvmaze.com", true)]
    [InlineData("tvmaze.com", true)]
    [InlineData("artworks.thetvdb.com", true)]
    [InlineData("image.tmdb.org", true)]
    [InlineData("cdn.anidb.net", true)]
    [InlineData("cdn.myanimelist.net", true)]
    [InlineData("s4.anilist.co", true)]
    [InlineData("example.com", false)]
    [InlineData("nottvmaze.com", false)]
    [InlineData("tvmaze.com.evil.com", false)]
    public void IsAllowedHost_MatchesSuffixes(string host, bool expected)
    {
        Assert.Equal(expected, ImageUrlRewriter.IsAllowedHost(host));
    }
}
