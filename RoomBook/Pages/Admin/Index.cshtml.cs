using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace RoomBook.Pages.Admin;

public class IndexModel(
    IUserService users,
    IRoomService rooms,
    IAdminBookingService adminBookings,
    IDataResetService reset,
    IAuditService audit,
    IConfiguration config) : AdminPageModel
{
    public int UserCount { get; private set; }
    public int ActiveUserCount { get; private set; }
    public int RoomCount { get; private set; }
    public int DepartmentCount { get; private set; }
    public int PendingCount { get; private set; }
    public int SuspendedCount { get; private set; }
    public bool AllowReset => config.GetValue<bool>("Features:AllowDataReset");

    public void OnGet()
    {
        var all = users.GetAll();
        UserCount = all.Count;
        ActiveUserCount = all.Count(u => u.IsActive);
        SuspendedCount = all.Count(users.IsSuspended);
        RoomCount = rooms.GetAll().Count;
        DepartmentCount = users.GetDepartments().Count;
        PendingCount = adminBookings.GetPending().Count;
    }

    /// <summary>重置為範例資料：僅管理員（PageModel 層級授權），完成後登出並回到登入頁，並寫入稽核紀錄。</summary>
    public async Task<IActionResult> OnPostResetAsync()
    {
        if (!AllowReset) return NotFound();

        var actorId = CurrentUserId;
        var result = reset.Reset();
        if (!result.Success)
        {
            Error(result.Message);
            return RedirectToPage();
        }

        // 範例資料的 Id 是固定的，重置後仍可找到同一位管理員來記錄稽核
        audit.Log("重置為範例資料", "System", null, "清空 App_Data 並從 SeedData 重新複製", users.GetById(actorId));
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToPage("/Account/Login", new { reset = true });
    }
}
