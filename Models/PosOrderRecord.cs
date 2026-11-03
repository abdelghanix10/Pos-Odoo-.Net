using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OdooPosLogin;

public class PosOrderLineRecord : INotifyPropertyChanged
{
    private long _id;
    private int? _odooId;
    private long _orderId;
    private int? _orderOdooId;
    private int? _productId;
    private string _productName = string.Empty;
    private bool _isEdited;
    private decimal _qty = 1.00m;
    private decimal _priceUnit = 0.00m;
    private decimal _discount = 0.00m;
    private string _taxes = "15%";
    private decimal _priceSubtotal = 0.00m; // Tax Excl.
    private decimal _priceSubtotalIncl = 0.00m; // Tax Incl.

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

    public long OrderId
    {
        get => _orderId;
        set { if (_orderId != value) { _orderId = value; OnPropertyChanged(); } }
    }

    public int? OrderOdooId
    {
        get => _orderOdooId;
        set { if (_orderOdooId != value) { _orderOdooId = value; OnPropertyChanged(); } }
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

    public bool IsEdited
    {
        get => _isEdited;
        set { if (_isEdited != value) { _isEdited = value; OnPropertyChanged(); } }
    }

    public decimal Qty
    {
        get => _qty;
        set
        {
            if (_qty != value)
            {
                _qty = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(QtyDisplay));
            }
        }
    }

