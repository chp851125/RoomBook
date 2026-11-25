using RoomBook.Application.Dtos;

namespace RoomBook.Application.Services;

public class BookingQueryService(
    IRepository<Booking> bookings,
    IRepository<BookingSeries> seriesRepo,
    IRepository<BookingException> exceptions,
    IRepository<Room> rooms,
    IUserService users,
    IBusinessCalendarService calendar,
    ITimeProvider time) : IBookingQueryService
{
    private static readonly string[] DayNames = ["日", "一", "二", "三", "四", "五", "六"];

    public IReadOnlyList<BookingSlot> GetSlots(DateTime fromUtc, DateTime toUtc, Guid? roomId = null, bool includeFinished = false)
    {
        var result = new List<BookingSlot>();
        foreach (var b in bookings.Query(b =>
                     (roomId is null || b.RoomId == roomId)
                     && (b.Status is BookingStatus.Confirmed or BookingStatus.Pending
                         || (includeFinished && b.Status == BookingStatus.Finished))
                     && b.StartUtc < toUtc && b.EndUtc > fromUtc))
            result.Add(ToSlot(b));

        foreach (var s in seriesRepo.Query(s =>
                     (roomId is null || s.RoomId == roomId)
                     && s.Status is BookingStatus.Confirmed or BookingStatus.Pending))
            result.AddRange(ExpandSeries(s).Where(x => x.StartUtc < toUtc && x.EndUtc > fromUtc));

        return result.OrderBy(x => x.StartUtc).ToList();
    }

    /// <summary>動態展開週期預約：排除假日、單次取消、已實體化為 Booking 的那次；單次改時間的以例外時間為準。</summary>
    public IReadOnlyList<BookingSlot> ExpandSeries(BookingSeries s)
    {
        var ex = exceptions.Query(x => x.SeriesId == s.Id).ToDictionary(x => x.OccurrenceDate);
        var materialized = bookings.Query(b => b.SeriesId == s.Id && b.OccurrenceDate != null)
            .Select(b => b.OccurrenceDate!.Value).ToHashSet();
        var holidays = calendar.GetHolidays();
        var people = s.ParticipantIds.Append(s.OrganizerId).Distinct().ToList();

        var list = new List<BookingSlot>();
        foreach (var date in RecurrenceExpander.Expand(s))
        {
            if (holidays.ContainsKey(date) || materialized.Contains(date)) continue;
            ex.TryGetValue(date, out var e);
            if (e is { IsCancelled: true }) continue;
            var start = e?.NewStartUtc ?? time.ToUtc(date.ToDateTime(s.StartTime));
            var end = e?.NewEndUtc ?? time.ToUtc(date.ToDateTime(s.EndTime));
            list.Add(new BookingSlot(null, s.Id, date, s.RoomId, start, end, s.Title, s.OrganizerId, s.Status, people));
        }
        return list;
    }

    public IReadOnlyList<BookingSlot> FindRoomConflicts(Guid roomId, DateTime startUtc, DateTime endUtc, Func<BookingSlot, bool>? exclude = null)
        => GetSlots(startUtc, endUtc, roomId)
            .Where(s => s.StartUtc < endUtc && startUtc < s.EndUtc) // 左閉右開：首尾相接不衝突
            .Where(s => exclude is null || !exclude(s))
            .ToList();

    public IReadOnlyList<BookingSlot> FindPersonConflicts(IReadOnlyCollection<Guid> personIds, DateTime startUtc, DateTime endUtc)
        => GetSlots(startUtc, endUtc)
            .Where(s => s.Status == BookingStatus.Confirmed
                        && s.StartUtc < endUtc && startUtc < s.EndUtc
                        && s.PersonIds.Any(personIds.Contains))
            .ToList();

    public BookingDetail? GetDetail(Guid? bookingId, Guid? seriesId, DateOnly? occurrenceDate)
    {
        if (bookingId is { } bid)
        {
            var b = bookings.GetById(bid);
            return b is null ? null : FromBooking(b);
        }
        if (seriesId is { } sid && occurrenceDate is { } date)
        {
            var s = seriesRepo.GetById(sid);
            if (s is null || !RecurrenceExpander.Expand(s).Contains(date)) return null;
            var materialized = bookings.Query(b => b.SeriesId == sid && b.OccurrenceDate == date).FirstOrDefault();
            return materialized is not null ? FromBooking(materialized) : FromSeries(s, date);
        }
        return null;
    }

    private BookingDetail FromBooking(Booking b)
    {
        var d = Common(b.RoomId, b.OrganizerId, b.ParticipantIds);
        d.BookingId = b.Id; d.SeriesId = b.SeriesId; d.OccurrenceDate = b.OccurrenceDate;
        d.BookingNo = b.BookingNo; d.Title = b.Title; d.Description = b.Description;
        d.StartUtc = b.StartUtc; d.EndUtc = b.EndUtc; d.Status = b.Status; d.AttendeeCount = b.AttendeeCount;
        d.Equipment = b.RequiredEquipment; d.Guests = b.ExternalGuests;
        d.History = b.History.OrderBy(h => h.AtUtc).ToList();
        d.ReviewNote = b.ReviewNote; d.CancelReason = b.CancelReason; d.CheckedInAtUtc = b.CheckedInAtUtc;
        if (b.SeriesId is { } sid && seriesRepo.GetById(sid) is { } s) d.RecurrenceText = DescribeRecurrence(s);
        return d;
    }

    private BookingDetail FromSeries(BookingSeries s, DateOnly date)
    {
        var d = Common(s.RoomId, s.OrganizerId, s.ParticipantIds);
        var ex = exceptions.Query(x => x.SeriesId == s.Id && x.OccurrenceDate == date).FirstOrDefault();
        d.SeriesId = s.Id; d.OccurrenceDate = date;
        d.BookingNo = "週期預約"; d.Title = s.Title; d.Description = s.Description;
        d.StartUtc = ex?.NewStartUtc ?? time.ToUtc(date.ToDateTime(s.StartTime));
        d.EndUtc = ex?.NewEndUtc ?? time.ToUtc(date.ToDateTime(s.EndTime));
        d.AttendeeCount = s.AttendeeCount; d.Equipment = s.RequiredEquipment; d.Guests = s.ExternalGuests;
        d.History = s.History.OrderBy(h => h.AtUtc).ToList();
        d.RecurrenceText = DescribeRecurrence(s);
        d.IsOccurrenceCancelled = ex is { IsCancelled: true };
        d.IsOccurrenceModified = ex is { IsCancelled: false };
        d.ExceptionReason = ex?.Reason;
        d.Status = d.IsOccurrenceCancelled ? BookingStatus.Cancelled : s.Status;
        return d;
    }

    private BookingDetail Common(Guid roomId, Guid organizerId, IEnumerable<Guid> participantIds)
    {
        var room = rooms.GetById(roomId);
        var d = new BookingDetail
        {
            RoomId = roomId, RoomName = room?.Name ?? "(已刪除的會議室)", RoomFloor = room?.Floor ?? 0,
            OrganizerId = organizerId, OrganizerName = users.GetById(organizerId)?.DisplayName ?? "(未知)"
        };
        foreach (var pid in participantIds)
        {
            var u = users.GetById(pid);
            if (u is null) continue;
            d.Participants.Add(new ParticipantView(u.Id, u.DisplayName, users.GetDepartment(u.DepartmentId)?.Name ?? ""));
        }
        return d;
    }

    public static string DescribeRecurrence(BookingSeries s)
    {
        var range = $"{s.StartDate:yyyy/MM/dd}～{s.EndDate:yyyy/MM/dd}";
        var days = string.Join("、", s.DaysOfWeek.Select(d => "週" + DayNames[(int)d]));
        return s.Recurrence switch
        {
            RecurrenceType.Weekly => $"每週 {days}（{range}）",
            RecurrenceType.BiWeekly => $"每兩週 {days}（{range}）",
            RecurrenceType.Monthly => $"每月 {s.StartDate.Day} 日（{range}）",
            _ => range
        };
    }

    private static BookingSlot ToSlot(Booking b)
        => new(b.Id, b.SeriesId, b.OccurrenceDate, b.RoomId, b.StartUtc, b.EndUtc, b.Title, b.OrganizerId, b.Status,
            b.ParticipantIds.Append(b.OrganizerId).Distinct().ToList());
}
