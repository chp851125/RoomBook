namespace RoomBook.Application.Services;

public class SettingService(IRepository<AppSetting> settings) : ISettingService
{
    public string GetString(string key, string defaultValue = "")
        => settings.Query(s => s.Key == key).FirstOrDefault()?.Value ?? defaultValue;

    public int GetInt(string key, int defaultValue = 0)
        => int.TryParse(GetString(key), out var v) ? v : defaultValue;

    public bool GetBool(string key, bool defaultValue = false)
        => bool.TryParse(GetString(key), out var v) ? v : defaultValue;
}
