using RoomBook.Application.Dtos;

namespace RoomBook.Application.Services;

public class BookingService(
    IRepository<Booking> bookings,
    IRepository<BookingSeries> seriesRepo,
    IRepository<BookingException> exceptions,
    IRoomService rooms,
    IUserService users,
    IBookingQueryService query,
    IBusinessCalendarService calendar,
    ISettingService settings,
    INotificationService notifications,
    IAuditService audit,
    BookingNumberService numbers,
    RoomLockProvider locks,
    ITimeProvider time) : IBookingService
{

    public BookingResult Preview(CreateBookingRequest request) => Process(request, commit: false);

    public BookingResult Create(CreateBookingRequest request) => Process(request, commit: true);

    // ------------------------------------------------------------------ 建立

    private sealed record Occurrence(DateOnly Date, DateTime StartUtc, DateTime EndUtc);

    private BookingResult Process(CreateBookingRequest r, bool commit)
    {
        var result = new BookingResult();
        var now = time.UtcNow;

        var user = users.GetById(r.OrganizerId);
        if (user is null || !user.IsActive) return Invalid(result, "找不到預約人，或帳號已停用。");
        // 管理員代為預約：ActorId 是管理員且不是預約人本人
        var actor = r.ActorId is { } aid && aid != user.Id ? users.GetById(aid) : null;
        if (r.ActorId is { } aid2 && aid2 != user.Id && actor is not { Role: UserRole.Admin })
            return Invalid(result, "只有管理員可以代為預約。");
        var byAdmin = actor is not null;
        var isAdmin = user.Role == UserRole.Admin || byAdmin;

        // 修改既有預約（編輯模式）：只能改尚未開始、仍有效的單次預約
        Booking? editing = null;
        if (r.EditBookingId is { } editId)
        {
            editing = bookings.GetById(editId);
            if (editing is null) return Invalid(result, "找不到要修改的預約。");
            if (editing.OrganizerId != user.Id && !isAdmin) return Invalid(result, "只有預約人或管理員可以修改預約。");
            if (editing.Status is not (BookingStatus.Confirmed or BookingStatus.Pending))
                return Invalid(result, "這筆預約已不是有效狀態，無法修改。");
            if (editing.StartUtc <= now) return Invalid(result, "已開始的預約無法修改，請使用「延長」或「提前結束」。");
        }
        var organizerId = editing?.OrganizerId ?? user.Id;
        var recurrence = editing is null ? r.Recurrence : RecurrenceType.None;

        if (editing is null && !byAdmin && users.IsSuspended(user))
            return Invalid(result, $"你的預約權限已暫停至 {time.Format(user.SuspendedUntilUtc!.Value)}，期間無法建立新預約。");

        var room = rooms.GetById(r.RoomId);
        if (room is null) return Invalid(result, "請選擇會議室。");
        if (room.Status != RoomStatus.Active)
            return Invalid(result, $"「{room.Name}」目前{(room.Status == RoomStatus.Maintenance ? "維護中" : "已停用")}，無法預約。");

        var errors = new List<string>();
        var title = (r.Title ?? "").Trim();
        if (title.Length == 0) errors.Add("請輸入會議主題。");
        if (title.Length > 100) errors.Add("會議主題最多 100 字。");
        if (r.EndTime <= r.StartTime) errors.Add("結束時間必須晚於開始時間（預約不可跨午夜）。");
        if (r.AttendeeCount < 1) errors.Add("參會人數至少 1 人。");
        if (r.AttendeeCount > room.Capacity) errors.Add($"參會人數 {r.AttendeeCount} 超過「{room.Name}」容納人數 {room.Capacity} 人。");
        if (recurrence != RecurrenceType.None && (r.RecurrenceEndDate is null || r.RecurrenceEndDate < r.Date))
            errors.Add("請設定有效的重複結束日期。");

        var maxHours = settings.GetInt(SettingKeys.MaxDurationHours, 4);
        if (r.EndTime > r.StartTime && (r.EndTime - r.StartTime).TotalHours > maxHours)
            errors.Add($"單次預約最長 {maxHours} 小時。");
        if (errors.Count > 0) return Invalid(result, errors.ToArray());

        // 參與者：排除預約人、去重、必須是啟用中的同事
        var participantIds = r.ParticipantIds.Distinct().Where(id => id != organizerId && users.GetById(id) is { IsActive: true }).ToList();
        var people = participantIds.Append(organizerId).ToList();
        var guests = r.Guests.Where(g => !string.IsNullOrWhiteSpace(g.Name))
            .Select(g => new ExternalGuest { Name = g.Name!.Trim(), Company = (g.Company ?? "").Trim(), Email = string.IsNullOrWhiteSpace(g.Email) ? null : g.Email.Trim() })
            .ToList();

        // 展開日期並套用營業時段／假日
        var isSeries = recurrence != RecurrenceType.None;
        var template = new BookingSeries
        {
            Recurrence = recurrence, StartDate = r.Date, EndDate = r.RecurrenceEndDate ?? r.Date,
            DaysOfWeek = r.DaysOfWeek.Count > 0 ? r.DaysOfWeek.Distinct().ToList() : [r.Date.DayOfWeek]
        };
        var dates = isSeries ? RecurrenceExpander.Expand(template) : [r.Date];
        var valid = new List<Occurrence>();
        foreach (var date in dates)
        {
            var holiday = calendar.GetHolidayName(date);
            if (holiday is not null) { result.SkippedDates.Add(new SkippedDate(date, $"假日：{holiday}")); continue; }
            var window = calendar.GetOpenWindow(date);
            if (window is null) { result.SkippedDates.Add(new SkippedDate(date, "非營業日")); continue; }
            if (r.StartTime < window.Value.Open || r.EndTime > window.Value.Close)
            {
                result.SkippedDates.Add(new SkippedDate(date, $"不在營業時段內（{window.Value.Open:HH\\:mm}–{window.Value.Close:HH\\:mm}）"));
                continue;
            }
            valid.Add(new Occurrence(date, time.ToUtc(date.ToDateTime(r.StartTime)), time.ToUtc(date.ToDateTime(r.EndTime))));
        }
        if (valid.Count == 0)
        {
            var why = result.SkippedDates.Count == 1 ? result.SkippedDates[0].Reason : "所有日期都是假日、非營業日或超出營業時段";
            return Invalid(result, isSeries ? $"沒有可預約的日期（{why}）。" : $"無法預約：{why}。");
        }
        if (!isSeries && result.SkippedDates.Count > 0) { /* 單次預約的略過原因已在 valid.Count==0 處理 */ }

        var first = valid[0];
        if (!isAdmin && !settings.GetBool(SettingKeys.AllowEmployeePast) && first.StartUtc < now)
            errors.Add("不可預約已經過去的時段。");
        var maxAdvance = settings.GetInt(SettingKeys.MaxAdvanceDays, 60);
        if (first.StartUtc > now.AddDays(maxAdvance)) errors.Add($"最多只能提前 {maxAdvance} 天預約。");
        if (!isAdmin && editing is null)
        {
            var limit = settings.GetInt(SettingKeys.MaxFutureBookingsPerUser, 10);
            if (CountFutureBookings(user.Id, now) >= limit) errors.Add($"你目前的未來預約已達上限 {limit} 筆。");
        }
        if (errors.Count > 0) return Invalid(result, errors.ToArray());

        result.RequiresApproval = room.RequiresApproval && !isAdmin;

        lock (locks.For(room.Id))
        {
            // 會議室衝突（硬性阻擋）
            foreach (var occ in valid)
            {
                var conflicts = query.FindRoomConflicts(room.Id, occ.StartUtc, occ.EndUtc, editing is null ? null : s => s.BookingId == editing.Id);
                if (conflicts.Count == 0) continue;
                result.ConflictDates.Add(occ.Date);
                foreach (var c in conflicts)
                    result.RoomConflicts.Add(new ConflictInfo(occ.Date, room.Name, c.Title, NameOf(c.OrganizerId), c.StartUtc, c.EndUtc));
            }

            if (result.ConflictDates.Count > 0)
            {
                if (!isSeries)
                {
                    result.Outcome = BookingOutcome.Invalid;
                    result.Errors.Add("這個時段已被預約，請改選其他時段或會議室。");
                    return result;
                }
                if (r.ConflictPolicy == ConflictPolicy.Ask)
                {
                    result.Outcome = BookingOutcome.NeedsConflictDecision;
                    return result;
                }
                if (r.ConflictPolicy == ConflictPolicy.CancelAll)
                    return Invalid(result, "已取消建立：週期預約中有日期與其他預約衝突。");
                valid.RemoveAll(o => result.ConflictDates.Contains(o.Date));
                if (valid.Count == 0) return Invalid(result, "所有日期都與其他預約衝突，沒有可建立的日期。");
            }

            // 參與者衝突（警告，可選擇仍然送出）
            foreach (var occ in valid)
                foreach (var c in query.FindPersonConflicts(people, occ.StartUtc, occ.EndUtc).Where(c => editing is null || c.BookingId != editing.Id))
                    foreach (var pid in c.PersonIds.Intersect(people))
                        result.ParticipantWarnings.Add(new ParticipantWarning(occ.Date, NameOf(pid), c.Title,
                            rooms.GetById(c.RoomId)?.Name ?? "", c.StartUtc, c.EndUtc));
            if (result.ParticipantWarnings.Count > 0 && !r.ConfirmParticipantConflicts)
            {
                result.Outcome = BookingOutcome.NeedsParticipantConfirm;
                return result;
            }

            result.OccurrenceCount = valid.Count;
            result.Outcome = BookingOutcome.Created;
            if (!commit) return result;

            // ---- 寫入 ----
            var status = result.RequiresApproval ? BookingStatus.Pending : BookingStatus.Confirmed;

            if (editing is not null)
            {
                var fresh = bookings.GetById(editing.Id);
                if (fresh is null) return Invalid(result, "找不到要修改的預約。");
                var occ = valid[0];
                var newInvitees = participantIds.Except(fresh.ParticipantIds).ToList();
                var needsReapproval = status == BookingStatus.Pending && fresh.Status == BookingStatus.Confirmed;
                fresh.RoomId = room.Id; fresh.Title = title; fresh.Description = r.Description?.Trim() ?? "";
                fresh.StartUtc = occ.StartUtc; fresh.EndUtc = occ.EndUtc; fresh.AttendeeCount = r.AttendeeCount;
                fresh.RequiredEquipment = r.RequiredEquipment.ToList(); fresh.ParticipantIds = participantIds;
                fresh.ExternalGuests = guests; fresh.Status = status;
                fresh.History.Add(History(user, "修改預約", needsReapproval ? "修改後需重新審核" : null));
                try { bookings.Update(fresh); }
                catch (ConcurrencyException) { return Invalid(result, "這筆預約剛被其他人修改，請重新載入後再試。"); }
                result.Booking = fresh;
                audit.Log("修改預約", nameof(Booking), fresh.BookingNo, $"{room.Name} / {title}", user);
                NotifyAfterCreate(user, title, room, occ.StartUtc, newInvitees, status == BookingStatus.Pending && needsReapproval ? BookingStatus.Pending : BookingStatus.Confirmed, fresh.Id);
                return result;
            }
            var history = new List<BookingHistoryEntry>
            {
                History(actor ?? user, byAdmin ? "管理員代為預約" : "建立預約", isSeries ? $"週期預約，共 {valid.Count} 次" : null)
            };
            if (status == BookingStatus.Pending) history.Add(History(user, "送出審核", "此會議室需管理員審核"));

            if (!isSeries)
            {
                var occ = valid[0];
                var booking = bookings.Add(new Booking
                {
                    BookingNo = numbers.Next(now),
                    RoomId = room.Id, OrganizerId = user.Id, Title = title, Description = r.Description?.Trim() ?? "",
                    StartUtc = occ.StartUtc, EndUtc = occ.EndUtc, AttendeeCount = r.AttendeeCount,
                    RequiredEquipment = r.RequiredEquipment.ToList(), ParticipantIds = participantIds, ExternalGuests = guests,
                    Status = status, History = history
                });
                result.Booking = booking;
                audit.Log(byAdmin ? "代為預約" : "建立預約", nameof(Booking), booking.BookingNo,
                    $"{room.Name} / {title}" + (byAdmin ? $"（預約人 {user.DisplayName}）" : ""), actor ?? user);
                NotifyAfterCreate(user, title, room, occ.StartUtc, participantIds, status, booking.Id);
                if (byAdmin)
                    notifications.Notify(user.Id, NotificationType.Invited, "管理員已為你預約會議",
                        $"{actor!.DisplayName} 已為你預約「{title}」（{room.Name}，{time.Format(occ.StartUtc)}）。", booking.Id);
            }
            else
            {
                var series = seriesRepo.Add(new BookingSeries
                {
                    RoomId = room.Id, OrganizerId = user.Id, Title = title, Description = r.Description?.Trim() ?? "",
                    AttendeeCount = r.AttendeeCount, RequiredEquipment = r.RequiredEquipment.ToList(),
                    ParticipantIds = participantIds, ExternalGuests = guests, Status = status,
                    Recurrence = recurrence, StartDate = r.Date, EndDate = r.RecurrenceEndDate!.Value,
                    DaysOfWeek = template.DaysOfWeek, StartTime = r.StartTime, EndTime = r.EndTime, History = history
                });
                foreach (var date in result.ConflictDates.Where(d => r.ConflictPolicy == ConflictPolicy.Skip))
                    exceptions.Add(new BookingException
                    {
                        SeriesId = series.Id, OccurrenceDate = date, IsCancelled = true,
                        Reason = "建立時與其他預約衝突，已略過", CreatedById = user.Id
                    });
                result.Series = series;
                audit.Log("建立週期預約", nameof(BookingSeries), series.Id.ToString(), $"{room.Name} / {title}，{valid.Count} 次", user);
                NotifyAfterCreate(user, title, room, first.StartUtc, participantIds, status, null);
            }
            return result;
        }
    }

    private void NotifyAfterCreate(User organizer, string title, Room room, DateTime startUtc, List<Guid> invitees, BookingStatus status, Guid? bookingId)
    {
        foreach (var id in invitees)
            notifications.Notify(id, NotificationType.Invited, "你被邀請參加會議",
                $"{organizer.DisplayName} 邀請你參加「{title}」（{room.Name}，{time.Format(startUtc)} 起）。", bookingId);
        if (status == BookingStatus.Pending)
            foreach (var admin in users.GetActiveUsers().Where(u => u.Role == UserRole.Admin))
                notifications.Notify(admin.Id, NotificationType.Invited, "有新的預約待審核",
                    $"{organizer.DisplayName} 申請{room.Name}：{title}（{time.Format(startUtc)} 起）。", bookingId);
    }

    private int CountFutureBookings(Guid userId, DateTime now)
    {
        var singles = bookings.Query(b => b.OrganizerId == userId && b.StartUtc > now
                                          && b.Status is BookingStatus.Confirmed or BookingStatus.Pending).Count;
        var today = time.LocalToday;
        var series = seriesRepo.Query(s => s.OrganizerId == userId && s.EndDate >= today
                                           && s.Status is BookingStatus.Confirmed or BookingStatus.Pending).Count;
        return singles + series;
    }


    public OperationResult CancelOccurrence(Guid seriesId, DateOnly date, Guid actorId, string? reason)
    {
        var series = seriesRepo.GetById(seriesId);
        var actor = users.GetById(actorId);
        if (series is null || actor is null) return OperationResult.Fail("找不到這個週期預約。");
        if (series.OrganizerId != actorId && actor.Role != UserRole.Admin) return OperationResult.Fail("只有預約人或管理員可以取消。");

        lock (locks.For(series.RoomId))
        {
            var slot = FindOccurrence(series, date);
            if (slot is null) return OperationResult.Fail("找不到這一次會議，或它已被取消。");
            var now = time.UtcNow;
            if (slot.EndUtc <= now) return OperationResult.Fail("這場會議已經結束，無法取消。");
            var cutoff = settings.GetInt(SettingKeys.CancelCutoffMinutes, 30);
            if (actor.Role != UserRole.Admin && slot.StartUtc - now < TimeSpan.FromMinutes(cutoff))
                return OperationResult.Fail($"距離開始不到 {cutoff} 分鐘，無法取消。");

            try
            {
                UpsertException(series, date, e => { e.IsCancelled = true; e.NewStartUtc = null; e.NewEndUtc = null; e.Reason = reason; }, actorId);
                series.History.Add(History(actor, $"取消單次 {date:yyyy/MM/dd}", reason));
                seriesRepo.Update(series);
            }
            catch (ConcurrencyException) { return OperationResult.Fail("資料剛被其他人更新，請重新整理後再試。"); }
            audit.Log("取消週期預約單次", nameof(BookingSeries), series.Id.ToString(), $"{series.Title} {date:yyyy-MM-dd}", actor);
            foreach (var pid in series.ParticipantIds.Where(p => p != actorId))
                notifications.Notify(pid, NotificationType.BookingCancelled, "會議已被取消",
                    $"{actor.DisplayName} 取消了「{series.Title}」{date:MM/dd} 這一次。{(string.IsNullOrWhiteSpace(reason) ? "" : "原因：" + reason)}");
            return OperationResult.Ok();
        }
    }

    public OperationResult ModifyOccurrence(Guid seriesId, DateOnly date, TimeOnly newStart, TimeOnly newEnd, Guid actorId)
    {
        var series = seriesRepo.GetById(seriesId);
        var actor = users.GetById(actorId);
        if (series is null || actor is null) return OperationResult.Fail("找不到這個週期預約。");
        if (series.OrganizerId != actorId && actor.Role != UserRole.Admin) return OperationResult.Fail("只有預約人或管理員可以修改。");
        if (newEnd <= newStart) return OperationResult.Fail("結束時間必須晚於開始時間。");
        var maxHours = settings.GetInt(SettingKeys.MaxDurationHours, 4);
        if ((newEnd - newStart).TotalHours > maxHours) return OperationResult.Fail($"單次預約最長 {maxHours} 小時。");
        var window = calendar.GetOpenWindow(date);
        if (window is null) return OperationResult.Fail("該日是假日或非營業日。");
        if (newStart < window.Value.Open || newEnd > window.Value.Close)
            return OperationResult.Fail($"需在營業時段 {window.Value.Open:HH\\:mm}–{window.Value.Close:HH\\:mm} 內。");

        lock (locks.For(series.RoomId))
        {
            var slot = FindOccurrence(series, date);
            if (slot is null) return OperationResult.Fail("找不到這一次會議，或它已被取消。");
            var now = time.UtcNow;
            var startUtc = time.ToUtc(date.ToDateTime(newStart));
            var endUtc = time.ToUtc(date.ToDateTime(newEnd));
            if (slot.EndUtc <= now) return OperationResult.Fail("這場會議已經結束，無法修改。");
            if (actor.Role != UserRole.Admin && startUtc < now) return OperationResult.Fail("不可改到過去的時間。");

            var conflicts = query.FindRoomConflicts(series.RoomId, startUtc, endUtc,
                s => s.SeriesId == series.Id && s.OccurrenceDate == date);
            if (conflicts.Count > 0)
                return OperationResult.Fail($"新時段與「{conflicts[0].Title}」（{NameOf(conflicts[0].OrganizerId)}）衝突。");

            try
            {
                UpsertException(series, date, e => { e.IsCancelled = false; e.NewStartUtc = startUtc; e.NewEndUtc = endUtc; }, actorId);
                series.History.Add(History(actor, $"修改單次 {date:yyyy/MM/dd}", $"{newStart:HH\\:mm}–{newEnd:HH\\:mm}"));
                seriesRepo.Update(series);
            }
            catch (ConcurrencyException) { return OperationResult.Fail("資料剛被其他人更新，請重新整理後再試。"); }
            audit.Log("修改週期預約單次", nameof(BookingSeries), series.Id.ToString(), $"{series.Title} {date:yyyy-MM-dd} → {newStart:HH\\:mm}–{newEnd:HH\\:mm}", actor);
            return OperationResult.Ok();
        }
    }

    private BookingSlot? FindOccurrence(BookingSeries series, DateOnly date)
        => query.ExpandSeries(series).FirstOrDefault(s => s.OccurrenceDate == date);

    private void UpsertException(BookingSeries series, DateOnly date, Action<BookingException> apply, Guid actorId)
    {
        var existing = exceptions.Query(x => x.SeriesId == series.Id && x.OccurrenceDate == date).FirstOrDefault();
        if (existing is null)
        {
            var created = new BookingException { SeriesId = series.Id, OccurrenceDate = date, CreatedById = actorId };
            apply(created);
            exceptions.Add(created);
        }
        else
        {
            apply(existing);
            exceptions.Update(existing);
        }
    }

    // ------------------------------------------------------------------ 輔助

    private BookingHistoryEntry History(User actor, string action, string? note)
        => new() { AtUtc = time.UtcNow, ActorId = actor.Id, ActorName = actor.DisplayName, Action = action, Note = note };

    private string NameOf(Guid userId) => users.GetById(userId)?.DisplayName ?? "(未知)";

    private static BookingResult Invalid(BookingResult result, params string[] errors)
    {
        result.Outcome = BookingOutcome.Invalid;
        result.Errors.AddRange(errors);
        return result;
    }
}
