using Microsoft.AspNetCore.Mvc;
using RoomBook.Application.Dtos;

namespace RoomBook.Pages.Bookings;

public class DetailsModel(IBookingQueryService query, IBookingService bookings, IMyBookingService mine, IIcsService ics, ITimeProvider time) : AppPageModel
{
    public BookingDetail Detail { get; private set; } = null!;
    public bool CanManage { get; private set; }
    public bool CanChangeOccurrence { get; private set; }
    public MyBookingItem? Item { get; private set; }
    public string StartText => time.Format(Detail.StartUtc, "HH:mm");
    public string EndText => time.Format(Detail.EndUtc, "HH:mm");

    public IActionResult OnGet(Guid? id, Guid? seriesId, DateOnly? date)
        => Load(id, seriesId, date) ? Page() : NotFound();

    public IActionResult OnGetIcs(Guid? id, Guid? seriesId, DateOnly? date)
        => Load(id, seriesId, date)
            ? File(System.Text.Encoding.UTF8.GetBytes(ics.ForOccurrence(Detail)), "text/calendar; charset=utf-8", "roombook-meeting.ics")
            : NotFound();

    public IActionResult OnPostModifyOccurrence(Guid seriesId, DateOnly date, TimeOnly newStart, TimeOnly newEnd)
    {
        var result = bookings.ModifyOccurrence(seriesId, date, newStart, newEnd, CurrentUserId);
        if (result.Success) Success($"已修改 {date:MM/dd} 這一次的時間。");
        else Error(result.Error!);
        return RedirectToPage(new { seriesId, date = date.ToString("yyyy-MM-dd") });
    }

    private bool Load(Guid? id, Guid? seriesId, DateOnly? date)
    {
        var detail = query.GetDetail(id, seriesId, date);
        if (detail is null) return false;
        Detail = detail;
        Item = mine.FindActive(CurrentUserId, new BookingRef(detail.BookingId, detail.SeriesId, detail.OccurrenceDate));
        CanManage = IsAdmin || detail.OrganizerId == CurrentUserId;
        CanChangeOccurrence = CanManage && detail.IsSeries && detail.BookingId is null
                              && !detail.IsOccurrenceCancelled && detail.EndUtc > time.UtcNow
                              && detail.Status is BookingStatus.Confirmed or BookingStatus.Pending;
        return true;
    }
}
