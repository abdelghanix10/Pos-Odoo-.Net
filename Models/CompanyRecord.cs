using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OdooPosLogin;

public class CompanyRecord : INotifyPropertyChanged
{
    private long _id;
    private int _odooId;
    private string _name = string.Empty;
    private string _street = string.Empty;
    private string _street2 = string.Empty;
    private string _city = string.Empty;
    private string _stateName = string.Empty;
    private string _zip = string.Empty;
    private string _countryName = string.Empty;
    private string _vat = string.Empty;
    private string _ice = string.Empty;
    private string _currencyName = "MAD";
    private string _phone = string.Empty;
    private string _email = string.Empty;
    private string _website = string.Empty;
    private string _emailDomain = string.Empty;
    private int _colorIndex = 2;
    private string _colorHex = "#D97736";
    private int? _parentId;
    private string _parentName = string.Empty;
    private string? _logoBase64;
    private BitmapImage? _logoBitmap;

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

    public string Street
    {
        get => _street;
        set
        {
            if (_street != value)
            {
                _street = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AddressLineDisplay));
            }
        }
    }

    public string Street2
    {
        get => _street2;
        set
        {
            if (_street2 != value)
            {
                _street2 = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Street2Display));
                OnPropertyChanged(nameof(IsStreet2Placeholder));
            }
        }
    }

    public string City
    {
        get => _city;
        set
        {
            if (_city != value)
            {
                _city = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AddressLineDisplay));
            }
        }
    }

    public string StateName
    {
        get => _stateName;
        set
        {
            if (_stateName != value)
            {
                _stateName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StateDisplay));
                OnPropertyChanged(nameof(IsStatePlaceholder));
            }
        }
    }

    public string Zip
    {
        get => _zip;
        set { if (_zip != value) { _zip = value; OnPropertyChanged(); } }
    }

    public string CountryName
    {
        get => _countryName;
        set
        {
            if (_countryName != value)
            {
                _countryName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AddressLineDisplay));
            }
        }
    }

    public string Vat
    {
        get => _vat;
        set { if (_vat != value) { _vat = value; OnPropertyChanged(); } }
    }

    public string Ice
    {
        get => _ice;
        set { if (_ice != value) { _ice = value; OnPropertyChanged(); } }
    }

    public string CurrencyName
    {
        get => _currencyName;
        set { if (_currencyName != value) { _currencyName = value; OnPropertyChanged(); } }
    }

    public string Phone
    {
        get => _phone;
        set
        {
            if (_phone != value)
            {
                _phone = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasPhone));
                OnPropertyChanged(nameof(PhoneVisibility));
            }
        }
    }

    public string Email
    {
        get => _email;
        set
        {
            if (_email != value)
            {
                _email = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasEmail));
                OnPropertyChanged(nameof(EmailVisibility));
            }
        }
    }

    public string Website
    {
        get => _website;
        set { if (_website != value) { _website = value; OnPropertyChanged(); } }
    }

    public string EmailDomain
    {
        get => _emailDomain;
        set { if (_emailDomain != value) { _emailDomain = value; OnPropertyChanged(); } }
    }

    public int ColorIndex
    {
        get => _colorIndex;
        set
        {
            if (_colorIndex != value)
            {
                _colorIndex = value;
                if (value > 0)
                {
                    ColorHex = PosCategoryRecord.GetOdooColorHex(value);
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(ColorBrush));
            }
        }
    }

    public string ColorHex
    {
        get => _colorHex;
        set
        {
            if (_colorHex != value)
            {
                _colorHex = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ColorBrush));
            }
        }
    }

    public int? ParentId
    {
        get => _parentId;
        set { if (_parentId != value) { _parentId = value; OnPropertyChanged(); } }
    }

    public string ParentName
    {
        get => _parentName;
        set { if (_parentName != value) { _parentName = value; OnPropertyChanged(); } }
    }

    public string? LogoBase64
    {
        get => _logoBase64;
        set
        {
            if (_logoBase64 != value)
            {
                _logoBase64 = value;
                LogoBitmap = PosImageUtils.Base64ToBitmapImage(value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasLogo));
                OnPropertyChanged(nameof(HasNoLogo));
            }
        }
    }

    public BitmapImage? LogoBitmap
    {
        get => _logoBitmap;
        set
        {
            if (_logoBitmap != value)
            {
                _logoBitmap = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasLogo));
                OnPropertyChanged(nameof(HasNoLogo));
            }
        }
    }

    public ObservableCollection<CompanyRecord> Branches { get; } = [];

    // ==================== COMPUTED & DISPLAY PROPERTIES ====================
    public bool HasLogo => LogoBitmap != null;
    public bool HasNoLogo => LogoBitmap == null;

    public bool HasEmail => !string.IsNullOrWhiteSpace(Email);
    public bool HasPhone => !string.IsNullOrWhiteSpace(Phone);

    public Visibility EmailVisibility => HasEmail ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PhoneVisibility => HasPhone ? Visibility.Visible : Visibility.Collapsed;

    public string Street2Display => string.IsNullOrWhiteSpace(Street2) ? "Street 2..." : Street2;
    public bool IsStreet2Placeholder => string.IsNullOrWhiteSpace(Street2);

    public string StateDisplay => string.IsNullOrWhiteSpace(StateName) ? "State" : StateName;
    public bool IsStatePlaceholder => string.IsNullOrWhiteSpace(StateName);

    public string AddressLineDisplay
    {
        get
        {
            var parts = new[] { Street, City, CountryName }
                .Where(s => !string.IsNullOrWhiteSpace(s));
            string joined = string.Join(", ", parts);
            return string.IsNullOrWhiteSpace(joined) ? "—" : joined;
        }
    }

    public Brush ColorBrush
    {
        get
        {
            try
            {
                string hex = string.IsNullOrWhiteSpace(ColorHex) ? "#D97736" : ColorHex.Trim();
                if (!hex.StartsWith('#')) hex = "#" + hex;
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
                brush.Freeze();
                return brush;
            }
            catch
            {
                return new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x36));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}