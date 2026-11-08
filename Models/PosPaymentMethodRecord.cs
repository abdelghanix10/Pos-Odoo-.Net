using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OdooPosLogin;

public class PosPaymentMethodRecord : INotifyPropertyChanged
{
    private long _id;
    private int _odooId;
    private string _name = string.Empty;
    private string _methodType = "cash"; // "cash", "bank", "pay_later" (Customer Account), "online"
    private int? _journalId;
    private string _journalName = string.Empty;
    private int? _companyId;
    private string _companyName = string.Empty;
    private string _currenciesDisplay = "Company currency";
    private string _linkedPosConfigs = string.Empty;
    private string _integration = "None required";
    private string? _imageBase64;
    private BitmapImage? _imageBitmap;
    private bool _isSelected;

    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
    }

    public long Id
    {
        get => _id;
        set { if (_id != value) { _id = value; OnPropertyChanged(); } }
    }

    public int OdooId
    {
        get => _odooId;
        set { if (_odooId != value) { _odooId = value; OnPropertyChanged(); } }
    }

    public string Name
    {
        get => _name;
        set { if (_name != value) { _name = value; OnPropertyChanged(); } }
    }

    public string MethodType
    {
        get => _methodType;
        set
        {
            if (_methodType != value)
            {
                _methodType = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MethodTypeDisplay));
                OnPropertyChanged(nameof(IsTypeCash));
                OnPropertyChanged(nameof(IsTypeBank));
                OnPropertyChanged(nameof(IsTypeCustomerAccount));
                OnPropertyChanged(nameof(IsTypeOnline));
            }
        }
    }

    public int? JournalId
    {
        get => _journalId;
        set { if (_journalId != value) { _journalId = value; OnPropertyChanged(); } }
    }

    public string JournalName
    {
        get => _journalName;
        set
        {
            if (_journalName != value)
            {
                _journalName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasJournal));
                OnPropertyChanged(nameof(JournalKanbanDisplay));
            }
        }
    }

    public int? CompanyId
    {
        get => _companyId;
        set { if (_companyId != value) { _companyId = value; OnPropertyChanged(); } }
    }

    public string CompanyName
    {
        get => _companyName;
        set { if (_companyName != value) { _companyName = value; OnPropertyChanged(); } }
    }

    public string CurrenciesDisplay
    {
        get => _currenciesDisplay;
        set { if (_currenciesDisplay != value) { _currenciesDisplay = value; OnPropertyChanged(); } }
    }

    public string LinkedPosConfigs
    {
        get => _linkedPosConfigs;
        set
        {
            if (_linkedPosConfigs != value)
            {
                _linkedPosConfigs = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LinkedShopsList));
                OnPropertyChanged(nameof(HasLinkedShops));
                OnPropertyChanged(nameof(HasLinkedShopsVisibility));
                OnPropertyChanged(nameof(NoLinkedShopsVisibility));
            }
        }
    }

    public string Integration
    {
        get => _integration;
        set
        {
            if (_integration != value)
            {
                _integration = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IntegrationDisplay));
            }
        }
    }

    public string? ImageBase64
    {
        get => _imageBase64;
        set
        {
            if (_imageBase64 != value)
            {
                _imageBase64 = value;
                ImageBitmap = PosImageUtils.Base64ToBitmapImage(value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasImage));
                OnPropertyChanged(nameof(HasNoImage));
            }
        }
    }

    public BitmapImage? ImageBitmap
    {
        get => _imageBitmap;
        set
        {
            if (_imageBitmap != value)
            {
                _imageBitmap = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasImage));
                OnPropertyChanged(nameof(HasNoImage));
            }
        }
    }

    // ==================== COMPUTED & DISPLAY PROPERTIES ====================
    public bool IsTypeCash => string.Equals(MethodType, "cash", StringComparison.OrdinalIgnoreCase);
    public bool IsTypeBank => string.Equals(MethodType, "bank", StringComparison.OrdinalIgnoreCase);
    public bool IsTypeCustomerAccount =>
        string.Equals(MethodType, "pay_later", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(MethodType, "customer_account", StringComparison.OrdinalIgnoreCase);
    public bool IsTypeOnline => string.Equals(MethodType, "online", StringComparison.OrdinalIgnoreCase);

    public string MethodTypeDisplay => MethodType?.ToLowerInvariant() switch
    {
        "cash" => "Cash",
        "bank" => "Bank",
        "pay_later" or "customer_account" => "Customer Account",
        "online" => "Online",
        _ => "Cash"
    };

    public bool HasJournal => !string.IsNullOrWhiteSpace(JournalName);
    public string JournalKanbanDisplay => HasJournal ? JournalName : "No Journal";

    public List<string> LinkedShopsList
    {
        get
        {
            if (string.IsNullOrWhiteSpace(LinkedPosConfigs)) return [];
            return LinkedPosConfigs
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }
    }

    public bool HasLinkedShops => LinkedShopsList.Count > 0;
    public Visibility HasLinkedShopsVisibility => HasLinkedShops ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoLinkedShopsVisibility => HasLinkedShops ? Visibility.Collapsed : Visibility.Visible;

    public string IntegrationDisplay => string.IsNullOrWhiteSpace(Integration) ? "None required" : Integration;

    public bool HasImage => ImageBitmap != null;
    public bool HasNoImage => ImageBitmap == null;

    public ImageSource? IconSource
    {
        get
        {
            if (ImageBitmap != null) return ImageBitmap;
            var t = (MethodType ?? string.Empty).ToLowerInvariant();
            var n = (Name ?? string.Empty).ToLowerInvariant();
            string asset = (t == "cash" || n.Contains("cash"))
                ? "pack://application:,,,/Assets/money.png"
                : (t == "pay_later" || t == "customer_account" || n.Contains("customer") || n.Contains("account") || n.Contains("later"))
                    ? "pack://application:,,,/Assets/pay-later.png"
                    : "pack://application:,,,/Assets/card-bank.png";

            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(asset, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null;
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}