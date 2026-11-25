namespace RoomBook.Application.Dtos;

public sealed class RoomEditDto
{
    public Guid? Id { get; set; }
    public int Version { get; set; }
    public string Name { get; set; } = "";
    public int Floor { get; set; } = 1;
    public int Capacity { get; set; } = 4;
    /// <summary>設備以逗號、頓號或空白分隔。</summary>
    public string EquipmentText { get; set; } = "";
    public string Description { get; set; } = "";
    public RoomStatus Status { get; set; } = RoomStatus.Active;
    public bool RequiresApproval { get; set; }
    public int SortOrder { get; set; }
    /// <summary>停用／維護時，若仍有未來預約，需勾選確認。</summary>
    public bool ConfirmDisable { get; set; }
}

public sealed class UserEditDto
{
    public Guid? Id { get; set; }
    public int Version { get; set; }
    public string Account { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
    public Guid DepartmentId { get; set; }
    public string JobTitle { get; set; } = "";
    public UserRole Role { get; set; } = UserRole.Employee;
    public bool IsActive { get; set; } = true;
}

public sealed record PasswordResult(bool Success, string? Error, string? Password);

public sealed class SettingEditDto
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public int Version { get; set; }
}

public sealed class HoursEditDto
{
    public DayOfWeek Day { get; set; }
    public int Version { get; set; }
    public bool IsOpen { get; set; }
    public TimeOnly OpenTime { get; set; }
    public TimeOnly CloseTime { get; set; }
}

public sealed class ApprovalRow
{
    public Guid? BookingId { get; set; }
    public Guid? SeriesId { get; set; }
    public string Title { get; set; } = "";
    public string RoomName { get; set; } = "";
    public string Organizer { get; set; } = "";
    public string Department { get; set; } = "";
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? RecurrenceText { get; set; }
    public int AttendeeCount { get; set; }
    public string Description { get; set; } = "";
}

public sealed class AdminBookingQuery
{
    public string? Keyword { get; set; }
    public BookingStatus? Status { get; set; }
    public Guid? RoomId { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 15;
}

public sealed class AuditQuery
{
    public string? Keyword { get; set; }
    public Guid? UserId { get; set; }
    public string? Action { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

// ---- 統計 ----
public sealed record RoomUsage(string Name, double Hours, double Rate);
public sealed record DeptHours(string Name, double Hours);
public sealed record HeatCell(int Day, int Hour, double Hours);
public sealed record CheckInStats(int CheckedIn, int NoShow, int Other)
{
    public int Total => CheckedIn + NoShow + Other;
    public double CheckInRate => Total == 0 ? 0 : Math.Round(CheckedIn * 100.0 / Total, 1);
    public double NoShowRate => Total == 0 ? 0 : Math.Round((NoShow + Other) * 100.0 / Total, 1);
}
public sealed record TrendData(IReadOnlyList<string> Months, IReadOnlyList<double> Hours, IReadOnlyList<int> Bookings, IReadOnlyList<int> NoShows);

public sealed class StatsData
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public int BookingCount { get; set; }
    public double TotalHours { get; set; }
    public double OverallRate { get; set; }
    public IReadOnlyList<RoomUsage> Rooms { get; set; } = [];
    public IReadOnlyList<DeptHours> Departments { get; set; } = [];
    public IReadOnlyList<HeatCell> Heat { get; set; } = [];
    public IReadOnlyList<int> HeatHours { get; set; } = [];
    public CheckInStats CheckIn { get; set; } = new(0, 0, 0);
    public TrendData Trend { get; set; } = new([], [], [], []);
}
