using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace ShortUrl.Api.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static int? GetUserId(this ClaimsPrincipal principal)
    {
        var subject = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);

        return int.TryParse(
            subject,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var userId) && userId > 0
            ? userId
            : null;
    }
}
