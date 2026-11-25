using RoomBook.Application.Dtos;

namespace RoomBook.Application.Services;

public class ScheduleAdminService(
    IRepository<BusinessHours> hours,
    IRepository<Holiday> holidays,
    IUserService users,
    IAuditService audit) : IScheduleAdminService
{
    public IReadOnlyList<Holiday> GetHolidays() => holidays.GetAll().OrderBy(h => h.Date).ToList();

    public OperationResult SaveHours(IReadOnlyList<HoursEditDto> input, Guid adminId)
    {
        foreach (var h in input)
            if (h.IsOpen && h.CloseTime <= h.OpenTime)
                return OperationResult.Fail($"{DayName(h.Day)}：結束時間必須晚於開始時間。");

        var current = hours.GetAll().ToDictionary(h => h.Day);
        var changes = new List<string>();
        foreach (var h in input)
        {
            if (!current.TryGetValue(h.Day, out var row)) continue;
            if (row.IsOpen == h.IsOpen && row.OpenTime == h.OpenTime && row.CloseTime == h.CloseTime) continue;
            row.IsOpen = h.IsOpen; row.OpenTime = h.OpenTime; row.CloseTime = h.CloseTime; row.Version = h.Version;
            try { hours.Update(row); }
            catch (ConcurrencyException) { return OperationResult.Fail("營業時段剛被其他人修改，請重新載入後再儲存。"); }
            changes.Add($"{DayName(h.Day)} {(h.IsOpen ? $"{h.OpenTime:HH\\:mm}–{h.CloseTime:HH\\:mm}" : "公休")}");
        }
        if (changes.Count > 0) audit.Log("修改營業時段", nameof(BusinessHours), null, string.Join("；", changes), users.GetById(adminId));
        return OperationResult.Ok();
    }

    public OperationResult AddHoliday(DateOnly date, string name, Guid adminId)
    {
        name = name?.Trim() ?? "";
        if (name.Length is 0 or > 30) return OperationResult.Fail("請輸入假日名稱（最多 30 字）。");
        if (holidays.Query(h => h.Date == date).Count > 0) return OperationResult.Fail("這一天已經設定為假日。");
        holidays.Add(new Holiday { Date = date, Name = name });
        audit.Log("新增假日", nameof(Holiday), date.ToString("yyyy-MM-dd"), name, users.GetById(adminId));
        return OperationResult.Ok();
    }

    public OperationResult DeleteHoliday(Guid id, Guid adminId)
    {
        var h = holidays.GetById(id);
        if (h is null) return OperationResult.Fail("找不到這個假日。");
        holidays.Delete(id);
        audit.Log("刪除假日", nameof(Holiday), h.Date.ToString("yyyy-MM-dd"), h.Name, users.GetById(adminId));
        return OperationResult.Ok();
    }

    private static string DayName(DayOfWeek d) => "週" + "日一二三四五六"[(int)d];
}
