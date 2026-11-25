namespace RoomBook.Domain.Entities;

public class Holiday : BaseEntity
{
    public DateOnly Date { get; set; }
    public string Name { get; set; } = "";
}
