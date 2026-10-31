using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OdooPosLogin;

public class ComboChoiceLineRecord : INotifyPropertyChanged
{
    private long _id;
    private int? _odooId;
    private int? _comboId;
    private int? _productId;
    private string _productName = string.Empty;
    private decimal _originalPrice = 0.00m;
    private decimal _extraPrice = 0.00m;

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

    public int? ComboId
    {
        get => _comboId;
        set { if (_comboId != value) { _comboId = value; OnPropertyChanged(); } }
    }

    public int? ProductId
    {
        get => _productId;
        set { if (_productId != value) { _productId = value; OnPropertyChanged(); } }
    }

    public string ProductName
    {
        get => _productName;
        set { if (_productName != value) { _productName = value; OnPropertyChanged(); } }
    }

    public decimal OriginalPrice
    {
        get => _originalPrice;
        set
        {
            if (_originalPrice != value)
            {
                _originalPrice = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(OriginalPriceDisplay));
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
                OnPropertyChanged(nameof(ExtraPriceDisplay));
            }
        }
    }

    public string OriginalPriceDisplay => $"$ {OriginalPrice:N2}";
    public string ExtraPriceDisplay => ExtraPrice > 0 ? $"+ $ {ExtraPrice:N2}" : "$ 0.00";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class ComboChoiceRecord : INotifyPropertyChanged
{
    private long _id;
    private int? _odooId;
    private string _name = string.Empty;

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

    public ObservableCollection<ComboChoiceLineRecord> Lines { get; } = [];

    public int ComboItemsCount => Lines.Count;
    public string ComboItemsCountDisplay => $"{Lines.Count} {(Lines.Count == 1 ? "item" : "items")}";

    public List<string> IncludedProductNames => Lines.Select(l => l.ProductName).ToList();

    public void NotifyLinesChanged()
    {
        OnPropertyChanged(nameof(ComboItemsCount));
        OnPropertyChanged(nameof(ComboItemsCountDisplay));
        OnPropertyChanged(nameof(IncludedProductNames));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
