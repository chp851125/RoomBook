using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace RoomBook.Pages.Account;

public class SettingsModel(IAccountService account, IUserService users, IRoomService rooms, ISettingService settings) : AppPageModel
{
    public class PasswordInput
    {
        [Required(ErrorMessage = "請輸入目前的密碼"), DataType(DataType.Password)] public string Current { get; set; } = "";
        [Required(ErrorMessage = "請輸入新密碼"), DataType(DataType.Password)] public string New { get; set; } = "";
        [Required(ErrorMessage = "請再輸入一次新密碼"), DataType(DataType.Password), Compare(nameof(New), ErrorMessage = "兩次輸入的新密碼不一致")]
        public string Confirm { get; set; } = "";
    }

    [BindProperty] public PasswordInput Password { get; set; } = new();
    [BindProperty] public List<Guid> Favorites { get; set; } = new();
    [BindProperty(SupportsGet = true)] public bool Force { get; set; }

    public User? Me { get; private set; }
    public IReadOnlyList<Room> Rooms { get; private set; } = [];
    public int MinLength { get; private set; }

    public void OnGet() => Load();

    public IActionResult OnPostPassword()
    {
        Load();
        ModelState.Remove("Favorites");
        if (!ModelState.IsValid) return Page();
        var result = account.ChangePassword(CurrentUserId, Password.Current, Password.New);
        if (!result.Success) { ModelState.AddModelError("Password.Current", result.Error!); return Page(); }
        Success("密碼已變更。");
        return RedirectToPage();
    }

    public IActionResult OnPostFavorites()
    {
        var result = account.SetFavoriteRooms(CurrentUserId, Favorites);
        if (result.Success) Success("常用會議室已儲存，新增預約時會優先帶入。"); else Error(result.Error!);
        return RedirectToPage();
    }

    private void Load()
    {
        Me = users.GetById(CurrentUserId);
        Rooms = rooms.GetAll().Where(r => r.Status == RoomStatus.Active).ToList();
        MinLength = settings.GetInt(RoomBook.Application.Services.SettingKeys.PasswordMinLength, 8);
        if (Favorites.Count == 0 && Me is not null) Favorites = Me.FavoriteRoomIds.ToList();
    }
}
