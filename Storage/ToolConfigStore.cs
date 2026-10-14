using System.Text.Json;
using EcuRamTool.Ram;

namespace EcuRamTool.Storage;

public sealed class ToolConfig
{
    public int Version { get; set; } = 3;

    public bool StopReading { get; set; }
    public bool StopForceWrite { get; set; }

    // Optionaler Pfad zur zuletzt für diese Config verwendeten .ecu-Datei.
    // Dadurch muss sie nach Load/Restart nicht erneut ausgewählt werden.
    public string EcuFilePath { get; set; } = string.Empty;

    public List<WatchItem> WatchItems { get; set; } = new();
}

public static class ToolConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static void Save(string path, IEnumerable<WatchItem> watchItems, string? ecuFilePath = null, bool stopReading = false, bool stopForceWrite = false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

        var config = new ToolConfig
        {
            Version = 3,
            StopReading = stopReading,
            StopForceWrite = stopForceWrite,
            EcuFilePath = ecuFilePath ?? string.Empty,
            WatchItems = watchItems.Select(CloneWatchItem).ToList()
        };

        File.WriteAllText(path, JsonSerializer.Serialize(config, Options));
    }

    public static ToolConfig Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Config-Datei wurde nicht gefunden.", path);

        string json = File.ReadAllText(path);

        // Neues Format: { version, ecuFilePath, watchItems }
        try
        {
            ToolConfig? config = JsonSerializer.Deserialize<ToolConfig>(json, Options);
            if (config is not null && config.WatchItems is not null)
            {
                config.EcuFilePath ??= string.Empty;
                return config;
            }
        }
        catch (JsonException)
        {
            // Danach Fallback auf alte reine Watchlist-Dateien versuchen.
        }

        // Fallback für alte reine Watchlist-Dateien: [ { ... }, { ... } ]
        List<WatchItem>? oldWatchList = JsonSerializer.Deserialize<List<WatchItem>>(json, Options);
        return new ToolConfig
        {
            Version = 1,
            EcuFilePath = string.Empty,
            WatchItems = oldWatchList ?? new List<WatchItem>()
        };
    }

    private static WatchItem CloneWatchItem(WatchItem item)
    {
        return new WatchItem
        {
            Address24 = item.Address24,
            ValueType = item.ValueType,
            Bit = item.Bit,
            MaskValue = item.MaskValue,
            Name = item.Name,
            Unit = item.Unit ?? string.Empty,
            LastRawValue = item.LastRawValue,
            Factor = item.Factor,
            ReadMethod = item.ReadMethod,
            WriteMethod = item.WriteMethod
        };
    }
}
