namespace RoomBook.Pages.Account;

public class MeModel(IUserService users) : AppPageModel
{
    public User? Me { get; private set; }
    public string DepartmentName { get; private set; } = "";
    public bool IsSuspended { get; private set; }

    public void OnGet()
    {
        Me = users.GetById(CurrentUserId);
        if (Me is null) return;
        DepartmentName = users.GetDepartment(Me.DepartmentId)?.Name ?? "";
        IsSuspended = users.IsSuspended(Me);
    }
}
