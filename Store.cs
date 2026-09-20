using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IptvRecorder;

public static class Store
{
    public static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IptvRecorder");

    private static readonly string SettingsFile = Path.Combine(Dir, "settings.json");
    private static readonly string RecordingsFile = Path.Combine(Dir, "recordings.json");
    public static readonly string ChannelsCache = Path.Combine(Dir, "channels.m3u");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new RecordingStatusJsonConverter() }
    };

    public static AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsFile))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFile), Options) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public static void SaveSettings(AppSettings s)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(SettingsFile, JsonSerializer.Serialize(s, Options));
    }

    public static List<Recording> LoadRecordings()
    {
        try
        {
            if (File.Exists(RecordingsFile))
                return JsonSerializer.Deserialize<List<Recording>>(File.ReadAllText(RecordingsFile), Options) ?? new();
        }
        catch { }
        return new();
    }

    public static void SaveRecordings(IEnumerable<Recording> list)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(RecordingsFile, JsonSerializer.Serialize(list.ToList(), Options));
    }

    private static readonly string FavoritesFile = Path.Combine(Dir, "favorites.json");

    public static HashSet<string> LoadFavorites()
    {
        try
        {
            if (File.Exists(FavoritesFile))
            {
                var list = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(FavoritesFile), Options);
                if (list != null) return new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch { }
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public static void SaveFavorites(IEnumerable<string> keys)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FavoritesFile, JsonSerializer.Serialize(keys.ToList(), Options));
        }
        catch { }
    }

    public static string? LoadChannelsCache()
    {
        try { return File.Exists(ChannelsCache) ? File.ReadAllText(ChannelsCache) : null; }
        catch { return null; }
    }

    public static void SaveChannelsCache(string content)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(ChannelsCache, content);
    }
}
