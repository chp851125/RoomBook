namespace RoomBook.Domain.Enums;

public enum BookingStatus { Confirmed, Pending, Cancelled, Rejected, Finished, Released }

public enum RoomStatus { Active, Maintenance, Disabled }

public enum UserRole { Employee, Admin }

public enum RecurrenceType { None, Weekly, BiWeekly, Monthly }

public enum NotificationType
{
    Invited,            // 被邀請參加會議
    BookingApproved,    // 預約核准
    BookingRejected,    // 預約駁回
    BookingCancelled,   // 預約被取消
    ForceCancelled,     // 被管理員強制取消
    Reminder,           // 會議前提醒
    AutoReleased,       // 時段被自動釋出
    Violation,          // 違規記點
    Suspended           // 被暫停預約
}
