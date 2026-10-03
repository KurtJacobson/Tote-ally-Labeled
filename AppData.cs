using System.Text.Json;

namespace ToteLabels;

// TopOffsetMm moves everything down the label (or up, if negative) to correct
// a printer that starts printing slightly off the top edge.
public record AppSettings(string PrinterIp, int Dpi, double TopOffsetMm = 0);

// Settings and the logo live in the folder given, %LOCALAPPDATA%\ToteLabels.
public class AppData
{
    static readonly AppSettings Defaults = new("", 203);
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    readonly string settingsPath;

    public AppData(string folder)
    {
        Directory.CreateDirectory(folder);
        settingsPath = Path.Combine(folder, "settings.json");
        LogoPath = Path.Combine(folder, "logo.png");
    }

    public string LogoPath { get; }

    public bool HasLogo => File.Exists(LogoPath);

    public AppSettings LoadSettings() => File.Exists(settingsPath)
        ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(settingsPath)) ?? Defaults
        : Defaults;

    public void SaveSettings(AppSettings settings) =>
        File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings, JsonOptions));
}
