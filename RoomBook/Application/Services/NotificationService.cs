using RoomBook.Application.Dtos;

namespace RoomBook.Application.Services;

public static class NotificationLabels
{
    public static string Of(NotificationType type) => type switch
    {
        NotificationType.Invited => "被邀請／待審核",
        NotificationType.BookingApproved => "預約核准",
        NotificationType.BookingRejected => "預約駁回",
        NotificationType.BookingCancelled => "預約被取消",
        NotificationType.ForceCancelled => "被管理員強制取消",
        NotificationType.Reminder => "會議前提醒",
        NotificationType.AutoReleased => "時段被自動釋出",
        NotificationType.Violation => "違規記點",
        NotificationType.Suspended => "預約權限暫停",
        _ => type.ToString()
    };

    public static string Icon(NotificationType type) => type switch
    {
        NotificationType.Invited => "bi-person-plus",
        NotificationType.BookingApproved => "bi-check-circle",
        NotificationType.BookingRejected => "bi-x-circle",
        NotificationType.BookingCancelled or NotificationType.ForceCancelled => "bi-calendar-x",
        NotificationType.Reminder => "bi-alarm",
        NotificationType.AutoReleased => "bi-unlock",
        NotificationType.Violation => "bi-exclamation-triangle",
        NotificationType.Suspended => "bi-slash-circle",
        _ => "bi-bell"
    };
}

public class NotificationService(
    IRepository<Notification> notifications,
    IRepository<User> users,
    IEmailSender email,
    ITimeProvider time) : INotificationService
{
    public int GetUnreadCount(Guid userId) => notifications.Query(n => n.UserId == userId && !n.IsRead).Count;

    public void Notify(Guid userId, NotificationType type, string title, string message, Guid? bookingId = null)
    {
        notifications.Add(new Notification
        {
            UserId = userId, Type = type, Title = title, Message = message, BookingId = bookingId
        });

        // Email：使用者可依通知類型自行關閉；停用的帳號不寄
        var user = users.GetById(userId);
        if (user is { IsActive: true } && !user.EmailDisabledTypes.Contains(type))
            email.Send(user, type, $"[RoomBook] {title}", message);
    }

    public PagedResult<Notification> GetPage(Guid userId, bool unreadOnly, int page, int pageSize)
    {
        var all = notifications.Query(n => n.UserId == userId && (!unreadOnly || !n.IsRead))
            .OrderByDescending(n => n.CreatedAt).ToList();
        page = Math.Max(1, page);
        return new PagedResult<Notification>(all.Skip((page - 1) * pageSize).Take(pageSize).ToList(), page, pageSize, all.Count);
    }

    public void MarkRead(Guid userId, Guid notificationId)
    {
        var n = notifications.GetById(notificationId);
        if (n is null || n.UserId != userId || n.IsRead) return;
        n.IsRead = true;
        n.ReadAtUtc = time.UtcNow;
        try { notifications.Update(n); } catch (ConcurrencyException) { }
    }

    public void MarkAllRead(Guid userId)
    {
        foreach (var n in notifications.Query(n => n.UserId == userId && !n.IsRead)) MarkRead(userId, n.Id);
    }

    public IReadOnlyList<NotificationType> GetEmailDisabled(Guid userId)
        => users.GetById(userId)?.EmailDisabledTypes ?? [];

    public void SetEmailDisabled(Guid userId, IEnumerable<NotificationType> disabled)
    {
        var user = users.GetById(userId);
        if (user is null) return;
        user.EmailDisabledTypes = disabled.Distinct().ToList();
        users.Update(user);
    }
}
