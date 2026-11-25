using Microsoft.AspNetCore.Mvc;
using RoomBook.Application.Dtos;

namespace RoomBook.Pages.Admin.Settings;

public class IndexModel(ISettingAdminService settingAdmin) : AdminPageModel
{
    [BindProperty] public List<SettingEditDto> Values { get; set; } = new();
    public IReadOnlyList<AppSetting> Settings { get; private set; } = [];

    public void OnGet() => Settings = settingAdmin.GetAll();

    public IActionResult OnPost()
    {
        var r = settingAdmin.Save(Values, CurrentUserId);
        if (r.Success) { Success("設定已儲存，立即生效。"); return RedirectToPage(); }
        Error(r.Error!);
        Settings = settingAdmin.GetAll();
        return Page();
    }

    public string? ValueFor(AppSetting s) => Values.FirstOrDefault(v => v.Key == s.Key)?.Value ?? s.Value;
}
