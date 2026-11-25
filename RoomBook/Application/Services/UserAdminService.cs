using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using RoomBook.Application.Dtos;

namespace RoomBook.Application.Services;

public partial class UserAdminService(
    IRepository<User> users,
    IRepository<Violation> violations,
    IRepository<Department> departments,
    IPasswordHasher<User> hasher,
    IUserService userService,
    INotificationService notifications,
    IAuditService audit) : IUserAdminService
{
    [GeneratedRegex(@"^[A-Za-z0-9._-]{3,30}$")] private static partial Regex AccountPattern();
    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")] private static partial Regex EmailPattern();

    public PasswordResult Create(UserEditDto dto, Guid adminId)
    {
        var error = Validate(dto);
        if (error is not null) return new PasswordResult(false, error, null);
        if (users.GetAll().Any(u => string.Equals(u.Account, dto.Account.Trim(), StringComparison.OrdinalIgnoreCase)))
            return new PasswordResult(false, "這個帳號已經存在。", null);

        var temp = PasswordPolicy.Generate();
        var user = new User
        {
            Account = dto.Account.Trim(), DisplayName = dto.DisplayName.Trim(), Email = dto.Email.Trim(),
            DepartmentId = dto.DepartmentId, JobTitle = dto.JobTitle?.Trim() ?? "", Role = dto.Role, IsActive = dto.IsActive,
            MustChangePassword = true
        };
        user.PasswordHash = hasher.HashPassword(user, temp);
        var created = users.Add(user);
        audit.Log("新增使用者", nameof(User), created.Id.ToString(), $"{created.DisplayName}（{created.Role}）", userService.GetById(adminId));
        return new PasswordResult(true, null, temp);
    }

    public OperationResult Update(UserEditDto dto, Guid adminId)
    {
        var error = Validate(dto);
        if (error is not null) return OperationResult.Fail(error);
        var user = dto.Id is { } id ? users.GetById(id) : null;
        if (user is null) return OperationResult.Fail("找不到這位使用者。");

        // 不能把自己降級或停用，避免系統沒有管理員
        if (user.Id == adminId && (dto.Role != UserRole.Admin || !dto.IsActive))
            return OperationResult.Fail("不能停用自己或移除自己的管理員角色。");
        if (user.Role == UserRole.Admin && dto.Role != UserRole.Admin
            && users.Query(u => u.Role == UserRole.Admin && u.IsActive && u.Id != user.Id).Count == 0)
            return OperationResult.Fail("至少要保留一位啟用中的管理員。");

        var changes = new List<string>();
        if (user.Role != dto.Role) changes.Add($"角色 {user.Role}→{dto.Role}");
        if (user.IsActive != dto.IsActive) changes.Add(dto.IsActive ? "啟用" : "停用");
        if (user.DepartmentId != dto.DepartmentId) changes.Add("部門變更");

        user.DisplayName = dto.DisplayName.Trim(); user.Email = dto.Email.Trim(); user.DepartmentId = dto.DepartmentId;
        user.JobTitle = dto.JobTitle?.Trim() ?? ""; user.Role = dto.Role; user.IsActive = dto.IsActive;
        user.Version = dto.Version;
        try { users.Update(user); }
        catch (ConcurrencyException) { return OperationResult.Fail("這位使用者剛被其他人修改，請重新載入後再編輯。"); }
        audit.Log(dto.IsActive ? "修改使用者" : "停用使用者", nameof(User), user.Id.ToString(),
            $"{user.DisplayName}{(changes.Count > 0 ? "；" + string.Join("、", changes) : "")}", userService.GetById(adminId));
        return OperationResult.Ok();
    }

    public PasswordResult ResetPassword(Guid userId, Guid adminId)
    {
        var user = users.GetById(userId);
        if (user is null) return new PasswordResult(false, "找不到這位使用者。", null);
        var temp = PasswordPolicy.Generate();
        user.PasswordHash = hasher.HashPassword(user, temp);
        user.MustChangePassword = true;
        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        try { users.Update(user); }
        catch (ConcurrencyException) { return new PasswordResult(false, "資料剛被其他人更新，請重試。", null); }
        audit.Log("重設密碼", nameof(User), user.Id.ToString(), user.DisplayName, userService.GetById(adminId));
        return new PasswordResult(true, null, temp);
    }

    public OperationResult ReleaseSuspension(Guid userId, Guid adminId)
    {
        var user = users.GetById(userId);
        if (user is null) return OperationResult.Fail("找不到這位使用者。");
        foreach (var v in violations.Query(v => v.UserId == userId && !v.IsRevoked))
        {
            v.IsRevoked = true; v.RevokedById = adminId;
            try { violations.Update(v); } catch (ConcurrencyException) { }
        }
        user.ViolationPoints = 0;
        user.SuspendedUntilUtc = null;
        try { users.Update(user); }
        catch (ConcurrencyException) { return OperationResult.Fail("資料剛被其他人更新，請重試。"); }
        audit.Log("解除預約暫停", nameof(User), user.Id.ToString(), $"{user.DisplayName}，違規點數歸零", userService.GetById(adminId));
        notifications.Notify(userId, NotificationType.Suspended, "預約權限已恢復", "管理員已解除你的預約暫停並清除違規點數，現在可以再次預約。");
        return OperationResult.Ok();
    }

    public IReadOnlyList<Violation> GetViolations(Guid userId)
        => violations.Query(v => v.UserId == userId).OrderByDescending(v => v.OccurredAtUtc).ToList();

    private string? Validate(UserEditDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.DisplayName) || dto.DisplayName.Trim().Length > 30) return "請輸入姓名（最多 30 字）。";
        if (!AccountPattern().IsMatch(dto.Account?.Trim() ?? "")) return "帳號需為 3–30 個英文字母、數字或 . _ -。";
        if (!EmailPattern().IsMatch(dto.Email?.Trim() ?? "")) return "Email 格式不正確。";
        if (departments.GetById(dto.DepartmentId) is null) return "請選擇部門。";
        return null;
    }
}
