using RoomBook.Application.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RoomBook.Application.Dtos;

namespace RoomBook.Pages.Display;

public sealed record BoardRow(string Time, string? Title, string Organizer, bool IsCurrent, bool IsPast);

public sealed class BoardModel
{
    public string RoomName { get; init; } = "";
    public int Floor { get; init; }
    public int Capacity { get; init; }
    public string State { get; init; } = "free";      // busy | free | closed | maintenance
    public string StateText { get; init; } = "";
    public string? CurrentTitle { get; init; }
    public string? CurrentOrganizer { get; init; }
    public string? CurrentTime { get; init; }
    public string? NextFree { get; init; }
    public bool ShowTitle { get; init; }
    public string NowText { get; init; } = "";
    public IReadOnlyList<BoardRow> Today { get; init; } = [];
}

/// <summary>會議室看板：免登入、唯讀；只顯示主題（可由設定關閉）與預約人姓名，不含其他敏感資訊。</summary>
public class IndexModel(
    IRoomService rooms,
    IBookingQueryService query,
    IUserService users,
    ISettingService settings,
    ITimeProvider time) : PageModel
{
    public BoardModel Board { get; private set; } = null!;
    public int RefreshSeconds { get; private set; }
    public Guid RoomId { get; private set; }

    public IActionResult OnGet(Guid roomId)
    {
        var board = Build(roomId);
        if (board is null) return NotFound();
        Board = board;
        RoomId = roomId;
        RefreshSeconds = Math.Max(10, settings.GetInt(SettingKeys.DisplayRefreshSeconds, 30));
        return Page();
    }

    /// <summary>自動更新用：只回傳看板區塊的 HTML。</summary>
    public IActionResult OnGetBoard(Guid roomId)
    {
        var board = Build(roomId);
        return board is null ? NotFound() : Partial("_Board", board);
    }

    private BoardModel? Build(Guid roomId)
    {
        var room = rooms.GetById(roomId);
        if (room is null) return null;

        var showTitle = settings.GetBool(SettingKeys.DisplayShowTitle, true);
        var now = time.UtcNow;
        var status = rooms.GetNowStatus().First(s => s.Room.Id == roomId);
        var todayStart = time.ToUtc(time.LocalToday.ToDateTime(TimeOnly.MinValue));
        var slots = query.GetSlots(todayStart, todayStart.AddDays(1), roomId)
            .Where(s => s.Status == BookingStatus.Confirmed).OrderBy(s => s.StartUtc).ToList();

        string Name(Guid id) => users.GetById(id)?.DisplayName ?? "";
        var rows = slots.Select(s => new BoardRow(
            $"{time.Format(s.StartUtc, "HH:mm")}–{time.Format(s.EndUtc, "HH:mm")}",
            showTitle ? s.Title : null, Name(s.OrganizerId),
            s.StartUtc <= now && now < s.EndUtc, s.EndUtc <= now)).ToList();

        var current = slots.FirstOrDefault(s => s.StartUtc <= now && now < s.EndUtc);
        var state = room.Status != RoomStatus.Active ? "maintenance" : current is not null ? "busy" : status.OpenNow ? "free" : "closed";

        // 下一個空檔：目前使用中 → 連續會議結束後；空閒 → 現在
        string? nextFree = null;
        if (room.Status == RoomStatus.Active && status.OpenNow)
        {
            var cursor = now;
            foreach (var s in slots.Where(s => s.EndUtc > now))
            {
                if (s.StartUtc > cursor) break;
                cursor = s.EndUtc;
            }
            var until = slots.FirstOrDefault(s => s.StartUtc > cursor)?.StartUtc;
            nextFree = cursor == now ? null : $"{time.Format(cursor, "HH:mm")} 起";
            if (state == "free")
                nextFree = until is null ? "現在起整天有空" : $"現在起至 {time.Format(until.Value, "HH:mm")}";
            else if (until is not null && nextFree is not null)
                nextFree += $"（至 {time.Format(until.Value, "HH:mm")}）";
        }

        return new BoardModel
        {
            RoomName = room.Name, Floor = room.Floor, Capacity = room.Capacity,
            State = state,
            StateText = state switch { "busy" => "使用中", "free" => "空閒", "maintenance" => room.Status == RoomStatus.Maintenance ? "維護中" : "停用", _ => "非營業時段" },
            CurrentTitle = current is null ? null : (showTitle ? current.Title : "會議進行中"),
            CurrentOrganizer = current is null ? null : Name(current.OrganizerId),
            CurrentTime = current is null ? null : $"{time.Format(current.StartUtc, "HH:mm")}–{time.Format(current.EndUtc, "HH:mm")}",
            NextFree = nextFree,
            ShowTitle = showTitle,
            NowText = time.Format(now, "yyyy/MM/dd HH:mm"),
            Today = rows
        };
    }
}
