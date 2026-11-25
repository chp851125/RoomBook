using RoomBook.Application.Dtos;

namespace RoomBook.Application.Services;

public class StatsService(
    IRepository<Booking> bookings,
    IRepository<Room> rooms,
    IBookingQueryService query,
    IBusinessCalendarService calendar,
    IUserService users,
    ITimeProvider time) : IStatsService
{
    public StatsData Get(DateOnly from, DateOnly to)
    {
        if (to < from) (from, to) = (to, from);
        var fromUtc = time.ToUtc(from.ToDateTime(TimeOnly.MinValue));
        var toUtc = time.ToUtc(to.AddDays(1).ToDateTime(TimeOnly.MinValue));
        var now = time.UtcNow;

        // 有實際使用的時段：已確認與已結束（待審核、取消、駁回、釋出都不算）
        var slots = query.GetSlots(fromUtc, toUtc, null, includeFinished: true)
            .Where(s => s.Status is BookingStatus.Confirmed or BookingStatus.Finished).ToList();

        double Hours(BookingSlot s)
        {
            var a = s.StartUtc < fromUtc ? fromUtc : s.StartUtc;
            var b = s.EndUtc > toUtc ? toUtc : s.EndUtc;
            return Math.Max(0, (b - a).TotalHours);
        }

        // 可用時數 = 範圍內每個營業日的營業時間（每間會議室相同）
        double available = 0;
        for (var d = from; d <= to; d = d.AddDays(1))
            if (calendar.GetOpenWindow(d) is { } w) available += (w.Close - w.Open).TotalHours;

        var allRooms = rooms.GetAll().OrderBy(r => r.SortOrder).ToList();
        var roomUsage = allRooms.Select(r =>
        {
            var h = slots.Where(s => s.RoomId == r.Id).Sum(Hours);
            return new RoomUsage(r.Name, Math.Round(h, 1), available <= 0 ? 0 : Math.Round(h * 100 / available, 1));
        }).ToList();

        var deptName = users.GetDepartments().ToDictionary(d => d.Id, d => d.Name);
        var deptHours = slots.GroupBy(s => deptName.GetValueOrDefault(users.GetById(s.OrganizerId)?.DepartmentId ?? Guid.Empty, "未分類"))
            .Select(g => new DeptHours(g.Key, Math.Round(g.Sum(Hours), 1))).OrderByDescending(x => x.Hours).ToList();

        // 熱門時段：星期（週一=0）× 小時
        var week = calendar.GetWeek().Where(h => h.IsOpen).ToList();
        var minHour = week.Count > 0 ? week.Min(h => h.OpenTime.Hour) : 8;
        var maxHour = week.Count > 0 ? week.Max(h => h.CloseTime.Hour - (h.CloseTime.Minute == 0 ? 1 : 0)) : 18;
        var heat = new Dictionary<(int, int), double>();
        foreach (var s in slots)
        {
            var cursor = s.StartUtc < fromUtc ? fromUtc : s.StartUtc;
            var end = s.EndUtc > toUtc ? toUtc : s.EndUtc;
            while (cursor < end)
            {
                var local = time.ToLocal(cursor);
                var nextHourLocal = new DateTime(local.Year, local.Month, local.Day, local.Hour, 0, 0).AddHours(1);
                var next = time.ToUtc(nextHourLocal);
                if (next > end) next = end;
                var key = (((int)local.DayOfWeek + 6) % 7, local.Hour);
                heat[key] = heat.GetValueOrDefault(key) + (next - cursor).TotalHours;
                cursor = next;
            }
        }

        // 簽到率：只看已過開始時間的實體預約
        var past = bookings.Query(b => b.StartUtc >= fromUtc && b.StartUtc < toUtc && b.StartUtc < now
                                       && b.Status is BookingStatus.Confirmed or BookingStatus.Finished or BookingStatus.Released);
        var checkedIn = past.Count(b => b.CheckedInAtUtc is not null);
        var noShow = past.Count(b => b.Status == BookingStatus.Released);
        var checkIn = new CheckInStats(checkedIn, noShow, past.Count - checkedIn - noShow);

        return new StatsData
        {
            From = from.ToString("yyyy-MM-dd"), To = to.ToString("yyyy-MM-dd"),
            BookingCount = slots.Count,
            TotalHours = Math.Round(slots.Sum(Hours), 1),
            OverallRate = available <= 0 || allRooms.Count == 0 ? 0 : Math.Round(slots.Sum(Hours) * 100 / (available * allRooms.Count), 1),
            Rooms = roomUsage, Departments = deptHours,
            Heat = heat.Select(kv => new HeatCell(kv.Key.Item1, kv.Key.Item2, Math.Round(kv.Value, 1))).ToList(),
            HeatHours = Enumerable.Range(minHour, Math.Max(1, maxHour - minHour + 1)).ToList(),
            CheckIn = checkIn,
            Trend = BuildTrend()
        };
    }

    /// <summary>近 6 個月（含本月）每月使用時數、預約場次與未簽到釋出場次。</summary>
    private TrendData BuildTrend()
    {
        var today = time.LocalToday;
        var months = new List<string>(); var hours = new List<double>(); var counts = new List<int>(); var noShows = new List<int>();
        for (var i = 5; i >= 0; i--)
        {
            var first = new DateOnly(today.Year, today.Month, 1).AddMonths(-i);
            var startUtc = time.ToUtc(first.ToDateTime(TimeOnly.MinValue));
            var endUtc = time.ToUtc(first.AddMonths(1).ToDateTime(TimeOnly.MinValue));
            var slots = query.GetSlots(startUtc, endUtc, null, includeFinished: true)
                .Where(s => s.Status is BookingStatus.Confirmed or BookingStatus.Finished
                            && s.StartUtc >= startUtc && s.StartUtc < endUtc).ToList();
            months.Add(first.ToString("yyyy/MM"));
            hours.Add(Math.Round(slots.Sum(s => (s.EndUtc - s.StartUtc).TotalHours), 1));
            counts.Add(slots.Count);
            noShows.Add(bookings.Query(b => b.Status == BookingStatus.Released && b.StartUtc >= startUtc && b.StartUtc < endUtc).Count);
        }
        return new TrendData(months, hours, counts, noShows);
    }
}
