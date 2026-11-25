using Microsoft.AspNetCore.Mvc;
using RoomBook.Application.Dtos;

namespace RoomBook.Pages.Admin.Audit;

public class IndexModel(IAuditQueryService auditQuery, IReportService reports, IUserService users, ITimeProvider time) : AdminPageModel
{
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? UserId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Action { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? To { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNo { get; set; } = 1;

    public PagedResult<AuditLog> Result { get; private set; } = new([], 1, 20, 0);
    public IReadOnlyList<string> Actions { get; private set; } = [];
    public IReadOnlyList<User> Users { get; private set; } = [];

    public void OnGet()
    {
        Actions = auditQuery.GetActions();
        Users = users.GetAll();
        var q = BuildQuery();
        q.Page = PageNo;
        Result = auditQuery.Search(q);
    }

    public IActionResult OnGetExport()
    {
        var bytes = reports.AuditReport(BuildQuery());
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"roombook-audit-{time.UtcNow:yyyyMMdd}.xlsx");
    }

    public string? PageUrl(int page)
        => Url.Page("/Admin/Audit/Index", new { q = Q, userId = UserId, action = Action, from = From?.ToString("yyyy-MM-dd"), to = To?.ToString("yyyy-MM-dd"), pageNo = page });

    private AuditQuery BuildQuery() => new() { Keyword = Q, UserId = UserId, Action = Action, From = From, To = To };
}
