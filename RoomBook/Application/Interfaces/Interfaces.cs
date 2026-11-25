using RoomBook.Application.Dtos;

namespace RoomBook.Application.Interfaces;

public static class TimeProviderExtensions
{
    /// <summary>UTC 轉台北時間並格式化。</summary>
    public static string Format(this ITimeProvider time, DateTime utc, string format = "yyyy/MM/dd HH:mm")
        => time.ToLocal(utc).ToString(format);
}

/// <summary>集中取得時間；一律以 UTC 運算，顯示時才轉台北時間。不得直接使用 DateTime.Now。</summary>
public interface ITimeProvider
{
    DateTime UtcNow { get; }
    TimeZoneInfo LocalZone { get; }
    /// <summary>台北今天的日期。</summary>
    DateOnly LocalToday { get; }
    DateTime ToLocal(DateTime utc);
    /// <summary>將台北時間（Kind 不拘）轉為 UTC。</summary>
    DateTime ToUtc(DateTime local);
}

/// <summary>資料存取抽象。Service 只依賴此介面，日後可換成資料庫實作。</summary>
public interface IRepository<T> where T : BaseEntity
{
    IReadOnlyList<T> GetAll();
    T? GetById(Guid id);
    IReadOnlyList<T> Query(Func<T, bool> predicate);
    T Add(T entity);
    /// <summary>版本不符時丟 ConcurrencyException；成功後 Version 加 1。</summary>
    T Update(T entity);
    void Delete(Guid id);
}

public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid? UserId { get; }
    string Name { get; }
    bool IsAdmin { get; }
    string IpAddress { get; }
}

public interface IUserService
{
    User? GetById(Guid id);
    User? GetByAccount(string account);
    IReadOnlyList<User> GetAll();
    IReadOnlyList<User> GetActiveUsers();
    Department? GetDepartment(Guid id);
    IReadOnlyList<Department> GetDepartments();
    bool IsSuspended(User user);
}

public interface IAuthService
{
    LoginResult Login(string account, string password);
}

public interface ISettingService
{
    string GetString(string key, string defaultValue = "");
    int GetInt(string key, int defaultValue = 0);
    bool GetBool(string key, bool defaultValue = false);
}

public interface IAuditService
{
    void Log(string action, string targetType, string? targetId = null, string? detail = null, User? actor = null);
}

public interface INotificationService
{
    int GetUnreadCount(Guid userId);
    /// <summary>建立站內通知（階段 3 會加入 Email 與偏好處理）。</summary>
    void Notify(Guid userId, NotificationType type, string title, string message, Guid? bookingId = null);
    PagedResult<Notification> GetPage(Guid userId, bool unreadOnly, int page, int pageSize);
    void MarkRead(Guid userId, Guid notificationId);
    void MarkAllRead(Guid userId);
    IReadOnlyList<NotificationType> GetEmailDisabled(Guid userId);
    void SetEmailDisabled(Guid userId, IEnumerable<NotificationType> disabled);
}

public interface IRoomService
{
    IReadOnlyList<Room> GetAll();
    Room? GetById(Guid id);
    IReadOnlyList<int> GetFloors();
    IReadOnlyList<string> GetEquipmentOptions();
    /// <summary>依樓層、人數、設備、可用時段篩選；有指定時段時只回傳該時段可預約的會議室。</summary>
    IReadOnlyList<Room> Search(RoomFilter filter);
    /// <summary>每間會議室「現在」的使用狀態。</summary>
    IReadOnlyList<RoomStatusInfo> GetNowStatus();
}

public interface IBusinessCalendarService
{
    IReadOnlyList<BusinessHours> GetWeek();
    IReadOnlyDictionary<DateOnly, string> GetHolidays();
    string? GetHolidayName(DateOnly date);
    /// <summary>當日營業時段（台北時間）；假日或公休日回傳 null。</summary>
    (TimeOnly Open, TimeOnly Close)? GetOpenWindow(DateOnly date);
}

public interface IBookingQueryService
{
    IReadOnlyList<BookingSlot> GetSlots(DateTime fromUtc, DateTime toUtc, Guid? roomId = null, bool includeFinished = false);
    IReadOnlyList<BookingSlot> ExpandSeries(BookingSeries series);
    IReadOnlyList<BookingSlot> FindRoomConflicts(Guid roomId, DateTime startUtc, DateTime endUtc, Func<BookingSlot, bool>? exclude = null);
    IReadOnlyList<BookingSlot> FindPersonConflicts(IReadOnlyCollection<Guid> personIds, DateTime startUtc, DateTime endUtc);
    BookingDetail? GetDetail(Guid? bookingId, Guid? seriesId, DateOnly? occurrenceDate);
}

public interface IBookingService
{
    /// <summary>試算：執行所有規則與衝突檢查但不寫入。</summary>
    BookingResult Preview(CreateBookingRequest request);
    /// <summary>建立預約（會議室鎖之內完成衝突檢查加寫入）。</summary>
    BookingResult Create(CreateBookingRequest request);
    OperationResult CancelOccurrence(Guid seriesId, DateOnly date, Guid actorId, string? reason);
    OperationResult ModifyOccurrence(Guid seriesId, DateOnly date, TimeOnly newStart, TimeOnly newEnd, Guid actorId);
}

public interface IDataResetService
{
    ResetResult Reset();
}
