using ClosedXML.Excel;
using RoomBook.Application.Dtos;

namespace RoomBook.Application.Services;

/// <summary>Excel 匯出（ClosedXML）：使用報表與稽核紀錄。</summary>
public class ReportService(
    IRepository<Booking> bookings,
    IBookingQueryService query,
    IRoomService rooms,
    IUserService users,
    IStatsService stats,
    IAuditQueryService audit,
    ITimeProvider time) : IReportService
{
    private static string StatusText(BookingStatus s) => s switch
    {
        BookingStatus.Confirmed => "已確認", BookingStatus.Pending => "待審核", BookingStatus.Cancelled => "已取消",
        BookingStatus.Rejected => "已駁回", BookingStatus.Finished => "已結束", _ => "未簽到釋出"
    };

    public byte[] UsageReport(DateOnly from, DateOnly to)
    {
        if (to < from) (from, to) = (to, from);
        var fromUtc = time.ToUtc(from.ToDateTime(TimeOnly.MinValue));
        var toUtc = time.ToUtc(to.AddDays(1).ToDateTime(TimeOnly.MinValue));
        var roomMap = rooms.GetAll().ToDictionary(r => r.Id);
        var data = stats.Get(from, to);

        using var wb = new XLWorkbook();

        // 摘要
        var sum = wb.AddWorksheet("摘要");
        sum.Cell(1, 1).Value = "RoomBook 使用報表";
        sum.Cell(1, 1).Style.Font.Bold = true; sum.Cell(1, 1).Style.Font.FontSize = 16;
        sum.Cell(2, 1).Value = $"期間：{from:yyyy/MM/dd} – {to:yyyy/MM/dd}";
        sum.Cell(3, 1).Value = $"產生時間：{time.Format(time.UtcNow)}（台北時間）";
        sum.Cell(5, 1).Value = "預約場次"; sum.Cell(5, 2).Value = data.BookingCount;
        sum.Cell(6, 1).Value = "使用總時數"; sum.Cell(6, 2).Value = data.TotalHours;
        sum.Cell(7, 1).Value = "整體使用率 %"; sum.Cell(7, 2).Value = data.OverallRate;
        sum.Cell(8, 1).Value = "簽到率 %"; sum.Cell(8, 2).Value = data.CheckIn.CheckInRate;
        sum.Cell(9, 1).Value = "未簽到率 %"; sum.Cell(9, 2).Value = data.CheckIn.NoShowRate;
        sum.Column(1).Width = 20; sum.Column(2).Width = 14;

        // 各會議室使用率
        var rs = wb.AddWorksheet("會議室使用率");
        Header(rs, "會議室", "使用時數", "使用率 %");
        var row = 2;
        foreach (var r in data.Rooms) { rs.Cell(row, 1).Value = r.Name; rs.Cell(row, 2).Value = r.Hours; rs.Cell(row, 3).Value = r.Rate; row++; }
        rs.Columns().AdjustToContents();

        // 各部門
        var ds = wb.AddWorksheet("部門使用時數");
        Header(ds, "部門", "使用時數");
        row = 2;
        foreach (var d in data.Departments) { ds.Cell(row, 1).Value = d.Name; ds.Cell(row, 2).Value = d.Hours; row++; }
        ds.Columns().AdjustToContents();

        // 明細：實體預約 + 尚未實體化的週期預約
        var detail = wb.AddWorksheet("預約明細");
        Header(detail, "單號", "主題", "會議室", "樓層", "預約人", "部門", "日期", "開始", "結束", "時數", "狀態", "已簽到", "人數", "建立時間");
        var lines = new List<(string No, string Title, Guid RoomId, Guid Org, DateTime S, DateTime E, string Status, bool Checked, int Count, DateTime Created)>();
        foreach (var b in bookings.Query(b => b.StartUtc >= fromUtc && b.StartUtc < toUtc))
            lines.Add((b.BookingNo, b.Title, b.RoomId, b.OrganizerId, b.StartUtc, b.EndUtc, StatusText(b.Status), b.CheckedInAtUtc is not null, b.AttendeeCount, b.CreatedAt));
        foreach (var s in query.GetSlots(fromUtc, toUtc).Where(s => s.IsSeries && s.BookingId is null && s.StartUtc >= fromUtc && s.StartUtc < toUtc))
            lines.Add(("週期預約", s.Title, s.RoomId, s.OrganizerId, s.StartUtc, s.EndUtc, StatusText(s.Status), false, s.PersonIds.Count, s.StartUtc));
        row = 2;
        foreach (var l in lines.OrderBy(l => l.S))
        {
            var org = users.GetById(l.Org);
            var room = roomMap.GetValueOrDefault(l.RoomId);
            var ls = time.ToLocal(l.S); var le = time.ToLocal(l.E);
            detail.Cell(row, 1).Value = l.No; detail.Cell(row, 2).Value = l.Title;
            detail.Cell(row, 3).Value = room?.Name ?? ""; detail.Cell(row, 4).Value = room?.Floor ?? 0;
            detail.Cell(row, 5).Value = org?.DisplayName ?? "";
            detail.Cell(row, 6).Value = users.GetDepartment(org?.DepartmentId ?? Guid.Empty)?.Name ?? "";
            detail.Cell(row, 7).Value = ls.ToString("yyyy-MM-dd"); detail.Cell(row, 8).Value = ls.ToString("HH:mm"); detail.Cell(row, 9).Value = le.ToString("HH:mm");
            detail.Cell(row, 10).Value = Math.Round((l.E - l.S).TotalHours, 2);
            detail.Cell(row, 11).Value = l.Status; detail.Cell(row, 12).Value = l.Checked ? "是" : "否";
            detail.Cell(row, 13).Value = l.Count; detail.Cell(row, 14).Value = time.Format(l.Created);
            row++;
        }
        detail.Columns().AdjustToContents();
        detail.SheetView.FreezeRows(1);

        return Save(wb);
    }

    public byte[] AuditReport(AuditQuery q)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("稽核紀錄");
        Header(ws, "時間（台北）", "使用者", "動作", "對象類型", "對象", "說明", "IP");
        var row = 2;
        foreach (var l in audit.All(q))
        {
            ws.Cell(row, 1).Value = time.Format(l.OccurredAtUtc, "yyyy-MM-dd HH:mm:ss");
            ws.Cell(row, 2).Value = l.UserName; ws.Cell(row, 3).Value = l.Action; ws.Cell(row, 4).Value = l.TargetType;
            ws.Cell(row, 5).Value = l.TargetId ?? ""; ws.Cell(row, 6).Value = Sanitize(l.Detail); ws.Cell(row, 7).Value = l.IpAddress;
            row++;
        }
        ws.Columns().AdjustToContents();
        ws.SheetView.FreezeRows(1);
        return Save(wb);
    }

    /// <summary>避免 Excel 公式注入：以 = + - @ 開頭的文字前面補單引號。</summary>
    private static string Sanitize(string? s)
        => !string.IsNullOrEmpty(s) && "=+-@".Contains(s[0]) ? "'" + s : s ?? "";

    private static void Header(IXLWorksheet ws, params string[] titles)
    {
        for (var i = 0; i < titles.Length; i++) ws.Cell(1, i + 1).Value = titles[i];
        var range = ws.Range(1, 1, 1, titles.Length);
        range.Style.Font.Bold = true;
        range.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F3A5F");
        range.Style.Font.FontColor = XLColor.White;
    }

    private static byte[] Save(XLWorkbook wb)
    {
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
