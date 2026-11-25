using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace RoomBook.ViewComponents;

public record DevToolbarModel(IReadOnlyList<User> Users, Guid CurrentUserId, bool AllowReset);

/// <summary>Development 專用工具列：快速切換使用者、重置範例資料。</summary>
public class DevToolbarViewComponent(IUserService users) : ViewComponent
{
    public IViewComponentResult Invoke(bool allowReset)
    {
        var current = Guid.TryParse(UserClaimsPrincipal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
        return View(new DevToolbarModel(users.GetActiveUsers(), current, allowReset));
    }
}
