namespace RoomBook.Domain;

/// <summary>更新時實體版本與儲存版本不符（資料已被他人修改）。</summary>
public class ConcurrencyException(string entityName)
    : Exception($"{entityName} 已被其他人修改，請重新載入後再試。")
{
    public string EntityName { get; } = entityName;
}
