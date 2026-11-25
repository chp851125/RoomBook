using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using RoomBook.Infrastructure.Security;

namespace RoomBook.Pages.Account;

/// <summary>Development 專用：快速切換登入使用者（不需密碼）。非 Development 一律 404。</summary>
public class SwitchUserModel(IUserService users, IAuditService audit, IWebHostEnvironment env) : AppPageModel
{
    public IActionResult OnGet() => NotFound();

    public async Task<IActionResult> OnPostAsync(Guid userId, string? returnUrl)
    {
        if (!env.IsDevelopment()) return NotFound();

        var target = users.GetById(userId);
        if (target is null || !target.IsActive) return NotFound();

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, ClaimsFactory.Create(target));
        audit.Log("快速切換使用者（Development）", nameof(User), target.Id.ToString(), target.DisplayName, target);
        return LocalRedirect(!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/");
    }
}
