using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ToteLabels;

// The desktop window: the app's own web page, shown in WebView2 (Edge).
public class MainWindow : Form
{
    readonly WebView2 webView = new() { Dock = DockStyle.Fill };
    readonly string url;
    readonly string browserDataFolder;

    public MainWindow(string url, string dataFolder)
    {
        this.url = url;
        browserDataFolder = Path.Combine(dataFolder, "WebView2");

        Text = "Tote-ally Labeled";
        // tote.ico at the size this screen's scaling needs. The exe's own icon gives only 32 px, which the taskbar
        // stretches and blurs on a scaled display.
        int iconSize = LogicalToDeviceUnits(32);
        using (var iconStream = typeof(MainWindow).Assembly.GetManifestResourceStream("tote.ico")!)
            Icon = new Icon(iconStream, iconSize, iconSize);
        ClientSize = new Size(1100, 760);
        MinimumSize = new Size(480, 480);
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(webView);

        Load += async (_, _) => await ShowPage();
    }

    async Task ShowPage()
    {
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: browserDataFolder);
            await webView.EnsureCoreWebView2Async(environment);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show(this,
                "Tote-ally Labeled needs the Microsoft Edge WebView2 Runtime. Install it from Microsoft's website, then open Tote-ally Labeled again.",
                "Tote-ally Labeled", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
            return;
        }

        // Keep the window's title in step with the page, and open any outside links in the normal browser.
        webView.CoreWebView2.DocumentTitleChanged += (_, _) => Text = webView.CoreWebView2.DocumentTitle;
        webView.CoreWebView2.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            Process.Start(new ProcessStartInfo(e.Uri) { UseShellExecute = true });
        };

        webView.Source = new Uri(url);
    }
}
