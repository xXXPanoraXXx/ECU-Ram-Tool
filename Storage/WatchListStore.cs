using System.Text.Json;
using EcuRamTool.Ram;

namespace EcuRamTool.Storage;

public static class WatchListStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static void Save(string path, IEnumerable<WatchItem> items)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllText(path, JsonSerializer.Serialize(items, Options));
    }

    public static List<WatchItem> Load(string path)
    {
        if (!File.Exists(path))
            return new List<WatchItem>();
        return JsonSerializer.Deserialize<List<WatchItem>>(File.ReadAllText(path), Options) ?? new List<WatchItem>();
    }
}
