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

    public List<string> LinkedShopsList
    {
        get
        {
            if (string.IsNullOrWhiteSpace(LinkedPosConfigs)) return [];
            return LinkedPosConfigs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }
    }

    public string ParentCategoryDisplay => string.IsNullOrWhiteSpace(ParentCategoryName) ? "—" : ParentCategoryName;
    public bool HasImage => ImageBitmap != null;
    public bool HasNoImage => ImageBitmap == null;

    public static string GetOdooColorHex(int colorIndex) => colorIndex switch
    {
        1 => "#F06050",  // Red
        2 => "#F4A460",  // Orange
        3 => "#F7CD1F",  // Yellow
        4 => "#6CC1ED",  // Light Blue
        5 => "#814968",  // Dark Purple
        6 => "#EB7E7F",  // Salmon
        7 => "#2C8397",  // Teal
        8 => "#475577",  // Blue
        9 => "#D6145F",  // Magenta / Deep Pink
        10 => "#30C381", // Green
        11 => "#9365B8", // Purple
        _ => "#9CA3AF"   // Default / Gray
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
