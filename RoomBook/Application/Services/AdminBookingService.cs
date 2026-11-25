using RoomBook.Application.Dtos;

namespace RoomBook.Application.Services;

public class AdminBookingService(
    IRepository<Booking> bookings,
    IRepository<BookingSeries> seriesRepo,
    IBookingQueryService query,
    IRoomService rooms,
    IUserService users,
    INotificationService notifications,
    IAuditService audit,
    RoomLockProvider locks,
    ITimeProvider time) : IAdminBookingService
{
    public PagedResult<MyBookingItem> Search(AdminBookingQuery q, Guid adminId)
    {
        var now = time.UtcNow;
        var roomNames = rooms.GetAll().ToDictionary(r => r.Id, r => r.Name);
        string Person(Guid id) => users.GetById(id)?.DisplayName ?? "";

        MyBookingItem Make(Guid? bid, Guid? sid, DateOnly? date, string no, string title, Guid room, Guid org,
            DateTime s, DateTime e, BookingStatus st, DateTime? checkedIn)
            => new()
            {
                BookingId = bid, SeriesId = sid, OccurrenceDate = date, BookingNo = no, Title = title, RoomId = room,
                RoomName = roomNames.GetValueOrDefault(room, ""), OrganizerId = org, OrganizerName = Person(org),
                StartUtc = s, EndUtc = e, Status = st, IsSeries = sid is not null, CheckedIn = checkedIn is not null,
                InProgress = st == BookingStatus.Confirmed && s <= now && now < e,
                CanCancel = st is BookingStatus.Confirmed or BookingStatus.Pending && e > now
            };

        var items = bookings.GetAll().Select(b => Make(b.Id, b.SeriesId, b.OccurrenceDate, b.BookingNo, b.Title, b.RoomId,
                b.OrganizerId, b.StartUtc, b.EndUtc, b.Status, b.CheckedInAtUtc)).ToList();
        foreach (var s in seriesRepo.Query(s => s.Status is BookingStatus.Confirmed or BookingStatus.Pending))
            items.AddRange(query.ExpandSeries(s)
                .Where(o => o.EndUtc > now.AddDays(-30) && o.StartUtc < now.AddDays(90))
                .Select(o => Make(null, s.Id, o.OccurrenceDate, "週期預約", o.Title, o.RoomId, o.OrganizerId, o.StartUtc, o.EndUtc, o.Status, null)));

        var list = items.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(q.Keyword))
        {
            var k = q.Keyword.Trim();
            list = list.Where(i => i.Title.Contains(k, StringComparison.OrdinalIgnoreCase) || i.OrganizerName.Contains(k, StringComparison.OrdinalIgnoreCase)
                                   || i.BookingNo.Contains(k, StringComparison.OrdinalIgnoreCase) || i.RoomName.Contains(k, StringComparison.OrdinalIgnoreCase));
        }
        if (q.Status is { } st2) list = list.Where(i => i.Status == st2);
        if (q.RoomId is { } rid) list = list.Where(i => i.RoomId == rid);
        if (q.From is { } f) list = list.Where(i => DateOnly.FromDateTime(time.ToLocal(i.StartUtc)) >= f);
        if (q.To is { } t) list = list.Where(i => DateOnly.FromDateTime(time.ToLocal(i.StartUtc)) <= t);

        var sorted = list.OrderByDescending(i => i.StartUtc).ToList();
        var page = Math.Max(1, q.Page);
        return new PagedResult<MyBookingItem>(sorted.Skip((page - 1) * q.PageSize).Take(q.PageSize).ToList(), page, q.PageSize, sorted.Count);
    }

    public IReadOnlyList<ApprovalRow> GetPending()
    {
        var roomNames = rooms.GetAll().ToDictionary(r => r.Id, r => r.Name);
        string Dept(Guid userId) => users.GetDepartment(users.GetById(userId)?.DepartmentId ?? Guid.Empty)?.Name ?? "";
        var rows = new List<ApprovalRow>();
        foreach (var b in bookings.Query(b => b.Status == BookingStatus.Pending))
            rows.Add(new ApprovalRow
            {
                BookingId = b.Id, Title = b.Title, RoomName = roomNames.GetValueOrDefault(b.RoomId, ""),
                Organizer = users.GetById(b.OrganizerId)?.DisplayName ?? "", Department = Dept(b.OrganizerId),
                StartUtc = b.StartUtc, EndUtc = b.EndUtc, CreatedAt = b.CreatedAt, AttendeeCount = b.AttendeeCount, Description = b.Description
            });
        foreach (var s in seriesRepo.Query(s => s.Status == BookingStatus.Pending))
        {
            var first = query.ExpandSeries(s).FirstOrDefault();
            rows.Add(new ApprovalRow
            {
                SeriesId = s.Id, Title = s.Title, RoomName = roomNames.GetValueOrDefault(s.RoomId, ""),
                Organizer = users.GetById(s.OrganizerId)?.DisplayName ?? "", Department = Dept(s.OrganizerId),
                StartUtc = first?.StartUtc ?? time.ToUtc(s.StartDate.ToDateTime(s.StartTime)),
                EndUtc = first?.EndUtc ?? time.ToUtc(s.StartDate.ToDateTime(s.EndTime)),
                CreatedAt = s.CreatedAt, AttendeeCount = s.AttendeeCount, Description = s.Description,
                RecurrenceText = BookingQueryService.DescribeRecurrence(s)
            });
        }
        return rows.OrderBy(r => r.StartUtc).ToList();
    }

    public OperationResult Approve(BookingRef target, Guid adminId) => Review(target, adminId, approve: true, null);

    public OperationResult Reject(BookingRef target, Guid adminId, string reason)
        => string.IsNullOrWhiteSpace(reason) ? OperationResult.Fail("駁回必須填寫原因，系統會通知申請人。") : Review(target, adminId, false, reason.Trim());

    private OperationResult Review(BookingRef target, Guid adminId, bool approve, string? reason)
    {
        var admin = users.GetById(adminId);
        if (admin is not { Role: UserRole.Admin }) return OperationResult.Fail("只有管理員可以審核。");
        var verb = approve ? "核准" : "駁回";
        var now = time.UtcNow;

        if (target.BookingId is { } bid)
        {
            var peek = bookings.GetById(bid);
            if (peek is null) return OperationResult.Fail("找不到這筆預約。");
            lock (locks.For(peek.RoomId))
            {
                var b = bookings.GetById(bid)!;
                if (b.Status != BookingStatus.Pending) return OperationResult.Fail("這筆預約已經被處理過了。");
                b.Status = approve ? BookingStatus.Confirmed : BookingStatus.Rejected;
                b.ReviewedById = adminId; b.ReviewedAtUtc = now; b.ReviewNote = reason;
                b.History.Add(new BookingHistoryEntry { AtUtc = now, ActorId = adminId, ActorName = admin.DisplayName, Action = verb, Note = reason });
                try { bookings.Update(b); }
                catch (ConcurrencyException) { return OperationResult.Fail("這筆預約剛被更新，請重新整理後再處理。"); }
                var room = rooms.GetById(b.RoomId)?.Name ?? "";
                if (approve)
                    notifications.Notify(b.OrganizerId, NotificationType.BookingApproved, "預約已核准",
                        $"「{b.Title}」（{room}，{time.Format(b.StartUtc)}）已通過審核。", b.Id);
                else
                    notifications.Notify(b.OrganizerId, NotificationType.BookingRejected, "預約已被駁回",
                        $"「{b.Title}」（{room}，{time.Format(b.StartUtc)}）已被駁回：{reason}", b.Id);
                audit.Log($"{verb}預約", nameof(Booking), b.BookingNo, reason, admin);
                return OperationResult.Ok();
            }
        }

        if (target.SeriesId is { } sid)
        {
            var peek = seriesRepo.GetById(sid);
            if (peek is null) return OperationResult.Fail("找不到這組週期預約。");
            lock (locks.For(peek.RoomId))
            {
                var s = seriesRepo.GetById(sid)!;
                if (s.Status != BookingStatus.Pending) return OperationResult.Fail("這組預約已經被處理過了。");
                s.Status = approve ? BookingStatus.Confirmed : BookingStatus.Rejected;
                s.History.Add(new BookingHistoryEntry { AtUtc = now, ActorId = adminId, ActorName = admin.DisplayName, Action = verb, Note = reason });
                try { seriesRepo.Update(s); }
                catch (ConcurrencyException) { return OperationResult.Fail("這組預約剛被更新，請重新整理後再處理。"); }
                var room = rooms.GetById(s.RoomId)?.Name ?? "";
                if (approve)
                    notifications.Notify(s.OrganizerId, NotificationType.BookingApproved, "週期預約已核准", $"「{s.Title}」（{room}）整組週期預約已通過審核。");
                else
                    notifications.Notify(s.OrganizerId, NotificationType.BookingRejected, "週期預約已被駁回", $"「{s.Title}」（{room}）已被駁回：{reason}");
                audit.Log($"{verb}週期預約", nameof(BookingSeries), s.Id.ToString(), reason, admin);
                return OperationResult.Ok();
            }
        }
        return OperationResult.Fail("找不到要審核的預約。");
    }
}
