using Microsoft.AspNetCore.Mvc;
using RoomBook.Application.Dtos;

namespace RoomBook.Pages.Rooms;

public class DetailsModel(IRoomService rooms, IBookingQueryService query, IUserService users, ITimeProvider time) : AppPageModel
{
    public sealed record Row(BookingSlot Slot, string Organizer);

    public Room Room { get; private set; } = null!;
    public RoomStatusInfo? NowStatus { get; private set; }
    public IReadOnlyList<IGrouping<DateOnly, Row>> Upcoming { get; private set; } = [];

    public IActionResult OnGet(Guid id)
    {
        var room = rooms.GetById(id);
        if (room is null) return NotFound();
        Room = room;
        NowStatus = rooms.GetNowStatus().FirstOrDefault(s => s.Room.Id == id);

        // 今天（含已開始）到未來 7 天的概況
        var from = time.ToUtc(time.LocalToday.ToDateTime(TimeOnly.MinValue));
        var rows = query.GetSlots(from, from.AddDays(8), id)
            .Select(s => new Row(s, users.GetById(s.OrganizerId)?.DisplayName ?? "(未知)"));
        Upcoming = rows.GroupBy(r => DateOnly.FromDateTime(time.ToLocal(r.Slot.StartUtc))).ToList();
        return Page();
    }
}
