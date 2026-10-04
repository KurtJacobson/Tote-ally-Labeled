using System.ComponentModel;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace ToteLabels;

// Where labels go: a network printer by IP address, or a printer installed in Windows (usually USB) by name.
public record PrinterTarget(string Connection, string Ip, string Name)
{
    public bool Usb => Connection == "usb";
}

// A printer installed in Windows, for the list in Settings.
public record InstalledPrinter(string Name, string Driver, string Port, bool Online, bool LooksLikeZebra);

public static class PrinterClient
{
    const int Port = 9100;

    // Sends ZPL to the printer. Sending nothing to a network printer just opens and closes a connection,
    // which checks it is reachable; a Windows printer is checked with IsOnline instead, so no empty job is queued.
    public static Task SendAsync(PrinterTarget target, string zpl) =>
        target.Usb ? Task.Run(() => WindowsPrinter.SendRaw(target.Name, Encoding.UTF8.GetBytes(zpl))) : SendToNetwork(target.Ip, zpl);

    // Zebra network printers accept raw ZPL on port 9100.
    static async Task SendToNetwork(string ip, string zpl)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var client = new TcpClient();
        await client.ConnectAsync(ip, Port, timeout.Token);
        await client.GetStream().WriteAsync(Encoding.UTF8.GetBytes(zpl), timeout.Token);
    }
}

// Printing to a printer installed in Windows, through the print spooler. Jobs are sent as RAW data, which
// the printer's driver passes straight through, so ZPL reaches the printer unchanged by driver settings.
public static class WindowsPrinter
{
    const int PRINTER_ENUM_LOCAL = 0x2, PRINTER_ENUM_CONNECTIONS = 0x4;
    const int PRINTER_ATTRIBUTE_WORK_OFFLINE = 0x400;  // set by Windows when a USB printer is unplugged or off
    const int PRINTER_STATUS_OFFLINE = 0x80;

    public static IEnumerable<InstalledPrinter> List() =>
        Enumerate()
            .Select(info => new InstalledPrinter(info.pPrinterName ?? "", info.pDriverName ?? "", info.pPortName ?? "",
                Online(info), LooksLikeZebra(info.pDriverName)))
            .Where(printer => printer.Name.Length > 0)
            .OrderByDescending(printer => printer.LooksLikeZebra)
            .ThenBy(printer => printer.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    // Null when there is no printer by that name, otherwise whether Windows has it online.
    public static bool? IsOnline(string name) =>
        Enumerate().Where(info => string.Equals(info.pPrinterName, name, StringComparison.OrdinalIgnoreCase))
                   .Select(info => (bool?)Online(info))
                   .FirstOrDefault();

    public static void SendRaw(string printerName, byte[] data)
    {
        if (!OpenPrinter(printerName, out var printer, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var document = new DOC_INFO_1 { pDocName = "Tote-ally Labeled", pDataType = "RAW" };
            if (StartDocPrinter(printer, 1, document) == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                if (!StartPagePrinter(printer) || !WritePrinter(printer, data, data.Length, out int written) || written != data.Length)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                EndPagePrinter(printer);
            }
            finally
            {
                EndDocPrinter(printer);
            }
        }
        finally
        {
            ClosePrinter(printer);
        }
    }

    static bool Online(PRINTER_INFO_2 info) =>
        (info.Attributes & PRINTER_ATTRIBUTE_WORK_OFFLINE) == 0 && (info.Status & PRINTER_STATUS_OFFLINE) == 0;

    // Zebra's drivers are named "ZDesigner …"; ZPL in the name covers other ZPL drivers.
    static bool LooksLikeZebra(string? driver) =>
        driver is not null && (driver.Contains("ZDesigner", StringComparison.OrdinalIgnoreCase) ||
                               driver.Contains("Zebra", StringComparison.OrdinalIgnoreCase) ||
                               driver.Contains("ZPL", StringComparison.OrdinalIgnoreCase));

    static List<PRINTER_INFO_2> Enumerate()
    {
        const int flags = PRINTER_ENUM_LOCAL | PRINTER_ENUM_CONNECTIONS;
        EnumPrinters(flags, null, 2, IntPtr.Zero, 0, out int needed, out _);
        if (needed == 0) return [];

        IntPtr buffer = Marshal.AllocHGlobal(needed);
        try
        {
            if (!EnumPrinters(flags, null, 2, buffer, needed, out _, out int count))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            int size = Marshal.SizeOf<PRINTER_INFO_2>();
            return Enumerable.Range(0, count)
                .Select(i => Marshal.PtrToStructure<PRINTER_INFO_2>(buffer + i * size))
                .ToList();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    class DOC_INFO_1
    {
        public string? pDocName;
        public string? pOutputFile;
        public string? pDataType;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PRINTER_INFO_2
    {
        public string? pServerName, pPrinterName, pShareName, pPortName, pDriverName, pComment, pLocation;
        public IntPtr pDevMode;
        public string? pSepFile, pPrintProcessor, pDatatype, pParameters;
        public IntPtr pSecurityDescriptor;
        public int Attributes, Priority, DefaultPriority, StartTime, UntilTime, Status, cJobs, AveragePPM;
    }

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool EnumPrinters(int flags, string? name, int level, IntPtr buffer, int size, out int needed, out int returned);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool OpenPrinter(string name, out IntPtr printer, IntPtr defaults);

    [DllImport("winspool.drv", SetLastError = true)]
    static extern bool ClosePrinter(IntPtr printer);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern int StartDocPrinter(IntPtr printer, int level, DOC_INFO_1 document);

    [DllImport("winspool.drv", SetLastError = true)]
    static extern bool EndDocPrinter(IntPtr printer);

    [DllImport("winspool.drv", SetLastError = true)]
    static extern bool StartPagePrinter(IntPtr printer);

    [DllImport("winspool.drv", SetLastError = true)]
    static extern bool EndPagePrinter(IntPtr printer);

    [DllImport("winspool.drv", SetLastError = true)]
    static extern bool WritePrinter(IntPtr printer, byte[] data, int count, out int written);
}
