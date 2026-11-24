using System.IO;
using System.Printing;
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

    private static readonly object _printLock = new();

    public static bool SendRawBytesToPrinter(string printerName, byte[] bytes)
    {
        if (string.IsNullOrWhiteSpace(printerName) || bytes == null || bytes.Length == 0)
            return false;

        lock (_printLock)
        {
            IntPtr hPrinter = IntPtr.Zero;
            var di = new DOCINFOA();
            bool success = false;

            if (OpenPrinter(printerName, out hPrinter, IntPtr.Zero))
            {
                try
                {
                    if (StartDocPrinter(hPrinter, 1, di))
                    {
                        try
                        {
                            if (StartPagePrinter(hPrinter))
                            {
                                // Thermal printers typically expose only a 4-8 KB receive buffer.
                                // Writing a 50-100 KB raster job in one call overruns it, the
                                // printer drops bytes mid-band, then swallows the next GS v 0
                                // header as pixel data and dumps the rest as CP437 garbage.
                                // Chunk the write and pace it so the buffer drains.
                                const int maxChunkSize = 2048;
                                IntPtr pUnmanagedBytes = Marshal.AllocCoTaskMem(maxChunkSize);
                                try
                                {
                                    int offset = 0;
                                    success = true;
                                    while (offset < bytes.Length)
                                    {
                                        int chunkLen = Math.Min(maxChunkSize, bytes.Length - offset);
                                        Marshal.Copy(bytes, offset, pUnmanagedBytes, chunkLen);

                                        if (!WritePrinter(hPrinter, pUnmanagedBytes, chunkLen, out int dwWritten) || dwWritten <= 0)
                                        {
                                            success = false;
                                            break;
                                        }

                                        offset += dwWritten;

                                        if (offset < bytes.Length)
                                            Thread.Sleep(15);
                                    }
                                }
                                finally
                                {
                                    Marshal.FreeCoTaskMem(pUnmanagedBytes);
                                }

                                EndPagePrinter(hPrinter);
                            }
                        }
                        finally
                        {
                            EndDocPrinter(hPrinter);
                        }
                    }
                }
                finally
                {
                    ClosePrinter(hPrinter);
                }
            }

            return success;
        }
    }

    public static int GetPrinterDotWidth(string? paperWidth, string? printerName = null)
    {
        if (string.Equals(paperWidth, "Driver", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(paperWidth, "Auto", StringComparison.OrdinalIgnoreCase))
        {
            var (widthMm, _) = GetDriverPaperSize(printerName);
            if (widthMm.HasValue && widthMm.Value > 0 && widthMm.Value <= 64.0)
                return 384; // 58mm roll (48mm printable)
            return 576;     // 80mm roll (72mm printable)
        }

        return !string.IsNullOrWhiteSpace(paperWidth) && paperWidth.Contains("58", StringComparison.OrdinalIgnoreCase)
            ? 384
            : 576;
    }

    public static string FormatPaperWidthDisplay(string? paperWidth, string? printerName = null)
    {
        if (string.Equals(paperWidth, "Driver", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(paperWidth, "Auto", StringComparison.OrdinalIgnoreCase))
        {
            var (_, label) = GetDriverPaperSize(printerName);
            return string.IsNullOrWhiteSpace(label) ? "Driver Default" : $"Driver ({label})";
        }

        return string.IsNullOrWhiteSpace(paperWidth) ? "80mm" : paperWidth;
    }

    [DllImport("winspool.Drv", EntryPoint = "DocumentPropertiesA", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    private static extern int DocumentProperties(
        IntPtr hwnd,
        IntPtr hPrinter,
        [MarshalAs(UnmanagedType.LPStr)] string pDeviceName,
        IntPtr pDevModeOutput,
        IntPtr pDevModeInput,
        int fMode);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct DEVMODEA
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public short dmOrientation;
        public short dmPaperSize;
        public short dmPaperLength;
        public short dmPaperWidth;
        public short dmScale;
        public short dmCopies;
        public short dmDefaultSource;
        public short dmPrintQuality;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
    }

    /// <summary>
    /// Reads the paper width the Windows printer driver is configured for.
    /// Tries the WPF PrintQueue ticket first, then falls back to the raw DEVMODE.
    /// </summary>
    public static (double? WidthMm, string? SizeLabel) GetDriverPaperSize(string? printerName)
    {
        string targetPrinter = !string.IsNullOrWhiteSpace(printerName)
            ? printerName
            : GetDefaultPrinter();

        if (string.IsNullOrWhiteSpace(targetPrinter))
            return (null, null);

        try
        {
            using var server = new LocalPrintServer();
            using var queue = server.GetPrintQueue(targetPrinter);
            var ticket = queue.DefaultPrintTicket ?? queue.UserPrintTicket;
            var media = ticket?.PageMediaSize;
            if (media?.Width is double dipWidth && dipWidth > 0)
            {
                double mm = Math.Round(dipWidth * 25.4 / 96.0, 1);
                string sizeName = media.PageMediaSizeName.HasValue && media.PageMediaSizeName.Value != PageMediaSizeName.Unknown
                    ? media.PageMediaSizeName.Value.ToString()
                    : $"{mm:0.#}mm";
                return (mm, $"{mm:0.#}mm — {sizeName}");
            }
        }
        catch { }

        return GetDriverPaperSizeFromDevMode(targetPrinter);
    }

    private static (double? WidthMm, string? SizeLabel) GetDriverPaperSizeFromDevMode(string printerName)
    {
        const int DM_OUT_BUFFER = 2;
        IntPtr hPrinter = IntPtr.Zero;
        IntPtr pDevMode = IntPtr.Zero;

        try
        {
            if (!OpenPrinter(printerName, out hPrinter, IntPtr.Zero))
                return (null, null);

            int devModeSize = DocumentProperties(IntPtr.Zero, hPrinter, printerName, IntPtr.Zero, IntPtr.Zero, 0);
            if (devModeSize < Marshal.SizeOf<DEVMODEA>())
                return (null, null);

            pDevMode = Marshal.AllocCoTaskMem(devModeSize);
            if (DocumentProperties(IntPtr.Zero, hPrinter, printerName, pDevMode, IntPtr.Zero, DM_OUT_BUFFER) < 0)
                return (null, null);

            var dm = Marshal.PtrToStructure<DEVMODEA>(pDevMode);

            if (dm.dmPaperWidth > 0)
            {
                double mm = Math.Round(dm.dmPaperWidth / 10.0, 1);
                string form = string.IsNullOrWhiteSpace(dm.dmFormName) ? $"{mm:0.#}mm" : dm.dmFormName.Trim();
                return (mm, $"{mm:0.#}mm — {form}");
            }

            double? stdMm = dm.dmPaperSize switch
            {
                1 or 5 => 215.9, // Letter / Legal
                9 => 210.0,      // A4
                11 => 148.0,     // A5
                _ => null
            };

            if (stdMm.HasValue)
            {
                string form = string.IsNullOrWhiteSpace(dm.dmFormName) ? $"{stdMm.Value:0.#}mm" : dm.dmFormName.Trim();
                return (stdMm.Value, $"{stdMm.Value:0.#}mm — {form}");
            }
        }
        catch { }
        finally
        {
            if (pDevMode != IntPtr.Zero) Marshal.FreeCoTaskMem(pDevMode);
            if (hPrinter != IntPtr.Zero) ClosePrinter(hPrinter);
        }

        return (null, null);
    }

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
        else if (string.Equals(cutMode, "PrePrintBackfeedDirect", StringComparison.OrdinalIgnoreCase))
        {
            // Some firmwares ignore or mishandle GS P. Send the raw ESC j 96 with no
            // motion-unit change, so the feed uses whatever the device already has.
            ms.Write([0x1B, 0x6A, 0x60]);
        }
        else if (string.Equals(cutMode, "PrePrintBackfeedEscK", StringComparison.OrdinalIgnoreCase))
        {
            // ESC K is the documented ESC/POS "print and reverse feed" command
            // (1B 4B n). ESC j is not part of the Epson TM ESC/POS set at all, which is
            // why it silently does nothing on many thermal units. Same 12mm distance.
            ms.Write([0x1D, 0x50, 0xCB, 0xCB, 0x1B, 0x4B, 0x60, 0x1D, 0x50, 0x00, 0x00]);
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

        // 32 rows x 72 bytes = 2,304 bytes per GS v 0 band. Keeps yH at 0 (some firmware
        // misreads yH = 1 when bandHeight is exactly 256) and stays far inside the
        // printer's receive buffer.
        const int maxBandHeight = 32;
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
        sb.AppendLine($"Width:   {FormatPaperWidthDisplay(paperWidth, printerName)}");
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
