using System.Text.Json;

namespace EcuRamTool.Profiles;

public static class ProfileLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static IReadOnlyList<string> FindProfileFiles(string profileDirectory)
    {
        Directory.CreateDirectory(profileDirectory);
        return Directory.GetFiles(profileDirectory, "*.json").OrderBy(x => x).ToArray();
    }

    public static EcuProfile Load(string path)
    {
        string json = File.ReadAllText(path);
        EcuProfile? profile = JsonSerializer.Deserialize<EcuProfile>(json, JsonOptions);
        if (profile is null)
            throw new InvalidOperationException("Profile konnte nicht geladen werden: " + path);
        Validate(profile);
        return profile;
    }

    public static void Save(string path, EcuProfile profile)
    {
        Validate(profile);
        string json = JsonSerializer.Serialize(profile, JsonOptions);
        File.WriteAllText(path, json);
    }

    private static void Validate(EcuProfile profile)
    {
        if (profile.TargetAddresses.Count == 0)
            throw new InvalidOperationException("Profile braucht mindestens eine TargetAddress.");
        if (profile.MemoryRanges.Count == 0)
            throw new InvalidOperationException("Profile braucht mindestens eine MemoryRange.");
        if (profile.Timing.KeepAliveIntervalMs < 250)
            throw new InvalidOperationException("KeepAliveIntervalMs ist unrealistisch klein.");
        if (profile.Timing.DefaultResponseTimeoutMs < 100)
            throw new InvalidOperationException("DefaultResponseTimeoutMs ist unrealistisch klein.");
        if (profile.Timing.StartCommunicationTimeoutMs < 100)
            throw new InvalidOperationException("StartCommunicationTimeoutMs ist unrealistisch klein.");
        if (profile.Timing.InterByteIdleMs < 1)
            throw new InvalidOperationException("InterByteIdleMs ist unrealistisch klein.");
        if (profile.Timing.SerialPollSleepMs < 0)
            throw new InvalidOperationException("SerialPollSleepMs darf nicht negativ sein.");
    }
}
