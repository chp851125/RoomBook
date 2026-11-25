using RoomBook.Application.Dtos;
using RoomBook.Infrastructure.Seed;

namespace RoomBook.Infrastructure.Json;

/// <summary>清空 App_Data 並從 SeedData 重新複製，然後通知所有 Repository 重新載入。</summary>
public sealed class DataResetService(
    DataPaths paths,
    SeedDataGenerator generator,
    JsonRepositoryRegistry registry,
    IHostEnvironment env,
    ILogger<DataResetService> logger) : IDataResetService
{
    private static readonly object Gate = new();

    public ResetResult Reset()
    {
        lock (Gate)
        {
            // Development：重新產生範例資料，讓「今天進行中」等相對日期回到正確位置
            if (env.IsDevelopment())
            {
                var gen = generator.Generate(paths.SeedDirectory);
                if (!gen.Success)
                    return new ResetResult(false, "範例資料自我驗證失敗：" + string.Join("；", gen.Errors));
            }

            Directory.CreateDirectory(paths.DataDirectory);
            foreach (var file in Directory.GetFiles(paths.DataDirectory))
                File.Delete(file);

            foreach (var name in EntityFileNames.All)
            {
                var src = Path.Combine(paths.SeedDirectory, name);
                var dst = Path.Combine(paths.DataDirectory, name);
                if (File.Exists(src)) File.Copy(src, dst, overwrite: true);
                else File.WriteAllText(dst, "[]");
            }

            registry.ReloadAll();
            logger.LogWarning("資料已重置為範例資料。");
            return new ResetResult(true, "已重置為範例資料。");
        }
    }
}
