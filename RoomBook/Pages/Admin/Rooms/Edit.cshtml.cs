using Microsoft.AspNetCore.Mvc;
using RoomBook.Application.Dtos;

namespace RoomBook.Pages.Admin.Rooms;

public class EditModel(IRoomAdminService roomAdmin) : AdminPageModel
{
    [BindProperty] public RoomEditDto Input { get; set; } = new();
    public int FutureCount { get; private set; }
    public bool IsNew => Input.Id is null;

    public IActionResult OnGet(Guid? id)
    {
        if (id is null) return Page();
        var room = roomAdmin.GetAll().FirstOrDefault(r => r.Id == id);
        if (room is null) return NotFound();
        Input = new RoomEditDto
        {
            Id = room.Id, Version = room.Version, Name = room.Name, Floor = room.Floor, Capacity = room.Capacity,
            EquipmentText = string.Join("、", room.Equipment), Description = room.Description, Status = room.Status,
            RequiresApproval = room.RequiresApproval, SortOrder = room.SortOrder
        };
        FutureCount = roomAdmin.CountFutureBookings(room.Id);
        return Page();
    }

    public IActionResult OnPost()
    {
        var result = roomAdmin.Save(Input, CurrentUserId);
        if (result.Success)
        {
            Success(IsNew ? "已新增會議室。" : "已儲存會議室設定。");
            return RedirectToPage("/Admin/Rooms/Index");
        }
        ModelState.AddModelError(string.Empty, result.Error!);
        if (Input.Id is { } id) FutureCount = roomAdmin.CountFutureBookings(id);
        return Page();
    }
}
