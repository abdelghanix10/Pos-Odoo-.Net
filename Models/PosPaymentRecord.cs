using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OdooPosLogin;

public class PosPaymentRecord : INotifyPropertyChanged
{
    private long _id;
    private int? _odooId;
    private long? _orderId;
    private int? _orderOdooId;
    private string _orderName = string.Empty;
    private long? _sessionId;
    private int? _sessionOdooId;
    private string _sessionName = string.Empty;
    private string _cashierName = string.Empty;
    private int? _paymentMethodId;
    private string _paymentMethodName = "Cash";
    private decimal _amount = 0.00m;
    private string _currencyName = "USD ($)";
    private string _foreignCurrency = "—";
    private DateTime? _paymentDate;

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

    public long? OrderId
    {
        get => _orderId;
        set { if (_orderId != value) { _orderId = value; OnPropertyChanged(); } }
    }

    public int? OrderOdooId
    {
        get => _orderOdooId;
        set { if (_orderOdooId != value) { _orderOdooId = value; OnPropertyChanged(); } }
    }

    public string OrderName
    {
        get => _orderName;
        set { if (_orderName != value) { _orderName = value; OnPropertyChanged(); } }
    }

    public long? SessionId
    {
        get => _sessionId;
        set { if (_sessionId != value) { _sessionId = value; OnPropertyChanged(); } }
    }

    public int? SessionOdooId
    {
        get => _sessionOdooId;
        set { if (_sessionOdooId != value) { _sessionOdooId = value; OnPropertyChanged(); } }
    }

    public string SessionName
    {
        get => _sessionName;
        set { if (_sessionName != value) { _sessionName = value; OnPropertyChanged(); } }
    }

    public string CashierName
    {
        get => _cashierName;
        set { if (_cashierName != value) { _cashierName = value; OnPropertyChanged(); } }
    }

    public int? PaymentMethodId
    {
        get => _paymentMethodId;
        set { if (_paymentMethodId != value) { _paymentMethodId = value; OnPropertyChanged(); } }
    }

    public string PaymentMethodName
    {
        get => _paymentMethodName;
        set { if (_paymentMethodName != value) { _paymentMethodName = value; OnPropertyChanged(); } }
    }

    public decimal Amount
    {
        get => _amount;
        set
        {
            if (_amount != value)
            {
                _amount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AmountDisplay));
                OnPropertyChanged(nameof(AmountInCurrencyDisplay));
            }
        }
    }

    public string CurrencyName
    {
        get => _currencyName;
        set { if (_currencyName != value) { _currencyName = value; OnPropertyChanged(); } }
    }

    public string ForeignCurrency
    {
        get => _foreignCurrency;
        set { if (_foreignCurrency != value) { _foreignCurrency = value; OnPropertyChanged(); } }
    }

    public DateTime? PaymentDate
    {
        get => _paymentDate;
        set
        {
            if (_paymentDate != value)
            {
                _paymentDate = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PaymentDateDisplay));
            }
        }
    }

    // ==================== COMPUTED & DISPLAY PROPERTIES ====================
    public string AmountDisplay => $"$ {Amount:N2}";
    public string AmountInCurrencyDisplay => $"$ {Amount:N2}";
    public string PaymentDateDisplay => PaymentDate.HasValue ? PaymentDate.Value.ToString("yyyy-MM-dd HH:mm:ss") : "—";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class PaymentGroupRecord : INotifyPropertyChanged
{
    private string _groupKey = string.Empty;
    private bool _isExpanded = true;
    private decimal _subtotal = 0.00m;

    public string GroupKey
    {
        get => _groupKey;
        set
        {
            if (_groupKey != value)
            {
                _groupKey = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(GroupName));
                OnPropertyChanged(nameof(HeaderLabel));
            }
        }
    }

    public string GroupName
    {
        get => _groupKey;
        set => GroupKey = value;
    }

    public string HeaderChevron => CollapseIconGlyph;
    public string HeaderLabel => $"{_groupKey} {CountDisplay}";

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CollapseIconGlyph));
                OnPropertyChanged(nameof(HeaderChevron));
            }
        }
    }

    public decimal Subtotal
    {
        get => _subtotal;
        set
        {
            if (_subtotal != value)
            {
                _subtotal = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SubtotalDisplay));
            }
        }
    }

    public ObservableCollection<PosPaymentRecord> Payments { get; } = [];

    public int Count => Payments.Count;
    public string CountDisplay => $"({Count} {(Count == 1 ? "record" : "records")})";
    public string SubtotalDisplay => $"$ {Subtotal:N2}";
    public string CollapseIconGlyph => IsExpanded ? "▾" : "▸";

    public void Recalculate()
    {
        Subtotal = 0m;
        foreach (var p in Payments)
        {
            Subtotal += p.Amount;
        }
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(CountDisplay));
        OnPropertyChanged(nameof(Subtotal));
        OnPropertyChanged(nameof(SubtotalDisplay));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

