using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace OdooPosLogin;

public class SelectableTagItem : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private int? _odooId;
    private bool _isSelected;

    public string Name
    {
        get => _name;
        set { if (_name != value) { _name = value; OnPropertyChanged(); } }
    }

    public int? OdooId
    {
        get => _odooId;
        set { if (_odooId != value) { _odooId = value; OnPropertyChanged(); } }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
}

public class PosVariantOptionItem : INotifyPropertyChanged
{
    private string _attributeName = string.Empty;
    private string _valueName = string.Empty;
    private string? _htmlColor;
    private decimal _extraPrice;
    private bool _isCustom;
    private string _customValueText = string.Empty;
    private bool _isSelected;

    public PosVariantAttributeGroup? ParentGroup { get; set; }

    public string AttributeName
    {
        get => _attributeName;
        set { if (_attributeName != value) { _attributeName = value; OnPropertyChanged(); } }
    }

    public string ValueName
    {
        get => _valueName;
        set { if (_valueName != value) { _valueName = value; OnPropertyChanged(); } }
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
                OnPropertyChanged(nameof(HasColor));
                OnPropertyChanged(nameof(ColorVisibility));
                OnPropertyChanged(nameof(ColorBrush));
            }
        }
    }

    public decimal ExtraPrice
    {
        get => _extraPrice;
        set
        {
            if (_extraPrice != value)
            {
                _extraPrice = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasExtraPrice));
                OnPropertyChanged(nameof(ExtraPriceVisibility));
                OnPropertyChanged(nameof(ExtraPriceDisplay));
            }
        }
    }

    public bool IsCustom
    {
        get => _isCustom;
        set
        {
            if (_isCustom != value)
            {
                _isCustom = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowCustomInputVisibility));
            }
        }
    }

    public string CustomValueText
    {
        get => _customValueText;
        set { if (_customValueText != value) { _customValueText = value; OnPropertyChanged(); OnPropertyChanged(nameof(CustomTextValue)); } }
    }

    public string CustomTextValue
    {
        get => _customValueText;
        set => CustomValueText = value;
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowCustomInputVisibility));
            }
        }
    }

    public bool HasColor => !string.IsNullOrWhiteSpace(HtmlColor) && !string.Equals(HtmlColor.Trim(), "false", StringComparison.OrdinalIgnoreCase);
    public bool HasColorSwatch => HasColor;
    public Visibility ColorVisibility => HasColor ? Visibility.Visible : Visibility.Collapsed;

    public Brush SwatchBrush => ColorBrush;

    public Brush ColorBrush
    {
        get
        {
            if (HasColor)
            {
                try
                {
                    string hex = HtmlColor!.Trim();
                    if (!hex.StartsWith('#')) hex = "#" + hex;
                    var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
                    b.Freeze();
                    return b;
                }
                catch { }
            }
            return Brushes.Transparent;
        }
    }

    public bool HasExtraPrice => ExtraPrice > 0.001m;
    public Visibility ExtraPriceVisibility => HasExtraPrice ? Visibility.Visible : Visibility.Collapsed;
    public string ExtraPriceDisplay => HasExtraPrice ? $"+ $ {ExtraPrice:N2}" : string.Empty;

    public Visibility ShowCustomInputVisibility => (IsSelected && (IsCustom || string.Equals(ValueName, "Custom", StringComparison.OrdinalIgnoreCase)))
        ? Visibility.Visible
        : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
}

public class PosVariantAttributeGroup : INotifyPropertyChanged
{
    private string _attributeName = string.Empty;
    private string _displayType = "pills";

    public string AttributeName
    {
        get => _attributeName;
        set { if (_attributeName != value) { _attributeName = value; OnPropertyChanged(); } }
    }

    public string DisplayType
    {
        get => _displayType;
        set { if (_displayType != value) { _displayType = value; OnPropertyChanged(); } }
    }

    public bool IsMultiSelect => string.Equals(DisplayType, "multi", StringComparison.OrdinalIgnoreCase);

    public ObservableCollection<PosVariantOptionItem> Options { get; } = [];

    public PosVariantOptionItem? SelectedOption => Options.FirstOrDefault(o => o.IsSelected);

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
}
