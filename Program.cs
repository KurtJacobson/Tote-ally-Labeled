using System.Drawing;
using System.Drawing.Imaging;
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

var app = builder.Build();
string dataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToteLabels");
var data = new AppData(dataFolder);

app.UseDefaultFiles();  // serves wwwroot/index.html at /
app.UseStaticFiles(new StaticFileOptions
{
    // Check for a newer copy every time, so an updated app never shows a stale page.
    OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache"
});

app.MapGet("/api/layouts", () => LabelLayouts.All);

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

    data.SaveSettings(settings with { PrinterIp = settings.PrinterIp?.Trim() ?? "" });
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

app.MapPost("/api/print", async (PrintRequest request) =>
{
    var layout = LabelLayouts.Find(request.LayoutId);
    if (layout is null)
        return Results.Text("Unknown label size.", statusCode: 400);
    if (string.IsNullOrWhiteSpace(request.Title))
        return Results.Text("Add a title before printing.", statusCode: 400);

    var settings = data.LoadSettings();
    var label = request with { Copies = Math.Clamp(request.Copies, 1, 99) };
    string zpl = ZplBuilder.Label(layout, label, settings, data.HasLogo ? data.LogoPath : null);

    return await SendToPrinter(settings.PrinterIp, zpl);
});

// Both accept an optional ?ip= so Settings can test an address before saving it.
// Sending nothing just opens and closes a connection, which checks the printer is reachable.
app.MapPost("/api/printer/check", (string? ip) => SendToPrinter(ip ?? data.LoadSettings().PrinterIp, ""));
app.MapPost("/api/printer/calibrate", (string? ip) => SendToPrinter(ip ?? data.LoadSettings().PrinterIp, "~JC"));

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

static async Task<IResult> SendToPrinter(string ip, string zpl)
{
    if (string.IsNullOrWhiteSpace(ip))
        return Results.Text("Add the printer's IP address in Settings first.", statusCode: 400);

    try
    {
        await PrinterClient.SendAsync(ip, zpl);
        return Results.Ok();
    }
    catch (Exception ex) when (ex is SocketException or OperationCanceledException)
    {
        return Results.Text($"Couldn't reach the printer at {ip}. Check that it's on and the IP address is right.", statusCode: 502);
    }
}
