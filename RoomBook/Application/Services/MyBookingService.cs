using RoomBook.Application.Dtos;

namespace RoomBook.Application.Services;

public class MyBookingService(
    IRepository<Booking> bookings,
    IRepository<BookingSeries> seriesRepo,
    IBookingQueryService query,
    IRoomService rooms,
    IUserService users,
    ISettingService settings,
    ITimeProvider time) : IMyBookingService
{
    public MyBookingItem? FindActive(Guid userId, BookingRef target)
        => Collect(userId, MyTab.Now).Concat(Collect(userId, MyTab.Upcoming))
            .FirstOrDefault(i => i.BookingId == target.BookingId && i.SeriesId == target.SeriesId && i.OccurrenceDate == target.Date);

    public IReadOnlyDictionary<MyTab, int> CountByTab(Guid userId)
        => Enum.GetValues<MyTab>().ToDictionary(t => t, t => Collect(userId, t).Count);

    public PagedResult<MyBookingItem> Search(Guid userId, MyBookingQuery q)
    {
        var items = Collect(userId, q.Tab).AsEnumerable();

        if (!string.IsNullOrWhiteSpace(q.Keyword))
        {
            var k = q.Keyword.Trim();
            items = items.Where(i => i.Title.Contains(k, StringComparison.OrdinalIgnoreCase)
                                     || i.RoomName.Contains(k, StringComparison.OrdinalIgnoreCase)
                                     || i.OrganizerName.Contains(k, StringComparison.OrdinalIgnoreCase)
                                     || i.BookingNo.Contains(k, StringComparison.OrdinalIgnoreCase));
        }
        if (q.RoomId is { } rid) items = items.Where(i => i.RoomId == rid);
        if (q.From is { } from) items = items.Where(i => DateOnly.FromDateTime(time.ToLocal(i.StartUtc)) >= from);
        if (q.To is { } to) items = items.Where(i => DateOnly.FromDateTime(time.ToLocal(i.StartUtc)) <= to);

        var list = q.Tab is MyTab.History or MyTab.Cancelled
            ? items.OrderByDescending(i => i.StartUtc).ToList()
            : items.OrderBy(i => i.StartUtc).ToList();

        var page = Math.Max(1, q.Page);
        return new PagedResult<MyBookingItem>(list.Skip((page - 1) * q.PageSize).Take(q.PageSize).ToList(), page, q.PageSize, list.Count);
    }

    private List<MyBookingItem> Collect(Guid userId, MyTab tab)
    {
        var now = time.UtcNow;
        var roomNames = rooms.GetAll().ToDictionary(r => r.Id, r => r.Name);
        string Room(Guid id) => roomNames.GetValueOrDefault(id, "");
        string Person(Guid id) => users.GetById(id)?.DisplayName ?? "";
        var me = users.GetById(userId);
        var isAdmin = me?.Role == UserRole.Admin;
        var cutoff = settings.GetInt(SettingKeys.CancelCutoffMinutes, 30);
        var early = settings.GetInt(SettingKeys.CheckInEarlyMinutes, 10);
        var late = settings.GetInt(SettingKeys.CheckInLateMinutes, 15);

        MyBookingItem Make(Guid? bookingId, Guid? seriesId, DateOnly? date, string no, string title, Guid roomId, Guid organizer,
            DateTime start, DateTime end, BookingStatus status, DateTime? checkedIn)
        {
            var mine = organizer == userId;
            var own = mine || isAdmin;
            var active = status is BookingStatus.Confirmed or BookingStatus.Pending;
            var inProgress = status == BookingStatus.Confirmed && start <= now && now < end;
            return new MyBookingItem
            {
                BookingId = bookingId, SeriesId = seriesId, OccurrenceDate = date, BookingNo = no, Title = title,
                RoomId = roomId, RoomName = Room(roomId), OrganizerId = organizer, OrganizerName = Person(organizer),
                StartUtc = start, EndUtc = end, Status = status, IsSeries = seriesId is not null,
                CheckedIn = checkedIn is not null, InProgress = inProgress,
                CanCheckIn = own && status == BookingStatus.Confirmed && checkedIn is null
                             && now >= start.AddMinutes(-early) && now <= start.AddMinutes(late),
                CanExtend = own && inProgress,
                CanFinish = own && inProgress,
                CanCancel = own && active && end > now && (isAdmin || start - now >= TimeSpan.FromMinutes(cutoff)),
                CanEdit = own && active && start > now && bookingId is not null
            };
        }

        // 與我有關的實體預約
        var mineBookings = bookings.Query(b => b.OrganizerId == userId || b.ParticipantIds.Contains(userId));
        var result = new List<MyBookingItem>();

        MyBookingItem FromBooking(Booking b) => Make(b.Id, b.SeriesId, b.OccurrenceDate, b.BookingNo, b.Title, b.RoomId,
            b.OrganizerId, b.StartUtc, b.EndUtc, b.Status, b.CheckedInAtUtc);

        // 週期預約尚未實體化的場次（未來 120 天內）
        IEnumerable<MyBookingItem> SeriesItems(Func<BookingSeries, bool> pick)
            => seriesRepo.Query(s => s.Status is BookingStatus.Confirmed or BookingStatus.Pending && pick(s))
                .SelectMany(query.ExpandSeries)
                .Where(s => s.EndUtc > now && s.StartUtc < now.AddDays(120))
                .Select(s => Make(null, s.SeriesId, s.OccurrenceDate, "週期預約", s.Title, s.RoomId, s.OrganizerId, s.StartUtc, s.EndUtc, s.Status, null));

        switch (tab)
        {
            case MyTab.Upcoming:
                result.AddRange(mineBookings.Where(b => b.OrganizerId == userId && b.Status is BookingStatus.Confirmed or BookingStatus.Pending && b.StartUtc > now).Select(FromBooking));
                result.AddRange(SeriesItems(s => s.OrganizerId == userId).Where(i => i.StartUtc > now));
                break;
            case MyTab.Now:
                result.AddRange(mineBookings.Where(b => b.OrganizerId == userId && b.Status == BookingStatus.Confirmed && b.StartUtc <= now && now < b.EndUtc).Select(FromBooking));
                result.AddRange(SeriesItems(s => s.OrganizerId == userId).Where(i => i.StartUtc <= now));
                break;
            case MyTab.History:
                result.AddRange(mineBookings.Where(b => b.OrganizerId == userId
                    && (b.Status is BookingStatus.Finished or BookingStatus.Released || (b.Status == BookingStatus.Confirmed && b.EndUtc <= now))).Select(FromBooking));
                break;
            case MyTab.Cancelled:
                result.AddRange(mineBookings.Where(b => b.OrganizerId == userId && b.Status is BookingStatus.Cancelled or BookingStatus.Rejected).Select(FromBooking));
                break;
            case MyTab.Invited:
                result.AddRange(mineBookings.Where(b => b.OrganizerId != userId && b.Status is BookingStatus.Confirmed or BookingStatus.Pending && b.EndUtc > now).Select(FromBooking));
                result.AddRange(SeriesItems(s => s.OrganizerId != userId && s.ParticipantIds.Contains(userId)));
                break;
        }
        return result;
    }
}
