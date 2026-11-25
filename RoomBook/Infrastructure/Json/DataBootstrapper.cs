using RoomBook.Infrastructure.Seed;

namespace RoomBook.Infrastructure.Json;

/// <summary>啟動時確保 App_Data 有資料：缺少的檔案從 SeedData 複製（Development 且範例資料過期時先重新產生）。</summary>
public sealed class DataBootstrapper(
    DataPaths paths,
    SeedDataGenerator generator,
    IHostEnvironment env,
    ILogger<DataBootstrapper> logger)
{
    public void EnsureData()
    {
        Directory.CreateDirectory(paths.DataDirectory);
        var missing = EntityFileNames.All.Where(f => !File.Exists(Path.Combine(paths.DataDirectory, f))).ToList();
        if (missing.Count == 0) return;

        if (env.IsDevelopment() && (generator.IsStale(paths.SeedDirectory) || !SeedComplete()))
        {
            var result = generator.Generate(paths.SeedDirectory);
            if (!result.Success)
                throw new InvalidOperationException("範例資料自我驗證失敗：" + string.Join("；", result.Errors));
            logger.LogInformation("已重新產生範例資料（SeedData）。{Notes}", string.Join(" ", result.Notes));
        }

        foreach (var file in missing)
        {
            var src = Path.Combine(paths.SeedDirectory, file);
            var dst = Path.Combine(paths.DataDirectory, file);
            if (File.Exists(src)) File.Copy(src, dst, overwrite: true);
            else File.WriteAllText(dst, "[]");
        }
        logger.LogInformation("已從 SeedData 複製 {Count} 個檔案到 App_Data。", missing.Count);
    }

    private bool SeedComplete()
        => EntityFileNames.All.All(f => File.Exists(Path.Combine(paths.SeedDirectory, f)));
}
