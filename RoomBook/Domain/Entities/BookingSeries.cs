namespace RoomBook.Domain.Entities;

/// <summary>週期性預約規則；不預先展開，顯示與衝突檢查時動態展開（最多 52 次）。</summary>
public class BookingSeries : BaseEntity
{
    public Guid RoomId { get; set; }
    public Guid OrganizerId { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public int AttendeeCount { get; set; }
    public List<string> RequiredEquipment { get; set; } = new();
    public List<Guid> ParticipantIds { get; set; } = new();
    public List<ExternalGuest> ExternalGuests { get; set; } = new();
    public BookingStatus Status { get; set; } = BookingStatus.Confirmed;
    public RecurrenceType Recurrence { get; set; } = RecurrenceType.Weekly;
    /// <summary>第一次發生日（台北日期）。Monthly 以此日的「日」為準。</summary>
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public List<DayOfWeek> DaysOfWeek { get; set; } = new();
    /// <summary>台北時間的開始與結束時刻（同一天內）。</summary>
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public List<BookingHistoryEntry> History { get; set; } = new();
}
