namespace RoomBook.Domain.Entities;

public class User : BaseEntity
{
    public string Account { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public UserRole Role { get; set; } = UserRole.Employee;
    public Guid DepartmentId { get; set; }
    public string JobTitle { get; set; } = "";
    public bool IsActive { get; set; } = true;
    /// <summary>管理員重設密碼或新增帳號後，下次登入須先變更密碼。</summary>
    public bool MustChangePassword { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEndUtc { get; set; }
    public int ViolationPoints { get; set; }
    public DateTime? SuspendedUntilUtc { get; set; }
    public List<Guid> FavoriteRoomIds { get; set; } = new();
    /// <summary>使用者自行關閉 Email 的通知類型。</summary>
    public List<NotificationType> EmailDisabledTypes { get; set; } = new();
}
