using System.Text.Json;

namespace EcuRamTool.Storage;

public sealed class RecentConfigState
{
    public string LastConfigPath { get; set; } = string.Empty;
}

public static class RecentConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static string StatePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EcuRamTool",
        "recent_config.json");

    public static string? LoadLastConfigPath()
    {
        try
        {
            if (!File.Exists(StatePath))
                return null;

            RecentConfigState? state = JsonSerializer.Deserialize<RecentConfigState>(File.ReadAllText(StatePath), Options);
            string? path = state?.LastConfigPath;
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }
        catch
        {
            return null;
        }
    }

    public static void SaveLastConfigPath(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath) ?? ".");
            var state = new RecentConfigState { LastConfigPath = path };
            File.WriteAllText(StatePath, JsonSerializer.Serialize(state, Options));
        }
        catch
        {
            // Nicht kritisch. Das Tool darf deshalb nicht scheitern.
        }
    }
}
