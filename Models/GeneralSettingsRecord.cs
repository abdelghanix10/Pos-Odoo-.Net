using System.ComponentModel;
using System.IO;
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
    private string _printerCutMode = "Standard";

    // Backup settings
    private string _backupFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Backups");
    private bool _autoBackupEnabled = false;
    private string _backupFrequency = "Daily"; // "Daily" or "Weekly"
    private string _backupTime = "23:00";      // "HH:mm" (24-hour format)
    private string? _lastBackupAt;

    // Language setting for POS interface ("English" or "Arabic")
    private string _posLanguage = "English";
    private bool _showUnsyncedButtonInPos = true;
    private bool _showSetupButtonOnLogin = true;
    private bool _autoPurgeOldDataEnabled = true;
    private int _dataRetentionMonths = 2;
    private string? _lastPurgeAt;
    private string _syncDataRetrievalPeriod = "Week";
    private string _autoPurgeOlderThanPeriod = "3Months";
    private string _autoPurgeFrequency = "Daily"; // "Daily", "Weekly", or "Monthly"
    private string _autoPurgeTime = "23:00";      // "HH:mm" (24-hour format)
    private double _posZoomScale = 1.0;
    private string _currency = "$";

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

    public string PrinterCutMode
    {
        get => _printerCutMode;
        set
        {
            string normalized = string.IsNullOrWhiteSpace(value) ? "Standard" : value.Trim();
            if (_printerCutMode != normalized)
            {
                _printerCutMode = normalized;
                OnPropertyChanged();
            }
        }
    }

    public string BackupFolderPath
    {
        get => _backupFolderPath;
        set { if (_backupFolderPath != value) { _backupFolderPath = value; OnPropertyChanged(); } }
    }

    public bool AutoBackupEnabled
    {
        get => _autoBackupEnabled;
        set { if (_autoBackupEnabled != value) { _autoBackupEnabled = value; OnPropertyChanged(); } }
    }

    public string BackupFrequency
    {
        get => _backupFrequency;
        set { if (_backupFrequency != value) { _backupFrequency = value; OnPropertyChanged(); } }
    }

    public string BackupTime
    {
        get => _backupTime;
        set
        {
            string normalized = string.IsNullOrWhiteSpace(value) ? "23:00" : value.Trim();
            if (_backupTime != normalized)
            {
                _backupTime = normalized;
                OnPropertyChanged();
            }
        }
    }

    public string? LastBackupAt
    {
        get => _lastBackupAt;
        set { if (_lastBackupAt != value) { _lastBackupAt = value; OnPropertyChanged(); } }
    }

    public string PosLanguage
    {
        get => _posLanguage;
        set
        {
            string normalized = string.IsNullOrWhiteSpace(value) ? "English" : value.Trim();
            if (_posLanguage != normalized)
            {
                _posLanguage = normalized;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsArabicPos));
            }
        }
    }

    public bool IsArabicPos =>
        string.Equals(_posLanguage, "Arabic", StringComparison.OrdinalIgnoreCase) ||
        _posLanguage.Contains("Arabic", StringComparison.OrdinalIgnoreCase) ||
        _posLanguage.Contains("العربية", StringComparison.OrdinalIgnoreCase);

    public bool ShowUnsyncedButtonInPos
    {
        get => _showUnsyncedButtonInPos;
        set { if (_showUnsyncedButtonInPos != value) { _showUnsyncedButtonInPos = value; OnPropertyChanged(); } }
    }

    public bool ShowSetupButtonOnLogin
    {
        get => _showSetupButtonOnLogin;
        set { if (_showSetupButtonOnLogin != value) { _showSetupButtonOnLogin = value; OnPropertyChanged(); } }
    }

    public bool AutoPurgeOldDataEnabled
    {
        get => _autoPurgeOldDataEnabled;
        set { if (_autoPurgeOldDataEnabled != value) { _autoPurgeOldDataEnabled = value; OnPropertyChanged(); } }
    }

    public int DataRetentionMonths
    {
        get => _dataRetentionMonths;
        set
        {
            int normalized = Math.Max(1, value);
            if (_dataRetentionMonths != normalized) { _dataRetentionMonths = normalized; OnPropertyChanged(); }
        }
    }

    public string? LastPurgeAt
    {
        get => _lastPurgeAt;
        set { if (_lastPurgeAt != value) { _lastPurgeAt = value; OnPropertyChanged(); } }
    }

    public string SyncDataRetrievalPeriod
    {
        get => _syncDataRetrievalPeriod;
        set
        {
            string normalized = string.IsNullOrWhiteSpace(value) ? "Week" : value.Trim();
            if (_syncDataRetrievalPeriod != normalized) { _syncDataRetrievalPeriod = normalized; OnPropertyChanged(); }
        }
    }

    public string AutoPurgeOlderThanPeriod
    {
        get => _autoPurgeOlderThanPeriod;
        set
        {
            string normalized = string.IsNullOrWhiteSpace(value) ? "3Months" : value.Trim();
            if (_autoPurgeOlderThanPeriod != normalized) { _autoPurgeOlderThanPeriod = normalized; OnPropertyChanged(); }
        }
    }

    public double PosZoomScale
    {        get => _posZoomScale;
        set
        {
            double clamped = Math.Clamp(Math.Round(value, 2), 0.6, 1.8);
            if (Math.Abs(_posZoomScale - clamped) > 0.001)
            {
                _posZoomScale = clamped;
                OnPropertyChanged();
            }
        }
    }

    public string Currency
    {
        get => _currency;
        set
        {
            string normalized = CurrencyFormatter.Normalize(value);
            if (_currency != normalized)
            {
                _currency = normalized;
                OnPropertyChanged();
            }
        }
    }

    public string AutoPurgeFrequency
    {
        get => _autoPurgeFrequency;
        set
        {
            string normalized = string.IsNullOrWhiteSpace(value) ? "Daily" : value.Trim();
            if (_autoPurgeFrequency != normalized)
            {
                _autoPurgeFrequency = normalized;
                OnPropertyChanged();
            }
        }
    }

    public string AutoPurgeTime
    {
        get => _autoPurgeTime;
        set
        {
            string normalized = string.IsNullOrWhiteSpace(value) ? "23:00" : value.Trim();
            if (_autoPurgeTime != normalized)
            {
                _autoPurgeTime = normalized;
                OnPropertyChanged();
            }
        }
    }

    public static DateTime? GetRetrievalCutoffUtc(string? period) => (period ?? "Week").Trim().ToLowerInvariant() switch
    {
        "today" => DateTime.Today.ToUniversalTime(),
        "yesterday" => DateTime.Today.AddDays(-1).ToUniversalTime(),
        "week" => DateTime.UtcNow.AddDays(-7),
        "month" => DateTime.UtcNow.AddMonths(-1),
        "year" => DateTime.UtcNow.AddYears(-1),
        "all" => null,
        _ => DateTime.UtcNow.AddDays(-7)
    };

    public static DateTime GetAutoPurgeCutoffLocal(string? period) => (period ?? "3Months").Trim().ToLowerInvariant() switch
    {
        "week" => DateTime.Now.AddDays(-7),
        "month" => DateTime.Now.AddMonths(-1),
        "3months" or "3month" => DateTime.Now.AddMonths(-3),
        "year" => DateTime.Now.AddYears(-1),
        _ => DateTime.Now.AddMonths(-3)
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
}
