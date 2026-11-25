using Microsoft.AspNetCore.Mvc;
using RoomBook.Application.Dtos;

namespace RoomBook.Pages.Admin.Hours;

public class IndexModel(IBusinessCalendarService calendar, IScheduleAdminService schedule) : AdminPageModel
{
    [BindProperty] public List<HoursEditDto> Hours { get; set; } = new();
    public IReadOnlyList<Holiday> Holidays { get; private set; } = [];
    [BindProperty] public DateOnly? HolidayDate { get; set; }
    [BindProperty] public string? HolidayName { get; set; }

    public void OnGet() => Load();

    public IActionResult OnPostSaveHours()
    {
        var r = schedule.SaveHours(Hours, CurrentUserId);
        if (r.Success) { Success("營業時段已儲存。"); return RedirectToPage(); }
        Error(r.Error!);
        Holidays = schedule.GetHolidays();
        return Page();
    }

    public IActionResult OnPostAddHoliday()
    {
        if (HolidayDate is null) { Error("請選擇日期。"); return RedirectToPage(); }
        var r = schedule.AddHoliday(HolidayDate.Value, HolidayName ?? "", CurrentUserId);
        if (r.Success) Success("已新增假日。"); else Error(r.Error!);
        return RedirectToPage();
    }

    public IActionResult OnPostDeleteHoliday(Guid id)
    {
        var r = schedule.DeleteHoliday(id, CurrentUserId);
        if (r.Success) Success("已刪除假日。"); else Error(r.Error!);
        return RedirectToPage();
    }

    private void Load()
    {
        Hours = calendar.GetWeek().Select(h => new HoursEditDto { Day = h.Day, Version = h.Version, IsOpen = h.IsOpen, OpenTime = h.OpenTime, CloseTime = h.CloseTime }).ToList();
        Holidays = schedule.GetHolidays();
    }
}
