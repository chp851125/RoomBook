namespace RoomBook.Application.Services;

public class BusinessCalendarService(
    IRepository<BusinessHours> hours,
    IRepository<Holiday> holidays) : IBusinessCalendarService
{
    public IReadOnlyList<BusinessHours> GetWeek()
        => hours.GetAll().OrderBy(h => ((int)h.Day + 6) % 7).ToList(); // 週一為首

    public IReadOnlyDictionary<DateOnly, string> GetHolidays()
        => holidays.GetAll().GroupBy(h => h.Date).ToDictionary(g => g.Key, g => g.First().Name);

    public string? GetHolidayName(DateOnly date)
        => holidays.Query(h => h.Date == date).FirstOrDefault()?.Name;

    public (TimeOnly Open, TimeOnly Close)? GetOpenWindow(DateOnly date)
    {
        if (GetHolidayName(date) is not null) return null;
        var h = hours.Query(x => x.Day == date.DayOfWeek).FirstOrDefault();
        return h is { IsOpen: true } ? (h.OpenTime, h.CloseTime) : null;
    }
}
