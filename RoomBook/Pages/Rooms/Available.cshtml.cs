using Microsoft.AspNetCore.Mvc;
using RoomBook.Application.Dtos;

namespace RoomBook.Pages.Rooms;

public class AvailableModel(IRoomService rooms, ITimeProvider time) : AppPageModel
{
    public sealed record Item(RoomStatusInfo Info, string? BookUrl, DateTime? SuggestEndLocal);

    public IReadOnlyList<Item> Free { get; private set; } = [];
    public IReadOnlyList<Item> Busy { get; private set; } = [];
    public IReadOnlyList<RoomStatusInfo> Unavailable { get; private set; } = [];
    public bool OpenNow { get; private set; }

    public void OnGet()
    {
        var all = rooms.GetNowStatus();
        OpenNow = all.FirstOrDefault()?.OpenNow ?? false;

        // 起點取下一個 15 分鐘刻度，避免員工預約到「已經過去」的時段
        var nowLocal = time.ToLocal(time.UtcNow);
        var startLocal = new DateTime(nowLocal.Year, nowLocal.Month, nowLocal.Day, nowLocal.Hour, nowLocal.Minute / 15 * 15, 0).AddMinutes(15);

        var free = new List<Item>();
        foreach (var i in all.Where(x => x.IsFreeNow))
        {
            var untilLocal = time.ToLocal(i.FreeUntilUtc!.Value);
            var endLocal = new[] { startLocal.AddMinutes(60), untilLocal }.Min();
            string? url = null;
            if (endLocal > startLocal)
                url = Url.Page("/Bookings/Create", new { roomId = i.Room.Id, start = startLocal.ToString("yyyy-MM-ddTHH:mm"), end = endLocal.ToString("yyyy-MM-ddTHH:mm") });
            free.Add(new Item(i, url, endLocal));
        }
        Free = free;
        Busy = all.Where(x => x.Room.Status == RoomStatus.Active && !x.IsFreeNow && x.Current is not null)
            .Select(i => new Item(i, null, null)).ToList();
        Unavailable = all.Where(x => !x.IsFreeNow && x.Current is null).ToList();
    }
}
