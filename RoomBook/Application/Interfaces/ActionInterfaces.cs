using RoomBook.Application.Dtos;

namespace RoomBook.Application.Interfaces;

public interface IBookingActionService
{
    OperationResult CheckIn(BookingRef target, Guid actorId);
    OperationResult Cancel(BookingRef target, Guid actorId, string? reason);
    OperationResult Extend(BookingRef target, Guid actorId, int minutes);
    OperationResult FinishEarly(BookingRef target, Guid actorId);
    /// <summary>背景工作：開始後超過簽到時窗仍未簽到者釋出時段並記違規。回傳處理筆數。</summary>
    int ReleaseNoShows();
    /// <summary>背景工作：會議前 N 分鐘提醒。回傳處理筆數。</summary>
    int SendReminders();
}

public interface IMyBookingService
{
    PagedResult<MyBookingItem> Search(Guid userId, MyBookingQuery query);
    /// <summary>各分頁的筆數徽章（不含搜尋條件）。</summary>
    IReadOnlyDictionary<MyTab, int> CountByTab(Guid userId);
    /// <summary>找出進行中或即將開始的那一場（含可執行的動作旗標）；找不到回傳 null。</summary>
    MyBookingItem? FindActive(Guid userId, BookingRef target);
}

public interface IViolationService
{
    /// <summary>記一點違規；達上限時暫停預約並通知。</summary>
    void Record(Guid userId, Guid? bookingId, string reason);
}

public interface IIcsService
{
    string ForOccurrence(BookingDetail detail);
    string ForUser(Guid userId);
}

public interface IEmailSender
{
    /// <summary>以 .eml 檔寫入 outbox 資料夾並留下寄送紀錄（模擬寄信）。</summary>
    void Send(User user, NotificationType type, string subject, string body);
}
