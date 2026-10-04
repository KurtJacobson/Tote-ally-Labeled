using System.Drawing;
using System.Drawing.Imaging;
using System.ComponentModel;
using System.Net.Sockets;
using System.Reflection;
using ToteLabels;

const int Port = 5050;

// Only one copy can run, since both would need the same port.
using var singleInstance = new Mutex(true, "ToteLabels", out bool firstInstance);
if (!firstInstance)
{
    MessageBox.Show("Tote-ally Labeled is already open.", "Tote-ally Labeled", MessageBoxButtons.OK, MessageBoxIcon.Information);
    return;
}

// The page is served from the app's own folder, wherever it was started from.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
builder.WebHost.UseUrls($"http://*:{Port}");  // also reachable from other devices on your network
builder.Services.AddResponseCompression();  // shrinks the icon list to about a third of its size for phones

var app = builder.Build();
string dataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToteLabels");
var data = new AppData(dataFolder);

app.UseResponseCompression();
app.UseDefaultFiles();  // serves wwwroot/index.html at /
app.UseStaticFiles(new StaticFileOptions
{
    // Check for a newer copy every time, so an updated app never shows a stale page.
    OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache"
});

// Layouts depend on the logo's proportions, so they are worked out with the logo as it is now.
LabelLayout Layout(LabelSize size) => LabelLayouts.Build(size, data.LogoAspect);

app.MapGet("/api/layouts", () => data.LoadSizes().Select(Layout));

// The layout a size would get, for the live preview while it is being edited. Nothing is saved.
app.MapPost("/api/sizes/preview", (LabelSize size) =>
    LabelLayouts.Problem(size) is { } problem
        ? Results.Text(problem, statusCode: 400)
        : Results.Ok(Layout(size)));

// Replaces the whole list of sizes. A size with no Id is new and gets one here.
app.MapPut("/api/sizes", (LabelSize[] sizes) =>
{
    if (sizes.Length == 0)
        return Results.Text("Keep at least one label size.", statusCode: 400);

    sizes = sizes.Select(size => size with
    {
        Id = string.IsNullOrWhiteSpace(size.Id) ? Guid.NewGuid().ToString("N")[..8] : size.Id,
        Name = size.Name?.Trim() ?? "",
        HeightInches = size.Round ? size.WidthInches : size.HeightInches
    }).ToArray();

    foreach (var size in sizes)
        if (LabelLayouts.Problem(size) is { } problem)
            return Results.Text($"{LabelLayouts.DisplayName(size)}: {problem}", statusCode: 400);
    if (sizes.Select(size => size.Id).Distinct().Count() != sizes.Length)
        return Results.Text("Two sizes have the same id.", statusCode: 400);

    data.SaveSizes(sizes);
    return Results.Ok(sizes.Select(Layout));
});

// The release version from Directory.Build.props, without the +<git describe> build detail.
app.MapGet("/api/version", () => new
{
    Version = typeof(AppData).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
        .InformationalVersion.Split('+')[0] ?? "unknown"
});

app.MapGet("/api/settings", () => data.LoadSettings());

app.MapPut("/api/settings", (AppSettings settings) =>
{
    if (settings.Dpi is not (203 or 300))
        return Results.Text("Resolution must be 203 or 300 dpi.", statusCode: 400);
    if (settings.TopOffsetMm is < -10 or > 10)
        return Results.Text("Top offset must be between -10 and 10 mm.", statusCode: 400);
    if (settings.Connection is not ("network" or "usb"))
        return Results.Text("Connection must be network or USB.", statusCode: 400);

    data.SaveSettings(settings with { PrinterIp = settings.PrinterIp?.Trim() ?? "", PrinterName = settings.PrinterName ?? "" });
    return Results.Ok();
});

app.MapGet("/api/logo", () => data.HasLogo ? Results.File(data.LogoPath, "image/png") : Results.NotFound());

app.MapPut("/api/logo", async (HttpRequest request) =>
{
    using var buffer = new MemoryStream();
    await request.Body.CopyToAsync(buffer);
    buffer.Position = 0;

    try
    {
        using var image = new Bitmap(buffer);
        image.Save(data.LogoPath, ImageFormat.Png);  // always stored as PNG
        return Results.Ok();
    }
    catch (ArgumentException)
    {
        return Results.Text("That file isn't an image this app can read. Try a PNG or JPG.", statusCode: 400);
    }
});

app.MapDelete("/api/logo", () =>
{
    File.Delete(data.LogoPath);
    return Results.Ok();
});

// Your own icons. The browser converts uploads (including SVGs) to PNG before sending them.
app.MapGet("/api/icons", () => data.IconNames());

app.MapGet("/api/icons/{name}", (string name) =>
    data.IconPath(name) is { } path && File.Exists(path) ? Results.File(path, "image/png") : Results.NotFound());

app.MapPut("/api/icons", async (string name, HttpRequest request) =>
{
    using var buffer = new MemoryStream();
    await request.Body.CopyToAsync(buffer);
    buffer.Position = 0;

    try
    {
        using var image = new Bitmap(buffer);
        string savedName = data.UniqueIconName(name);
        image.Save(data.IconPath(savedName)!, ImageFormat.Png);
        return Results.Ok(new { name = savedName });
    }
    catch (ArgumentException)
    {
        return Results.Text("That file isn't an image this app can read. Try a PNG, JPG or SVG.", statusCode: 400);
    }
});

