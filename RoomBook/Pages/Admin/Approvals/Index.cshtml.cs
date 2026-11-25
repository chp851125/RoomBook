using Microsoft.AspNetCore.Mvc;
using RoomBook.Application.Dtos;

namespace RoomBook.Pages.Admin.Approvals;

public class IndexModel(IAdminBookingService adminBookings) : AdminPageModel
{
    public IReadOnlyList<ApprovalRow> Rows { get; private set; } = [];

    public void OnGet() => Rows = adminBookings.GetPending();

    public IActionResult OnPostApprove(Guid? id, Guid? seriesId)
    {
        var r = adminBookings.Approve(new BookingRef(id, seriesId, null), CurrentUserId);
        if (r.Success) Success("已核准，申請人會收到通知。"); else Error(r.Error!);
        return RedirectToPage();
    }

    public IActionResult OnPostReject(Guid? id, Guid? seriesId, string? reason)
    {
        var r = adminBookings.Reject(new BookingRef(id, seriesId, null), CurrentUserId, reason ?? "");
        if (r.Success) Success("已駁回，時段已釋出，申請人會收到通知。"); else Error(r.Error!);
        return RedirectToPage();
    }
}
