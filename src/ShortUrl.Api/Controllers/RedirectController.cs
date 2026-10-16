using Microsoft.AspNetCore.Mvc;
using ShortUrl.Application.Services;

namespace ShortUrl.Api.Controllers;

[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class RedirectController : ControllerBase
{
    private readonly IShortUrlService _service;

    public RedirectController(IShortUrlService service)
    {
        _service = service;
    }

    [HttpGet("/{code}")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    public async Task<IActionResult> RedirectToOriginal(
        string code,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetOriginalAsync(code, cancellationToken);

        return result.Status switch
        {
            GetShortUrlStatus.Found => Redirect(result.OriginalUrl!),
            GetShortUrlStatus.Expired => StatusCode(StatusCodes.Status410Gone),
            _ => NotFound()
        };
    }
}
