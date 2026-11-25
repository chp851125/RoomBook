using System.Text.Json;

namespace RoomBook.Infrastructure.Json;

/// <summary>資料目錄設定：執行資料 App_Data、範例資料 SeedData。</summary>
public sealed record DataPaths(string DataDirectory, string SeedDirectory)
{
    public static DataPaths Create(string contentRoot)
        => new(Path.Combine(contentRoot, "App_Data"), Path.Combine(contentRoot, "SeedData"));
}

public interface IReloadable
{
    void Reload();
}

/// <summary>追蹤所有已建立的 Repository，重置資料後統一重新載入。</summary>
public sealed class JsonRepositoryRegistry
{
    private readonly List<IReloadable> _items = new();
    private readonly object _gate = new();

    public void Register(IReloadable item) { lock (_gate) _items.Add(item); }

    public void ReloadAll()
    {
        IReloadable[] copy;
        lock (_gate) copy = _items.ToArray();
        foreach (var item in copy) item.Reload();
    }
}

/// <summary>
/// 啟動時載入記憶體；異動時「寫暫存檔再取代」寫回；以單一鎖保護，執行緒安全。
/// 對外一律回傳複本，避免呼叫端直接改到記憶體內的物件。
/// </summary>
public sealed class JsonFileRepository<T> : IRepository<T>, IReloadable where T : BaseEntity
{
    private readonly object _gate = new();
    private readonly string _path;
    private readonly ITimeProvider _time;
    private List<T> _items = new();

    public JsonFileRepository(DataPaths paths, ITimeProvider time, JsonRepositoryRegistry registry)
    {
        _time = time;
        Directory.CreateDirectory(paths.DataDirectory);
        _path = Path.Combine(paths.DataDirectory, EntityFileNames.For<T>());
        Load();
        registry.Register(this);
    }

    public IReadOnlyList<T> GetAll()
    {
        lock (_gate) return _items.Select(Clone).ToList();
    }

    public T? GetById(Guid id)
    {
        lock (_gate)
        {
            var found = _items.FirstOrDefault(x => x.Id == id);
            return found is null ? null : Clone(found);
        }
    }

    public IReadOnlyList<T> Query(Func<T, bool> predicate)
    {
        lock (_gate) return _items.Where(predicate).Select(Clone).ToList();
    }

    public T Add(T entity)
    {
        lock (_gate)
        {
            var now = _time.UtcNow;
            var stored = Clone(entity);
            if (stored.Id == Guid.Empty) stored.Id = Guid.NewGuid();
            if (_items.Any(x => x.Id == stored.Id))
                throw new InvalidOperationException($"{typeof(T).Name} Id 重複：{stored.Id}");
            stored.Version = 1;
            stored.CreatedAt = now;
            stored.UpdatedAt = now;
            _items.Add(stored);
            Persist();
            entity.Id = stored.Id;
            entity.Version = stored.Version;
            entity.CreatedAt = stored.CreatedAt;
            entity.UpdatedAt = stored.UpdatedAt;
            return Clone(stored);
        }
    }

    public T Update(T entity)
    {
        lock (_gate)
        {
            var index = _items.FindIndex(x => x.Id == entity.Id);
            if (index < 0) throw new KeyNotFoundException($"找不到 {typeof(T).Name}：{entity.Id}");
            var current = _items[index];
            if (current.Version != entity.Version) throw new ConcurrencyException(typeof(T).Name);

            var stored = Clone(entity);
            stored.Version = current.Version + 1;
            stored.CreatedAt = current.CreatedAt;
            stored.UpdatedAt = _time.UtcNow;
            _items[index] = stored;
            Persist();
            entity.Version = stored.Version;
            entity.UpdatedAt = stored.UpdatedAt;
            return Clone(stored);
        }
    }

    public void Delete(Guid id)
    {
        lock (_gate)
        {
            if (_items.RemoveAll(x => x.Id == id) > 0) Persist();
        }
    }

    public void Reload()
    {
        lock (_gate) Load();
    }

    private void Load()
    {
        if (!File.Exists(_path))
        {
            _items = new List<T>();
            return;
        }
        using var stream = File.OpenRead(_path);
        _items = JsonSerializer.Deserialize<List<T>>(stream, JsonDefaults.Options) ?? new List<T>();
        foreach (var item in _items) NormalizeUtc(item);
    }

    private void Persist()
    {
        var temp = _path + ".tmp";
        File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(_items, JsonDefaults.Options));
        File.Move(temp, _path, overwrite: true);
    }

    private static T Clone(T source)
        => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(source, JsonDefaults.Options), JsonDefaults.Options)!;

    private static void NormalizeUtc(T entity)
    {
        entity.CreatedAt = DateTime.SpecifyKind(entity.CreatedAt, DateTimeKind.Utc);
        entity.UpdatedAt = DateTime.SpecifyKind(entity.UpdatedAt, DateTimeKind.Utc);
    }
}
