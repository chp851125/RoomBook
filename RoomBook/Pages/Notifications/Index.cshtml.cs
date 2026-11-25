using Microsoft.AspNetCore.Mvc;
using RoomBook.Application.Dtos;

namespace RoomBook.Pages.Notifications;

public class IndexModel(INotificationService notifications) : AppPageModel
{
    [BindProperty(SupportsGet = true)] public bool UnreadOnly { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNo { get; set; } = 1;

    public PagedResult<Notification> Result { get; private set; } = new([], 1, 15, 0);
    public int Unread { get; private set; }
    public IReadOnlyList<NotificationType> EmailDisabled { get; private set; } = [];

    public void OnGet()
    {
        Result = notifications.GetPage(CurrentUserId, UnreadOnly, PageNo, 15);
        Unread = notifications.GetUnreadCount(CurrentUserId);
        EmailDisabled = notifications.GetEmailDisabled(CurrentUserId);
    }

    /// <summary>點開通知：標為已讀並前往對應的預約（沒有對應預約就回到通知中心）。</summary>
    public IActionResult OnPostOpen(Guid id, Guid? bookingId)
    {
        notifications.MarkRead(CurrentUserId, id);
        return bookingId is { } b ? RedirectToPage("/Bookings/Details", new { id = b }) : RedirectToPage();
    }

    public IActionResult OnPostMarkRead(Guid id)
    {
        notifications.MarkRead(CurrentUserId, id);
        return RedirectToPage(new { unreadOnly = UnreadOnly, pageNo = PageNo });
    }

    public IActionResult OnPostMarkAll()
    {
        notifications.MarkAllRead(CurrentUserId);
        Success("已全部標為已讀。");
        return RedirectToPage();
    }

    /// <summary>各通知類型是否寄 Email：表單送來的是「要寄」的類型，其餘視為關閉。</summary>
    public IActionResult OnPostEmailPrefs(List<NotificationType> enabled)
    {
        var disabled = Enum.GetValues<NotificationType>().Except(enabled ?? []).ToList();
        notifications.SetEmailDisabled(CurrentUserId, disabled);
        Success("Email 通知偏好已儲存。");
        return RedirectToPage();
    }
}
