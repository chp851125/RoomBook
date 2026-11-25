using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace RoomBook.Pages;

[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[IgnoreAntiforgeryToken]
public class ErrorModel : PageModel
{
    public string? RequestId { get; private set; }
    public int Code { get; private set; } = 500;
    public string Heading { get; private set; } = "發生了一點問題";
    public string Message { get; private set; } = "系統暫時無法完成你的要求，請稍後再試。若問題持續發生，請提供下方的追蹤代碼給管理員。";

    public void OnGet(int? code)
    {
        RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        Code = code ?? 500;
        (Heading, Message) = Code switch
        {
            404 => ("找不到這個頁面", "網址可能輸入錯誤，或這筆資料已被移除。"),
            403 => ("沒有權限", "你沒有權限執行這個操作。"),
            401 => ("請先登入", "你的登入已逾時，請重新登入。"),
            _ => (Heading, Message)
        };
        Response.StatusCode = Code;
    }
}
