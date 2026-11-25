using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using RoomBook.Application.Dtos;

namespace RoomBook.Pages.Admin.Stats;

public class IndexModel(IStatsService stats, IReportService reports, IAuditService audit, IUserService users, ITimeProvider time) : AdminPageModel
{
    [BindProperty(SupportsGet = true)] public DateOnly? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? To { get; set; }

    public StatsData Data { get; private set; } = new();
    public string DataJson { get; private set; } = "{}";

    public void OnGet()
    {
        Normalize();
        Data = stats.Get(From!.Value, To!.Value);
        // System.Text.Json 預設會跳脫 < > & 等字元，內嵌在 <script type="application/json"> 是安全的
        DataJson = JsonSerializer.Serialize(Data, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    /// <summary>匯出 Excel 使用報表（含摘要、會議室使用率、部門時數、預約明細）。</summary>
    public IActionResult OnGetExport()
    {
        Normalize();
        var bytes = reports.UsageReport(From!.Value, To!.Value);
        audit.Log("匯出使用報表", "Report", null, $"{From:yyyy-MM-dd} ~ {To:yyyy-MM-dd}", users.GetById(CurrentUserId));
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"roombook-usage-{From:yyyyMMdd}-{To:yyyyMMdd}.xlsx");
    }

    private void Normalize()
    {
        var today = time.LocalToday;
        To ??= today;
        From ??= To.Value.AddDays(-29);
        if (From > To) (From, To) = (To, From);
        if (To.Value.DayNumber - From.Value.DayNumber > 366) From = To.Value.AddDays(-366);
    }
}
