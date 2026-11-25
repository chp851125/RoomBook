namespace RoomBook.Domain.Entities;

/// <summary>週期預約中的單次例外：取消或改時間。</summary>
public class BookingException : BaseEntity
{
    public Guid SeriesId { get; set; }
    public DateOnly OccurrenceDate { get; set; }
    public bool IsCancelled { get; set; }
    public DateTime? NewStartUtc { get; set; }
    public DateTime? NewEndUtc { get; set; }
    public string? Reason { get; set; }
    public Guid? CreatedById { get; set; }
}
