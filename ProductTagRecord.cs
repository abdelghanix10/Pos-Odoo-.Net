using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace OdooPosLogin;

public class ProductTagRecord : INotifyPropertyChanged
{
    private long _id;
    private int _odooId;
    private string _name = string.Empty;
    private int _colorIndex = 0;
    private int _productsCount = 0;

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

    public int ColorIndex
    {
        get => _colorIndex;
        set
        {
            if (_colorIndex != value)
            {
                _colorIndex = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ColorHex));
                OnPropertyChanged(nameof(ColorBrush));
                OnPropertyChanged(nameof(SoftBackgroundBrush));
                OnPropertyChanged(nameof(ColorNameDisplay));
            }
        }
    }

    public int ProductsCount
    {
        get => _productsCount;
        set
        {
            if (_productsCount != value)
            {
                _productsCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ProductsCountDisplay));
            }
        }
    }

    public string ColorHex => PosCategoryRecord.GetOdooColorHex(ColorIndex);

    public Brush ColorBrush
    {
        get
        {
            try
            {
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(ColorHex));
            }
            catch
            {
                return new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));
            }
        }
    }

    public Brush SoftBackgroundBrush
    {
        get
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(ColorHex);
                // 15% opacity tint for tag pill background
                return new SolidColorBrush(Color.FromArgb(38, color.R, color.G, color.B));
            }
            catch
            {
                return new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6));
            }
        }
    }

    public string ColorNameDisplay => ColorIndex == 0 ? "Default Gray" : $"Color {ColorIndex}";
    public string ProductsCountDisplay => $"{ProductsCount} Products";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
}
