namespace RoomBook.Domain.Entities;

public class EmailLog : BaseEntity
{
    public Guid? UserId { get; set; }
    public string ToEmail { get; set; } = "";
    public string Subject { get; set; } = "";
    public NotificationType Type { get; set; }
    public DateTime SentAtUtc { get; set; }
    /// <summary>outbox 資料夾內的 .eml 檔名。</summary>
    public string FileName { get; set; } = "";
    public bool Success { get; set; } = true;
    public string? Error { get; set; }
}
