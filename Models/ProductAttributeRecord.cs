using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OdooPosLogin;

public class ProductAttributeRecord : INotifyPropertyChanged
{
    private long _id;
    private int _odooId;
    private string _name = string.Empty;
    private string _displayType = "radio";
    private string _createVariant = "always";
    private int _numberRelatedProducts = 0;
    private List<ProductAttributeValueRecord> _values = [];

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

    public string DisplayType
    {
        get => _displayType;
        set
        {
            if (_displayType != value)
            {
                _displayType = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayTypeFormatted));
            }
        }
    }

    public string CreateVariant
    {
        get => _createVariant;
        set
        {
            if (_createVariant != value)
            {
                _createVariant = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CreateVariantFormatted));
                OnPropertyChanged(nameof(IsCreateInstantly));
                OnPropertyChanged(nameof(IsCreateDynamically));
                OnPropertyChanged(nameof(IsCreateNever));
            }
        }
    }

    public int NumberRelatedProducts
    {
        get => _numberRelatedProducts;
        set
        {
            if (_numberRelatedProducts != value)
            {
                _numberRelatedProducts = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ProductsCountDisplay));
            }
        }
    }

    public List<ProductAttributeValueRecord> Values
    {
        get => _values;
        set { if (_values != value) { _values = value; OnPropertyChanged(); OnPropertyChanged(nameof(ValuesCount)); } }
    }

    public int ValuesCount => Values.Count;
    public string ProductsCountDisplay => $"{NumberRelatedProducts} Products";

    public string DisplayTypeFormatted => DisplayType?.ToLowerInvariant() switch
    {
        "radio" => "Radio",
        "pills" => "Pills",
        "select" => "Select",
        "color" => "Color",
        "multi" => "Multi-checkbox",
        _ => string.IsNullOrWhiteSpace(DisplayType) ? "Radio" : char.ToUpperInvariant(DisplayType[0]) + DisplayType[1..]
    };

    public string CreateVariantFormatted => CreateVariant?.ToLowerInvariant() switch
    {
        "always" => "Instantly",
        "dynamic" => "Dynamically",
        "no_variant" => "Never",
        _ => "Instantly"
    };

    public bool IsCreateInstantly => (CreateVariant?.ToLowerInvariant() ?? "always") == "always";
    public bool IsCreateDynamically => (CreateVariant?.ToLowerInvariant() ?? "") == "dynamic";
    public bool IsCreateNever => (CreateVariant?.ToLowerInvariant() ?? "") == "no_variant";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
}

public class ProductAttributeValueRecord : INotifyPropertyChanged
{
    private long _id;
    private int _odooId;
    private int _attributeOdooId;
    private string _name = string.Empty;
    private bool _isCustom = false;
    private string? _htmlColor;
    private string? _imageBase64;
    private BitmapImage? _imageBitmap;
    private double _defaultExtraPrice = 0.0;

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

    public int AttributeOdooId
    {
        get => _attributeOdooId;
        set { if (_attributeOdooId != value) { _attributeOdooId = value; OnPropertyChanged(); } }
    }

    public string Name
    {
        get => _name;
        set { if (_name != value) { _name = value; OnPropertyChanged(); } }
    }

    public bool IsCustom
    {
        get => _isCustom;
        set { if (_isCustom != value) { _isCustom = value; OnPropertyChanged(); OnPropertyChanged(nameof(CustomTextDisplay)); } }
    }

    public string? HtmlColor
    {
        get => _htmlColor;
        set
        {
            if (_htmlColor != value)
            {
                _htmlColor = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ColorBrush));
                OnPropertyChanged(nameof(HasColor));
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
                OnPropertyChanged();
                ImageBitmap = PosImageUtils.Base64ToBitmapImage(value);
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

    public double DefaultExtraPrice
    {
        get => _defaultExtraPrice;
        set
        {
            if (Math.Abs(_defaultExtraPrice - value) > 0.001)
            {
                _defaultExtraPrice = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PriceExtraDisplay));
            }
        }
    }

    public Brush ColorBrush
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(HtmlColor))
            {
                try
                {
                    string hex = HtmlColor.Trim();
                    if (!hex.StartsWith("#")) hex = "#" + hex;
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
                }
                catch { }
            }
            return new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF));
        }
    }

    public bool HasColor => !string.IsNullOrWhiteSpace(HtmlColor) && HtmlColor.Trim() != "false";
    public bool HasImage => ImageBitmap != null;
    public bool HasNoImage => ImageBitmap == null;
    public string PriceExtraDisplay => DefaultExtraPrice.ToString("F2");
    public string CustomTextDisplay => IsCustom ? "Yes" : "No";

    public Visibility CustomCheckVisibility => IsCustom ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ColorVisibility => HasColor ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoColorVisibility => HasColor ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ImageVisibility => HasImage ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoImageVisibility => HasNoImage ? Visibility.Visible : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
}
