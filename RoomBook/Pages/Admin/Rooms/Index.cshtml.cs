namespace RoomBook.Pages.Admin.Rooms;

public class IndexModel(IRoomAdminService roomAdmin) : AdminPageModel
{
    public IReadOnlyList<Room> Rooms { get; private set; } = [];
    public Dictionary<Guid, int> FutureCounts { get; private set; } = new();

    public void OnGet()
    {
        Rooms = roomAdmin.GetAll();
        FutureCounts = Rooms.ToDictionary(r => r.Id, r => roomAdmin.CountFutureBookings(r.Id));
    }
}
