using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace League_Data_Recorder;

internal static class PreferenceFiles
{
    internal static string PathFor(string filename) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "League Data Recorder", filename);
    internal static T Load<T>(string filename) where T : new() => File.Exists(PathFor(filename))
        ? JsonSerializer.Deserialize<T>(File.ReadAllText(PathFor(filename))) ?? new T() : new T();
    internal static void Save<T>(string filename, T value)
    {
        string path = PathFor(filename);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

internal sealed class AppPreferences
{
    public string RecordingsFolder { get; set; } = "";
    public string AllyTeam { get; set; } = "Allies";
    public string EnemyTeam { get; set; } = "Enemies";
    public string GameType { get; set; } = "Scrim";
    public bool RecordVideo { get; set; } = true;
    public bool RecordAudio { get; set; } = true;
    public bool SaveReplay { get; set; } = true;
    public bool SaveRawData { get; set; } = true;
    public decimal PollingIntervalMs { get; set; } = 1000;
    public decimal GameExpirySeconds { get; set; } = 300;
}

internal sealed class VideoPlayerPreferences
{
    public double Rate { get; set; } = 1;
    public double Volume { get; set; } = .8;
    public bool Muted { get; set; }
    public bool Captions { get; set; } = true;
    public int PreviewCommsVersion { get; set; }
    public string[] Timelines { get; set; } = ["fights", "calls", "objectives", "gold", "outlook"];
    internal void Validate()
    {
        // Restore preview bubbles once for users coming from the overlay removal
        // build. Subsequent explicit Comms toggles continue to persist normally.
        if (PreviewCommsVersion < 1) Captions = true;
        PreviewCommsVersion = 1;
        if (!new[] { .5, .75, 1, 1.25, 1.5, 2 }.Contains(Rate)) Rate = 1;
        Volume = double.IsFinite(Volume) ? Math.Clamp(Volume, 0, 1) : .8;
        Timelines = (Timelines ?? ["fights", "calls", "objectives", "gold", "outlook"])
            .Select(t => t == "valence" ? "outlook" : t)
            .Where(t => new[] { "fights", "calls", "objectives", "gold", "outlook" }.Contains(t)).Distinct().ToArray();
    }
}

internal static class GameTypes
{
    internal const string Unspecified = "Unspecified";
    internal static readonly string[] Suggestions = ["Scrim", "Tournament", "Ranked", "Normal", "Practice", "Other", Unspecified];
    internal static string Normalize(string? value)
    {
        string clean = RecordingTeamNames.Clean(value ?? "");
        return Suggestions.FirstOrDefault(t => t.Equals(clean, StringComparison.OrdinalIgnoreCase))
            ?? (clean.Length == 0 ? Unspecified : clean);
    }
}
