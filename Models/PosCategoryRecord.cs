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
    private BitmapSource? _imageBitmap;

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
                ImageBitmap = PosImageUtils.Base64ToBitmapSource(value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasImage));
                OnPropertyChanged(nameof(HasNoImage));
            }
        }
    }

    public BitmapSource? ImageBitmap
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
                return BrushCache.Get(hex);
            }
            catch
            {
                return BrushCache.Get("#9CA3AF");
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
                return BrushCache.Get(hex);
            }
            catch
            {
                return BrushCache.Get("#E9ECEF");
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
                return BrushCache.Get(hex);
            }
            catch
            {
                return BrushCache.Get("#212529");
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
                return BrushCache.Get(hex);
            }
            catch
            {
                return BrushCache.Get("#71639E");
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
        1 => "#FAA0A0",
        2 => "#F7C698",
        3 => "#FDE388",
        4 => "#BBD7F8",
        5 => "#D9A8CC",
        6 => "#F5D7CB",
        7 => "#A6E4DF",
        8 => "#97A6F9",
        9 => "#FF9ECC",
        10 => "#B7EDBE",
        11 => "#CAB5F7",
        12 => "#D4E4BC",
        _ => "#DEE2E6"
    };

    /// <summary>
    /// Odoo 20 pastel category button background colors (from exact HTML export)
    /// </summary>
    public static string GetOdooSubtleBgHex(int colorIndex) => colorIndex switch
    {
        1 => "#FF9C9C",  // Chairs (red/salmon)
        2 => "#F7C698",  // Pâtisserie
        3 => "#FDE388",  // Desks (yellow)
        4 => "#BBD7F8",  // Supplements
        5 => "#D9A8CC",  // PDJ/Omelete
        6 => "#F8D6C8",  // Premium
        7 => "#A6E4DF",  // Mint teal
        8 => "#97A6F9",  // Jus
        9 => "#FF9ECC",  // Misc (pink)
        10 => "#B7EDBE", // Tranches/Danone
        11 => "#CAB5F7", // Lavender
        12 => "#D4E4BC", // Sage
        _ => "#FFFFFF"   // Default
    };

    /// <summary>
    /// Odoo 20 category button text colors (from exact HTML export)
    /// </summary>
    public static string GetOdooSubtleFgHex(int colorIndex) => colorIndex switch
    {
        1 => "#430C0C",
        2 => "#35210F",
        3 => "#2F270A",
        4 => "#19324F",
        5 => "#1D181C",
        6 => "#552F1F",
        7 => "#0C3B37",
        8 => "#0E1436",
        9 => "#450C27",
        10 => "#1F3922",
        11 => "#1F1145",
        12 => "#212529",
        _ => "#212529"
    };

    /// <summary>
    /// Odoo 20 active category button 2px border colors (from exact HTML export)
    /// </summary>
    public static string GetOdooActiveBorderHex(int colorIndex) => colorIndex switch
    {
        1 => "#A70E0E",
        2 => "#915319",
        3 => "#90740F",
        4 => "#2361AB",
        5 => "#603B56",
        6 => "#B15129",
        7 => "#1A7B74",
        8 => "#162994",
        9 => "#A90E58",
        10 => "#348A40",
        11 => "#4925A0",
        12 => "#5C7A33",
        _ => "#71639E"
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}