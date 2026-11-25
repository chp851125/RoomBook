using System.Collections.Concurrent;

namespace RoomBook.Application.Services;

/// <summary>每間會議室一把鎖：衝突檢查與寫入必須在同一把鎖內完成，避免併發重複預約。</summary>
public sealed class RoomLockProvider
{
    private readonly ConcurrentDictionary<Guid, object> _locks = new();

    public object For(Guid roomId) => _locks.GetOrAdd(roomId, _ => new object());
}
