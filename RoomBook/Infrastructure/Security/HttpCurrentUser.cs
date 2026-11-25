using System.Security.Claims;
using RoomBook.Application.Services;

namespace RoomBook.Infrastructure.Security;

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? UserId
        => Guid.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public string Name => Principal?.Identity?.Name ?? "";

    public bool IsAdmin => Principal?.IsInRole(AuthConstants.AdminRole) == true;

    public string IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "-";
}

public static class ClaimsFactory
{
    public static ClaimsPrincipal Create(User user)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.DisplayName),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim(AuthConstants.DepartmentClaim, user.DepartmentId.ToString())
        ], Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        return new ClaimsPrincipal(identity);
    }
}