app.MapDelete("/api/icons/{name}", (string name) =>
{
    if (data.IconPath(name) is { } path)
        File.Delete(path);
    return Results.Ok();
});

app.MapPost("/api/print", async (PrintRequest request) =>
{
    var layout = data.LoadSizes().Where(size => size.Id == request.LayoutId).Select(Layout).FirstOrDefault();
    if (layout is null)
        return Results.Text("Unknown label size.", statusCode: 400);
    if (string.IsNullOrWhiteSpace(request.Title))
        return Results.Text("Add a title before printing.", statusCode: 400);

    var settings = data.LoadSettings();
    var label = request with { Copies = Math.Clamp(request.Copies, 1, 99) };
    if (request.IconPng?.Length > 2_000_000)
        return Results.Text("The icon image is too large.", statusCode: 400);

    string zpl;
    try
    {
        zpl = ZplBuilder.Label(layout, label, settings, data.HasLogo ? data.LogoPath : null);
    }
    catch (Exception ex) when (ex is FormatException or ArgumentException)
    {
        return Results.Text("The icon couldn't be read. Choose it again and retry.", statusCode: 400);
    }

    return await SendToPrinter(settings.Target, zpl);
});

// The printers installed in Windows, for choosing a USB printer in Settings.
app.MapGet("/api/printers", () => WindowsPrinter.List());

// Both accept the connection, ?ip= and ?name= so Settings can try a printer before saving it;
// without them they use the saved printer.
PrinterTarget Target(string? connection, string? ip, string? name) =>
    connection is null ? data.LoadSettings().Target : new PrinterTarget(connection, ip ?? "", name ?? "");

app.MapPost("/api/printer/check", (string? connection, string? ip, string? name) => CheckPrinter(Target(connection, ip, name)));
app.MapPost("/api/printer/calibrate", (string? connection, string? ip, string? name) => SendToPrinter(Target(connection, ip, name), "~JC"));

try
{
    await app.StartAsync();
}
catch (IOException)
{
    MessageBox.Show($"Tote-ally Labeled couldn't start because another program is using port {Port}.",
        "Tote-ally Labeled", MessageBoxButtons.OK, MessageBoxIcon.Error);
    return;
}

// WinForms needs its own single-threaded thread; the web server keeps running alongside it.
// Closing the window stops the app, including access from other devices.
var window = new Thread(() =>
{
    ApplicationConfiguration.Initialize();
    Application.Run(new MainWindow($"http://localhost:{Port}", dataFolder));
});
window.SetApartmentState(ApartmentState.STA);
window.Start();
window.Join();

await app.StopAsync();

// A network printer is checked by connecting to it. A USB printer is checked by asking Windows whether it is
// installed and online: Windows marks a USB printer offline when it is unplugged or switched off.
static async Task<IResult> CheckPrinter(PrinterTarget target)
{
    if (!target.Usb)
        return await SendToPrinter(target, "");

    if (string.IsNullOrWhiteSpace(target.Name))
        return Results.Text("Choose the USB printer in Settings first.", statusCode: 400);

    return WindowsPrinter.IsOnline(target.Name) switch
    {
        null => Results.Text($"There's no printer called {target.Name} on this PC. Choose it again in Settings.", statusCode: 502),
        false => Results.Text($"{target.Name} is offline. Check that it's plugged in and switched on.", statusCode: 502),
        true => Results.Ok()
    };
}

static async Task<IResult> SendToPrinter(PrinterTarget target, string zpl)
{
    if (target.Usb)
    {
        if (string.IsNullOrWhiteSpace(target.Name))
            return Results.Text("Choose the USB printer in Settings first.", statusCode: 400);

        // Windows accepts a job for an unplugged printer and holds it until the printer comes back, which would
        // report a label as sent that hasn't printed. So an offline printer is turned away instead.
        if (WindowsPrinter.IsOnline(target.Name) is not { } online)
            return Results.Text($"There's no printer called {target.Name} on this PC. Choose it again in Settings.", statusCode: 502);
        if (!online)
            return Results.Text($"{target.Name} is offline. Check that it's plugged in and switched on.", statusCode: 502);

        try
        {
            await PrinterClient.SendAsync(target, zpl);
            return Results.Ok();
        }
        catch (Win32Exception)
        {
            return WindowsPrinter.IsOnline(target.Name) is null
                ? Results.Text($"There's no printer called {target.Name} on this PC. Choose it again in Settings.", statusCode: 502)
                : Results.Text($"Couldn't print to {target.Name}. Check that it's plugged in and switched on.", statusCode: 502);
        }
    }

    if (string.IsNullOrWhiteSpace(target.Ip))
        return Results.Text("Add the printer's IP address in Settings first.", statusCode: 400);

    try
    {
        await PrinterClient.SendAsync(target, zpl);
        return Results.Ok();
    }
    catch (Exception ex) when (ex is SocketException or OperationCanceledException)
    {
        return Results.Text($"Couldn't reach the printer at {target.Ip}. Check that it's on and the IP address is right.", statusCode: 502);
    }
}
