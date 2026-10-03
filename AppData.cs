using System.Text.Json;

namespace ToteLabels;

// TopOffsetMm moves everything down the label (or up, if negative) to correct
// a printer that starts printing slightly off the top edge.
public record AppSettings(string PrinterIp, int Dpi, double TopOffsetMm = 0);

// Settings, label sizes and the logo live in the folder given, %LOCALAPPDATA%\ToteLabels.
public class AppData
{
    static readonly AppSettings Defaults = new("", 203);
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    readonly string settingsPath;
    readonly string sizesPath;

    public AppData(string folder)
    {
        Directory.CreateDirectory(folder);
        settingsPath = Path.Combine(folder, "settings.json");
        sizesPath = Path.Combine(folder, "sizes.json");
        LogoPath = Path.Combine(folder, "logo.png");
    }

    public string LogoPath { get; }

    public bool HasLogo => File.Exists(LogoPath);

    public AppSettings LoadSettings() => File.Exists(settingsPath)
        ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(settingsPath)) ?? Defaults
        : Defaults;

    public void SaveSettings(AppSettings settings) =>
        File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings, JsonOptions));

    // Until the user changes a size, the built-in ones.
    public LabelSize[] LoadSizes() => File.Exists(sizesPath)
        ? JsonSerializer.Deserialize<LabelSize[]>(File.ReadAllText(sizesPath)) is { Length: > 0 } sizes ? sizes : LabelLayouts.Defaults
        : LabelLayouts.Defaults;

    public void SaveSizes(IEnumerable<LabelSize> sizes) =>
        File.WriteAllText(sizesPath, JsonSerializer.Serialize(sizes, JsonOptions));
}
