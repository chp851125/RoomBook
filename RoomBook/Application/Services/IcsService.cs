using System.Text;
using RoomBook.Application.Dtos;

namespace RoomBook.Application.Services;

/// <summary>產生 iCalendar（.ics）：時間以 UTC 表示，行事曆軟體會自動換成本地時區。</summary>
public class IcsService(
    IBookingQueryService query,
    IRoomService rooms,
    IUserService users,
    ITimeProvider time) : IIcsService
{
    public string ForOccurrence(BookingDetail d)
    {
        var sb = Begin();
        AddEvent(sb, UidOf(d.BookingId, d.SeriesId, d.OccurrenceDate), d.Title, d.StartUtc, d.EndUtc,
            $"{d.RoomName}（{d.RoomFloor} 樓）", Describe(d.Description, d.OrganizerName, d.Status), d.Status);
        return End(sb);
    }

    public string ForUser(Guid userId)
    {
        var now = time.UtcNow;
        var sb = Begin();
        var roomMap = rooms.GetAll().ToDictionary(r => r.Id);
        foreach (var s in query.GetSlots(now.AddDays(-1), now.AddDays(120)).Where(s => s.PersonIds.Contains(userId)))
        {
            var room = roomMap.GetValueOrDefault(s.RoomId);
            AddEvent(sb, UidOf(s.BookingId, s.SeriesId, s.OccurrenceDate), s.Title, s.StartUtc, s.EndUtc,
                room is null ? "" : $"{room.Name}（{room.Floor} 樓）",
                Describe("", users.GetById(s.OrganizerId)?.DisplayName ?? "", s.Status), s.Status);
        }
        return End(sb);
    }

    private static string UidOf(Guid? bookingId, Guid? seriesId, DateOnly? date)
        => (bookingId is { } b ? $"{b}" : $"{seriesId}-{date:yyyyMMdd}") + "@roombook";

    private static string Describe(string? description, string organizer, BookingStatus status)
        => $"預約人：{organizer}" + (status == BookingStatus.Pending ? "（待審核）" : "") + (string.IsNullOrWhiteSpace(description) ? "" : "\n" + description);

    private StringBuilder Begin()
    {
        var sb = new StringBuilder();
        sb.Append("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//RoomBook//會議室預約//ZH\r\nCALSCALE:GREGORIAN\r\nMETHOD:PUBLISH\r\n");
        sb.Append("X-WR-CALNAME:RoomBook 會議\r\n");
        return sb;
    }

    private static string End(StringBuilder sb) => sb.Append("END:VCALENDAR\r\n").ToString();

    private void AddEvent(StringBuilder sb, string uid, string title, DateTime startUtc, DateTime endUtc, string location, string description, BookingStatus status)
    {
        sb.Append("BEGIN:VEVENT\r\n");
        Line(sb, "UID:" + uid);
        Line(sb, "DTSTAMP:" + Stamp(time.UtcNow));
        Line(sb, "DTSTART:" + Stamp(startUtc));
        Line(sb, "DTEND:" + Stamp(endUtc));
        Line(sb, "SUMMARY:" + Escape(title));
        Line(sb, "LOCATION:" + Escape(location));
        Line(sb, "DESCRIPTION:" + Escape(description));
        Line(sb, "STATUS:" + (status == BookingStatus.Pending ? "TENTATIVE" : status is BookingStatus.Cancelled or BookingStatus.Rejected ? "CANCELLED" : "CONFIRMED"));
        sb.Append("END:VEVENT\r\n");
    }

    private static string Stamp(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyyMMdd'T'HHmmss'Z'");

    private static string Escape(string s)
        => s.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\r\n", "\\n").Replace("\n", "\\n");

    /// <summary>依 RFC 5545 折行：每行最多 75 個位元組，續行以空白開頭。</summary>
    private static void Line(StringBuilder sb, string line)
    {
        var bytes = 0;
        foreach (var rune in line.EnumerateRunes())
        {
            var size = rune.Utf8SequenceLength;
            if (bytes + size > 74) { sb.Append("\r\n "); bytes = 1; }
            sb.Append(rune.ToString());
            bytes += size;
        }
        sb.Append("\r\n");
    }
}
