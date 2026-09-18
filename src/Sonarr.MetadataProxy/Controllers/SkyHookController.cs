using Microsoft.AspNetCore.Mvc;
using Sonarr.MetadataProxy.Services;

namespace Sonarr.MetadataProxy.Controllers;

[ApiController]
[Route("v1/tvdb")]
public sealed class SkyHookController : ControllerBase
{
    private readonly MetadataRequestHandler _handler;
    private readonly ILogger<SkyHookController> _logger;

    public SkyHookController(MetadataRequestHandler handler, ILogger<SkyHookController> logger)
    {
        _handler = handler;
        _logger = logger;
    }

    [HttpGet("search/{language}")]
    public Task<IResult> Search(string language, [FromQuery] string term, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return Task.FromResult<IResult>(Results.Ok(Array.Empty<object>()));
        }

        return _handler.SearchAsync(term, cancellationToken);
    }

    [HttpGet("shows/{language}/{tvdbId:int}")]
    public Task<IResult> Show(string language, int tvdbId, CancellationToken cancellationToken)
    {
        return _handler.ShowAsync(tvdbId, cancellationToken);
    }
}