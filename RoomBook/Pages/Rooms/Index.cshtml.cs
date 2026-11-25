using Microsoft.AspNetCore.Mvc;
using RoomBook.Application.Dtos;

namespace RoomBook.Pages.Rooms;

public class IndexModel(IRoomService rooms, ITimeProvider time) : AppPageModel
{
    [BindProperty(SupportsGet = true)] public int? Floor { get; set; }
    [BindProperty(SupportsGet = true)] public int? MinCapacity { get; set; }
    [BindProperty(SupportsGet = true)] public List<string> Equipment { get; set; } = new();
    [BindProperty(SupportsGet = true)] public DateOnly? Date { get; set; }
    [BindProperty(SupportsGet = true)] public TimeOnly? From { get; set; }
    [BindProperty(SupportsGet = true)] public TimeOnly? To { get; set; }
    [BindProperty(SupportsGet = true)] public string View { get; set; } = "card";

    public IReadOnlyList<Room> Rooms { get; private set; } = [];
    public IReadOnlyList<int> Floors { get; private set; } = [];
    public IReadOnlyList<string> EquipmentOptions { get; private set; } = [];
    public bool SlotFilterActive { get; private set; }
    public string? SlotQuery { get; private set; }

    public void OnGet()
    {
        Floors = rooms.GetFloors();
        EquipmentOptions = rooms.GetEquipmentOptions();

        var filter = new RoomFilter { Floor = Floor, MinCapacity = MinCapacity, Equipment = Equipment };
        if (Date is { } d && From is { } f && To is { } t && t > f)
        {
            filter.StartUtc = time.ToUtc(d.ToDateTime(f));
            filter.EndUtc = time.ToUtc(d.ToDateTime(t));
            SlotFilterActive = true;
            SlotQuery = $"{d:yyyy-MM-dd}T{f:HH\\:mm}";
        }
        Rooms = rooms.Search(filter);
    }

    public string? BookUrl(Room room)
        => SlotFilterActive
            ? Url.Page("/Bookings/Create", new { roomId = room.Id, start = $"{Date:yyyy-MM-dd}T{From:HH\\:mm}", end = $"{Date:yyyy-MM-dd}T{To:HH\\:mm}" })
            : null;
}
