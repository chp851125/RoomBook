using System.Text.RegularExpressions;
using RoomBook.Application.Dtos;

namespace RoomBook.Application.Services;

public class RoomAdminService(
    IRepository<Room> rooms,
    IRepository<Booking> bookings,
    IRepository<BookingSeries> seriesRepo,
    IUserService users,
    IAuditService audit,
    ITimeProvider time) : IRoomAdminService
{
    public IReadOnlyList<Room> GetAll() => rooms.GetAll().OrderBy(r => r.SortOrder).ThenBy(r => r.Name).ToList();

    public int CountFutureBookings(Guid roomId)
    {
        var now = time.UtcNow;
        var today = time.LocalToday;
        return bookings.Query(b => b.RoomId == roomId && b.EndUtc > now && b.Status is BookingStatus.Confirmed or BookingStatus.Pending).Count
               + seriesRepo.Query(s => s.RoomId == roomId && s.EndDate >= today && s.Status is BookingStatus.Confirmed or BookingStatus.Pending).Count;
    }

    public OperationResult Save(RoomEditDto dto, Guid adminId)
    {
        var name = dto.Name?.Trim() ?? "";
        if (name.Length == 0 || name.Length > 40) return OperationResult.Fail("請輸入會議室名稱（最多 40 字）。");
        if (dto.Capacity is < 1 or > 500) return OperationResult.Fail("容納人數需介於 1 到 500。");
        if (dto.Floor is < -5 or > 100) return OperationResult.Fail("樓層不正確。");
        if (rooms.GetAll().Any(r => r.Id != dto.Id && string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)))
            return OperationResult.Fail("已有同名的會議室。");

        var equipment = Regex.Split(dto.EquipmentText ?? "", @"[,，、\s]+").Where(s => s.Length > 0).Distinct().ToList();
        var admin = users.GetById(adminId);

        if (dto.Id is null)
        {
            var created = rooms.Add(new Room
            {
                Name = name, Floor = dto.Floor, Capacity = dto.Capacity, Equipment = equipment, Description = dto.Description?.Trim() ?? "",
                Status = dto.Status, RequiresApproval = dto.RequiresApproval, SortOrder = dto.SortOrder
            });
            audit.Log("新增會議室", nameof(Room), created.Id.ToString(), name, admin);
            return OperationResult.Ok();
        }

        var room = rooms.GetById(dto.Id.Value);
        if (room is null) return OperationResult.Fail("找不到這間會議室。");
        if (dto.Status != RoomStatus.Active && room.Status == RoomStatus.Active && !dto.ConfirmDisable)
        {
            var count = CountFutureBookings(room.Id);
            if (count > 0)
                return OperationResult.Fail($"「{room.Name}」還有 {count} 筆未來預約。停用或維護不會自動取消它們，請先處理，或勾選「仍要停用」。");
        }

        var changes = new List<string>();
        if (room.Status != dto.Status) changes.Add($"狀態 {room.Status}→{dto.Status}");
        if (room.RequiresApproval != dto.RequiresApproval) changes.Add($"需審核 {room.RequiresApproval}→{dto.RequiresApproval}");
        if (room.Capacity != dto.Capacity) changes.Add($"容納 {room.Capacity}→{dto.Capacity}");

        room.Name = name; room.Floor = dto.Floor; room.Capacity = dto.Capacity; room.Equipment = equipment;
        room.Description = dto.Description?.Trim() ?? ""; room.Status = dto.Status;
        room.RequiresApproval = dto.RequiresApproval; room.SortOrder = dto.SortOrder;
        room.Version = dto.Version; // 以表單載入時的版本做樂觀並行檢查
        try { rooms.Update(room); }
        catch (ConcurrencyException) { return OperationResult.Fail("這間會議室剛被其他人修改，請重新載入後再編輯。"); }
        audit.Log("修改會議室", nameof(Room), room.Id.ToString(), $"{name}；{string.Join("、", changes)}", admin);
        return OperationResult.Ok();
    }
}
