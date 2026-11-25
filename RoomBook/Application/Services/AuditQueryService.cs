using RoomBook.Application.Dtos;

namespace RoomBook.Application.Services;

public class AuditQueryService(IRepository<AuditLog> logs, ITimeProvider time) : IAuditQueryService
{
    public PagedResult<AuditLog> Search(AuditQuery q)
    {
        var all = Filter(q).ToList();
        var page = Math.Max(1, q.Page);
        return new PagedResult<AuditLog>(all.Skip((page - 1) * q.PageSize).Take(q.PageSize).ToList(), page, q.PageSize, all.Count);
    }

    public IReadOnlyList<string> GetActions() => logs.GetAll().Select(l => l.Action).Distinct().Order().ToList();

    /// <summary>供匯出使用：套用同樣篩選但不分頁（最多 10000 筆）。</summary>
    public IReadOnlyList<AuditLog> All(AuditQuery q) => Filter(q).Take(10000).ToList();

    private IEnumerable<AuditLog> Filter(AuditQuery q)
    {
        var list = logs.GetAll().AsEnumerable();
        if (!string.IsNullOrWhiteSpace(q.Keyword))
        {
            var k = q.Keyword.Trim();
            list = list.Where(l => l.Action.Contains(k, StringComparison.OrdinalIgnoreCase)
                                   || l.UserName.Contains(k, StringComparison.OrdinalIgnoreCase)
                                   || (l.TargetId?.Contains(k, StringComparison.OrdinalIgnoreCase) ?? false)
                                   || (l.Detail?.Contains(k, StringComparison.OrdinalIgnoreCase) ?? false)
                                   || l.IpAddress.Contains(k, StringComparison.OrdinalIgnoreCase));
        }
        if (q.UserId is { } uid) list = list.Where(l => l.UserId == uid);
        if (!string.IsNullOrWhiteSpace(q.Action)) list = list.Where(l => l.Action == q.Action);
        if (q.From is { } f) list = list.Where(l => DateOnly.FromDateTime(time.ToLocal(l.OccurredAtUtc)) >= f);
        if (q.To is { } t) list = list.Where(l => DateOnly.FromDateTime(time.ToLocal(l.OccurredAtUtc)) <= t);
        return list.OrderByDescending(l => l.OccurredAtUtc);
    }
}
