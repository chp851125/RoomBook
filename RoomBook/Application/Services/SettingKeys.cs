namespace RoomBook.Application.Services;

/// <summary>AppSetting 的 Key 常數；預設值定義在 SeedData/appSettings.json。</summary>
public static class SettingKeys
{
    public const string MaxDurationHours = "Booking.MaxDurationHours";
    public const string MaxAdvanceDays = "Booking.MaxAdvanceDays";
    public const string MaxFutureBookingsPerUser = "Booking.MaxFutureBookingsPerUser";
    public const string CancelCutoffMinutes = "Booking.CancelCutoffMinutes";
    public const string AllowEmployeePast = "Booking.AllowEmployeePast";
    public const string CheckInEarlyMinutes = "CheckIn.EarlyMinutes";
    public const string CheckInLateMinutes = "CheckIn.LateMinutes";
    public const string ViolationPointLimit = "Violation.PointLimit";
    public const string ViolationSuspendDays = "Violation.SuspendDays";
    public const string ReminderMinutes = "Notification.ReminderMinutes";
    public const string DisplayShowTitle = "Display.ShowTitle";
    public const string DisplayRefreshSeconds = "Display.RefreshSeconds";
    public const string MaxLoginFailures = "Security.MaxLoginFailures";
    public const string LockoutMinutes = "Security.LockoutMinutes";
    public const string PasswordMinLength = "Security.PasswordMinLength";
}

public static class AuthConstants
{
    public const string AdminRole = "Admin";
    public const string AdminPolicy = "AdminOnly";
    public const string DepartmentClaim = "department";
}
