using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace RoomBook.ViewComponents;

/// <summary>導覽列的通知鈴鐺（未讀紅點）。</summary>
public class NotificationBellViewComponent(INotificationService notifications) : ViewComponent
{
    public IViewComponentResult Invoke()
    {
        var unread = Guid.TryParse(UserClaimsPrincipal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? notifications.GetUnreadCount(id) : 0;
        return View(unread);
    }
}
