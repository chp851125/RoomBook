using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using RoomBook.Application.Dtos;

namespace RoomBook.Pages.Bookings;

public class CreateModel(
    IBookingService bookings,
    IBookingQueryService query,
    IRoomService rooms,
    IUserService users,
    IBusinessCalendarService calendar,
    ISettingService settings,
    ITimeProvider time) : AppPageModel
{
    public class InputModel
    {
        [Required(ErrorMessage = "請選擇會議室")] public Guid? RoomId { get; set; }
        [Required(ErrorMessage = "請選擇日期")] public DateOnly? Date { get; set; }
        [Required(ErrorMessage = "請選擇開始時間")] public TimeOnly? StartTime { get; set; }
        [Required(ErrorMessage = "請選擇結束時間")] public TimeOnly? EndTime { get; set; }
        [Required(ErrorMessage = "請輸入會議主題"), StringLength(100, ErrorMessage = "會議主題最多 100 字")]
        public string Title { get; set; } = "";
        [StringLength(1000, ErrorMessage = "說明最多 1000 字")] public string? Description { get; set; }
        [Range(1, 500, ErrorMessage = "參會人數至少 1 人")] public int AttendeeCount { get; set; } = 1;
        public List<string> RequiredEquipment { get; set; } = new();
        public List<Guid> ParticipantIds { get; set; } = new();
        public List<GuestInput> Guests { get; set; } = new();
        public RecurrenceType Recurrence { get; set; } = RecurrenceType.None;
        public DateOnly? RecurrenceEndDate { get; set; }
        public List<DayOfWeek> DaysOfWeek { get; set; } = new();
        public bool ConfirmParticipantConflicts { get; set; }
        public Guid? EditId { get; set; }
        /// <summary>管理員代為預約時的預約人（非管理員會被忽略）。</summary>
        public Guid? OrganizerId { get; set; }
        public ConflictPolicy ConflictPolicy { get; set; } = ConflictPolicy.Ask;
    }

    [BindProperty] public InputModel Input { get; set; } = new();
    public BookingResult? Result { get; private set; }
    public IReadOnlyList<Room> Rooms { get; private set; } = [];
    public IReadOnlyList<User> People { get; private set; } = [];
    public IReadOnlyList<string> EquipmentOptions { get; private set; } = [];
    public Dictionary<Guid, string> Departments { get; private set; } = new();
    public IReadOnlyList<User> Candidates { get; private set; } = [];
    public int MaxDurationHours { get; private set; }
    public bool IsEdit => Input.EditId is not null;
    public bool IsAdminUser => IsAdmin;
    public Guid CurrentUserIdValue => CurrentUserId;

    public IActionResult OnGet(Guid? roomId, string? start, string? end, Guid? copyFrom, Guid? copySeries, DateOnly? copyDate, Guid? editId)
    {
        LoadLists();
        var now = time.ToLocal(time.UtcNow);
        var rounded = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute / 30 * 30, 0).AddMinutes(30);
        Input.Date = DateOnly.FromDateTime(rounded);
        Input.StartTime = TimeOnly.FromDateTime(rounded);
        Input.EndTime = TimeOnly.FromDateTime(rounded.AddHours(1));
        Input.RoomId = roomId ?? users.GetById(CurrentUserId)?.FavoriteRoomIds.FirstOrDefault(id => Rooms.Any(r => r.Id == id));
        if (Input.RoomId == Guid.Empty) Input.RoomId = null;
        if (IsAdmin) Input.OrganizerId = CurrentUserId;
        if (editId is { } eid)
        {
            var editSrc = query.GetDetail(eid, null, null);
            if (editSrc is null || editSrc.BookingId is null || (editSrc.OrganizerId != CurrentUserId && !IsAdmin)) return NotFound();
            ApplyCopy(editSrc);
            Input.EditId = eid;
            Input.Date = DateOnly.FromDateTime(time.ToLocal(editSrc.StartUtc));
            return Page();
        }


        if (copyFrom is { } cid && query.GetDetail(cid, null, null) is { } src) ApplyCopy(src);
        else if (copySeries is { } sid && query.GetDetail(null, sid, copyDate) is { } srcS) ApplyCopy(srcS);

        if (DateTime.TryParse(start, out var s))
        {
            Input.Date = DateOnly.FromDateTime(s);
            Input.StartTime = TimeOnly.FromDateTime(s);
            Input.EndTime = DateTime.TryParse(end, out var e) && e > s && e.Date == s.Date
                ? TimeOnly.FromDateTime(e)
                : TimeOnly.FromDateTime(s.AddHours(1));
        }
        if (Input.EndTime <= Input.StartTime) Input.EndTime = new TimeOnly(23, 59);
        return Page();
    }

    private void ApplyCopy(BookingDetail src)
    {
        Input.RoomId = src.RoomId;
        Input.Title = src.Title;
        Input.Description = src.Description;
        Input.AttendeeCount = src.AttendeeCount;
        Input.RequiredEquipment = src.Equipment.ToList();
        Input.ParticipantIds = src.Participants.Select(p => p.UserId).ToList();
        Input.Guests = src.Guests.Select(g => new GuestInput { Name = g.Name, Company = g.Company, Email = g.Email }).ToList();
        var localStart = time.ToLocal(src.StartUtc);
        var localEnd = time.ToLocal(src.EndUtc);
        Input.StartTime = TimeOnly.FromDateTime(localStart);
        Input.EndTime = TimeOnly.FromDateTime(localEnd);
        // 複製後日期預設為下一個營業日，使用者可再調整
        var d = time.LocalToday.AddDays(1);
        for (var i = 0; i < 14 && calendar.GetOpenWindow(d) is null; i++) d = d.AddDays(1);
        Input.Date = d;
    }

    public IActionResult OnPost()
    {
        LoadLists();
        if (!ModelState.IsValid) return Page();

        Result = bookings.Create(ToRequest());
        if (Result.Succeeded)
        {
            if (Result.Booking is { } b)
            {
                Success(IsEdit ? (Result.RequiresApproval ? "預約已更新，需重新審核。" : "預約已更新。") : Result.RequiresApproval
                    ? $"預約已送出，等待管理員審核（單號 {b.BookingNo}）。審核期間時段已為你保留。"
                    : $"預約成功（單號 {b.BookingNo}）。");
                return RedirectToPage("/Bookings/Details", new { id = b.Id });
            }
            var series = Result.Series!;
            Success($"週期預約已建立，共 {Result.OccurrenceCount} 次" + (Result.RequiresApproval ? "，等待管理員審核。" : "。"));
            return RedirectToPage("/Bookings/Details",
                new { seriesId = series.Id, date = FirstDate(series).ToString("yyyy-MM-dd") });
        }
        return Page();
    }

    /// <summary>即時檢查：回傳 HTML 片段（與送出後的提示共用同一個 Partial）。</summary>
    public IActionResult OnPostCheck()
    {
        if (Input.RoomId is null || Input.Date is null || Input.StartTime is null || Input.EndTime is null)
            return Content("", "text/html");
        var request = ToRequest();
        if (string.IsNullOrWhiteSpace(request.Title)) request.Title = "（預覽）";
        request.ConflictPolicy = ConflictPolicy.Ask;
        request.ConfirmParticipantConflicts = false;
        return Partial("_BookingCheck", (bookings.Preview(request), true));
    }

    private DateOnly FirstDate(BookingSeries series)
        => query.ExpandSeries(series).Select(s => s.OccurrenceDate!.Value).DefaultIfEmpty(series.StartDate).Min();

    private CreateBookingRequest ToRequest() => new()
    {
        OrganizerId = IsAdmin && Input.OrganizerId is { } oid && Input.EditId is null ? oid : CurrentUserId,
        ActorId = CurrentUserId,
        RoomId = Input.RoomId ?? Guid.Empty,
        Date = Input.Date ?? default,
        StartTime = Input.StartTime ?? default,
        EndTime = Input.EndTime ?? default,
        Title = Input.Title ?? "",
        Description = Input.Description,
        AttendeeCount = Input.AttendeeCount,
        RequiredEquipment = Input.RequiredEquipment,
        ParticipantIds = Input.ParticipantIds,
        Guests = Input.Guests,
        Recurrence = Input.Recurrence,
        RecurrenceEndDate = Input.RecurrenceEndDate,
        DaysOfWeek = Input.DaysOfWeek,
        ConfirmParticipantConflicts = Input.ConfirmParticipantConflicts,
        ConflictPolicy = Input.ConflictPolicy,
        EditBookingId = Input.EditId
    };

    private void LoadLists()
    {
        Rooms = rooms.GetAll().Where(r => r.Status == RoomStatus.Active).ToList();
        EquipmentOptions = rooms.GetEquipmentOptions();
        People = users.GetActiveUsers().Where(u => u.Id != CurrentUserId).ToList();
        Candidates = users.GetActiveUsers();
        Departments = users.GetDepartments().ToDictionary(d => d.Id, d => d.Name);
        MaxDurationHours = settings.GetInt(RoomBook.Application.Services.SettingKeys.MaxDurationHours, 4);
    }
}
