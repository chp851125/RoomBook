using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RoomBook.Application.Dtos;
using RoomBook.Infrastructure.Security;

namespace RoomBook.Pages.Account;

public class LoginModel(IAuthService auth, IWebHostEnvironment env) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool Reset { get; set; }

    public bool ShowDemoHint => env.IsDevelopment();

    public class InputModel
    {
        [Required(ErrorMessage = "請輸入帳號")]
        [Display(Name = "帳號")]
        public string Account { get; set; } = "";

        [Required(ErrorMessage = "請輸入密碼")]
        [DataType(DataType.Password)]
        [Display(Name = "密碼")]
        public string Password { get; set; } = "";

        [Display(Name = "保持登入")]
        public bool RememberMe { get; set; }
    }

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
            return LocalRedirect(SafeReturnUrl());
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var result = auth.Login(Input.Account, Input.Password);
        switch (result.Status)
        {
            case LoginStatus.Success:
                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    ClaimsFactory.Create(result.User!),
                    new AuthenticationProperties { IsPersistent = Input.RememberMe });
                if (result.User!.MustChangePassword) return RedirectToPage("/Account/Settings", new { force = true });
                return LocalRedirect(SafeReturnUrl());
            case LoginStatus.LockedOut:
                ModelState.AddModelError(string.Empty, $"登入失敗次數過多，帳號已暫時鎖定，請約 {result.LockoutMinutesLeft} 分鐘後再試。");
                break;
            case LoginStatus.Disabled:
                ModelState.AddModelError(string.Empty, "此帳號已停用，請洽管理員。");
                break;
            default:
                ModelState.AddModelError(string.Empty, "帳號或密碼錯誤。");
                break;
        }
        Input.Password = "";
        return Page();
    }

    private string SafeReturnUrl() => !string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : "/";
}
