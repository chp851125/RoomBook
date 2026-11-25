using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoomBook.Infrastructure.Json;

public static class JsonDefaults
{
    /// <summary>JSON 檔案格式：camelCase、縮排、列舉用字串、中文不跳脫（方便人工修改）。</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };
}

/// <summary>實體型別與 JSON 檔名（實體名複數、camelCase 起頭）。</summary>
public static class EntityFileNames
{
    private static readonly Dictionary<Type, string> Map = new()
    {
        [typeof(User)] = "users.json",
        [typeof(Department)] = "departments.json",
        [typeof(Room)] = "rooms.json",
        [typeof(Booking)] = "bookings.json",
        [typeof(BookingSeries)] = "bookingSeries.json",
        [typeof(BookingException)] = "bookingExceptions.json",
        [typeof(Notification)] = "notifications.json",
        [typeof(EmailLog)] = "emailLogs.json",
        [typeof(Violation)] = "violations.json",
        [typeof(BusinessHours)] = "businessHours.json",
        [typeof(Holiday)] = "holidays.json",
        [typeof(AuditLog)] = "auditLogs.json",
        [typeof(AppSetting)] = "appSettings.json"
    };

    public static IReadOnlyCollection<string> All => Map.Values;

    public static string For<T>() => Map[typeof(T)];
}
