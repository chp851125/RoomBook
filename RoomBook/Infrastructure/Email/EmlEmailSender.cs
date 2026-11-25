using System.Text;

namespace RoomBook.Infrastructure.Email;

/// <summary>模擬寄信：把信件寫成 .eml 放進 outbox 資料夾（可用郵件軟體直接開啟），並在 EmailLog 留下紀錄。</summary>
public sealed class EmlEmailSender(
    IRepository<EmailLog> logs,
    ITimeProvider time,
    IHostEnvironment env,
    ILogger<EmlEmailSender> logger) : IEmailSender
{
    private const string FromAddress = "roombook@roombook.example";

    public void Send(User user, NotificationType type, string subject, string body)
    {
        var now = time.UtcNow;
        var fileName = $"{now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}.eml";
        var log = new EmailLog
        {
            UserId = user.Id, ToEmail = user.Email, Subject = subject, Type = type, SentAtUtc = now, FileName = fileName, Success = true
        };
        try
        {
            var dir = Path.Combine(env.ContentRootPath, "outbox");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, fileName), Compose(user, subject, body, now), new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            log.Success = false;
            log.Error = ex.Message;
            logger.LogWarning(ex, "寫入 outbox 失敗：{File}", fileName);
        }
        logs.Add(log);
    }

    private static string Compose(User to, string subject, string body, DateTime sentUtc)
    {
        static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));
        var wrapped = string.Join("\r\n", Enumerable.Range(0, (B64(body).Length + 75) / 76)
            .Select(i => B64(body).Substring(i * 76, Math.Min(76, B64(body).Length - i * 76))));
        return string.Join("\r\n",
            $"From: =?UTF-8?B?{B64("RoomBook 會議室預約")}?= <{FromAddress}>",
            $"To: =?UTF-8?B?{B64(to.DisplayName)}?= <{to.Email}>",
            $"Subject: =?UTF-8?B?{B64(subject)}?=",
            $"Date: {new DateTimeOffset(sentUtc, TimeSpan.Zero):r}",
            "MIME-Version: 1.0",
            "Content-Type: text/plain; charset=UTF-8",
            "Content-Transfer-Encoding: base64",
            "",
            wrapped,
            "");
    }
}
