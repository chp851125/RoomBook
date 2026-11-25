using Microsoft.AspNetCore.Identity;
using RoomBook.Application.Dtos;

namespace RoomBook.Application.Services;

public class AccountService(
    IRepository<User> users,
    IRepository<Room> rooms,
    IPasswordHasher<User> hasher,
    ISettingService settings,
    IAuditService audit) : IAccountService
{
    public OperationResult ChangePassword(Guid userId, string current, string newPassword)
    {
        var user = users.GetById(userId);
        if (user is null) return OperationResult.Fail("找不到使用者。");
        if (hasher.VerifyHashedPassword(user, user.PasswordHash, current ?? "") == PasswordVerificationResult.Failed)
            return OperationResult.Fail("目前的密碼不正確。");
        var problem = PasswordPolicy.Validate(newPassword, settings.GetInt(SettingKeys.PasswordMinLength, 8));
        if (problem is not null) return OperationResult.Fail(problem);
        if (current == newPassword) return OperationResult.Fail("新密碼不能與目前的密碼相同。");

        user.PasswordHash = hasher.HashPassword(user, newPassword);
        user.MustChangePassword = false;
        try { users.Update(user); }
        catch (ConcurrencyException) { return OperationResult.Fail("資料剛被更新，請再試一次。"); }
        audit.Log("變更密碼", nameof(User), user.Id.ToString(), null, user);
        return OperationResult.Ok();
    }

    public OperationResult SetFavoriteRooms(Guid userId, IEnumerable<Guid> roomIds)
    {
        var user = users.GetById(userId);
        if (user is null) return OperationResult.Fail("找不到使用者。");
        var valid = rooms.GetAll().Select(r => r.Id).ToHashSet();
        user.FavoriteRoomIds = roomIds.Where(valid.Contains).Distinct().ToList();
        try { users.Update(user); }
        catch (ConcurrencyException) { return OperationResult.Fail("資料剛被更新，請再試一次。"); }
        return OperationResult.Ok();
    }
}
