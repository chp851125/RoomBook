namespace RoomBook.Application.Dtos;

/// <summary>指向一場會議：單次預約（BookingId）或週期預約的某一次（SeriesId + Date）。</summary>
public sealed record BookingRef(Guid? BookingId, Guid? SeriesId, DateOnly? Date);

public enum MyTab { Upcoming, Now, History, Cancelled, Invited }

public sealed class MyBookingQuery
{
    public MyTab Tab { get; set; } = MyTab.Upcoming;
    public string? Keyword { get; set; }
    public Guid? RoomId { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 8;
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
}

public sealed class MyBookingItem
{
    public Guid? BookingId { get; set; }
    public Guid? SeriesId { get; set; }
    public DateOnly? OccurrenceDate { get; set; }
    public string BookingNo { get; set; } = "";
    public string Title { get; set; } = "";
    public Guid RoomId { get; set; }
    public string RoomName { get; set; } = "";
    public Guid OrganizerId { get; set; }
    public string OrganizerName { get; set; } = "";
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public BookingStatus Status { get; set; }
    public bool IsSeries { get; set; }
    public bool CheckedIn { get; set; }
    public bool CanCheckIn { get; set; }
    public bool CanExtend { get; set; }
    public bool CanFinish { get; set; }
    public bool CanCancel { get; set; }
    public bool CanEdit { get; set; }
    public bool InProgress { get; set; }
    public BookingRef Ref => new(BookingId, SeriesId, OccurrenceDate);
}
