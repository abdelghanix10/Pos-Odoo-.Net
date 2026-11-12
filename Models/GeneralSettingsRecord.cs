using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OdooPosLogin;

public class GeneralSettingsRecord : INotifyPropertyChanged
{
    private string _screenMode = "Window"; // "Window" or "Fullscreen"
    private int? _defaultCompanyId;
    private string? _defaultCompanyName;

    // Printer settings
    private string _defaultPrinterName = string.Empty;
    private string _paperWidth = "80mm"; // "80mm" or "58mm"
    private bool _autoPrintReceipt = false;
    private bool _cashDrawerPulse = false;
    private bool _printHeaderLogo = true;
    private string _printerEncoding = "UTF-8";

    public string ScreenMode
    {
        get => _screenMode;
        set { if (_screenMode != value) { _screenMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsFullscreen)); } }
    }

    public bool IsFullscreen => string.Equals(_screenMode, "Fullscreen", StringComparison.OrdinalIgnoreCase);

    public int? DefaultCompanyId
    {
        get => _defaultCompanyId;
        set { if (_defaultCompanyId != value) { _defaultCompanyId = value; OnPropertyChanged(); } }
    }

    public string? DefaultCompanyName
    {
        get => _defaultCompanyName;
        set { if (_defaultCompanyName != value) { _defaultCompanyName = value; OnPropertyChanged(); } }
    }

    public string DefaultPrinterName
    {
        get => _defaultPrinterName;
        set { if (_defaultPrinterName != value) { _defaultPrinterName = value; OnPropertyChanged(); } }
    }

    public string PaperWidth
    {
        get => _paperWidth;
        set { if (_paperWidth != value) { _paperWidth = value; OnPropertyChanged(); } }
    }

    public bool AutoPrintReceipt
    {
        get => _autoPrintReceipt;
        set { if (_autoPrintReceipt != value) { _autoPrintReceipt = value; OnPropertyChanged(); } }
    }

    public bool CashDrawerPulse
    {
        get => _cashDrawerPulse;
        set { if (_cashDrawerPulse != value) { _cashDrawerPulse = value; OnPropertyChanged(); } }
    }

    public bool PrintHeaderLogo
    {
        get => _printHeaderLogo;
        set { if (_printHeaderLogo != value) { _printHeaderLogo = value; OnPropertyChanged(); } }
    }

    public string PrinterEncoding
    {
        get => _printerEncoding;
        set { if (_printerEncoding != value) { _printerEncoding = value; OnPropertyChanged(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
}
