using System.Text.Json;

namespace ToteLabels;

// TopOffsetMm moves everything down the label (or up, if negative) to correct
// a printer that starts printing slightly off the top edge.
// Connection is "network" (the printer at PrinterIp) or "usb" (the Windows printer called PrinterName).
public record AppSettings(string PrinterIp, int Dpi, double TopOffsetMm = 0, string Connection = "network", string PrinterName = "")
{
    public PrinterTarget Target => new(Connection, PrinterIp, PrinterName);
}

// Settings, label sizes, the logo and your own icons live in the folder given, %LOCALAPPDATA%\ToteLabels.
public class AppData
{
    static readonly AppSettings Defaults = new("", 203);
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    readonly string settingsPath;
    readonly string sizesPath;
    readonly string iconsFolder;

    public AppData(string folder)
    {
        iconsFolder = Path.Combine(folder, "icons");
        Directory.CreateDirectory(iconsFolder);
        settingsPath = Path.Combine(folder, "settings.json");
        sizesPath = Path.Combine(folder, "sizes.json");
        LogoPath = Path.Combine(folder, "logo.png");
    }

    public string LogoPath { get; }

    public bool HasLogo => File.Exists(LogoPath);

    // The logo's width / height, or null when none is uploaded. Read from the file's header only.
    public double? LogoAspect
    {
        get
        {
            if (!HasLogo) return null;
            using var stream = File.OpenRead(LogoPath);
            using var image = System.Drawing.Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: false);
            return image.Height > 0 ? (double)image.Width / image.Height : null;
        }
    }

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

    // Your own icons are stored as icons\<name>.png, so the file name is the icon's name.
    public IEnumerable<string> IconNames() =>
        Directory.GetFiles(iconsFolder, "*.png").Select(path => Path.GetFileNameWithoutExtension(path)).Order();

    // Returns null for names that aren't a plain file name, so nothing outside the folder can be reached.
    public string? IconPath(string name)
    {
        bool valid = !string.IsNullOrWhiteSpace(name) && name == Path.GetFileName(name) &&
                     name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        return valid ? Path.Combine(iconsFolder, name + ".png") : null;
    }

    // Cleans up a name for use as a file name and adds " 2", " 3"... if it's already taken.
    public string UniqueIconName(string requested)
    {
        var allowed = requested.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' or '\'');
        string name = new string(allowed.ToArray()).Trim();
        if (name.Length == 0) name = "Icon";
        if (name.Length > 40) name = name[..40].Trim();

        string candidate = name;
        for (int number = 2; File.Exists(IconPath(candidate)); number++)
            candidate = $"{name} {number}";
        return candidate;
    }
}
