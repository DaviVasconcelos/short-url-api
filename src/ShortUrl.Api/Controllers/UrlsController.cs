using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ShortUrl.Application.Dtos;
using ShortUrl.Application.Services;

namespace ShortUrl.Api.Controllers;

[ApiController]
[Route("api/urls")]
public sealed class UrlsController : ControllerBase
{
    private readonly IShortUrlService _service;

    public UrlsController(IShortUrlService service)
    {
        _service = service;
    }

    /// <summary>Shortens a URL, optionally with a custom alias and expiration.</summary>
    /// <response code="201">Short URL created. The <c>Location</c> header points to the stats resource.</response>
    /// <response code="400">The URL or alias is invalid.</response>
    /// <response code="409">The custom alias is already in use.</response>
    /// <response code="429">Too many requests.</response>
    [HttpPost]
    [EnableRateLimiting("create-url")]
    [ProducesResponseType(typeof(ShortUrlResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Create(
        [FromBody] CreateShortUrlRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.CreateAsync(request, cancellationToken);

        return result.Status switch
        {
            CreateShortUrlStatus.Created => CreatedAtAction(
                nameof(GetStats),
                new { code = result.Response!.ShortCode },
                result.Response),

            CreateShortUrlStatus.AliasConflict => Conflict(Problem(
                detail: "The custom alias is already in use.",
                statusCode: StatusCodes.Status409Conflict,
                title: "Alias conflict")),

            CreateShortUrlStatus.InvalidAlias => BadRequest(Problem(
                detail: "The custom alias is invalid or reserved.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid alias")),

            _ => BadRequest(Problem(
                detail: "The provided URL is invalid. Only absolute http/https URLs up to 2048 characters are accepted.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid URL"))
        };
    }

    /// <summary>Returns statistics for a short URL.</summary>
    /// <response code="200">Statistics returned.</response>
    /// <response code="404">The short code does not exist.</response>
    [HttpGet("{code}")]
    [ProducesResponseType(typeof(ShortUrlStatsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStats(
        string code,
        CancellationToken cancellationToken)
    {
        var stats = await _service.GetStatsAsync(code, cancellationToken);

        return stats is null ? NotFound() : Ok(stats);
    }

    /// <summary>Deletes a short URL.</summary>
    /// <response code="204">Deleted.</response>
    /// <response code="404">The short code does not exist.</response>
    [HttpDelete("{code}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        string code,
        CancellationToken cancellationToken)
    {
        var deleted = await _service.DeleteAsync(code, cancellationToken);

        return deleted ? NoContent() : NotFound();
    }
}
