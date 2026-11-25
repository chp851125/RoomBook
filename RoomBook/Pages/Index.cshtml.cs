namespace RoomBook.Pages;

public class IndexModel(IRoomService rooms, IUserService users) : AppPageModel
{
    public IReadOnlyList<Room> Rooms { get; private set; } = [];
    public User? Me { get; private set; }
    public bool IsSuspended { get; private set; }

    public void OnGet()
    {
        Rooms = rooms.GetAll();
        Me = users.GetById(CurrentUserId);
        IsSuspended = Me is not null && users.IsSuspended(Me);
    }
}
