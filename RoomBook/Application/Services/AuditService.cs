namespace RoomBook.Application.Services;

public class AuditService(
    IRepository<AuditLog> logs,
    ICurrentUser currentUser,
    ITimeProvider time) : IAuditService
{
    public void Log(string action, string targetType, string? targetId = null, string? detail = null, User? actor = null)
    {
        logs.Add(new AuditLog
        {
            UserId = actor?.Id ?? currentUser.UserId,
            UserName = actor?.DisplayName ?? (currentUser.IsAuthenticated ? currentUser.Name : "(系統)"),
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Detail = detail,
            IpAddress = currentUser.IpAddress,
            OccurredAtUtc = time.UtcNow
        });
    }
}
