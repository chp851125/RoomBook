using System.Text;
using Microsoft.AspNetCore.Mvc;
using RoomBook.Application.Dtos;

namespace RoomBook.Pages.MyBookings;

public class IndexModel(IMyBookingService mine, IBookingActionService actions, IIcsService ics, IRoomService rooms) : AppPageModel
{
    [BindProperty(SupportsGet = true)] public MyTab Tab { get; set; } = MyTab.Upcoming;
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? RoomId { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? To { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNo { get; set; } = 1;

    public PagedResult<MyBookingItem> Result { get; private set; } = new([], 1, 8, 0);
    public IReadOnlyDictionary<MyTab, int> Counts { get; private set; } = new Dictionary<MyTab, int>();
    public IReadOnlyList<Room> Rooms { get; private set; } = [];

    public void OnGet()
    {
        Rooms = rooms.GetAll();
        Counts = mine.CountByTab(CurrentUserId);
        Result = mine.Search(CurrentUserId, new MyBookingQuery
        {
            Tab = Tab, Keyword = Q, RoomId = RoomId, From = From, To = To, Page = PageNo
        });
    }

    public IActionResult OnPostCheckIn(Guid? id, Guid? seriesId, DateOnly? date, string? returnUrl)
        => Run(actions.CheckIn(new BookingRef(id, seriesId, date), CurrentUserId), "簽到成功，會議愉快！", returnUrl);

    public IActionResult OnPostExtend(Guid? id, Guid? seriesId, DateOnly? date, int minutes, string? returnUrl)
        => Run(actions.Extend(new BookingRef(id, seriesId, date), CurrentUserId, minutes), $"已延長 {minutes} 分鐘。", returnUrl);

    public IActionResult OnPostFinish(Guid? id, Guid? seriesId, DateOnly? date, string? returnUrl)
        => Run(actions.FinishEarly(new BookingRef(id, seriesId, date), CurrentUserId), "會議已結束，剩餘時段已釋出給其他人使用。", returnUrl);

    public IActionResult OnPostCancel(Guid? id, Guid? seriesId, DateOnly? date, string? reason, string? returnUrl)
        => Run(actions.Cancel(new BookingRef(id, seriesId, date), CurrentUserId, reason), "預約已取消。", returnUrl);

    /// <summary>匯出「我的全部預約」（我主辦或受邀的、未來 120 天）。</summary>
    public IActionResult OnGetIcsAll()
        => File(Encoding.UTF8.GetBytes(ics.ForUser(CurrentUserId)), "text/calendar; charset=utf-8", "roombook-my-bookings.ics");

    private IActionResult Run(OperationResult result, string successMessage, string? returnUrl)
    {
        if (result.Success) Success(successMessage); else Error(result.Error ?? "操作失敗。");
        return !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToPage();
    }
}
