using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RoomBook.Application.Services;

namespace RoomBook.Pages;

/// <summary>一般頁面的共同基底：提供目前使用者 Id 與訊息提示輔助。授權由 Program.cs 的資料夾慣例套用。</summary>
public abstract class AppPageModel : PageModel
{
    protected Guid CurrentUserId
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;

    protected bool IsAdmin => User.IsInRole(AuthConstants.AdminRole);

    protected void Success(string message) => TempData["Success"] = message;
    protected void Error(string message) => TempData["Error"] = message;
}

/// <summary>管理頁面的基底：在 PageModel 層級要求 Admin 角色（與資料夾慣例雙重保護，不靠隱藏按鈕）。</summary>
[Authorize(Roles = AuthConstants.AdminRole)]
public abstract class AdminPageModel : AppPageModel
{
}
