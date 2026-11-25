using RoomBook.Application.Dtos;

namespace RoomBook.Application.Services;

public class SettingAdminService(IRepository<AppSetting> settings, IUserService users, IAuditService audit) : ISettingAdminService
{
    private static readonly HashSet<string> BoolKeys =
        [SettingKeys.AllowEmployeePast, SettingKeys.DisplayShowTitle];

    // 各整數設定允許的範圍
    private static readonly Dictionary<string, (int Min, int Max)> IntRanges = new()
    {
        [SettingKeys.MaxDurationHours] = (1, 12),
        [SettingKeys.MaxAdvanceDays] = (1, 365),
        [SettingKeys.MaxFutureBookingsPerUser] = (1, 200),
        [SettingKeys.CancelCutoffMinutes] = (0, 1440),
        [SettingKeys.CheckInEarlyMinutes] = (0, 120),
        [SettingKeys.CheckInLateMinutes] = (1, 120),
        [SettingKeys.ViolationPointLimit] = (1, 20),
        [SettingKeys.ViolationSuspendDays] = (1, 365),
        [SettingKeys.ReminderMinutes] = (1, 240),
        [SettingKeys.DisplayRefreshSeconds] = (10, 600),
        [SettingKeys.MaxLoginFailures] = (3, 20),
        [SettingKeys.LockoutMinutes] = (1, 1440),
        [SettingKeys.PasswordMinLength] = (6, 64)
    };

    public IReadOnlyList<AppSetting> GetAll() => settings.GetAll().OrderBy(s => s.Group).ThenBy(s => s.Key).ToList();

    public OperationResult Save(IReadOnlyList<SettingEditDto> values, Guid adminId)
    {
        var all = settings.GetAll().ToDictionary(s => s.Key);
        var errors = new List<string>();
        foreach (var v in values)
        {
            if (!all.TryGetValue(v.Key, out var s)) continue;
            var value = (v.Value ?? "").Trim();
            if (BoolKeys.Contains(v.Key))
            {
                if (!bool.TryParse(value, out _)) errors.Add($"{s.Description}：請選擇是或否。");
            }
            else if (IntRanges.TryGetValue(v.Key, out var range))
            {
                if (!int.TryParse(value, out var n) || n < range.Min || n > range.Max)
                    errors.Add($"{s.Description}：請輸入 {range.Min}–{range.Max} 的整數。");
            }
        }
        if (errors.Count > 0) return OperationResult.Fail(string.Join(" ", errors));

        var changed = new List<string>();
        foreach (var v in values)
        {
            if (!all.TryGetValue(v.Key, out var s)) continue;
            var value = v.Value.Trim().ToLowerInvariant() is "true" or "false" && BoolKeys.Contains(v.Key) ? v.Value.Trim().ToLowerInvariant() : v.Value.Trim();
            if (s.Value == value) continue;
            changed.Add($"{s.Key}: {s.Value}→{value}");
            s.Value = value;
            s.Version = v.Version;
            try { settings.Update(s); }
            catch (ConcurrencyException) { return OperationResult.Fail("設定剛被其他人修改，請重新載入後再儲存。"); }
        }
        if (changed.Count > 0) audit.Log("修改設定", nameof(AppSetting), null, string.Join("；", changed), users.GetById(adminId));
        return OperationResult.Ok();
    }
}
