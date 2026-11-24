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

    public static int GetPrinterDotWidth(string? paperWidth) =>
        !string.IsNullOrWhiteSpace(paperWidth) && paperWidth.Contains("58", StringComparison.OrdinalIgnoreCase)
            ? 384
            : 576;

    private static void WritePrePrintBackfeed(MemoryStream ms, string? cutMode)
    {
        // GS P sets the vertical/horizontal motion unit. Normalizing it to 1/203" (203,203)
        // makes the ESC j feed distance deterministic across printers instead of depending
        // on whatever the device default happens to be. GS P 0 0 restores the defaults.
        if (string.Equals(cutMode, "PrePrintBackfeed", StringComparison.OrdinalIgnoreCase))
        {
            // GS P 203 203 -> ESC j 96 -> GS P 0 0
            ms.Write([0x1D, 0x50, 0xCB, 0xCB, 0x1B, 0x6A, 0x60, 0x1D, 0x50, 0x00, 0x00]);
        }
        else if (string.Equals(cutMode, "PrePrintBackfeed15", StringComparison.OrdinalIgnoreCase))
        {
            // GS P 203 203 -> ESC j 120 -> GS P 0 0
            ms.Write([0x1D, 0x50, 0xCB, 0xCB, 0x1B, 0x6A, 0x78, 0x1D, 0x50, 0x00, 0x00]);
        }
    }

    private static void WriteCutCommand(MemoryStream ms, string? cutMode)
    {
        switch ((cutMode ?? "Standard").Trim())
        {
            case "BackfeedCut":
                ms.Write([0x1D, 0x56, 0x68, 0x00]);
                break;
            case "ImmediateCut":
                ms.Write([0x1D, 0x56, 0x01]);
                break;
            default:
                ms.Write([0x1D, 0x56, 0x42, 0x00]);
                break;
        }
    }

    public static bool PrintBgraRasterToEscPos(
        string? printerName,
        byte[] bgraPixels,
        int width,
        int height,
        bool cashDrawerPulse = false,
        string cutMode = "Standard")
    {
        if (bgraPixels == null || bgraPixels.Length == 0 || width <= 0 || height <= 0)
            return false;

        string targetPrinter = !string.IsNullOrWhiteSpace(printerName)
            ? printerName
            : GetDefaultPrinter();

        var gray = new float[width * height];
        for (int i = 0, p = 0; i < gray.Length; i++, p += 4)
        {
            float lum = (0.299f * bgraPixels[p + 2]) +
                        (0.587f * bgraPixels[p + 1]) +
                        (0.114f * bgraPixels[p]);
            if (bgraPixels[p + 3] < 255)
                lum = lum + (255 - bgraPixels[p + 3]);
            gray[i] = Math.Clamp(lum, 0f, 255f);
        }

        int bytesPerRow = (width + 7) / 8;
        var monoBits = new byte[bytesPerRow * height];
        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * width;
            int outRowOffset = y * bytesPerRow;
            for (int x = 0; x < width; x++)
            {
                float oldPixel = gray[rowOffset + x];
                float newPixel = oldPixel < 165f ? 0f : 255f;
                float error = oldPixel - newPixel;
                if (newPixel == 0f)
                    monoBits[outRowOffset + (x >> 3)] |= (byte)(0x80 >> (x & 7));

                if (MathF.Abs(error) > 0.5f)
                {
                    if (x + 1 < width)
                        gray[rowOffset + x + 1] += error * (7f / 16f);
                    if (y + 1 < height)
                    {
                        int nextRow = rowOffset + width;
                        if (x > 0) gray[nextRow + x - 1] += error * (3f / 16f);
                        gray[nextRow + x] += error * (5f / 16f);
                        if (x + 1 < width) gray[nextRow + x + 1] += error * (1f / 16f);
                    }
                }
            }
        }

        int firstInkRow = -1;
        int lastInkRow = -1;
        for (int y = 0; y < height; y++)
        {
            int offset = y * bytesPerRow;
            for (int b = 0; b < bytesPerRow; b++)
            {
                if (monoBits[offset + b] != 0)
                {
                    if (firstInkRow < 0) firstInkRow = y;
                    lastInkRow = y;
                    break;
                }
            }
        }

        int startRow = firstInkRow >= 0 ? firstInkRow : 0;
        int endRow = lastInkRow >= 0 ? lastInkRow : height - 1;

        using var ms = new MemoryStream(monoBits.Length + 64);
        ms.Write([0x1B, 0x40]);
        if (cashDrawerPulse)
            ms.Write([0x1B, 0x70, 0x00, 0x19, 0xFA]);
        WritePrePrintBackfeed(ms, cutMode);
        ms.Write([0x1B, 0x61, 0x01]);

        const int maxBandHeight = 256;
        byte xL = (byte)(bytesPerRow & 0xFF);
        byte xH = (byte)((bytesPerRow >> 8) & 0xFF);
        for (int y = startRow; y <= endRow; y += maxBandHeight)
        {
            int bandHeight = Math.Min(maxBandHeight, endRow - y + 1);
            ms.Write([0x1D, 0x76, 0x30, 0x00, xL, xH,
                (byte)(bandHeight & 0xFF), (byte)((bandHeight >> 8) & 0xFF)]);
            ms.Write(monoBits, y * bytesPerRow, bandHeight * bytesPerRow);
        }

        WriteCutCommand(ms, cutMode);
        return SendRawBytesToPrinter(targetPrinter, ms.ToArray());
    }

    public static bool OpenCashDrawerOnly(string? printerName)
    {
        string targetPrinter = !string.IsNullOrWhiteSpace(printerName) ? printerName : GetDefaultPrinter();
        return SendRawBytesToPrinter(targetPrinter, [0x1B, 0x40, 0x1B, 0x70, 0x00, 0x19, 0xFA]);
    }

    public static bool PrintReceiptText(
        string? printerName,
        string receiptText,
        bool cashDrawerPulse = false,
        string encodingName = "UTF-8",
        string cutMode = "Standard")
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
        WritePrePrintBackfeed(ms, cutMode);

        // 3. Write text bytes
        byte[] textBytes = enc.GetBytes(receiptText);
        ms.Write(textBytes);

        // GS V 66 0 handles the feed to the cutter.
        ms.Write([0x0A]);
        WriteCutCommand(ms, cutMode);

        return SendRawBytesToPrinter(targetPrinter, ms.ToArray());
    }

    public static bool PrintTestReceipt(string? printerName, string paperWidth = "80mm", string cutMode = "Standard")
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

        return PrintReceiptText(printerName, sb.ToString(), cashDrawerPulse: false, cutMode: cutMode);
    }
}
