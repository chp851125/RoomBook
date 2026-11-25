using Microsoft.AspNetCore.Mvc;
using RoomBook.Application.Dtos;

namespace RoomBook.Pages.Admin.Bookings;

public class IndexModel(IAdminBookingService adminBookings, IRoomService rooms) : AdminPageModel
{
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public BookingStatus? Status { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? RoomId { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? To { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNo { get; set; } = 1;

    public PagedResult<MyBookingItem> Result { get; private set; } = new([], 1, 15, 0);
    public IReadOnlyList<Room> Rooms { get; private set; } = [];

    public void OnGet()
    {
        Rooms = rooms.GetAll();
        Result = adminBookings.Search(new AdminBookingQuery { Keyword = Q, Status = Status, RoomId = RoomId, From = From, To = To, Page = PageNo }, CurrentUserId);
    }

    public string? PageUrl(int page)
        => Url.Page("/Admin/Bookings/Index", new { q = Q, status = Status, roomId = RoomId, from = From?.ToString("yyyy-MM-dd"), to = To?.ToString("yyyy-MM-dd"), pageNo = page });
}
