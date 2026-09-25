using Sonarr.MetadataProxy.Options;
using Sonarr.MetadataProxy.Providers;

namespace Sonarr.MetadataProxy.Services;

public static class MetadataProviderRegistry
{
    public static IMetadataProvider Create(string source, IServiceProvider serviceProvider)
    {
        switch ((source ?? string.Empty).ToLowerInvariant())
        {
            case "tmdb":
                return serviceProvider.GetRequiredService<TmdbMetadataProvider>();
            case "mal":
                return serviceProvider.GetRequiredService<MalMetadataProvider>();
            case "anilist":
                return serviceProvider.GetRequiredService<AniListMetadataProvider>();
            case "tvmaze":
                return serviceProvider.GetRequiredService<TvmazeMetadataProvider>();
            case "anidb":
                return serviceProvider.GetRequiredService<AnidbMetadataProvider>();
            case "imdb":
                return new NotImplementedProvider("imdb");
            case "tvdb":
            default:
                return new PassthroughOnlyProvider();
        }
    }
}