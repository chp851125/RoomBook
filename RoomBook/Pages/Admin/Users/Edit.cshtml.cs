using Microsoft.AspNetCore.Mvc;
using RoomBook.Application.Dtos;

namespace RoomBook.Pages.Admin.Users;

public class EditModel(IUserService users, IUserAdminService userAdmin) : AdminPageModel
{
    [BindProperty] public UserEditDto Input { get; set; } = new();
    public IReadOnlyList<Department> Departments { get; private set; } = [];
    public IReadOnlyList<Violation> Violations { get; private set; } = [];
    public User? Target { get; private set; }
    public bool IsNew => Input.Id is null;
    public bool IsSuspended => Target is not null && users.IsSuspended(Target);
    /// <summary>剛建立或重設的臨時密碼（只顯示這一次）。</summary>
    public string? TempPassword { get; private set; }

    public IActionResult OnGet(Guid? id)
    {
        Departments = users.GetDepartments();
        if (id is null) { Input.DepartmentId = Departments.FirstOrDefault()?.Id ?? Guid.Empty; return Page(); }
        return Load(id.Value) ? Page() : NotFound();
    }

    public IActionResult OnPostSave()
    {
        Departments = users.GetDepartments();
        if (Input.Id is null)
        {
            var created = userAdmin.Create(Input, CurrentUserId);
            if (!created.Success) { ModelState.AddModelError(string.Empty, created.Error!); return Page(); }
            TempData["TempPassword"] = created.Password;
            Success($"已新增 {Input.DisplayName}。請把臨時密碼交給本人（首次登入須變更）。");
            return RedirectToPage(new { id = users.GetByAccount(Input.Account.Trim())!.Id });
        }
        var result = userAdmin.Update(Input, CurrentUserId);
        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            Load(Input.Id.Value, keepInput: true);
            return Page();
        }
        Success("已儲存使用者資料。");
        return RedirectToPage(new { id = Input.Id });
    }

    public IActionResult OnPostResetPassword(Guid id)
    {
        var result = userAdmin.ResetPassword(id, CurrentUserId);
        if (result.Success) { TempData["TempPassword"] = result.Password; Success("密碼已重設，請把臨時密碼交給本人（下次登入須變更）。"); }
        else Error(result.Error!);
        return RedirectToPage(new { id });
    }

    public IActionResult OnPostRelease(Guid id)
    {
        var result = userAdmin.ReleaseSuspension(id, CurrentUserId);
        if (result.Success) Success("已解除預約暫停，違規點數歸零。"); else Error(result.Error!);
        return RedirectToPage(new { id });
    }

    private bool Load(Guid id, bool keepInput = false)
    {
        Departments = users.GetDepartments();
        Target = users.GetById(id);
        if (Target is null) return false;
        if (!keepInput)
            Input = new UserEditDto
            {
                Id = Target.Id, Version = Target.Version, Account = Target.Account, DisplayName = Target.DisplayName, Email = Target.Email,
                DepartmentId = Target.DepartmentId, JobTitle = Target.JobTitle, Role = Target.Role, IsActive = Target.IsActive
            };
        Violations = userAdmin.GetViolations(id);
        TempPassword = TempData["TempPassword"] as string;
        return true;
    }
}