    public decimal PriceUnit
    {
        get => _priceUnit;
        set
        {
            if (_priceUnit != value)
            {
                _priceUnit = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PriceUnitDisplay));
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
                OnPropertyChanged(nameof(DiscountDisplay));
            }
        }
    }

    public string Taxes
    {
        get => _taxes;
        set { if (_taxes != value) { _taxes = value; OnPropertyChanged(); } }
    }

    public decimal PriceSubtotal
    {
        get => _priceSubtotal;
        set
        {
            if (_priceSubtotal != value)
            {
                _priceSubtotal = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PriceSubtotalDisplay));
            }
        }
    }

    public decimal PriceSubtotalIncl
    {
        get => _priceSubtotalIncl;
        set
        {
            if (_priceSubtotalIncl != value)
            {
                _priceSubtotalIncl = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PriceSubtotalInclDisplay));
            }
        }
    }

    public string QtyDisplay => $"{Qty:N2}";
    public string PriceUnitDisplay => $"$ {PriceUnit:N2}";
    public string DiscountDisplay => $"{Discount:N1}%";
    public string PriceSubtotalDisplay => $"$ {PriceSubtotal:N2}";
    public string PriceSubtotalInclDisplay => $"$ {PriceSubtotalIncl:N2}";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class PosOrderRecord : INotifyPropertyChanged
{
    private long _id;
    private int? _odooId;
    private string _name = string.Empty; // Order Ref: "Bakery Shop - 000001"
    private string _posReference = string.Empty; // Receipt Number: "262-3-000001"
    private string _trackingNumber = string.Empty; // Order Number: "2001"
    private int? _sessionId;
    private int? _sessionOdooId;
    private string _sessionName = string.Empty; // "Bakery Shop/00004"
    private string _posConfigName = string.Empty; // "Bakery Shop"
    private DateTime? _dateOrder;
    private int? _cashierId;
    private string _cashierName = string.Empty;
    private string? _cashierAvatarBase64;
    private BitmapImage? _cashierAvatarBitmap;
    private string _customerName = string.Empty;
    private string _fiscalPosition = "—";
    private decimal _amountTotal = 0.00m;
    private decimal _amountTax = 0.00m;
    private decimal _amountPaid = 0.00m;
    private decimal _margin = 0.00m;
    private decimal _marginRatio = 0.00m;
    private string _state = "paid"; // draft, paid, done, invoiced, cancel
    private bool _isEdited = false;
    private string _company = "My Company";
    private string _note = string.Empty;
    private string? _refundedOrderIdsJson;

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
        set
        {
            if (_name != value)
            {
                _name = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsRefundOrder));
            }
        }
    }

    public string PosReference
    {
        get => _posReference;
        set { if (_posReference != value) { _posReference = value; OnPropertyChanged(); } }
    }

    public string TrackingNumber
    {
        get => _trackingNumber;
        set { if (_trackingNumber != value) { _trackingNumber = value; OnPropertyChanged(); } }
    }

    public int? SessionId
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

    public string PosConfigName
    {
        get => _posConfigName;
        set { if (_posConfigName != value) { _posConfigName = value; OnPropertyChanged(); } }
    }

    public DateTime? DateOrder
    {
        get => _dateOrder;
        set
        {
            if (_dateOrder != value)
            {
                _dateOrder = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DateDisplay));
                OnPropertyChanged(nameof(DateShortDisplay));
            }
        }
    }

    public int? CashierId
    {
        get => _cashierId;
        set { if (_cashierId != value) { _cashierId = value; OnPropertyChanged(); } }
    }

    public string CashierName
    {
        get => _cashierName;
        set
        {
            if (_cashierName != value)
            {
                _cashierName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CashierInitials));
            }
        }
    }

    public string? CashierAvatarBase64
    {
        get => _cashierAvatarBase64;
        set
        {
            if (_cashierAvatarBase64 != value)
            {
                _cashierAvatarBase64 = value;
                CashierAvatarBitmap = PosImageUtils.Base64ToBitmapImage(value);
                OnPropertyChanged();
            }
        }
    }

    public BitmapImage? CashierAvatarBitmap
    {
        get => _cashierAvatarBitmap;
        set
        {
            if (_cashierAvatarBitmap != value)
            {
                _cashierAvatarBitmap = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasCashierAvatar));
                OnPropertyChanged(nameof(HasNoCashierAvatar));
            }
        }
    }

    public string CustomerName
    {
        get => _customerName;
        set { if (_customerName != value) { _customerName = value; OnPropertyChanged(); } }
    }

    public string FiscalPosition
    {
        get => _fiscalPosition;
        set { if (_fiscalPosition != value) { _fiscalPosition = value; OnPropertyChanged(); } }
    }

    public decimal AmountTotal
    {
        get => _amountTotal;
        set
        {
            if (_amountTotal != value)
            {
                _amountTotal = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TotalDisplay));
                OnPropertyChanged(nameof(AmountTotalDisplay));
                OnPropertyChanged(nameof(IsRefundOrder));
            }
        }
    }

    public decimal AmountTax
    {
        get => _amountTax;
        set
        {
            if (_amountTax != value)
            {
                _amountTax = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TaxDisplay));
                OnPropertyChanged(nameof(AmountTaxDisplay));
            }
        }
    }

    public decimal AmountPaid
    {
        get => _amountPaid;
        set
        {
            if (_amountPaid != value)
            {
                _amountPaid = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PaidDisplay));
                OnPropertyChanged(nameof(AmountPaidDisplay));
            }
        }
    }

    public decimal Margin
    {
        get => _margin;
        set
        {
            if (_margin != value)
            {
                _margin = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MarginDisplay));
            }
        }
    }

    public decimal MarginRatio
    {
        get => _marginRatio;
        set
        {
            if (_marginRatio != value)
            {
                _marginRatio = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MarginDisplay));
            }
        }
    }

    public string State
    {
        get => _state;
        set
        {
            if (_state != value)
            {
                _state = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StateDisplay));
                OnPropertyChanged(nameof(StatusBrush));
                OnPropertyChanged(nameof(StatusSoftBrush));
                OnPropertyChanged(nameof(ListStatusBackground));
                OnPropertyChanged(nameof(ListStatusForeground));
                OnPropertyChanged(nameof(KanbanStatusBackground));
                OnPropertyChanged(nameof(KanbanStatusForeground));
                OnPropertyChanged(nameof(IsCancelled));
                OnPropertyChanged(nameof(IsStageNew));
                OnPropertyChanged(nameof(IsStagePaid));
                OnPropertyChanged(nameof(IsStagePosted));
                OnPropertyChanged(nameof(IsStageCancelled));
                OnPropertyChanged(nameof(NormalPipelineVisibility));
                OnPropertyChanged(nameof(CancelledPipelineVisibility));
            }
        }
    }

    public bool IsEdited
    {
        get => _isEdited;
        set { if (_isEdited != value) { _isEdited = value; OnPropertyChanged(); } }
    }

    public string Company
    {
        get => _company;
        set { if (_company != value) { _company = value; OnPropertyChanged(); } }
    }

    public string Note
    {
        get => _note;
        set { if (_note != value) { _note = value; OnPropertyChanged(); } }
    }

    public string? RefundedOrderIdsJson
    {
        get => _refundedOrderIdsJson;
        set { if (_refundedOrderIdsJson != value) { _refundedOrderIdsJson = value; OnPropertyChanged(); } }
    }

    public ObservableCollection<PosOrderLineRecord> Lines { get; } = [];
    public ObservableCollection<PosPaymentRecord> Payments { get; } = [];
    public ObservableCollection<PosOrderRecord> LinkedRefundOrders { get; } = [];

    public void NotifyLinkedRefundsChanged()
    {
        OnPropertyChanged(nameof(HasLinkedRefunds));
        OnPropertyChanged(nameof(LinkedRefundsVisibility));
        OnPropertyChanged(nameof(LinkedRefundLabel));
        OnPropertyChanged(nameof(LinkedRefundSummary));
        OnPropertyChanged(nameof(LinkedRefundCountDisplay));
    }

    // ==================== REFUND / ORIGINAL ORDER LINKING ====================
    public bool IsRefundOrder =>
        AmountTotal < 0 ||
        (!string.IsNullOrWhiteSpace(Name) && Name.Contains("REFUND", StringComparison.OrdinalIgnoreCase));

    public bool HasLinkedRefunds => LinkedRefundOrders.Count > 0;
    public Visibility LinkedRefundsVisibility => HasLinkedRefunds ? Visibility.Visible : Visibility.Collapsed;

    public string LinkedRefundLabel => IsRefundOrder ? "Refunded Order" : "Refund Orders";

    public string LinkedRefundCountDisplay =>
        IsRefundOrder
            ? "Original Order"
            : $"{LinkedRefundOrders.Count} {(LinkedRefundOrders.Count == 1 ? "Refund" : "Refunds")}";

    public string LinkedRefundSummary =>
        LinkedRefundOrders.Count > 0
            ? string.Join(", ", LinkedRefundOrders.Select(o => o.Name))
            : "—";

    public bool IsDraft => string.Equals(State, "draft", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(State, "ongoing", StringComparison.OrdinalIgnoreCase);

    // ==================== COMPUTED & DISPLAY PROPERTIES ====================
    public string DateDisplay => DateOrder.HasValue ? DateOrder.Value.ToString("yyyy-MM-dd HH:mm:ss") : "—";
    public string DateShortDisplay => DateOrder.HasValue ? DateOrder.Value.ToString("MMM d, h:mm tt", CultureInfo.InvariantCulture) : "—";

    public string TotalDisplay => $"$ {AmountTotal:N2}";
    public string TaxDisplay => $"$ {AmountTax:N2}";
    public string PaidDisplay => $"$ {AmountPaid:N2}";

    // Aliases for XAML compatibility
    public string AmountTotalDisplay => TotalDisplay;
    public string AmountTaxDisplay => TaxDisplay;
    public string AmountPaidDisplay => PaidDisplay;

    public string MarginDisplay => $"$ {Margin:N2} ({MarginRatio:N1}%)";

    public string StateDisplay => State?.ToLowerInvariant() switch
    {
        "draft" => "New",
        "paid" => "Paid",
        "done" or "invoiced" => "Posted",
        "cancel" => "Cancelled",
        _ => "Posted"
    };

    // Odoo 20 List View Pill (Green filled for Posted/Paid, Soft Gray for Cancelled)
    public Brush ListStatusBackground => State?.ToLowerInvariant() switch
    {
        "cancel" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E5E7EB")),
        "draft" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#017E84")),
        _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#28A745"))
    };

    public Brush ListStatusForeground => State?.ToLowerInvariant() switch
    {
        "cancel" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#374151")),
        _ => Brushes.White
    };

    // Odoo 20 Kanban View Pill (Solid Green for Posted/Paid, Solid Red for Cancelled)
    public Brush KanbanStatusBackground => State?.ToLowerInvariant() switch
    {
        "cancel" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC3545")),
        "draft" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#017E84")),
        _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#28A745"))
    };

    public Brush KanbanStatusForeground => Brushes.White;

    public Brush StatusBrush => State?.ToLowerInvariant() switch
    {
        "draft" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#017E84")),
        "paid" or "done" or "invoiced" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981")),
        "cancel" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626")),
        _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"))
    };

    public Brush StatusSoftBrush => State?.ToLowerInvariant() switch
    {
        "draft" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E6F4F5")),
        "paid" or "done" or "invoiced" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ECFDF5")),
        "cancel" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F3F4F6")),
        _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ECFDF5"))
    };

    public bool IsCancelled => string.Equals(State, "cancel", StringComparison.OrdinalIgnoreCase);

    public bool IsStageNew => string.Equals(State, "draft", StringComparison.OrdinalIgnoreCase);
    public bool IsStagePaid => string.Equals(State, "paid", StringComparison.OrdinalIgnoreCase);
    public bool IsStagePosted => string.Equals(State, "done", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(State, "invoiced", StringComparison.OrdinalIgnoreCase);
    public bool IsStageCancelled => IsCancelled;

    public Visibility NormalPipelineVisibility => IsCancelled ? Visibility.Collapsed : Visibility.Visible;
    public Visibility CancelledPipelineVisibility => IsCancelled ? Visibility.Visible : Visibility.Collapsed;

    public bool HasCashierAvatar => CashierAvatarBitmap != null;
    public bool HasNoCashierAvatar => CashierAvatarBitmap == null;

    public string CashierInitials
    {
        get
        {
            if (string.IsNullOrWhiteSpace(CashierName)) return "C";
            var parts = CashierName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1) return parts[0][..1].ToUpperInvariant();
            return $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}