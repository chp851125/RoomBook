namespace RoomBook.Domain.Entities;

public class Violation : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid? BookingId { get; set; }
    public string Reason { get; set; } = "";
    public int Points { get; set; } = 1;
    public DateTime OccurredAtUtc { get; set; }
    public bool IsRevoked { get; set; }
    public Guid? RevokedById { get; set; }
}
