using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OdooPosLogin;

public class ProductTagBadge
{
    public string Name { get; set; } = string.Empty;
    public Brush ColorBrush { get; set; } = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#017E84"));
    public Brush SoftBackgroundBrush { get; set; } = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E6F4F5"));
}

public class ProductAttributeLine
{
    public string AttributeName { get; set; } = string.Empty;
    public string ValuesSummary { get; set; } = string.Empty;
}

public class ProductComboLine
{
    public string ComboName { get; set; } = string.Empty;
    public string ItemsCountSummary { get; set; } = string.Empty;
}

public class ProductRecord : INotifyPropertyChanged
{
    private long _id;
    private int? _odooId;
    private string _name = string.Empty;
    private string _productType = "consu"; // "consu" (Goods), "service" (Service), "combo" (Combo)
    private decimal _listPrice = 0.00m;
    private decimal _standardPrice = 0.00m;
    private string _defaultCode = string.Empty;
    private string _barcode = string.Empty;
    private int? _posCategoryId;
    private string? _posCategoryName;
    private int _variantCount = 1;
    private string? _imageBase64;
    private BitmapImage? _imageBitmap;
    private string? _tagsJson;
    private string? _attributesJson;
    private string? _comboChoicesJson;
    private int _cartQuantity = 0;
    private string? _categoryColorHex = "#017E84";

