namespace RoomBook.Application.Services;

public class UserService(
    IRepository<User> users,
    IRepository<Department> departments,
    ITimeProvider time) : IUserService
{
    public User? GetById(Guid id) => users.GetById(id);

    public User? GetByAccount(string account)
        => users.Query(u => string.Equals(u.Account, account, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();

    public IReadOnlyList<User> GetAll() => users.GetAll().OrderBy(u => u.DisplayName).ToList();

    public IReadOnlyList<User> GetActiveUsers() => users.Query(u => u.IsActive).OrderBy(u => u.DisplayName).ToList();

    public Department? GetDepartment(Guid id) => departments.GetById(id);

    public IReadOnlyList<Department> GetDepartments() => departments.GetAll().OrderBy(d => d.Name).ToList();

    public bool IsSuspended(User user) => user.SuspendedUntilUtc is { } until && until > time.UtcNow;
}
