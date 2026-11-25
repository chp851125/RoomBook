namespace RoomBook.Infrastructure.Background;

/// <summary>每分鐘掃描一次：未簽到的預約自動釋出並記違規；會議前 N 分鐘發送提醒。</summary>
public sealed class BookingMaintenanceWorker(IServiceScopeFactory scopes, ILogger<BookingMaintenanceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            do { RunOnce(); } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) { /* 正常關閉 */ }
    }

    private void RunOnce()
    {
        try
        {
            using var scope = scopes.CreateScope();
            var actions = scope.ServiceProvider.GetRequiredService<IBookingActionService>();
            var released = actions.ReleaseNoShows();
            var reminded = actions.SendReminders();
            if (released + reminded > 0)
                logger.LogInformation("排程掃描：釋出 {Released} 筆未簽到、提醒 {Reminded} 筆。", released, reminded);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "排程掃描失敗，下一分鐘重試。");
        }
    }
}