    public int CartQuantity
    {
        get => _cartQuantity;
        set
        {
            if (_cartQuantity != value)
            {
                _cartQuantity = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasCartQuantity));
            }
        }
    }

    public bool HasCartQuantity => _cartQuantity > 0;

    public string? CategoryColorHex
    {
        get => _categoryColorHex;
        set
        {
            if (_categoryColorHex != value)
            {
                _categoryColorHex = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CategoryColorBrush));
            }
        }
    }

    public Brush CategoryColorBrush
    {
        get
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_categoryColorHex))
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString(_categoryColorHex));
            }
            catch { }
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#017E84"));
        }
    }

    public long Id
    {
        get => _id;
        set { if (_id != value) { _id = value; OnPropertyChanged(); } }
    }

    public int? OdooId
    {
        get => _odooId;
        set { if (_odooId != value) { _odooId = value; OnPropertyChanged(); } }
    }

    public string Name
    {
        get => _name;
        set { if (_name != value) { _name = value; OnPropertyChanged(); } }
    }

    public string ProductType
    {
        get => _productType;
        set
        {
            if (_productType != value)
            {
                _productType = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ProductTypeDisplay));
                OnPropertyChanged(nameof(IsCombo));
            }
        }
    }

    public decimal ListPrice
    {
        get => _listPrice;
        set
        {
            if (_listPrice != value)
            {
                _listPrice = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PriceDisplay));
            }
        }
    }

    public decimal StandardPrice
    {
        get => _standardPrice;
        set
        {
            if (_standardPrice != value)
            {
                _standardPrice = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CostDisplay));
            }
        }
    }

    public string DefaultCode
    {
        get => _defaultCode;
        set
        {
            if (_defaultCode != value)
            {
                _defaultCode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SkuDisplay));
                OnPropertyChanged(nameof(HasSku));
            }
        }
    }

    public string Barcode
    {
        get => _barcode;
        set
        {
            if (_barcode != value)
            {
                _barcode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BarcodeDisplay));
            }
        }
    }

    public int? PosCategoryId
    {
        get => _posCategoryId;
        set { if (_posCategoryId != value) { _posCategoryId = value; OnPropertyChanged(); } }
    }

    public string? PosCategoryName
    {
        get => _posCategoryName;
        set
        {
            if (_posCategoryName != value)
            {
                _posCategoryName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CategoryDisplay));
            }
        }
    }

    public int VariantCount
    {
        get => _variantCount;
        set
        {
            if (_variantCount != value)
            {
                _variantCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasMultipleVariants));
                OnPropertyChanged(nameof(VariantCountDisplay));
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

    public string? TagsJson
    {
        get => _tagsJson;
        set
        {
            if (_tagsJson != value)
            {
                _tagsJson = value;
                OnPropertyChanged();
                ParseTags();
            }
        }
    }

    public string? AttributesJson
    {
        get => _attributesJson;
        set
        {
            if (_attributesJson != value)
            {
                _attributesJson = value;
                OnPropertyChanged();
                ParseAttributes();
            }
        }
    }

    public string? ComboChoicesJson
    {
        get => _comboChoicesJson;
        set
        {
            if (_comboChoicesJson != value)
            {
                _comboChoicesJson = value;
                OnPropertyChanged();
                ParseComboChoices();
            }
        }
    }

    // ==================== COMPUTED & DISPLAY PROPERTIES ====================
    public bool HasMultipleVariants => VariantCount > 1;
    public string VariantCountDisplay => $"{VariantCount} Variants";
    public string PriceDisplay => $"$ {ListPrice:N2}";
    public string CostDisplay => $"$ {StandardPrice:N2}";
    public string SkuDisplay => string.IsNullOrWhiteSpace(DefaultCode) ? string.Empty : DefaultCode;
    public bool HasSku => !string.IsNullOrWhiteSpace(DefaultCode);
    public string BarcodeDisplay => string.IsNullOrWhiteSpace(Barcode) ? "—" : Barcode;
    public string CategoryDisplay => string.IsNullOrWhiteSpace(PosCategoryName) ? "—" : PosCategoryName;
    public bool HasImage => ImageBitmap != null;
    public bool HasNoImage => ImageBitmap == null;
    public bool IsCombo => string.Equals(ProductType, "combo", StringComparison.OrdinalIgnoreCase);

    public string ProductTypeDisplay => ProductType?.ToLowerInvariant() switch
    {
        "combo" => "Combo",
        "service" => "Service",
        _ => "Goods"
    };

    public ObservableCollection<ProductTagBadge> TagBadges { get; } = [];
    public ObservableCollection<ProductAttributeLine> AttributeLines { get; } = [];
    public ObservableCollection<ProductComboLine> ComboLines { get; } = [];

    public void ParseTags()
    {
        TagBadges.Clear();
        if (string.IsNullOrWhiteSpace(_tagsJson)) return;

        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(_tagsJson);
            if (list != null)
            {
                int i = 0;
                foreach (var tag in list)
                {
                    int colorIdx = (i % 6) + 1;
                    string hex = PosCategoryRecord.GetOdooColorHex(colorIdx);
                    TagBadges.Add(new ProductTagBadge
                    {
                        Name = tag,
                        ColorBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                        SoftBackgroundBrush = new SolidColorBrush(Color.FromArgb(24, 1, 126, 132))
                    });
                    i++;
                }
            }
        }
        catch
        {
            // Fallback comma-split
            var parts = _tagsJson.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            int i = 0;
            foreach (var p in parts)
            {
                int colorIdx = (i % 6) + 1;
                string hex = PosCategoryRecord.GetOdooColorHex(colorIdx);
                TagBadges.Add(new ProductTagBadge
                {
                    Name = p,
                    ColorBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                    SoftBackgroundBrush = new SolidColorBrush(Color.FromArgb(24, 113, 75, 103))
                });
                i++;
            }
        }
    }

    public void ParseAttributes()
    {
        AttributeLines.Clear();
        if (string.IsNullOrWhiteSpace(_attributesJson)) return;

        try
        {
            using var doc = JsonDocument.Parse(_attributesJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    string attr = el.TryGetProperty("AttributeName", out var a) ? a.GetString() ?? "" : "";
                    string vals = el.TryGetProperty("Values", out var v) ? v.GetString() ?? "" : "";
                    if (!string.IsNullOrEmpty(attr))
                    {
                        AttributeLines.Add(new ProductAttributeLine
                        {
                            AttributeName = attr,
                            ValuesSummary = vals
                        });
                    }
                }
            }
        }
        catch { }
    }

    public void ParseComboChoices()
    {
        ComboLines.Clear();
        if (string.IsNullOrWhiteSpace(_comboChoicesJson)) return;

        try
        {
            using var doc = JsonDocument.Parse(_comboChoicesJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    string combo = el.TryGetProperty("ComboName", out var c) ? c.GetString() ?? "" : "";
                    string count = el.TryGetProperty("ItemsCount", out var cnt) ? cnt.GetString() ?? "" : "";
                    if (!string.IsNullOrEmpty(combo))
                    {
                        ComboLines.Add(new ProductComboLine
                        {
                            ComboName = combo,
                            ItemsCountSummary = count
                        });
                    }
                }
            }
        }
        catch { }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
