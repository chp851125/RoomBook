using RoomBook.Application.Dtos;

namespace RoomBook.Application.Interfaces;

public interface IRoomAdminService
{
    IReadOnlyList<Room> GetAll();
    int CountFutureBookings(Guid roomId);
    OperationResult Save(RoomEditDto dto, Guid adminId);
}

public interface IUserAdminService
{
    /// <summary>新增帳號：回傳一組臨時密碼（只會顯示一次，使用者首次登入須變更）。</summary>
    PasswordResult Create(UserEditDto dto, Guid adminId);
    OperationResult Update(UserEditDto dto, Guid adminId);
    PasswordResult ResetPassword(Guid userId, Guid adminId);
    /// <summary>解除預約暫停並撤銷尚未撤銷的違規點數。</summary>
    OperationResult ReleaseSuspension(Guid userId, Guid adminId);
    IReadOnlyList<Violation> GetViolations(Guid userId);
}

public interface IAccountService
{
    OperationResult ChangePassword(Guid userId, string current, string newPassword);
    OperationResult SetFavoriteRooms(Guid userId, IEnumerable<Guid> roomIds);
}

public interface ISettingAdminService
{
    IReadOnlyList<AppSetting> GetAll();
    OperationResult Save(IReadOnlyList<SettingEditDto> values, Guid adminId);
}

public interface IScheduleAdminService
{
    IReadOnlyList<Holiday> GetHolidays();
    OperationResult SaveHours(IReadOnlyList<HoursEditDto> hours, Guid adminId);
    OperationResult AddHoliday(DateOnly date, string name, Guid adminId);
    OperationResult DeleteHoliday(Guid id, Guid adminId);
}

public interface IAdminBookingService
{
    PagedResult<MyBookingItem> Search(AdminBookingQuery query, Guid adminId);
    IReadOnlyList<ApprovalRow> GetPending();
    OperationResult Approve(BookingRef target, Guid adminId);
    OperationResult Reject(BookingRef target, Guid adminId, string reason);
}

public interface IStatsService
{
    StatsData Get(DateOnly from, DateOnly to);
}

public interface IReportService
{
    byte[] UsageReport(DateOnly from, DateOnly to);
    byte[] AuditReport(AuditQuery query);
}

public interface IAuditQueryService
{
    PagedResult<AuditLog> Search(AuditQuery query);
    IReadOnlyList<string> GetActions();
    /// <summary>套用篩選但不分頁（匯出用，最多 10000 筆）。</summary>
    IReadOnlyList<AuditLog> All(AuditQuery query);
}
