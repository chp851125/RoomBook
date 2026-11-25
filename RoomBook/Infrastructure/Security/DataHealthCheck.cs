using Microsoft.Extensions.Diagnostics.HealthChecks;
using RoomBook.Infrastructure.Json;

namespace RoomBook.Infrastructure.Security;

/// <summary>確認 App_Data 存在且可寫入。</summary>
public sealed class DataHealthCheck(DataPaths paths) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!Directory.Exists(paths.DataDirectory))
                return Task.FromResult(HealthCheckResult.Unhealthy("App_Data 不存在"));
            var probe = Path.Combine(paths.DataDirectory, ".health");
            File.WriteAllText(probe, DateTime.UtcNow.ToString("O"));
            File.Delete(probe);
            return Task.FromResult(HealthCheckResult.Healthy("App_Data 可寫入"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("App_Data 無法寫入", ex));
        }
    }
}
