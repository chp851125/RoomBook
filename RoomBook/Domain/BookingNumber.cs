namespace RoomBook.Domain;

public static class BookingNumber
{
    /// <summary>單號格式：RB-yyyyMMdd-0001（日期為台北建立日，序號每日從 1 起算）。</summary>
    public static string Format(DateOnly localDate, int sequence) => $"RB-{localDate:yyyyMMdd}-{sequence:0000}";
}
