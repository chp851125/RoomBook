namespace RoomBook.Domain.Entities;

public class ExternalGuest
{
    public string Name { get; set; } = "";
    public string Company { get; set; } = "";
    public string? Email { get; set; }
}

public class BookingHistoryEntry
{
    public DateTime AtUtc { get; set; }
    public Guid? ActorId { get; set; }
    public string ActorName { get; set; } = "";
    public string Action { get; set; } = "";
    public string? Note { get; set; }
}

public class Booking : BaseEntity
{
    /// <summary>單號，格式 RB-yyyyMMdd-0001。</summary>
    public string BookingNo { get; set; } = "";
    public Guid RoomId { get; set; }
    public Guid OrganizerId { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public int AttendeeCount { get; set; }
    public List<string> RequiredEquipment { get; set; } = new();
    public List<Guid> ParticipantIds { get; set; } = new();
    public List<ExternalGuest> ExternalGuests { get; set; } = new();
    public BookingStatus Status { get; set; } = BookingStatus.Confirmed;

    /// <summary>若為週期預約的單次實體化（例如單次修改或簽到後），指向 BookingSeries。</summary>
    public Guid? SeriesId { get; set; }
    public DateOnly? OccurrenceDate { get; set; }

    public DateTime? CheckedInAtUtc { get; set; }
    public DateTime? ReleasedAtUtc { get; set; }
    public Guid? ReviewedById { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? ReviewNote { get; set; }
    public string? CancelReason { get; set; }
    public Guid? CancelledById { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public DateTime? ReminderSentAtUtc { get; set; }
    public List<BookingHistoryEntry> History { get; set; } = new();
}
