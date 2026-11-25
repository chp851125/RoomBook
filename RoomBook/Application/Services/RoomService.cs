using RoomBook.Application.Dtos;

namespace RoomBook.Application.Services;

public class RoomService(
    IRepository<Room> rooms,
    IBookingQueryService query,
    IBusinessCalendarService calendar,
    IUserService users,
    ITimeProvider time) : IRoomService
{
    public IReadOnlyList<Room> GetAll() => rooms.GetAll().OrderBy(r => r.SortOrder).ThenBy(r => r.Name).ToList();

    public Room? GetById(Guid id) => rooms.GetById(id);

    public IReadOnlyList<int> GetFloors() => rooms.GetAll().Select(r => r.Floor).Distinct().Order().ToList();

    public IReadOnlyList<string> GetEquipmentOptions()
        => rooms.GetAll().SelectMany(r => r.Equipment).Distinct().Order().ToList();

    public IReadOnlyList<Room> Search(RoomFilter f)
    {
        var list = GetAll().Where(r =>
            (f.Floor is null || r.Floor == f.Floor)
            && (f.MinCapacity is null || r.Capacity >= f.MinCapacity)
            && f.Equipment.All(e => r.Equipment.Contains(e)));

        if (f.StartUtc is { } s && f.EndUtc is { } e && e > s)
            list = list.Where(r => r.Status == RoomStatus.Active && query.FindRoomConflicts(r.Id, s, e).Count == 0);
        return list.ToList();
    }

    public IReadOnlyList<RoomStatusInfo> GetNowStatus()
    {
        var now = time.UtcNow;
        var today = time.LocalToday;
        var window = calendar.GetOpenWindow(today);
        var localNow = TimeOnly.FromDateTime(time.ToLocal(now));
        var openNow = window is { } w && localNow >= w.Open && localNow < w.Close;
        DateTime? closeUtc = window is { } cw ? time.ToUtc(today.ToDateTime(cw.Close)) : null;

        var result = new List<RoomStatusInfo>();
        foreach (var room in GetAll())
        {
            var info = new RoomStatusInfo { Room = room, OpenNow = openNow };
            var slots = query.GetSlots(now.AddHours(-12), now.AddDays(1), room.Id);
            info.Current = slots.FirstOrDefault(s => s.StartUtc <= now && now < s.EndUtc);
            info.Next = slots.FirstOrDefault(s => s.StartUtc > now);
            if (info.Current is not null) info.CurrentOrganizer = users.GetById(info.Current.OrganizerId)?.DisplayName;
            info.IsFreeNow = room.Status == RoomStatus.Active && openNow && info.Current is null;
            if (info.IsFreeNow)
            {
                var until = closeUtc!.Value;
                if (info.Next is { } n && n.StartUtc < until) until = n.StartUtc;
                info.FreeUntilUtc = until;
            }
            result.Add(info);
        }
        return result;
    }
}
