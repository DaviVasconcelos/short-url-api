using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ShortUrl.Application.Dtos;
using ShortUrl.Application.Services;

namespace ShortUrl.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>Creates an account and returns a JWT for the new user.</summary>
    /// <response code="201">Account created and authenticated.</response>
    /// <response code="400">The email or password is invalid.</response>
    /// <response code="409">The email is already registered.</response>
    /// <response code="429">Too many requests.</response>
    [HttpPost("register")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _authService.RegisterAsync(request, cancellationToken);

        return result.Status switch
        {
            RegisterStatus.Created => Created("/api/auth/me", result.Response),

            RegisterStatus.EmailConflict => Conflict(Problem(
                detail: "An account with this email already exists.",
                statusCode: StatusCodes.Status409Conflict,
                title: "Email already registered")),

            RegisterStatus.InvalidPassword => BadRequest(Problem(
                detail: "Password must be between 8 and 128 characters.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid password")),

            _ => BadRequest(Problem(
                detail: "A valid email address is required.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid email"))
        };
    }

    /// <summary>Authenticates an account and returns a JWT.</summary>
    /// <response code="200">Authentication succeeded.</response>
    /// <response code="401">The email or password is invalid.</response>
    /// <response code="429">Too many requests.</response>
    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _authService.LoginAsync(request, cancellationToken);

        return result.Status == LoginStatus.Success
            ? Ok(result.Response)
            : Unauthorized(Problem(
                detail: "Invalid email or password.",
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Invalid credentials"));
    }

    /// <summary>Returns the authenticated user.</summary>
    /// <response code="200">Authenticated user returned.</response>
    /// <response code="401">The token is missing or invalid.</response>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var subject = User.FindFirstValue(JwtRegisteredClaimNames.Sub);

        if (!int.TryParse(
                subject,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var userId))
        {
            return Unauthorized();
        }

        var user = await _authService.GetUserAsync(userId, cancellationToken);

        return user is null ? Unauthorized() : Ok(user);
    }
}
