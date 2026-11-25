namespace RoomBook.Domain.Entities;

public class AppSetting : BaseEntity
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public string Group { get; set; } = "";
    public string Description { get; set; } = "";
}
