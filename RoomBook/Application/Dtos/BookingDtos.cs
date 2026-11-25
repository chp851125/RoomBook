namespace RoomBook.Application.Dtos;

/// <summary>佔用時段：可能來自單次預約，也可能是週期預約動態展開的一次。</summary>
public sealed record BookingSlot(
    Guid? BookingId, Guid? SeriesId, DateOnly? OccurrenceDate, Guid RoomId,
    DateTime StartUtc, DateTime EndUtc, string Title, Guid OrganizerId,
    BookingStatus Status, IReadOnlyList<Guid> PersonIds)
{
    public bool IsSeries => SeriesId is not null;
}

public enum ConflictPolicy { Ask, Skip, CancelAll }

public class GuestInput
{
    public string? Name { get; set; }
    public string? Company { get; set; }
    public string? Email { get; set; }
}

public class CreateBookingRequest
{
    public Guid OrganizerId { get; set; }
    public Guid RoomId { get; set; }
    /// <summary>台北日期與時刻。</summary>
    public DateOnly Date { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public int AttendeeCount { get; set; } = 1;
    public List<string> RequiredEquipment { get; set; } = new();
    public List<Guid> ParticipantIds { get; set; } = new();
    public List<GuestInput> Guests { get; set; } = new();
    public RecurrenceType Recurrence { get; set; } = RecurrenceType.None;
    public DateOnly? RecurrenceEndDate { get; set; }
    public List<DayOfWeek> DaysOfWeek { get; set; } = new();
    public bool ConfirmParticipantConflicts { get; set; }
    public ConflictPolicy ConflictPolicy { get; set; } = ConflictPolicy.Ask;
    /// <summary>有值代表修改這筆既有預約（只限單次預約）。</summary>
    public Guid? EditBookingId { get; set; }
    /// <summary>實際操作的人；與 OrganizerId 不同且為管理員時，視為「管理員代為預約」（直接確認、不受過去時段／上限／暫停限制）。</summary>
    public Guid? ActorId { get; set; }
}

public enum BookingOutcome { Created, Invalid, NeedsParticipantConfirm, NeedsConflictDecision }

public sealed record ConflictInfo(DateOnly Date, string RoomName, string Title, string OrganizerName, DateTime StartUtc, DateTime EndUtc);
public sealed record ParticipantWarning(DateOnly Date, string UserName, string Title, string RoomName, DateTime StartUtc, DateTime EndUtc);
public sealed record SkippedDate(DateOnly Date, string Reason);

public sealed class BookingResult
{
    public BookingOutcome Outcome { get; set; } = BookingOutcome.Created;
    public List<string> Errors { get; } = new();
    public List<ConflictInfo> RoomConflicts { get; } = new();
    public List<ParticipantWarning> ParticipantWarnings { get; } = new();
    public List<SkippedDate> SkippedDates { get; } = new();
    public List<DateOnly> ConflictDates { get; } = new();
    public Booking? Booking { get; set; }
    public BookingSeries? Series { get; set; }
    public bool RequiresApproval { get; set; }
    public int OccurrenceCount { get; set; }
    public bool Succeeded => Outcome == BookingOutcome.Created;
}

public sealed record OperationResult(bool Success, string? Error = null)
{
    public static OperationResult Ok() => new(true);
    public static OperationResult Fail(string error) => new(false, error);
}

public sealed record ParticipantView(Guid UserId, string Name, string Department);

public sealed class BookingDetail
{
    public Guid? BookingId { get; set; }
    public Guid? SeriesId { get; set; }
    public DateOnly? OccurrenceDate { get; set; }
    public string BookingNo { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public Guid RoomId { get; set; }
    public string RoomName { get; set; } = "";
    public int RoomFloor { get; set; }
    public Guid OrganizerId { get; set; }
    public string OrganizerName { get; set; } = "";
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public BookingStatus Status { get; set; }
    public int AttendeeCount { get; set; }
    public List<string> Equipment { get; set; } = new();
    public List<ParticipantView> Participants { get; set; } = new();
    public List<ExternalGuest> Guests { get; set; } = new();
    public List<BookingHistoryEntry> History { get; set; } = new();
    public string? RecurrenceText { get; set; }
    public bool IsOccurrenceCancelled { get; set; }
    public bool IsOccurrenceModified { get; set; }
    public string? ExceptionReason { get; set; }
    public string? ReviewNote { get; set; }
    public string? CancelReason { get; set; }
    public DateTime? CheckedInAtUtc { get; set; }
    public bool IsSeries => SeriesId is not null;
}

public sealed class RoomStatusInfo
{
    public Room Room { get; set; } = null!;
    public bool OpenNow { get; set; }
    public bool IsFreeNow { get; set; }
    public BookingSlot? Current { get; set; }
    public BookingSlot? Next { get; set; }
    public string? CurrentOrganizer { get; set; }
    /// <summary>空閒到何時（下一場開始或今日下班），僅 IsFreeNow 有值。</summary>
    public DateTime? FreeUntilUtc { get; set; }
}

public class RoomFilter
{
    public int? Floor { get; set; }
    public int? MinCapacity { get; set; }
    public List<string> Equipment { get; set; } = new();
    public DateTime? StartUtc { get; set; }
    public DateTime? EndUtc { get; set; }
}
