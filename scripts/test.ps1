dotnet test src/Sonarr.MetadataProxy.Tests/Sonarr.MetadataProxy.Tests.csproj

# Restore + run only the fast unit tests:
# dotnet test src/Sonarr.MetadataProxy.Tests/Sonarr.MetadataProxy.Tests.csproj --filter "Category!=integration"