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

        Text = "Tote Labels";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);  // tote.ico, built into the exe
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
                "Tote Labels needs the Microsoft Edge WebView2 Runtime. Install it from Microsoft's website, then open Tote Labels again.",
                "Tote Labels", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
