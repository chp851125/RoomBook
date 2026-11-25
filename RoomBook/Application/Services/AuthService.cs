using Microsoft.AspNetCore.Identity;
using RoomBook.Application.Dtos;

namespace RoomBook.Application.Services;

public class AuthService(
    IRepository<User> users,
    IPasswordHasher<User> hasher,
    ISettingService settings,
    IAuditService audit,
    ITimeProvider time) : IAuthService
{
    public LoginResult Login(string account, string password)
    {
        var user = users.Query(u => string.Equals(u.Account, account?.Trim(), StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
        if (user is null)
            return new LoginResult(LoginStatus.InvalidCredentials);

        if (!user.IsActive)
            return new LoginResult(LoginStatus.Disabled);

        var now = time.UtcNow;
        if (user.LockoutEndUtc is { } lockEnd && lockEnd > now)
            return new LoginResult(LoginStatus.LockedOut, null, (int)Math.Ceiling((lockEnd - now).TotalMinutes));

        var verify = hasher.VerifyHashedPassword(user, user.PasswordHash, password ?? "");
        if (verify == PasswordVerificationResult.Failed)
        {
            user.FailedLoginCount++;
            var maxFailures = settings.GetInt(SettingKeys.MaxLoginFailures, 5);
            if (user.FailedLoginCount >= maxFailures)
            {
                var minutes = settings.GetInt(SettingKeys.LockoutMinutes, 15);
                user.LockoutEndUtc = now.AddMinutes(minutes);
                user.FailedLoginCount = 0;
                SaveBestEffort(user);
                audit.Log("登入失敗次數過多，帳號鎖定", nameof(User), user.Id.ToString(), $"鎖定 {minutes} 分鐘", user);
                return new LoginResult(LoginStatus.LockedOut, null, minutes);
            }
            SaveBestEffort(user);
            return new LoginResult(LoginStatus.InvalidCredentials);
        }

        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        if (verify == PasswordVerificationResult.SuccessRehashNeeded)
            user.PasswordHash = hasher.HashPassword(user, password!);
        SaveBestEffort(user);
        audit.Log("登入", nameof(User), user.Id.ToString(), null, user);
        return new LoginResult(LoginStatus.Success, user);
    }

    /// <summary>登入計數的更新若遇並行衝突，重讀後再試一次，仍失敗則略過（不影響登入結果）。</summary>
    private void SaveBestEffort(User snapshot)
    {
        try { users.Update(snapshot); }
        catch (RoomBook.Domain.ConcurrencyException)
        {
            var fresh = users.GetById(snapshot.Id);
            if (fresh is null) return;
            fresh.FailedLoginCount = snapshot.FailedLoginCount;
            fresh.LockoutEndUtc = snapshot.LockoutEndUtc;
            fresh.PasswordHash = snapshot.PasswordHash;
            try { users.Update(fresh); } catch (RoomBook.Domain.ConcurrencyException) { }
        }
    }
}
