using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OdooPosLogin;

public class PosCategoryRecord : INotifyPropertyChanged
{
    private long _id;
    private int? _odooId;
    private string _name = string.Empty;
    private int? _parentCategoryId;
    private string? _parentCategoryName;
    private string? _linkedPosConfigs;
    private int _colorIndex = 0;
    private string _colorHex = "#9CA3AF";
    private int _sequence = 10;
    private string? _imageBase64;
    private BitmapImage? _imageBitmap;

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

    public int? ParentCategoryId
    {
        get => _parentCategoryId;
        set { if (_parentCategoryId != value) { _parentCategoryId = value; OnPropertyChanged(); } }
    }

    public string? ParentCategoryName
    {
        get => _parentCategoryName;
        set
        {
            if (_parentCategoryName != value)
            {
                _parentCategoryName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ParentCategoryDisplay));
            }
        }
    }

    public string? LinkedPosConfigs
    {
        get => _linkedPosConfigs;
        set
        {
            if (_linkedPosConfigs != value)
            {
                _linkedPosConfigs = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LinkedShopsList));
            }
        }
    }

    public int ColorIndex
    {
        get => _colorIndex;
        set
        {
            if (_colorIndex != value)
            {
                _colorIndex = value;
                ColorHex = GetOdooColorHex(value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(ColorBrush));
                OnPropertyChanged(nameof(ColorNameDisplay));
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
                OnPropertyChanged(nameof(ColorNameDisplay));
            }
        }
    }

    public int Sequence
    {
        get => _sequence;
        set { if (_sequence != value) { _sequence = value; OnPropertyChanged(); } }
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

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public Brush ColorBrush
    {
        get
        {
            try
            {
                string hex = string.IsNullOrWhiteSpace(ColorHex) ? GetOdooColorHex(ColorIndex) : ColorHex;
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            }
            catch
            {
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9CA3AF"));
            }
        }
    }

    /// <summary>
    /// Soft pastel background color matching Odoo 20 POS category buttons
    /// </summary>
    public Brush CategoryButtonBgBrush
    {
        get
        {
            try
            {
                string hex = GetOdooSubtleBgHex(ColorIndex);
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
                brush.Freeze();
                return brush;
            }
            catch
            {
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E9ECEF"));
            }
        }
    }

    /// <summary>
    /// High-contrast text color matching Odoo 20 category buttons
    /// </summary>
    public Brush CategoryButtonFgBrush
    {
        get
        {
            try
            {
                string hex = GetOdooSubtleFgHex(ColorIndex);
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
                brush.Freeze();
                return brush;
            }
            catch
            {
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#212529"));
            }
        }
    }

    /// <summary>
    /// 2px border color for active category button matching Odoo 20
    /// </summary>
    public Brush CategoryActiveBorderBrush
    {
        get
        {
            try
            {
                string hex = GetOdooActiveBorderHex(ColorIndex);
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
                brush.Freeze();
                return brush;
            }
            catch
            {
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#71639E"));
            }
        }
    }

    public List<string> LinkedShopsList
    {
        get
        {
            if (string.IsNullOrWhiteSpace(LinkedPosConfigs)) return [];
            return LinkedPosConfigs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }
    }

    public string ParentCategoryDisplay => string.IsNullOrWhiteSpace(ParentCategoryName) ? "—" : ParentCategoryName;
    public string ColorNameDisplay => ColorIndex == 0 ? "Default Gray" : $"Color {ColorIndex} ({ColorHex})";
    public bool HasImage => ImageBitmap != null;
    public bool HasNoImage => ImageBitmap == null;

    /// <summary>
    /// Odoo 20 product card bottom border accent colors
    /// </summary>
    public static string GetOdooColorHex(int colorIndex) => colorIndex switch
    {
        1 => "#EE2D2D",  // Color 1 (Chairs)
        2 => "#EE5C2D",  // Color 2
        3 => "#DC8534",  // Color 3 (Desks)
        4 => "#E8BB1D",  // Color 4
        5 => "#5794DD",  // Color 5
        6 => "#9F628F",  // Color 6
        7 => "#DB8865",  // Color 7
        8 => "#41A9A2",  // Color 8
        9 => "#F772B2",  // Color 9 (Misc)
        10 => "#EE2F8A", // Color 10
        11 => "#61C36E", // Color 11
        12 => "#9872E6", // Color 12
        _ => "#CCCCCC"   // Default gray
    };

    /// <summary>
    /// Odoo 20 pastel category button background colors (from exact HTML export)
    /// </summary>
    public static string GetOdooSubtleBgHex(int colorIndex) => colorIndex switch
    {
        1 => "#FF9C9C",  // Chairs (red/salmon)
        2 => "#FFBA9C",  // Orange pastel
        3 => "#FDE388",  // Desks (yellow)
        4 => "#FDF188",  // Light yellow
        5 => "#A8CEF7",  // Light blue
        6 => "#E2B8DA",  // Lavender
        7 => "#F7C2AC",  // Warm Peach
        8 => "#A6E4DF",  // Teal / Mint
        9 => "#FF9ECC",  // Misc (pink)
        10 => "#FF9EC2", // Magenta pastel
        11 => "#B0ECC6", // Green pastel
        12 => "#CAB5F7", // Purple pastel
        _ => "#E9ECEF"   // Default subtle gray
    };

    /// <summary>
    /// Odoo 20 category button text colors (from exact HTML export)
    /// </summary>
    public static string GetOdooSubtleFgHex(int colorIndex) => colorIndex switch
    {
        1 => "#430C0C",
        2 => "#431A0C",
        3 => "#2F270A",
        4 => "#302E0A",
        5 => "#102645",
        6 => "#3B1334",
        7 => "#431D0E",
        8 => "#0C3B37",
        9 => "#450C27",
        10 => "#450C20",
        11 => "#0E3D1D",
        12 => "#1F1145",
        _ => "#212529"
    };

    /// <summary>
    /// Odoo 20 active category button 2px border colors (from exact HTML export)
    /// </summary>
    public static string GetOdooActiveBorderHex(int colorIndex) => colorIndex switch
    {
        1 => "#A70E0E",
        2 => "#A73C0E",
        3 => "#90740F",
        4 => "#8E820F",
        5 => "#18569F",
        6 => "#7F236E",
        7 => "#9F441F",
        8 => "#1A7B74",
        9 => "#A90E58",
        10 => "#AA0E48",
        11 => "#1D823E",
        12 => "#4925A0",
        _ => "#71639E"
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}