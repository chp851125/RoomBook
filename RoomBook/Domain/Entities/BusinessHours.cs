namespace RoomBook.Domain.Entities;

/// <summary>每週各天的營業時段（台北時間）。</summary>
public class BusinessHours : BaseEntity
{
    public DayOfWeek Day { get; set; }
    public bool IsOpen { get; set; }
    public TimeOnly OpenTime { get; set; }
    public TimeOnly CloseTime { get; set; }
}
