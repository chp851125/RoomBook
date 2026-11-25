namespace RoomBook.Domain.Entities;

/// <summary>所有實體的共同基底。Version 用於樂觀並行控制，時間一律為 UTC。</summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; }
    public int Version { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
