namespace RoomBook.Application.Dtos;

public enum LoginStatus { Success, InvalidCredentials, Disabled, LockedOut }

public sealed record LoginResult(LoginStatus Status, User? User = null, int LockoutMinutesLeft = 0)
{
    public bool Succeeded => Status == LoginStatus.Success;
}

public sealed record ResetResult(bool Success, string Message);
