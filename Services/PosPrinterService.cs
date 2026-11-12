using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace OdooPosLogin;

public static class PosPrinterService
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public class DOCINFOA
    {
        [MarshalAs(UnmanagedType.LPStr)]
        public string pDocName = "Odoo POS Receipt";
        [MarshalAs(UnmanagedType.LPStr)]
        public string? pOutputFile = null;
        [MarshalAs(UnmanagedType.LPStr)]
        public string pDataType = "RAW";
    }

    [DllImport("winspool.Drv", EntryPoint = "OpenPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool OpenPrinter([MarshalAs(UnmanagedType.LPStr)] string szPrinter, out IntPtr hPrinter, IntPtr pd);

    [DllImport("winspool.Drv", EntryPoint = "ClosePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", EntryPoint = "StartDocPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool StartDocPrinter(IntPtr hPrinter, int level, [In, MarshalAs(UnmanagedType.LPStruct)] DOCINFOA di);

    [DllImport("winspool.Drv", EntryPoint = "EndDocPrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool EndDocPrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", EntryPoint = "StartPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool StartPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", EntryPoint = "EndPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool EndPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", EntryPoint = "WritePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);

    public static List<string> GetInstalledPrinters()
    {
        var printers = new List<string>();
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\Devices");
            if (key != null)
            {
                foreach (string name in key.GetValueNames())
                {
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        printers.Add(name);
                    }
                }
            }
        }
        catch { }

        if (printers.Count == 0)
        {
            printers.Add("POS-80C");
            printers.Add("Microsoft Print to PDF");
        }

        return printers.OrderBy(p => p).ToList();
    }

    public static string GetDefaultPrinter()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\Windows");
            string? def = key?.GetValue("Device") as string;
            if (!string.IsNullOrWhiteSpace(def))
            {
                string name = def.Split(',')[0].Trim();
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
        }
        catch { }

        var list = GetInstalledPrinters();
        var posMatch = list.FirstOrDefault(p => p.Contains("POS", StringComparison.OrdinalIgnoreCase));
        return posMatch ?? list.FirstOrDefault() ?? "POS-80C";
    }

    public static bool SendRawBytesToPrinter(string printerName, byte[] bytes)
    {
        if (string.IsNullOrWhiteSpace(printerName) || bytes == null || bytes.Length == 0)
            return false;

        IntPtr hPrinter = IntPtr.Zero;
        var di = new DOCINFOA();
        bool success = false;

        if (OpenPrinter(printerName, out hPrinter, IntPtr.Zero))
        {
            if (StartDocPrinter(hPrinter, 1, di))
            {
                if (StartPagePrinter(hPrinter))
                {
                    IntPtr pUnmanagedBytes = Marshal.AllocCoTaskMem(bytes.Length);
                    Marshal.Copy(bytes, 0, pUnmanagedBytes, bytes.Length);

                    success = WritePrinter(hPrinter, pUnmanagedBytes, bytes.Length, out _);
                    Marshal.FreeCoTaskMem(pUnmanagedBytes);

                    EndPagePrinter(hPrinter);
                }
                EndDocPrinter(hPrinter);
            }
            ClosePrinter(hPrinter);
        }

        return success;
    }

    public static bool PrintReceiptText(
        string? printerName,
        string receiptText,
        bool cashDrawerPulse = false,
        string encodingName = "UTF-8")
    {
        if (string.IsNullOrWhiteSpace(receiptText)) return false;

        string targetPrinter = !string.IsNullOrWhiteSpace(printerName)
            ? printerName
            : GetDefaultPrinter();

        Encoding enc;
        try
        {
            enc = Encoding.GetEncoding(encodingName);
        }
        catch
        {
            enc = Encoding.UTF8;
        }

        using var ms = new MemoryStream();

        // 1. ESC @ (Initialize printer)
        ms.Write([0x1B, 0x40]);

        // 2. Pulse cash drawer kick-out if requested: ESC p 0 25 250
        if (cashDrawerPulse)
        {
            ms.Write([0x1B, 0x70, 0x00, 0x19, 0xFA]);
        }

        // 3. Write text bytes
        byte[] textBytes = enc.GetBytes(receiptText);
        ms.Write(textBytes);

        // 4. Feed paper and partial cut: \n\n\n GS V 66 0
        ms.Write([0x0A, 0x0A, 0x0A, 0x0A]);
        ms.Write([0x1D, 0x56, 0x42, 0x00]);

        return SendRawBytesToPrinter(targetPrinter, ms.ToArray());
    }

    public static bool PrintTestReceipt(string? printerName, string paperWidth = "80mm")
    {
        var sb = new StringBuilder();
        sb.AppendLine("========================================");
        sb.AppendLine("            TEST PRINT TICKET           ");
        sb.AppendLine("========================================");
        sb.AppendLine($"Printer: {printerName ?? GetDefaultPrinter()}");
        sb.AppendLine($"Width:   {paperWidth}");
        sb.AppendLine($"Date:    {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("Status:  Hardware Connected OK");
        sb.AppendLine("----------------------------------------");
        sb.AppendLine("1 x Thermal Print Test          $ 0.00");
        sb.AppendLine("----------------------------------------");
        sb.AppendLine("TOTAL:                          $ 0.00");
        sb.AppendLine("========================================");
        sb.AppendLine("      Odoo POS Hardware Verified        ");
        sb.AppendLine("========================================");

        return PrintReceiptText(printerName, sb.ToString(), cashDrawerPulse: false);
    }
}
