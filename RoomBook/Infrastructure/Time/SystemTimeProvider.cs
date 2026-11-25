namespace RoomBook.Infrastructure.Time;

public sealed class SystemTimeProvider : ITimeProvider
{
    public SystemTimeProvider() => LocalZone = ResolveZone();

    public TimeZoneInfo LocalZone { get; }

    public DateTime UtcNow => DateTime.UtcNow;

    public DateOnly LocalToday => DateOnly.FromDateTime(ToLocal(UtcNow));

    public DateTime ToLocal(DateTime utc)
        => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), LocalZone);

    public DateTime ToUtc(DateTime local)
        => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), LocalZone);

    private static TimeZoneInfo ResolveZone()
    {
        foreach (var id in new[] { "Asia/Taipei", "Taipei Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("Asia/Taipei", TimeSpan.FromHours(8), "台北標準時間", "台北標準時間");
    }
}
