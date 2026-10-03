using System.Net.Sockets;
using System.Text;

namespace ToteLabels;

// Zebra network printers accept raw ZPL on port 9100.
public static class PrinterClient
{
    const int Port = 9100;

    public static async Task SendAsync(string ip, string zpl)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var client = new TcpClient();
        await client.ConnectAsync(ip, Port, timeout.Token);
        await client.GetStream().WriteAsync(Encoding.UTF8.GetBytes(zpl), timeout.Token);
    }
}
