using System;
using System.IO;
using System.Text.Json;

namespace XsheetMark.Settings;

/// <summary>
/// Persisted per-user preferences. Currently just window placement and the
/// two opacity sliders; future additions (selected tool, color, line width,
/// language override) plug into this same bag.
/// </summary>
public class UserSettings
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
    public double? WindowOpacity { get; set; }
    public double? ImageOpacity { get; set; }
    public bool? SuppressClickThroughWarning { get; set; }
}

/// <summary>
/// Reads and writes UserSettings as JSON under
/// %APPDATA%\xsheet-mark\settings.json. Any IO or deserialization error
/// is swallowed and a blank UserSettings is returned — persistence is a
/// convenience, not a correctness requirement.
/// </summary>
public static class SettingsStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "xsheet-mark",
        "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static UserSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new UserSettings();
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<UserSettings>(json) ?? new UserSettings();
        }
        catch
        {
            return new UserSettings();
        }
    }

    public static void Save(UserSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // Persistence is best-effort; silently ignore write failures.
        }
    }
}
