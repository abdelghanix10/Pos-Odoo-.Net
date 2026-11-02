using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OdooPosLogin;

public class PosCartItem : INotifyPropertyChanged
{
    private ProductRecord _product;
    private int _quantity = 1;
    private decimal _unitPrice;
    private decimal _discount = 0.0m;
    private bool _isSelected;

    public PosCartItem(ProductRecord product, int quantity = 1)
    {
        _product = product;
        _quantity = quantity;
        _unitPrice = product.ListPrice;
    }

    public ProductRecord Product
    {
        get => _product;
        set { _product = value; OnPropertyChanged(); }
    }

    public string ProductName => _product.Name;

    public int Quantity
    {
        get => _quantity;
        set
        {
            if (_quantity != value)
            {
                _quantity = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LineTotal));
                OnPropertyChanged(nameof(LineTotalDisplay));
                OnPropertyChanged(nameof(QuantityDisplay));
            }
        }
    }

    public decimal UnitPrice
    {
        get => _unitPrice;
        set
        {
            if (_unitPrice != value)
            {
                _unitPrice = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LineTotal));
                OnPropertyChanged(nameof(LineTotalDisplay));
            }
        }
    }

    public decimal Discount
    {
        get => _discount;
        set
        {
            if (_discount != value)
            {
                _discount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LineTotal));
                OnPropertyChanged(nameof(LineTotalDisplay));
            }
        }
    }

    public decimal LineTotal => Math.Round(Quantity * UnitPrice * (1.0m - (Discount / 100.0m)), 2);

    public string LineTotalDisplay => $"$ {LineTotal:N2}";
    public string QuantityDisplay => Quantity.ToString();

    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
