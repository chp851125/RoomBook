using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace RoomBook.Pages.Bookings;

public class IndexModel(
    IRoomService rooms,
    IBookingQueryService query,
    IBusinessCalendarService calendar,
    IUserService users,
    ITimeProvider time) : AppPageModel
{
    private static readonly string[] Palette = ["#0d6efd", "#198754", "#6f42c1", "#fd7e14", "#20c997", "#d63384", "#0dcaf0", "#6c757d"];

    [BindProperty(SupportsGet = true)] public Guid? RoomId { get; set; }
    [BindProperty(SupportsGet = true)] public int? Floor { get; set; }

    public IReadOnlyList<Room> Rooms { get; private set; } = [];
    public IReadOnlyList<int> Floors { get; private set; } = [];
    public string ConfigJson { get; private set; } = "{}";
    public Dictionary<Guid, string> Colors { get; private set; } = new();

    public void OnGet()
    {
        Rooms = rooms.GetAll();
        Floors = rooms.GetFloors();
        Colors = ColorMap();

        var week = calendar.GetWeek().Where(h => h.IsOpen).ToList();
        var min = week.Count > 0 ? week.Min(h => h.OpenTime.Hour) : 8;
        var max = week.Count > 0 ? Math.Min(24, week.Max(h => h.CloseTime.Hour) + (week.Max(h => h.CloseTime.Minute) > 0 ? 1 : 0)) : 19;
        var config = new
        {
            eventsUrl = Url.Page("/Bookings/Index", "Events"),
            createUrl = Url.Page("/Bookings/Create"),
            slotMin = $"{Math.Max(0, min - 0):00}:00:00",
            slotMax = $"{max:00}:00:00",
            isAdmin = IsAdmin,
            roomId = RoomId,
            floor = Floor,
            businessHours = week.Select(h => new
            {
                daysOfWeek = new[] { (int)h.Day },
                startTime = h.OpenTime.ToString("HH:mm"),
                endTime = h.CloseTime.ToString("HH:mm")
            })
        };
        ConfigJson = JsonSerializer.Serialize(config, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    /// <summary>FullCalendar 事件資料。timeZone 設為 UTC 並傳回「台北牆上時間」，使顯示不受瀏覽器時區影響。</summary>
    public IActionResult OnGetEvents(string start, string end, Guid? roomId, int? floor)
    {
        if (!TryWallClock(start, out var from) || !TryWallClock(end, out var to)) return BadRequest();
        var fromUtc = time.ToUtc(from);
        var toUtc = time.ToUtc(to);

        var all = rooms.GetAll();
        var colors = ColorMap();
        var allowed = all.Where(r => (roomId is null || r.Id == roomId) && (floor is null || r.Floor == floor))
            .ToDictionary(r => r.Id);
        var single = roomId is not null;

        var events = new List<object>();
        foreach (var slot in query.GetSlots(fromUtc, toUtc, roomId, includeFinished: true).Where(s => allowed.ContainsKey(s.RoomId)))
        {
            var room = allowed[slot.RoomId];
            var organizer = users.GetById(slot.OrganizerId)?.DisplayName ?? "";
            var classes = new List<string>();
            if (slot.Status == BookingStatus.Pending) classes.Add("fc-event-pending");
            if (slot.Status == BookingStatus.Finished) classes.Add("fc-event-finished");
            events.Add(new
            {
                id = slot.BookingId?.ToString() ?? $"{slot.SeriesId}-{slot.OccurrenceDate:yyyyMMdd}",
                title = (slot.Status == BookingStatus.Pending ? "[待審] " : "") + (single ? slot.Title : $"{room.Name}｜{slot.Title}"),
                start = time.ToLocal(slot.StartUtc).ToString("yyyy-MM-ddTHH:mm:ss"),
                end = time.ToLocal(slot.EndUtc).ToString("yyyy-MM-ddTHH:mm:ss"),
                backgroundColor = colors[room.Id],
                borderColor = colors[room.Id],
                classNames = classes,
                url = slot.BookingId is { } bid
                    ? Url.Page("/Bookings/Details", new { id = bid })
                    : Url.Page("/Bookings/Details", new { seriesId = slot.SeriesId, date = slot.OccurrenceDate!.Value.ToString("yyyy-MM-dd") }),
                extendedProps = new { room = room.Name, organizer, recurring = slot.IsSeries }
            });
        }

        var fromDate = DateOnly.FromDateTime(from);
        var toDate = DateOnly.FromDateTime(to);
        foreach (var (date, name) in calendar.GetHolidays().Where(h => h.Key >= fromDate && h.Key < toDate))
            events.Add(new
            {
                id = $"holiday-{date:yyyyMMdd}", title = name, start = date.ToString("yyyy-MM-dd"),
                allDay = true, display = "background", backgroundColor = "#f8d7da"
            });

        return new JsonResult(events);
    }

    private Dictionary<Guid, string> ColorMap()
        => rooms.GetAll().Select((r, i) => (r.Id, Color: Palette[i % Palette.Length])).ToDictionary(x => x.Id, x => x.Color);

    private static bool TryWallClock(string value, out DateTime wall)
    {
        wall = default;
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto)) return false;
        wall = DateTime.SpecifyKind(dto.DateTime, DateTimeKind.Unspecified);
        return true;
    }
}
