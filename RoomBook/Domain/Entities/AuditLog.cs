namespace RoomBook.Domain.Entities;

public class AuditLog : BaseEntity
{
    public Guid? UserId { get; set; }
    public string UserName { get; set; } = "";
    public string Action { get; set; } = "";
    public string TargetType { get; set; } = "";
    public string? TargetId { get; set; }
    public string? Detail { get; set; }
    public string IpAddress { get; set; } = "";
    public DateTime OccurredAtUtc { get; set; }
}
