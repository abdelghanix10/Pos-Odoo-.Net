using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace OdooPosLogin;

public class PosCartItem : INotifyPropertyChanged
{
    private ProductRecord _product;
    private decimal _quantity = 1m;
    private decimal _unitPrice;
    private decimal _taxRate;
    private decimal _discount = 0.0m;
    private string _customerNote = string.Empty;
    private bool _isSelected;
    private bool _isUnitPriceTaxIncluded;

    public PosCartItem(ProductRecord product, decimal quantity = 1m, bool unitPriceIsTaxIncluded = false)
    {
        _product = product;
        _quantity = quantity;
        _unitPrice = product.ListPrice;
        _taxRate = product.SalesTaxRate >= 0m ? product.SalesTaxRate : 15.0m;
        _isUnitPriceTaxIncluded = unitPriceIsTaxIncluded;
    }

    public ProductRecord Product
    {
        get => _product;
        set { _product = value; OnPropertyChanged(); }
    }

    public int? ProductOdooId => _product.OdooId;
    public string ProductName => _product.Name;

    public decimal Quantity
    {
        get => _quantity;
        set
        {
            if (_quantity != value)
            {
                _quantity = value;
                OnPropertyChanged();
                NotifyAmountsChanged();
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
                NotifyAmountsChanged();
            }
        }
    }

    public decimal TaxRate
    {
        get => _taxRate;
        set
        {
            if (_taxRate != value)
            {
                _taxRate = value;
                OnPropertyChanged();
                NotifyAmountsChanged();
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
                NotifyAmountsChanged();
            }
        }
    }

    public string CustomerNote
    {
        get => _customerNote;
        set
        {
            if (_customerNote != value)
            {
                _customerNote = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasCustomerNote));
            }
        }
    }

    public bool HasCustomerNote => !string.IsNullOrWhiteSpace(_customerNote);

    public bool IsUnitPriceTaxIncluded
    {
        get => _isUnitPriceTaxIncluded;
        set
        {
            if (_isUnitPriceTaxIncluded != value)
            {
                _isUnitPriceTaxIncluded = value;
                OnPropertyChanged();
                NotifyAmountsChanged();
            }
        }
    }

    // Tax-excluded subtotal
    public decimal PriceSubtotal
    {
        get
        {
            decimal raw = Quantity * UnitPrice * (1.0m - (Discount / 100.0m));
            if (_isUnitPriceTaxIncluded)
            {
                decimal divisor = 1.0m + (TaxRate / 100.0m);
                return divisor > 0m ? Math.Round(raw / divisor, 2) : Math.Round(raw, 2);
            }
            return Math.Round(raw, 2);
        }
    }

    // Tax-included line total (displayed on the POS cart line in Odoo 20)
    public decimal LineTotal
    {
        get
        {
            decimal raw = Quantity * UnitPrice * (1.0m - (Discount / 100.0m));
            if (_isUnitPriceTaxIncluded)
            {
                return Math.Round(raw, 2);
            }
            return Math.Round(raw * (1.0m + (TaxRate / 100.0m)), 2);
        }
    }

    public decimal LineTaxAmount => LineTotal - PriceSubtotal;

    public string LineTotalDisplay => string.Format(CultureInfo.InvariantCulture, "$ {0:N2}", LineTotal);

    public string QuantityDisplay =>
        Quantity == Math.Truncate(Quantity)
            ? ((int)Quantity).ToString(CultureInfo.InvariantCulture)
            : Quantity.ToString("0.##", CultureInfo.InvariantCulture);

    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
    }

    private void NotifyAmountsChanged()
    {
        OnPropertyChanged(nameof(PriceSubtotal));
        OnPropertyChanged(nameof(LineTotal));
        OnPropertyChanged(nameof(LineTaxAmount));
        OnPropertyChanged(nameof(LineTotalDisplay));
        OnPropertyChanged(nameof(QuantityDisplay));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}