using Microsoft.AspNetCore.Mvc;
using Sonarr.MetadataProxy.Passthrough;
using Sonarr.MetadataProxy.Services;

namespace Sonarr.MetadataProxy.Controllers;

[ApiController]
public sealed class SkyHookController : ControllerBase
{
    private readonly MetadataRequestHandler _handler;
    private readonly ILogger<SkyHookController> _logger;

    public SkyHookController(MetadataRequestHandler handler, ILogger<SkyHookController> logger)
    {
        _handler = handler;
        _logger = logger;
    }

    [HttpGet("/v1/tvdb/search/{language}")]
    public Task<IResult> SearchV1(string language, [FromQuery] string term, CancellationToken cancellationToken)
    {
        return Search(term, SkyHookApiVersion.V1, cancellationToken);
    }

    [HttpGet("/v2/tvdb/search")]
    public Task<IResult> SearchV2([FromQuery] string? language, [FromQuery] string term, CancellationToken cancellationToken)
    {
        return Search(term, SkyHookApiVersion.V2, cancellationToken);
    }

    [HttpGet("/v1/tvdb/shows/{language}/{tvdbId:int}")]
    public Task<IResult> ShowV1(string language, int tvdbId, CancellationToken cancellationToken)
    {
        return _handler.ShowAsync(tvdbId, cancellationToken, SkyHookApiVersion.V1);
    }

    [HttpGet("/v2/tvdb/shows/{language}/{tvdbId:int}/{seasonType}")]
    public Task<IResult> ShowV2(string language, int tvdbId, string seasonType, CancellationToken cancellationToken)
    {
        return _handler.ShowAsync(tvdbId, cancellationToken, SkyHookApiVersion.V2);
    }

    private Task<IResult> Search(string? term, SkyHookApiVersion version, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return Task.FromResult<IResult>(Results.Ok(Array.Empty<object>()));
        }

        return _handler.SearchAsync(term, cancellationToken, version);
    }
}
