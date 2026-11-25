namespace RoomBook.Application.Services;

/// <summary>單號 RB-yyyyMMdd-0001：日期為台北建立日，序號取當日最大值加 1（全域鎖內進行以防重複）。</summary>
public class BookingNumberService(IRepository<Booking> bookings, ITimeProvider time)
{
    private static readonly object Gate = new();

    public string Next(DateTime nowUtc)
    {
        lock (Gate)
        {
            var date = DateOnly.FromDateTime(time.ToLocal(nowUtc));
            var prefix = BookingNumber.Format(date, 0)[..^4];
            var max = bookings.Query(b => b.BookingNo.StartsWith(prefix, StringComparison.Ordinal))
                .Select(b => int.TryParse(b.BookingNo[prefix.Length..], out var n) ? n : 0)
                .DefaultIfEmpty(0).Max();
            return BookingNumber.Format(date, max + 1);
        }
    }
}
