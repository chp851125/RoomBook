namespace RoomBook.Application.Services;

public class ViolationService(
    IRepository<Violation> violations,
    IRepository<User> users,
    ISettingService settings,
    INotificationService notifications,
    IAuditService audit,
    ITimeProvider time) : IViolationService
{
    public void Record(Guid userId, Guid? bookingId, string reason)
    {
        var now = time.UtcNow;
        violations.Add(new Violation { UserId = userId, BookingId = bookingId, Reason = reason, Points = 1, OccurredAtUtc = now });

        var limit = settings.GetInt(SettingKeys.ViolationPointLimit, 3);
        var days = settings.GetInt(SettingKeys.ViolationSuspendDays, 7);
        User? user = null;
        var justSuspended = false;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            user = users.GetById(userId);
            if (user is null) return;
            user.ViolationPoints++;
            justSuspended = user.ViolationPoints >= limit && !(user.SuspendedUntilUtc > now);
            if (justSuspended) user.SuspendedUntilUtc = now.AddDays(days);
            try { users.Update(user); break; }
            catch (ConcurrencyException) { user = null; }
        }
        if (user is null) return;

        audit.Log("違規記點", nameof(Violation), userId.ToString(), $"{reason}（累計 {user.ViolationPoints} 點）", user);
        notifications.Notify(userId, NotificationType.Violation, "違規記點通知",
            $"{reason}。已記 1 點，目前累計 {user.ViolationPoints} 點（達 {limit} 點將暫停預約 {days} 天）。", bookingId);
        if (justSuspended)
            notifications.Notify(userId, NotificationType.Suspended, "預約權限已暫停",
                $"你的違規點數已達 {limit} 點，預約權限暫停至 {time.Format(user.SuspendedUntilUtc!.Value)}。如有疑問請洽管理員。");
    }
}
