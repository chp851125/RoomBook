namespace RoomBook.Application.Services;

/// <summary>週期性預約規則的動態展開（不預先產生實體），最多展開 52 次。</summary>
public static class RecurrenceExpander
{
    public const int MaxOccurrences = 52;

    /// <summary>回傳規則展開後的所有發生日（台北日期），尚未排除假日與單次例外。</summary>
    public static IReadOnlyList<DateOnly> Expand(BookingSeries series)
    {
        var result = new List<DateOnly>();
        if (series.Recurrence == RecurrenceType.None)
        {
            result.Add(series.StartDate);
            return result;
        }

        var weekStart = series.StartDate.AddDays(-(int)series.StartDate.DayOfWeek);
        var days = series.DaysOfWeek.Count > 0 ? series.DaysOfWeek : new List<DayOfWeek> { series.StartDate.DayOfWeek };

        for (var d = series.StartDate; d <= series.EndDate && result.Count < MaxOccurrences; d = d.AddDays(1))
        {
            var match = series.Recurrence switch
            {
                RecurrenceType.Weekly => days.Contains(d.DayOfWeek),
                RecurrenceType.BiWeekly => days.Contains(d.DayOfWeek)
                                           && ((d.DayNumber - weekStart.DayNumber) / 7) % 2 == 0,
                RecurrenceType.Monthly => d.Day == series.StartDate.Day,
                _ => false
            };
            if (match) result.Add(d);
        }
        return result;
    }
}
