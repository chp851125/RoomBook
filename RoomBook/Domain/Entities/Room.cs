namespace RoomBook.Domain.Entities;

public class Room : BaseEntity
{
    public string Name { get; set; } = "";
    public int Floor { get; set; }
    public int Capacity { get; set; }
    public List<string> Equipment { get; set; } = new();
    public string Description { get; set; } = "";
    public RoomStatus Status { get; set; } = RoomStatus.Active;
    public bool RequiresApproval { get; set; }
    public int SortOrder { get; set; }
}
