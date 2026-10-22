using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Net.Http.Headers;
using ShortUrl.Api.Extensions;

namespace ShortUrl.Api.Filters;

[AttributeUsage(AttributeTargets.Method)]
public sealed class RequireValidOptionalTokenAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var request = context.HttpContext.Request;

        if (request.Headers.ContainsKey(HeaderNames.Authorization) &&
            context.HttpContext.User.GetUserId() is null)
        {
            context.Result = new UnauthorizedObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Invalid token",
                Detail = "The Authorization header is invalid or expired."
            });
        }
    }
}
