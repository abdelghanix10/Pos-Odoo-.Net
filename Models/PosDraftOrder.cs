using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OdooPosLogin;

public class PosDraftOrder : INotifyPropertyChanged
{
    private string _orderNumber; // e.g. "1002", "2001"
    private string _orderRef;    // e.g. "Order 00001-002-0001"
    private DateTime _createdAt = DateTime.Now;
    private string _note = string.Empty;
    private bool _isActive;

    public PosDraftOrder(string orderNumber)
    {
        _orderNumber = orderNumber;
        _orderRef = $"Order 00001-002-{orderNumber}";
    }

    public string OrderNumber
    {
        get => _orderNumber;
        set { if (_orderNumber != value) { _orderNumber = value; OnPropertyChanged(); } }
    }

    public string OrderRef
    {
        get => _orderRef;
        set { if (_orderRef != value) { _orderRef = value; OnPropertyChanged(); } }
    }

    public DateTime CreatedAt
    {
        get => _createdAt;
        set { if (_createdAt != value) { _createdAt = value; OnPropertyChanged(); } }
    }

    public string Note
    {
        get => _note;
        set { if (_note != value) { _note = value; OnPropertyChanged(); } }
    }

    public bool IsActive
    {
        get => _isActive;
        set { if (_isActive != value) { _isActive = value; OnPropertyChanged(); } }
    }

    public ObservableCollection<PosCartItem> CartItems { get; } = [];

    public decimal SubTotal => CartItems.Sum(i => i.LineTotal);
    public decimal Taxes => Math.Round(SubTotal * 0.15m, 2); // 15% standard tax
    public decimal TotalAmount => SubTotal + Taxes;

    public string SubTotalDisplay => $"$ {SubTotal:N2}";
    public string TaxesDisplay => $"$ {Taxes:N2}";
    public string TotalDisplay => $"$ {TotalAmount:N2}";

    public void NotifyTotalsChanged()
    {
        OnPropertyChanged(nameof(SubTotal));
        OnPropertyChanged(nameof(Taxes));
        OnPropertyChanged(nameof(TotalAmount));
        OnPropertyChanged(nameof(SubTotalDisplay));
        OnPropertyChanged(nameof(TaxesDisplay));
        OnPropertyChanged(nameof(TotalDisplay));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
