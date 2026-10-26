using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ShortUrl.Api.Extensions;
using ShortUrl.Api.Filters;
using ShortUrl.Application.Dtos;
using ShortUrl.Application.Services;

namespace ShortUrl.Api.Controllers;

[ApiController]
[Route("api/urls")]
public sealed class UrlsController : ControllerBase
{
    private const int DefaultPage = 1;
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    private readonly IShortUrlService _service;

    public UrlsController(IShortUrlService service)
    {
        _service = service;
    }

    /// <summary>Shortens a URL, optionally with a custom alias and expiration.</summary>
    /// <response code="201">Short URL created. The <c>Location</c> header points to the stats resource.</response>
    /// <response code="400">The URL or alias is invalid.</response>
    /// <response code="401">The Authorization header is invalid or expired.</response>
    /// <response code="409">The custom alias is already in use.</response>
    /// <response code="429">Too many requests.</response>
    [HttpPost]
    [EnableRateLimiting("create-url")]
    [RequireValidOptionalToken]
    [ProducesResponseType(typeof(ShortUrlResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Create(
        [FromBody] CreateShortUrlRequest request,
        CancellationToken cancellationToken)
    {
        var ownerId = User.GetUserId();
        var result = await _service.CreateAsync(request, ownerId, cancellationToken);

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

    /// <summary>Lists short URLs owned by the authenticated user.</summary>
    /// <param name="page">Page number, starting at 1.</param>
    /// <param name="pageSize">Page size, between 1 and 100.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">Page of short URLs returned.</response>
    /// <response code="400"><c>page</c> or <c>pageSize</c> is invalid.</response>
    /// <response code="401">The token is missing or invalid.</response>
    [HttpGet]
    [Authorize]
    [ProducesResponseType(typeof(PagedShortUrlResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> List(
        [FromQuery, Range(1, int.MaxValue)] int page = DefaultPage,
        [FromQuery, Range(1, MaxPageSize)] int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var userId = User.GetUserId();

        if (userId is null)
            return Unauthorized();

        var result = await _service.GetUserLinksAsync(
            userId.Value,
            page,
            pageSize,
            cancellationToken);

        return Ok(result);
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

    /// <summary>Deletes a short URL owned by the authenticated user.</summary>
    /// <response code="204">Deleted.</response>
    /// <response code="401">The token is missing or invalid.</response>
    /// <response code="403">The authenticated user does not own this URL.</response>
    /// <response code="404">The short code does not exist.</response>
    [HttpDelete("{code}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        string code,
        CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();

        if (userId is null)
            return Unauthorized();

        var result = await _service.DeleteAsync(code, userId, cancellationToken);

        return result.Status switch
        {
            DeleteShortUrlStatus.Deleted => NoContent(),
            DeleteShortUrlStatus.NotFound => NotFound(),
            _ => StatusCode(StatusCodes.Status403Forbidden)
        };
    }
}
