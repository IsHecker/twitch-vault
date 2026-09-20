using System.Security.Claims;

namespace TwitchVault.Api.Auth;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        string userId = principal.FindFirstValue(ClaimTypes.NameIdentifier)!;
        return Guid.Parse(userId);
    }
}