using RoomBook.Application.Dtos;

namespace RoomBook.Application.Services;

/// <summary>簽到、取消、延長、提前結束、自動釋出與提醒。每個動作都在該會議室的鎖內完成。</summary>
public class BookingActionService(
    IRepository<Booking> bookings,
    IRepository<BookingSeries> seriesRepo,
    IBookingQueryService query,
    IBookingService bookingService,
    IViolationService violations,
    IUserService users,
    IRoomService rooms,
    INotificationService notifications,
    ISettingService settings,
    IBusinessCalendarService calendar,
    IAuditService audit,
    BookingNumberService numbers,
    RoomLockProvider locks,
    ITimeProvider time) : IBookingActionService
{
    /// <summary>一場會議目前的樣貌（可能是尚未實體化的週期預約某一次）。</summary>
    private sealed record Target(
        Guid RoomId, Guid OrganizerId, string Title, DateTime StartUtc, DateTime EndUtc, BookingStatus Status,
        DateTime? CheckedInAtUtc, IReadOnlyList<Guid> ParticipantIds, Booking? Booking, BookingSeries? Series, DateOnly? Date);

    private Target? Peek(BookingRef r)
    {
        if (r.BookingId is { } id)
        {
            var b = bookings.GetById(id);
            return b is null ? null : FromBooking(b);
        }
        if (r.SeriesId is { } sid && r.Date is { } date)
        {
            var materialized = bookings.Query(b => b.SeriesId == sid && b.OccurrenceDate == date).FirstOrDefault();
            if (materialized is not null) return FromBooking(materialized);
            var series = seriesRepo.GetById(sid);
            var slot = series is null ? null : query.ExpandSeries(series).FirstOrDefault(s => s.OccurrenceDate == date);
            if (series is null || slot is null) return null;
            return new Target(series.RoomId, series.OrganizerId, series.Title, slot.StartUtc, slot.EndUtc, series.Status,
                null, series.ParticipantIds, null, series, date);
        }
        return null;
    }

    private static Target FromBooking(Booking b)
        => new(b.RoomId, b.OrganizerId, b.Title, b.StartUtc, b.EndUtc, b.Status, b.CheckedInAtUtc, b.ParticipantIds, b, null, b.OccurrenceDate);

    /// <summary>週期預約的某一次需要被「實體化」成 Booking 才能記錄簽到、延長、提前結束等狀態。</summary>
    private Booking Ensure(Target t)
    {
        if (t.Booking is not null) return t.Booking;
        var s = t.Series!;
        return bookings.Add(new Booking
        {
            BookingNo = numbers.Next(time.UtcNow),
            RoomId = s.RoomId, OrganizerId = s.OrganizerId, Title = s.Title, Description = s.Description,
            StartUtc = t.StartUtc, EndUtc = t.EndUtc, AttendeeCount = s.AttendeeCount,
            RequiredEquipment = s.RequiredEquipment.ToList(), ParticipantIds = s.ParticipantIds.ToList(),
            ExternalGuests = s.ExternalGuests.ToList(), Status = s.Status,
            SeriesId = s.Id, OccurrenceDate = t.Date,
            History = [new BookingHistoryEntry { AtUtc = time.UtcNow, ActorName = "系統", Action = "週期預約本次實體化" }]
        });
    }

    private BookingHistoryEntry Entry(User? actor, string action, string? note = null)
        => new() { AtUtc = time.UtcNow, ActorId = actor?.Id, ActorName = actor?.DisplayName ?? "系統", Action = action, Note = note };

    private static bool Concurrent(Action write)
    {
        try { write(); return true; } catch (ConcurrencyException) { return false; }
    }

    private const string ConcurrentMessage = "資料剛被其他人更新，請重新整理後再試。";

    // ------------------------------------------------------------------ 簽到

    public OperationResult CheckIn(BookingRef target, Guid actorId)
    {
        var actor = users.GetById(actorId);
        var t = Peek(target);
        if (actor is null || t is null) return OperationResult.Fail("找不到這筆預約。");
        if (t.OrganizerId != actorId && actor.Role != UserRole.Admin) return OperationResult.Fail("只有預約人可以簽到。");

        lock (locks.For(t.RoomId))
        {
            t = Peek(target);
            if (t is null) return OperationResult.Fail("找不到這筆預約。");
            if (t.Status == BookingStatus.Pending) return OperationResult.Fail("預約尚未通過審核，無法簽到。");
            if (t.Status != BookingStatus.Confirmed) return OperationResult.Fail("這筆預約目前無法簽到。");
            if (t.CheckedInAtUtc is not null) return OperationResult.Fail("已經簽到過了。");

            var early = settings.GetInt(SettingKeys.CheckInEarlyMinutes, 10);
            var late = settings.GetInt(SettingKeys.CheckInLateMinutes, 15);
            var now = time.UtcNow;
            var opens = t.StartUtc.AddMinutes(-early);
            var closes = t.StartUtc.AddMinutes(late);
            if (now < opens) return OperationResult.Fail($"還沒到簽到時間，{time.Format(opens, "HH:mm")} 起可簽到（開始前 {early} 分鐘）。");
            if (now > closes) return OperationResult.Fail($"已超過簽到時窗（開始後 {late} 分鐘），時段將被釋出。");

            var b = Ensure(t);
            b.CheckedInAtUtc = now;
            b.History.Add(Entry(actor, "簽到"));
            if (!Concurrent(() => bookings.Update(b))) return OperationResult.Fail(ConcurrentMessage);
            return OperationResult.Ok();
        }
    }

    // ------------------------------------------------------------------ 取消

    public OperationResult Cancel(BookingRef target, Guid actorId, string? reason)
    {
        var actor = users.GetById(actorId);
        var t = Peek(target);
        if (actor is null || t is null) return OperationResult.Fail("找不到這筆預約。");
        var isAdmin = actor.Role == UserRole.Admin;
        if (t.OrganizerId != actorId && !isAdmin) return OperationResult.Fail("只有預約人或管理員可以取消。");

        // 尚未實體化的週期預約：走單次例外
        if (t.Booking is null && t.Series is not null)
            return bookingService.CancelOccurrence(t.Series.Id, t.Date!.Value, actorId, reason);

        var forced = isAdmin && t.OrganizerId != actorId;
        reason = reason?.Trim();
        if (forced && string.IsNullOrEmpty(reason)) return OperationResult.Fail("強制取消必須填寫原因，系統會通知預約人。");

        lock (locks.For(t.RoomId))
        {
            t = Peek(target);
            if (t?.Booking is null) return OperationResult.Fail("找不到這筆預約。");
            if (t.Status is not (BookingStatus.Confirmed or BookingStatus.Pending)) return OperationResult.Fail("這筆預約已不是有效狀態，無法取消。");
            var now = time.UtcNow;
            if (t.EndUtc <= now) return OperationResult.Fail("會議已經結束，無法取消。");
            var cutoff = settings.GetInt(SettingKeys.CancelCutoffMinutes, 30);
            if (!isAdmin && t.StartUtc - now < TimeSpan.FromMinutes(cutoff))
                return OperationResult.Fail($"距離開始不到 {cutoff} 分鐘（或已開始），無法取消。進行中的會議請改用「提前結束」。");

            var b = t.Booking;
            b.Status = BookingStatus.Cancelled;
            b.CancelReason = reason;
            b.CancelledById = actorId;
            b.CancelledAtUtc = now;
            b.History.Add(Entry(actor, forced ? "管理員強制取消" : "取消預約", reason));
            if (!Concurrent(() => bookings.Update(b))) return OperationResult.Fail(ConcurrentMessage);

            var roomName = rooms.GetById(b.RoomId)?.Name ?? "";
            var detail = string.IsNullOrEmpty(reason) ? "" : $"原因：{reason}";
            if (forced)
                notifications.Notify(b.OrganizerId, NotificationType.ForceCancelled, "預約被管理員強制取消",
                    $"{actor.DisplayName} 取消了你的預約「{b.Title}」（{roomName}，{time.Format(b.StartUtc)}）。{detail}", b.Id);
            foreach (var pid in b.ParticipantIds.Where(p => p != actorId))
                notifications.Notify(pid, NotificationType.BookingCancelled, "會議已被取消",
                    $"{actor.DisplayName} 取消了「{b.Title}」（{roomName}，{time.Format(b.StartUtc)}）。{detail}", b.Id);
            audit.Log(forced ? "強制取消預約" : "取消預約", nameof(Booking), b.BookingNo, reason, actor);
            return OperationResult.Ok();
        }
    }

    // ------------------------------------------------------------------ 延長、提前結束

    public OperationResult Extend(BookingRef target, Guid actorId, int minutes)
    {
        var actor = users.GetById(actorId);
        var t = Peek(target);
        if (actor is null || t is null) return OperationResult.Fail("找不到這筆預約。");
        if (t.OrganizerId != actorId && actor.Role != UserRole.Admin) return OperationResult.Fail("只有預約人可以延長。");
        if (minutes is < 5 or > 240) return OperationResult.Fail("延長時間不正確。");

        lock (locks.For(t.RoomId))
        {
            t = Peek(target);
            if (t is null) return OperationResult.Fail("找不到這筆預約。");
            var now = time.UtcNow;
            if (t.Status != BookingStatus.Confirmed || t.StartUtc > now || t.EndUtc <= now)
                return OperationResult.Fail("只有進行中的會議可以延長。");

            var newEnd = t.EndUtc.AddMinutes(minutes);
            var localStart = time.ToLocal(t.StartUtc);
            var localNewEnd = time.ToLocal(newEnd);
            var window = calendar.GetOpenWindow(DateOnly.FromDateTime(localStart));
            if (localNewEnd.Date != localStart.Date || window is null || TimeOnly.FromDateTime(localNewEnd) > window.Value.Close)
                return OperationResult.Fail($"延長後會超出營業時段{(window is null ? "" : $"（{window.Value.Close:HH\\:mm} 結束）")}。");

            var conflicts = query.FindRoomConflicts(t.RoomId, t.EndUtc, newEnd);
            if (conflicts.Count > 0)
                return OperationResult.Fail($"後續時段 {time.Format(conflicts[0].StartUtc, "HH:mm")} 已有「{conflicts[0].Title}」預約，無法延長。");

            var b = Ensure(t);
            b.EndUtc = newEnd;
            b.History.Add(Entry(actor, $"延長 {minutes} 分鐘", $"新的結束時間 {time.Format(newEnd, "HH:mm")}"));
            if (!Concurrent(() => bookings.Update(b))) return OperationResult.Fail(ConcurrentMessage);
            audit.Log("延長會議", nameof(Booking), b.BookingNo, $"+{minutes} 分鐘", actor);
            return OperationResult.Ok();
        }
    }

    public OperationResult FinishEarly(BookingRef target, Guid actorId)
    {
        var actor = users.GetById(actorId);
        var t = Peek(target);
        if (actor is null || t is null) return OperationResult.Fail("找不到這筆預約。");
        if (t.OrganizerId != actorId && actor.Role != UserRole.Admin) return OperationResult.Fail("只有預約人可以結束會議。");

        lock (locks.For(t.RoomId))
        {
            t = Peek(target);
            if (t is null) return OperationResult.Fail("找不到這筆預約。");
            var now = time.UtcNow;
            if (t.Status != BookingStatus.Confirmed || t.StartUtc > now || t.EndUtc <= now)
                return OperationResult.Fail("只有進行中的會議可以提前結束。");

            var b = Ensure(t);
            var originalEnd = b.EndUtc;
            b.Status = BookingStatus.Finished;
            b.EndUtc = now;
            b.History.Add(Entry(actor, "提前結束", $"原訂 {time.Format(originalEnd, "HH:mm")} 結束，剩餘時段已釋出"));
            if (!Concurrent(() => bookings.Update(b))) return OperationResult.Fail(ConcurrentMessage);
            audit.Log("提前結束會議", nameof(Booking), b.BookingNo, null, actor);
            return OperationResult.Ok();
        }
    }

    // ------------------------------------------------------------------ 背景工作

    public int ReleaseNoShows()
    {
        var now = time.UtcNow;
        var late = settings.GetInt(SettingKeys.CheckInLateMinutes, 15);
        // 只處理「剛剛超過時窗」的預約（回溯 30 分鐘），避免服務停機後一次對舊資料大量記點
        var oldest = now.AddMinutes(-30);
        bool Due(DateTime start) => start.AddMinutes(late) < now && start.AddMinutes(late) >= oldest;

        var refs = new List<BookingRef>();
        refs.AddRange(bookings.Query(b => b.Status == BookingStatus.Confirmed && b.CheckedInAtUtc == null && Due(b.StartUtc))
            .Select(b => new BookingRef(b.Id, null, null)));
        refs.AddRange(query.GetSlots(oldest.AddMinutes(-late), now)
            .Where(s => s.IsSeries && s.BookingId is null && s.Status == BookingStatus.Confirmed && Due(s.StartUtc))
            .Select(s => new BookingRef(null, s.SeriesId, s.OccurrenceDate)));

        var released = 0;
        foreach (var r in refs)
        {
            var peek = Peek(r);
            if (peek is null) continue;
            lock (locks.For(peek.RoomId))
            {
                var t = Peek(r);
                if (t is null || t.Status != BookingStatus.Confirmed || t.CheckedInAtUtc is not null || !Due(t.StartUtc)) continue;

                var b = Ensure(t);
                b.Status = BookingStatus.Released;
                b.ReleasedAtUtc = now;
                b.History.Add(Entry(null, "逾時未簽到，自動釋出時段", $"簽到時窗為開始後 {late} 分鐘"));
                if (!Concurrent(() => bookings.Update(b))) continue;
                released++;

                var roomName = rooms.GetById(b.RoomId)?.Name ?? "";
                notifications.Notify(b.OrganizerId, NotificationType.AutoReleased, "預約時段已釋出",
                    $"你的預約「{b.Title}」（{roomName}，{time.Format(b.StartUtc)}）因開始後 {late} 分鐘內未簽到，時段已自動釋出。", b.Id);
                violations.Record(b.OrganizerId, b.Id, $"預約「{b.Title}」未於簽到時窗內簽到");
                audit.Log("自動釋出未簽到時段", nameof(Booking), b.BookingNo, null, users.GetById(b.OrganizerId));
            }
        }
        return released;
    }

    public int SendReminders()
    {
        var now = time.UtcNow;
        var minutes = settings.GetInt(SettingKeys.ReminderMinutes, 15);
        var until = now.AddMinutes(minutes);

        var refs = new List<BookingRef>();
        refs.AddRange(bookings.Query(b => b.Status == BookingStatus.Confirmed && b.ReminderSentAtUtc == null
                                          && b.StartUtc > now && b.StartUtc <= until)
            .Select(b => new BookingRef(b.Id, null, null)));
        refs.AddRange(query.GetSlots(now, until.AddMinutes(1))
            .Where(s => s.IsSeries && s.BookingId is null && s.Status == BookingStatus.Confirmed && s.StartUtc > now && s.StartUtc <= until)
            .Select(s => new BookingRef(null, s.SeriesId, s.OccurrenceDate)));

        var sent = 0;
        foreach (var r in refs)
        {
            var peek = Peek(r);
            if (peek is null) continue;
            lock (locks.For(peek.RoomId))
            {
                var t = Peek(r);
                if (t is null || t.Status != BookingStatus.Confirmed || t.StartUtc <= now) continue;
                var b = Ensure(t);
                if (b.ReminderSentAtUtc is not null) continue;
                b.ReminderSentAtUtc = now;
                if (!Concurrent(() => bookings.Update(b))) continue;
                sent++;

                var roomName = rooms.GetById(b.RoomId)?.Name ?? "";
                var wait = Math.Max(1, (int)Math.Ceiling((b.StartUtc - now).TotalMinutes));
                foreach (var pid in b.ParticipantIds.Append(b.OrganizerId).Distinct())
                    notifications.Notify(pid, NotificationType.Reminder, "會議即將開始",
                        $"「{b.Title}」將於 {wait} 分鐘後（{time.Format(b.StartUtc, "HH:mm")}）在{roomName}開始。", b.Id);
            }
        }
        return sent;
    }
}
