using Microsoft.AspNetCore.Mvc;

namespace RoomBook.Pages.Admin.Users;

public class IndexModel(IUserService users) : AdminPageModel
{
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public bool Suspended { get; set; }

    public IReadOnlyList<User> Users { get; private set; } = [];
    public Dictionary<Guid, string> Departments { get; private set; } = new();

    public void OnGet()
    {
        Departments = users.GetDepartments().ToDictionary(d => d.Id, d => d.Name);
        var list = users.GetAll().AsEnumerable();
        if (!string.IsNullOrWhiteSpace(Q))
            list = list.Where(u => u.DisplayName.Contains(Q.Trim(), StringComparison.OrdinalIgnoreCase)
                                   || u.Account.Contains(Q.Trim(), StringComparison.OrdinalIgnoreCase)
                                   || u.Email.Contains(Q.Trim(), StringComparison.OrdinalIgnoreCase));
        if (Suspended) list = list.Where(users.IsSuspended);
        Users = list.ToList();
    }

    public bool IsSuspended(User u) => users.IsSuspended(u);
}
