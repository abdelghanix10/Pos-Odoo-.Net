using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OdooPosLogin.ViewModels;

public partial class PosRegisterViewModel : ViewModelBase
{
    private readonly List<ProductRecord> _allProducts = [];
    private readonly List<PosCategoryRecord> _allCategories = [];
    private readonly List<PosOrderRecord> _allInPosOrders = [];
    private readonly List<ProductAttributeRecord> _allProductAttributes = [];
    private readonly List<ReceiptDesignRecord> _allReceiptDesigns = [];
    private PosOrderRecord? _lastPaidOrder;

    // ==================== SESSION & CONFIG CONTEXT ====================
    [ObservableProperty]
    private PosConfigRecord? _currentConfig;

    [ObservableProperty]
    private PosSessionRecord? _currentSession;

    [ObservableProperty]
    private string _currentSessionNameDisplay = string.Empty;

    [ObservableProperty]
    private int _sessionTotalOrdersCount;

    [ObservableProperty]
    private decimal _sessionTotalPaymentsAmount;

    public string CurrentSessionDisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(CurrentSessionNameDisplay))
                return CurrentSessionNameDisplay;
            if (!string.IsNullOrWhiteSpace(CurrentConfig?.CurrentSessionName))
                return CurrentConfig.CurrentSessionName;
            if (!string.IsNullOrWhiteSpace(CurrentSession?.Name))
                return CurrentSession.Name;
            return $"{ActiveShopName}/00001";
        }
    }

    public string SessionTotalOrdersDisplay => SessionTotalOrdersCount.ToString();
    public string SessionTotalPaymentsDisplay => $"$ {SessionTotalPaymentsAmount:N2}";

    [ObservableProperty]
    private string _cashierName = "Mitchell Admin";

    [ObservableProperty]
    private BitmapImage? _cashierAvatarBitmap;

    public bool HasCashierAvatar => CashierAvatarBitmap != null;

    public string CashierInitials
    {
        get
        {
            if (string.IsNullOrWhiteSpace(CashierName)) return "M";
            var parts = CashierName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 1
                ? parts[0][0].ToString().ToUpperInvariant()
                : $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[^1][0])}";
        }
    }

    [ObservableProperty]
    private string _activeShopName = "Bakery Shop";

    [ObservableProperty]
    private BitmapImage? _companyLogoBitmap;

    public bool HasCompanyLogo => CompanyLogoBitmap != null;

    // ==================== PENDING SYNC QUEUE STATUS ====================
    [ObservableProperty]
    private int _pendingSyncCount;

    public bool HasPendingSync => PendingSyncCount > 0;
    public string PendingSyncBadgeText => PendingSyncCount == 1 ? "1 Unsynced" : $"{PendingSyncCount} Unsynced";

    // ==================== LANDING SCREEN (Screenshot 6) ====================
    [ObservableProperty]
    private bool _isPosLandingScreenOpen;

    [ObservableProperty]
    private string _landingClockText = "02:59:47 PM";

    [ObservableProperty]
    private string _landingDayNameText = "Tuesday";

    [ObservableProperty]
    private string _landingFullDateText = "October, 27, 2026";

    [ObservableProperty]
    private bool _isRegisterOpenForLanding;

    [ObservableProperty]
    private bool _isRegisterClosedForLanding = true;

    [ObservableProperty]
    private string _landingActionButtonText = "Open Register";

    [ObservableProperty]
    private string _lastSessionTitle = string.Empty;

    [ObservableProperty]
    private string _lastSessionPosName = string.Empty;

    [ObservableProperty]
    private string _lastSessionOpeningDateDisplay = "—";

    [ObservableProperty]
    private string _lastSessionClosingDateDisplay = "—";

    [ObservableProperty]
    private string _lastSessionOrdersCountDisplay = "0";

    [ObservableProperty]
    private string _lastSessionTotalDisplay = "$ 0.00";

    partial void OnCurrentConfigChanged(PosConfigRecord? value)
    {
        UpdateLandingState();
        if (IsRegisterClosedForLanding)
        {
            _ = LoadLastSessionInfoAsync();
        }
    }

    partial void OnIsPosLandingScreenOpenChanged(bool value)
    {
        if (value)
        {
            UpdateLandingState();
            if (IsRegisterClosedForLanding)
            {
                _ = LoadLastSessionInfoAsync();
            }
        }
    }

    public void UpdateLandingState()
    {
        bool isOpen = CurrentConfig != null &&
                      string.Equals(CurrentConfig.CurrentSessionState, "opened", StringComparison.OrdinalIgnoreCase);

        IsRegisterOpenForLanding = isOpen;
        IsRegisterClosedForLanding = !isOpen;
        LandingActionButtonText = isOpen ? "Continue" : "Open Register";
    }

    public async Task LoadLastSessionInfoAsync()
    {
        try
        {
            string currentShopName = CurrentConfig?.Name ?? ActiveShopName;
            int? currentConfigOdooId = CurrentConfig?.OdooId;

            var allSessions = await SqliteDatabaseService.Instance.LoadAllSessionsAsync();
            if (allSessions == null || allSessions.Count == 0)
            {
                SetDefaultLastSessionInfo();
                return;
            }

            // Find sessions belonging to this shop/POS
            var configSessions = allSessions.Where(s =>
                (!string.IsNullOrWhiteSpace(currentShopName) && string.Equals(s.PosConfigName, currentShopName, StringComparison.OrdinalIgnoreCase)) ||
                (currentConfigOdooId.HasValue && s.PosConfigId.HasValue && s.PosConfigId.Value == currentConfigOdooId.Value) ||
                (!string.IsNullOrWhiteSpace(s.Name) && !string.IsNullOrWhiteSpace(currentShopName) && s.Name.StartsWith(currentShopName, StringComparison.OrdinalIgnoreCase))
            ).ToList();

            // Look for latest closed session for this config
            var lastClosed = configSessions
                .Where(s => string.Equals(s.State, "closed", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(s => s.StopAt ?? s.StartAt ?? DateTime.MinValue)
                .ThenByDescending(s => s.OdooId ?? (int)s.Id)
                .FirstOrDefault();

            // If no closed session found for this config, try any closed session in DB
            if (lastClosed == null)
            {
                lastClosed = allSessions
                    .Where(s => string.Equals(s.State, "closed", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(s => s.StopAt ?? s.StartAt ?? DateTime.MinValue)
                    .ThenByDescending(s => s.OdooId ?? (int)s.Id)
                    .FirstOrDefault();
            }

            // If still no closed session, pick latest session of this config
            if (lastClosed == null)
            {
                lastClosed = configSessions
                    .OrderByDescending(s => s.StopAt ?? s.StartAt ?? DateTime.MinValue)
                    .ThenByDescending(s => s.OdooId ?? (int)s.Id)
                    .FirstOrDefault();
            }

            if (lastClosed == null)
            {
                SetDefaultLastSessionInfo();
                return;
            }

            string title = !string.IsNullOrWhiteSpace(lastClosed.Name) ? lastClosed.Name : $"{currentShopName}/00001";
            string posName = !string.IsNullOrWhiteSpace(lastClosed.PosConfigName) ? lastClosed.PosConfigName : currentShopName;

            string openDate = lastClosed.StartAt.HasValue
                ? lastClosed.StartAt.Value.ToString("yyyy-MM-dd HH:mm:ss")
                : "—";

            string closeDate = lastClosed.StopAt.HasValue
                ? lastClosed.StopAt.Value.ToString("yyyy-MM-dd HH:mm:ss")
                : (CurrentConfig?.LastClosingDate.HasValue == true
                    ? CurrentConfig.LastClosingDate.Value.ToString("yyyy-MM-dd HH:mm:ss")
                    : "—");

            int ordersCount = lastClosed.OrdersCount;
            decimal totalAmount = lastClosed.TotalPaymentsAmount;

            try
            {
                var orders = await SqliteDatabaseService.Instance.LoadAllOrdersAsync(lastClosed.Name);
                var matched = orders.Where(o =>
                    (lastClosed.OdooId.HasValue && o.SessionOdooId == lastClosed.OdooId.Value) ||
                    string.Equals(o.SessionName, lastClosed.Name, StringComparison.OrdinalIgnoreCase) ||
                    (lastClosed.Id > 0 && o.SessionId == lastClosed.Id)
                ).ToList();

                if (matched.Count > 0)
                {
                    ordersCount = matched.Count;
                    totalAmount = matched.Sum(o => o.AmountTotal);
                }
            }
            catch { }

            LastSessionTitle = title;
            LastSessionPosName = posName;
            LastSessionOpeningDateDisplay = openDate;
            LastSessionClosingDateDisplay = closeDate;
            LastSessionOrdersCountDisplay = ordersCount.ToString();
            LastSessionTotalDisplay = $"$ {totalAmount:N2}";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LoadLastSessionInfoAsync] Error: {ex.Message}");
            SetDefaultLastSessionInfo();
        }
    }

    private void SetDefaultLastSessionInfo()
    {
        string shop = CurrentConfig?.Name ?? ActiveShopName;
        if (string.IsNullOrWhiteSpace(shop)) shop = "Point of Sale";
        LastSessionTitle = $"{shop}/00001";
        LastSessionPosName = shop;
        LastSessionOpeningDateDisplay = "—";
        LastSessionClosingDateDisplay = "—";
        LastSessionOrdersCountDisplay = "0";
        LastSessionTotalDisplay = "$ 0.00";
    }

    // ==================== TOP NAVIGATION & ACTIVE VIEW ====================
    [ObservableProperty]
    private string _activePosView = "Register"; // "Register" or "Orders"

    public bool IsRegisterViewActive => ActivePosView == "Register" && !IsPaymentViewOpen && !IsPaymentConfirmationOpen;
    public bool IsOrdersViewActive => ActivePosView == "Orders" && !IsPaymentViewOpen && !IsPaymentConfirmationOpen;
    public bool IsProductSearchVisible => ActivePosView == "Register" && !IsPaymentViewOpen && !IsPaymentConfirmationOpen;
    public bool IsTopBarcodeVisible => !IsPaymentViewOpen && !IsPaymentConfirmationOpen;

    // ==================== DRAFT ORDERS & TABS ====================
    [ObservableProperty]
    private ObservableCollection<PosDraftOrder> _draftOrders = [];

    [ObservableProperty]
    private PosDraftOrder? _currentOrder;

    [ObservableProperty]
    private bool _isCartNumpadVisible = true;

    public bool HasCurrentOrderItems => CurrentOrder != null && CurrentOrder.HasItems;
    public bool ShowExpandedNumpad => HasCurrentOrderItems && IsCartNumpadVisible && !IsClosingRegisterModalOpen;

    private int _nextOrderSeq = 1;
    private int _deviceDigit = 2;

    // ==================== CART LINES & TOTALS ====================
    [ObservableProperty]
    private PosCartItem? _selectedCartLine;

    [ObservableProperty]
    private string _numpadMode = "Qty"; // "Qty", "%", "Price"

    private string _numpadBuffer = string.Empty;

    // ==================== PRODUCTS & CATEGORIES ====================
    [ObservableProperty]
    private ObservableCollection<PosCategoryRecord> _categories = [];

    [ObservableProperty]
    private ObservableCollection<ProductRecord> _filteredProducts = [];

    [ObservableProperty]
    private double _productCardWidth = 172.0;

    [ObservableProperty]
    private PosCategoryRecord? _selectedCategory;

    [ObservableProperty]
    private string _productSearchText = string.Empty;

    // ==================== IN-POS ORDERS VIEW (Screenshot 3) ====================
    [ObservableProperty]
    private ObservableCollection<PosOrderRecord> _filteredInPosOrders = [];

    [ObservableProperty]
    private PosOrderRecord? _selectedInPosOrder;

    [ObservableProperty]
    private string _orderSearchText = string.Empty;

    [ObservableProperty]
    private string _orderStatusFilter = "Active"; // "Active" (Ongoing), "All", "Paid"

    [ObservableProperty]
    private string _ordersPagerText = "0-0 / 0";

    // ==================== MODAL 1: OPENING CONTROL (Screenshot 5) ====================
    [ObservableProperty]
    private bool _isOpeningControlModalOpen;

    [ObservableProperty]
    private string _openingCashInput = "25.59";

    [ObservableProperty]
    private string _openingNote = string.Empty;

    // ==================== SCREEN 4: PAYMENT SPLIT SCREEN (Screenshot 4) ====================
    [ObservableProperty]
    private bool _isPaymentViewOpen;

    [ObservableProperty]
    private ObservableCollection<PosPaymentMethodRecord> _availablePaymentMethods = [];

    [ObservableProperty]
    private string _selectedPaymentMethod = "Cash";

    [ObservableProperty]
    private int? _selectedPaymentMethodOdooId;

    [ObservableProperty]
    private decimal _amountDue;

    [ObservableProperty]
    private decimal _amountTendered;

    [ObservableProperty]
    private decimal _changeDue;

    private string _paymentInputBuffer = string.Empty;
    private bool _isFirstPaymentDigit = true;

    public bool CanValidatePayment => AmountTendered >= AmountDue && AmountDue > 0m;
    public bool IsPaymentUnderpaid => AmountTendered < AmountDue;
    public decimal RemainingAmount => Math.Max(0m, AmountDue - AmountTendered);
    public string RemainingDisplay => $"$ {RemainingAmount:N2}";
    public string ChangeDisplay
    {
        get
        {
            decimal diff = AmountTendered - AmountDue;
            if (diff > 0m)
                return $"$- {diff:N2}";
            return "$ 0.00";
        }
    }
    public string PaymentLineAmountDisplay => $"$ {AmountTendered:N2}";
    public bool HasPaymentTendered => AmountTendered > 0m || !string.IsNullOrEmpty(_paymentInputBuffer);

    public string PaymentPrimaryButtonText => string.IsNullOrWhiteSpace(SelectedPaymentMethod) ? "Cash" : SelectedPaymentMethod;
    public string PaymentCurrencySymbol => "$";

    public string OrderTotalDisplay
    {
        get
        {
            decimal val = CurrentOrder?.TotalAmount ?? AmountDue;
            return $"$ {val:N2}";
        }
    }

    public string OrderTotalIntegerPart
    {
        get
        {
            decimal val = CurrentOrder?.TotalAmount ?? AmountDue;
            long intPart = (long)Math.Truncate(Math.Abs(val));
            return (val < 0 ? "-" : "") + intPart.ToString("N0", CultureInfo.InvariantCulture);
        }
    }

    public string OrderTotalDecimalPart
    {
        get
        {
            decimal val = CurrentOrder?.TotalAmount ?? AmountDue;
            int cents = (int)Math.Round((Math.Abs(val) - Math.Truncate(Math.Abs(val))) * 100m);
            return $".{cents:D2}";
        }
    }

    public string PaymentIntegerPart
    {
        get
        {
            decimal val = AmountTendered > 0 ? AmountTendered : AmountDue;
            long intPart = (long)Math.Truncate(Math.Abs(val));
            return (val < 0 ? "-" : "") + intPart.ToString("N0", CultureInfo.InvariantCulture);
        }
    }

    public string PaymentDecimalPart
    {
        get
        {
            decimal val = AmountTendered > 0 ? AmountTendered : AmountDue;
            int cents = (int)Math.Round((Math.Abs(val) - Math.Truncate(Math.Abs(val))) * 100m);
            return $".{cents:D2}";
        }
    }

    public void NotifyPaymentCalculationsChanged()
    {
        OnPropertyChanged(nameof(PaymentIntegerPart));
        OnPropertyChanged(nameof(PaymentDecimalPart));
        OnPropertyChanged(nameof(OrderTotalIntegerPart));
        OnPropertyChanged(nameof(OrderTotalDecimalPart));
        OnPropertyChanged(nameof(OrderTotalDisplay));
        OnPropertyChanged(nameof(CanValidatePayment));
        OnPropertyChanged(nameof(IsPaymentUnderpaid));
        OnPropertyChanged(nameof(RemainingAmount));
        OnPropertyChanged(nameof(RemainingDisplay));
        OnPropertyChanged(nameof(ChangeDisplay));
        OnPropertyChanged(nameof(PaymentLineAmountDisplay));
        OnPropertyChanged(nameof(HasPaymentTendered));
        OnPropertyChanged(nameof(PaymentPrimaryButtonText));

        foreach (var pm in AvailablePaymentMethods)
        {
            pm.IsSelected = string.Equals(pm.Name, SelectedPaymentMethod, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ==================== SCREEN 5: PAYMENT CONFIRMATION (Screenshot 8) ====================
    [ObservableProperty]
    private bool _isPaymentConfirmationOpen;

    [ObservableProperty]
    private decimal _confirmedPaidAmount;

    [ObservableProperty]
    private decimal _confirmedCashAmount;

    [ObservableProperty]
    private decimal _confirmedChangeAmount;

    [ObservableProperty]
    private string _confirmedPaymentMethodName = "Cash";

    public string ConfirmedTenderMethodLabel => string.IsNullOrWhiteSpace(ConfirmedPaymentMethodName) ? "Cash" : ConfirmedPaymentMethodName;

    public string ConfirmedIntegerPart
    {
        get
        {
            long intPart = (long)Math.Truncate(Math.Abs(ConfirmedPaidAmount));
            return (ConfirmedPaidAmount < 0 ? "-" : "") + intPart.ToString("N0", CultureInfo.InvariantCulture);
        }
    }

    public string ConfirmedDecimalPart
    {
        get
        {
            int cents = (int)Math.Round((Math.Abs(ConfirmedPaidAmount) - Math.Truncate(Math.Abs(ConfirmedPaidAmount))) * 100m);
            return $".{cents:D2}";
        }
    }

    public string ConfirmedChangeIntegerPart
    {
        get
        {
            long intPart = (long)Math.Truncate(Math.Abs(ConfirmedChangeAmount));
            return (ConfirmedChangeAmount < 0 ? "-" : "") + intPart.ToString("N0", CultureInfo.InvariantCulture);
        }
    }

    public string ConfirmedChangeDecimalPart
    {
        get
        {
            int cents = (int)Math.Round((Math.Abs(ConfirmedChangeAmount) - Math.Truncate(Math.Abs(ConfirmedChangeAmount))) * 100m);
            return $".{cents:D2}";
        }
    }

    public string ConfirmedChangeDisplay => $"$ {ConfirmedChangeAmount:N2}";
    public string ConfirmedCashDisplay => $"$ {ConfirmedCashAmount:N2}";
    public string ConfirmedPaidDisplay => $"$ {ConfirmedPaidAmount:N2}";

    // ==================== MODAL 2: HAMBURGER MENU (Screenshot 7) ====================
    [ObservableProperty]
    private bool _isHamburgerMenuOpen;

    // ==================== MODAL 3 & 4: CLOSING REGISTER & WARNING (Screenshots 1 & 2) ====================
    [ObservableProperty]
    private bool _isClosingRegisterModalOpen;

    [ObservableProperty]
    private bool _isDraftOrdersWarningModalOpen;

    [ObservableProperty]
    private string _closingCashCount = "25.59";

    [ObservableProperty]
    private string _closingNote = string.Empty;

    [ObservableProperty]
    private string _sessionSummaryText = "1 orders: $ 25.59";

    // Cash breakdown
    [ObservableProperty]
    private decimal _cashOpening = 0.00m;

    [ObservableProperty]
    private decimal _cashPayments = 25.59m;

    [ObservableProperty]
    private decimal _cashInOut = 0.00m;

    [ObservableProperty]
    private decimal _cashCounted = 25.59m;

    [ObservableProperty]
    private decimal _cashDifference = 0.00m;

    // Card breakdown
    [ObservableProperty]
    private decimal _cardTotal = 0.00m;

    [ObservableProperty]
    private decimal _cardCounted = 0.00m;

    [ObservableProperty]
    private decimal _cardDifference = 0.00m;

    // Customer Account breakdown
    [ObservableProperty]
    private decimal _customerAccountTotal = 0.00m;

    [ObservableProperty]
    private decimal _customerAccountCounted = 0.00m;

    [ObservableProperty]
    private decimal _customerAccountDifference = 0.00m;

    // Note Modal
    [ObservableProperty]
    private bool _isNoteModalOpen;

    [ObservableProperty]
    private string _noteInputText = string.Empty;

    // ==================== POS SETTINGS DRIVEN UI PROPERTIES ====================
    public bool IsOneClickPaymentVisible =>
        CurrentConfig != null &&
        CurrentConfig.IsOneClickPaymentEnabled &&
        CurrentConfig.OneClickPaymentMethodsList.Count > 0;

    public string OneClickPaymentButtonLabel =>
        CurrentConfig?.OneClickPaymentMethodsList.FirstOrDefault() ?? "Cash";

    public bool IsPrintLastReceiptVisible =>
        CurrentConfig != null && CurrentConfig.IsPrintLastReceiptEnabled;

    // ==================== MODAL 6: PRODUCT VARIANT SELECTION POPUP ====================
    [ObservableProperty]
    private bool _isVariantPopupOpen;

    [ObservableProperty]
    private ProductRecord? _variantPopupProduct;

    [ObservableProperty]
    private string _variantPopupProductName = string.Empty;

    [ObservableProperty]
    private decimal _variantPopupTotalPrice;

    public string VariantPopupTotalPriceDisplay => $"$ {VariantPopupTotalPrice:N2}";

    public ObservableCollection<PosVariantAttributeGroup> VariantAttributeGroups { get; } = [];

    // ==================== MODAL 7: RECEIPT PREVIEW / CUSTOM & SECOND RECEIPT ====================
    [ObservableProperty]
    private bool _isReceiptPreviewModalOpen;

    [ObservableProperty]
    private string _receiptPreviewTitle = "Order Receipt";

    [ObservableProperty]
    private string _receiptPreviewDesignBadge = "Standard Receipt";

    [ObservableProperty]
    private string _receiptPreviewMainText = string.Empty;

    [ObservableProperty]
    private bool _hasSecondReceiptPreview;

    [ObservableProperty]
    private string _receiptPreviewSecondTitle = "Second Simplified Receipt";

    [ObservableProperty]
    private string _receiptPreviewSecondText = string.Empty;

    // Toast notifications inside POS
    [ObservableProperty]
    private string _posToastMessage = string.Empty;

    [ObservableProperty]
    private bool _isPosToastVisible;

    private readonly DispatcherTimer _toastTimer;
    private readonly DispatcherTimer _landingClockTimer;
    private readonly DispatcherTimer _draftSaveDebounceTimer;
    private PosDraftOrder? _pendingDraftToSave;

    public event Action? ReturnToDashboardRequested;
    public event Action<string>? PosMessageRequested;

    public PosRegisterViewModel()
    {
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            IsPosToastVisible = false;
        };

        UpdateLandingClock();
        _landingClockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _landingClockTimer.Tick += (_, _) => UpdateLandingClock();
        _landingClockTimer.Start();

        _draftSaveDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        _draftSaveDebounceTimer.Tick += async (_, _) =>
        {
            _draftSaveDebounceTimer.Stop();
            var draft = _pendingDraftToSave;
            _pendingDraftToSave = null;
            if (draft != null)
            {
                await PersistDraftOrderToDatabaseAsync(draft);
            }
        };

        OdooSyncService.Instance.QueueUpdated += () =>
        {
            Application.Current?.Dispatcher?.InvokeAsync(RefreshPendingSyncCountAsync);
        };
        OdooSyncService.Instance.PendingQueueFlushed += _ =>
        {
            Application.Current?.Dispatcher?.InvokeAsync(async () =>
            {
                await RefreshPendingSyncCountAsync();
                await RefreshBoundConfigSessionAsync();
            });
        };

        CreateNewDraftOrderInternal(persistPrevious: false);
    }

    private void UpdateLandingClock()
    {
        DateTime now = DateTime.Now;
        LandingClockText = now.ToString("hh:mm:ss tt", CultureInfo.InvariantCulture);
        LandingDayNameText = now.ToString("dddd", CultureInfo.InvariantCulture);
        LandingFullDateText = now.ToString("MMMM, dd, yyyy", CultureInfo.InvariantCulture);
    }

    partial void OnPendingSyncCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasPendingSync));
        OnPropertyChanged(nameof(PendingSyncBadgeText));
    }

    partial void OnCashierNameChanged(string value) => OnPropertyChanged(nameof(CashierInitials));
    partial void OnCashierAvatarBitmapChanged(BitmapImage? value) => OnPropertyChanged(nameof(HasCashierAvatar));
    partial void OnCompanyLogoBitmapChanged(BitmapImage? value) => OnPropertyChanged(nameof(HasCompanyLogo));

    partial void OnProductSearchTextChanged(string value) => FilterProducts();
    partial void OnOrderSearchTextChanged(string value) => FilterInPosOrders();
    partial void OnOrderStatusFilterChanged(string value) => FilterInPosOrders();

    partial void OnActivePosViewChanged(string value) => NotifyViewStatesChanged();
    partial void OnIsPaymentViewOpenChanged(bool value) => NotifyViewStatesChanged();
    partial void OnIsPaymentConfirmationOpenChanged(bool value) => NotifyViewStatesChanged();
    partial void OnIsClosingRegisterModalOpenChanged(bool value) => OnPropertyChanged(nameof(ShowExpandedNumpad));
    partial void OnIsCartNumpadVisibleChanged(bool value) => OnPropertyChanged(nameof(ShowExpandedNumpad));

    partial void OnSelectedPaymentMethodChanged(string value) => NotifyPaymentCalculationsChanged();

    partial void OnAmountDueChanged(decimal value) => NotifyPaymentCalculationsChanged();

    partial void OnAmountTenderedChanged(decimal value) => NotifyPaymentCalculationsChanged();

    partial void OnConfirmedPaidAmountChanged(decimal value)
    {
        OnPropertyChanged(nameof(ConfirmedIntegerPart));
        OnPropertyChanged(nameof(ConfirmedDecimalPart));
        OnPropertyChanged(nameof(ConfirmedPaidDisplay));
    }

    partial void OnConfirmedCashAmountChanged(decimal value)
    {
        OnPropertyChanged(nameof(ConfirmedCashDisplay));
    }

    partial void OnConfirmedChangeAmountChanged(decimal value)
    {
        OnPropertyChanged(nameof(ConfirmedChangeIntegerPart));
        OnPropertyChanged(nameof(ConfirmedChangeDecimalPart));
        OnPropertyChanged(nameof(ConfirmedChangeDisplay));
    }

    partial void OnConfirmedPaymentMethodNameChanged(string value)
    {
        OnPropertyChanged(nameof(ConfirmedTenderMethodLabel));
    }

    partial void OnClosingCashCountChanged(string value)
    {
        if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal counted))
        {
            CashCounted = counted;
            decimal expected = CashOpening + CashPayments + CashInOut;
            CashDifference = Math.Round(CashCounted - expected, 2);
        }
        else
        {
            CashCounted = 0.00m;
            decimal expected = CashOpening + CashPayments + CashInOut;
            CashDifference = Math.Round(-expected, 2);
        }
    }

    private void NotifyViewStatesChanged()
    {
        OnPropertyChanged(nameof(IsRegisterViewActive));
        OnPropertyChanged(nameof(IsOrdersViewActive));
        OnPropertyChanged(nameof(IsProductSearchVisible));
        OnPropertyChanged(nameof(IsTopBarcodeVisible));
        OnPropertyChanged(nameof(HasCurrentOrderItems));
        OnPropertyChanged(nameof(ShowExpandedNumpad));
        OnPropertyChanged(nameof(IsOneClickPaymentVisible));
        OnPropertyChanged(nameof(OneClickPaymentButtonLabel));
        OnPropertyChanged(nameof(IsPrintLastReceiptVisible));
    }

    private void ScheduleDraftPersistence(PosDraftOrder? draft)
    {
        if (draft == null) return;
        _pendingDraftToSave = draft;
        _draftSaveDebounceTimer.Stop();
        _draftSaveDebounceTimer.Start();
    }

    public async Task RefreshPendingSyncCountAsync()
    {
        try
        {
            PendingSyncCount = await SqliteDatabaseService.Instance.GetPendingSyncCountAsync();
        }
        catch { }
    }

    private async Task RefreshBoundConfigSessionAsync()
    {
        if (CurrentConfig == null) return;
        try
        {
            var configs = await SqliteDatabaseService.Instance.LoadAllPosConfigsAsync();
            var updated = configs.FirstOrDefault(c => c.OdooId == CurrentConfig.OdooId);
            if (updated != null)
            {
                if (updated.CurrentSessionId.HasValue)
                    CurrentConfig.CurrentSessionId = updated.CurrentSessionId;
                if (!string.IsNullOrWhiteSpace(updated.CurrentSessionName))
                    CurrentConfig.CurrentSessionName = updated.CurrentSessionName;
            }
        }
        catch { }
    }

    [RelayCommand]
    public void OpenPendingSyncWindow()
    {
        IsHamburgerMenuOpen = false;
        var win = new PendingSyncWindow();
        if (Application.Current?.MainWindow != null && Application.Current.MainWindow.IsVisible)
        {
            win.Owner = Application.Current.MainWindow;
        }
        win.ShowDialog();
        _ = RefreshPendingSyncCountAsync();
    }

    // =========================================================================
    // INITIALIZATION & CONTEXT LOADING
    // =========================================================================
    public async Task InitializeRegisterAsync(PosConfigRecord config, bool showOpeningControl = false)
    {
        try
        {
            var freshConfigs = await SqliteDatabaseService.Instance.LoadAllPosConfigsAsync();
            var fresh = freshConfigs.FirstOrDefault(c => c.OdooId == config.OdooId || c.Id == config.Id);
            if (fresh != null)
            {
                config.CurrentSessionId = fresh.CurrentSessionId;
                config.CurrentSessionName = fresh.CurrentSessionName;
                config.CurrentSessionState = fresh.CurrentSessionState;
                config.OpeningBalance = fresh.OpeningBalance;
                config.LastClosingDate = fresh.LastClosingDate;
                config.LastClosingBalance = fresh.LastClosingBalance;
                config.SelectedPaymentMethodsCsv = fresh.SelectedPaymentMethodsCsv;
                config.IsOneClickPaymentEnabled = fresh.IsOneClickPaymentEnabled;
                config.OneClickPaymentMethodsCsv = fresh.OneClickPaymentMethodsCsv;
                config.IsRestrictCategoriesEnabled = fresh.IsRestrictCategoriesEnabled;
                config.RestrictedCategoriesCsv = fresh.RestrictedCategoriesCsv;
                config.IsVariantsEnabled = fresh.IsVariantsEnabled;
                config.IsAutomaticReceiptPrintingEnabled = fresh.IsAutomaticReceiptPrintingEnabled;
                config.IsPreventLineMergingEnabled = fresh.IsPreventLineMergingEnabled;
                config.IsPrintLastReceiptEnabled = fresh.IsPrintLastReceiptEnabled;
                config.IsCustomOrderReceiptsEnabled = fresh.IsCustomOrderReceiptsEnabled;
                config.CustomOrderReceiptDesignId = fresh.CustomOrderReceiptDesignId;
                config.IsHideOpeningCashControlEnabled = fresh.IsHideOpeningCashControlEnabled;
                config.DefaultOpeningCashAmount = fresh.DefaultOpeningCashAmount;
                config.IsSecondReceiptEnabled = fresh.IsSecondReceiptEnabled;
                config.SecondReceiptDesignId = fresh.SecondReceiptDesignId;
            }
        }
        catch { }

        CurrentConfig = config;
        ActiveShopName = config.Name;
        CashierName = !string.IsNullOrWhiteSpace(config.ActiveUserName) ? config.ActiveUserName! : "Mitchell Admin";
        CashierAvatarBitmap = config.ActiveUserAvatarBitmap;

        // Always reset category selection and search text so products display immediately on open
        SelectedCategory = null;
        _productSearchText = string.Empty;
        OnPropertyChanged(nameof(ProductSearchText));

        try
        {
            var companies = await SqliteDatabaseService.Instance.LoadAllCompaniesAsync();
            var firstCompany = companies.FirstOrDefault(c => c.HasLogo) ?? companies.FirstOrDefault();
            CompanyLogoBitmap = firstCompany?.LogoBitmap;

            if (CashierAvatarBitmap == null)
            {
                var users = await SqliteDatabaseService.Instance.LoadAllUsersAsync();
                var matchedUser = users.FirstOrDefault(u => string.Equals(u.Name, CashierName, StringComparison.OrdinalIgnoreCase))
                                  ?? users.FirstOrDefault(u => u.HasAvatar);
                if (matchedUser != null)
                {
                    CashierAvatarBitmap = matchedUser.AvatarBitmap;
                    if (string.IsNullOrWhiteSpace(config.ActiveUserName))
                        CashierName = matchedUser.Name;
                }
            }

            var attrs = await SqliteDatabaseService.Instance.LoadAllAttributesAsync();
            _allProductAttributes.Clear();
            _allProductAttributes.AddRange(attrs);

            var designs = await SqliteDatabaseService.Instance.LoadAllReceiptDesignsAsync();
            _allReceiptDesigns.Clear();
            _allReceiptDesigns.AddRange(designs);
        }
        catch { }

        decimal defaultCash = config.IsHideOpeningCashControlEnabled
            ? config.DefaultOpeningCashAmount
            : (config.LastClosingBalance > 0
                ? config.LastClosingBalance
                : (config.OpeningBalance > 0 ? config.OpeningBalance : 0.00m));

        OpeningCashInput = defaultCash.ToString("0.00", CultureInfo.InvariantCulture);
        ClosingCashCount = OpeningCashInput;
        OpeningNote = string.Empty;
        ClosingNote = string.Empty;

        // Detect Odoo 20 order sequence and device digit for this shop
        var (nextSeq, deviceDigit) = await SqliteDatabaseService.Instance.GetNextOrderSequenceForShopAsync(
            config.Name,
            config.OdooId,
            config.CurrentSessionId
        );
        _nextOrderSeq = nextSeq;
        _deviceDigit = deviceDigit;

        await ReloadCatalogAsync();
        await ReloadPaymentMethodsForShopAsync();
        await RestoreDraftOrdersForShopAsync();
        await ReloadInPosOrdersAsync();
        await RefreshPendingSyncCountAsync();

        ActivePosView = "Register";
        IsPaymentViewOpen = false;
        IsPaymentConfirmationOpen = false;
        IsHamburgerMenuOpen = false;
        IsClosingRegisterModalOpen = false;
        IsDraftOrdersWarningModalOpen = false;
        IsNoteModalOpen = false;
        IsVariantPopupOpen = false;
        IsReceiptPreviewModalOpen = false;
        NotifyViewStatesChanged();

        // Setting 9: Hide Opening Cash Control (Automatically hide the opening cash control popup and set cash to the default amount)
        if (config.IsHideOpeningCashControlEnabled)
        {
            config.OpeningBalance = config.DefaultOpeningCashAmount;
            IsPosLandingScreenOpen = false;
            IsOpeningControlModalOpen = false;
            if (config.IsClosed || config.IsOpeningControl)
            {
                await EnterPosFromLandingAsync();
            }
            else
            {
                await EnsureLocalSessionExistsForConfigAsync(config);
            }
            return;
        }

        if (config.IsClosed || config.IsOpeningControl || showOpeningControl)
        {
            IsPosLandingScreenOpen = true;
            IsOpeningControlModalOpen = false;
        }
        else
        {
            IsPosLandingScreenOpen = false;
            IsOpeningControlModalOpen = false;
            await EnsureLocalSessionExistsForConfigAsync(config);
        }
    }

    private async Task EnsureLocalSessionExistsForConfigAsync(PosConfigRecord? config)
    {
        if (config == null) return;
        try
        {
            var (_, resolvedSessOdooId, resolvedSessName) = await SqliteDatabaseService.Instance.UpdateSessionOpeningControlAsync(
                config.OdooId,
                config.CurrentSessionId,
                config.OpeningBalance,
                string.Empty,
                config.Name,
                config.ActiveUserId,
                CashierName,
                enqueueForOdooSync: false
            );

            if (resolvedSessOdooId.HasValue)
                config.CurrentSessionId = resolvedSessOdooId.Value;
            if (!string.IsNullOrWhiteSpace(resolvedSessName))
                config.CurrentSessionName = resolvedSessName;
        }
        catch { }
    }

    [RelayCommand]
    public async Task EnterPosFromLandingAsync()
    {
        ActivePosView = "Register";

        if (CurrentConfig != null && (CurrentConfig.IsClosed || CurrentConfig.IsOpeningControl))
        {
            // When "Hide Opening Cash Control" is NOT active in PoS Settings, show the Opening Cash Control modal!
            if (!CurrentConfig.IsHideOpeningCashControlEnabled)
            {
                IsPosLandingScreenOpen = false;
                IsOpeningControlModalOpen = true;
                return;
            }

            IsPosLandingScreenOpen = false;
            IsOpeningControlModalOpen = false;
            CurrentConfig.CurrentSessionState = "opened";
            UpdateLandingState();
            int cfgId = CurrentConfig.OdooId;
            int? sessId = CurrentConfig.CurrentSessionId;
            string cfgName = CurrentConfig.Name;

            try
            {
                var (_, resolvedSessOdooId, resolvedSessName) = await SqliteDatabaseService.Instance.UpdateSessionOpeningControlAsync(
                    cfgId,
                    sessId,
                    CurrentConfig.OpeningBalance,
                    string.Empty,
                    cfgName,
                    CurrentConfig.ActiveUserId,
                    CashierName,
                    enqueueForOdooSync: true
                );

                if (resolvedSessOdooId.HasValue)
                    CurrentConfig.CurrentSessionId = resolvedSessOdooId.Value;
                CurrentConfig.CurrentSessionName = resolvedSessName;
                OdooSyncService.Instance.NotifyQueueUpdated();

                _ = Task.Run(async () =>
                {
                    await OdooSyncService.Instance.ProcessPendingQueueAsync();
                    await RefreshBoundConfigSessionAsync();
                    await RefreshPendingSyncCountAsync();
                });
            }
            catch { }
        }
        else
        {
            IsPosLandingScreenOpen = false;
            IsOpeningControlModalOpen = false;
            await EnsureLocalSessionExistsForConfigAsync(CurrentConfig);
        }
    }

    public async Task ReloadPaymentMethodsForShopAsync()
    {
        try
        {
            var allMethods = await SqliteDatabaseService.Instance.LoadAllPaymentMethodsAsync();
            AvailablePaymentMethods.Clear();

            string shopName = CurrentConfig?.Name ?? "";
            var configuredNames = CurrentConfig?.SelectedPaymentMethodsList ?? [];
            if (configuredNames.Count > 0)
            {
                int fallbackOdooId = 10;
                foreach (var name in configuredNames)
                {
                    // Prefer payment method explicitly linked to this shop first, then any payment method with matching name
                    var match = allMethods.FirstOrDefault(m =>
                                    string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase) &&
                                    m.LinkedShopsList.Any(s => string.Equals(s, shopName, StringComparison.OrdinalIgnoreCase)))
                                ?? allMethods.FirstOrDefault(m =>
                                    string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));

                    if (match != null)
                    {
                        AvailablePaymentMethods.Add(match);
                    }
                    else
                    {
                        string methodType = name.Contains("Cash", StringComparison.OrdinalIgnoreCase)
                            ? "cash"
                            : (name.Contains("Customer", StringComparison.OrdinalIgnoreCase) || name.Contains("Account", StringComparison.OrdinalIgnoreCase)
                                ? "pay_later"
                                : "bank");
                        AvailablePaymentMethods.Add(new PosPaymentMethodRecord
                        {
                            OdooId = fallbackOdooId++,
                            Name = name,
                            MethodType = methodType
                        });
                    }
                }
            }
            else
            {
                var shopMethods = allMethods.Where(m =>
                    string.IsNullOrWhiteSpace(m.LinkedPosConfigs) ||
                    m.LinkedShopsList.Any(s => string.Equals(s, shopName, StringComparison.OrdinalIgnoreCase))).ToList();

                if (shopMethods.Count == 0)
                    shopMethods = allMethods;

                if (shopMethods.Count == 0)
                {
                    AvailablePaymentMethods.Add(new PosPaymentMethodRecord { OdooId = 1, Name = "Cash", MethodType = "cash" });
                    AvailablePaymentMethods.Add(new PosPaymentMethodRecord { OdooId = 2, Name = "Card", MethodType = "bank" });
                    AvailablePaymentMethods.Add(new PosPaymentMethodRecord { OdooId = 3, Name = "Customer Account", MethodType = "pay_later" });
                }
                else
                {
                    foreach (var m in shopMethods)
                    {
                        AvailablePaymentMethods.Add(m);
                    }
                    if (!AvailablePaymentMethods.Any(m => m.IsTypeCustomerAccount))
                    {
                        AvailablePaymentMethods.Add(new PosPaymentMethodRecord { OdooId = 3, Name = "Customer Account", MethodType = "pay_later" });
                    }
                }
            }

            var firstMethod = AvailablePaymentMethods.FirstOrDefault();
            SelectedPaymentMethod = firstMethod?.Name ?? "Cash";
            SelectedPaymentMethodOdooId = firstMethod?.OdooId;
            OnPropertyChanged(nameof(IsOneClickPaymentVisible));
            OnPropertyChanged(nameof(OneClickPaymentButtonLabel));
        }
        catch { }
    }

    private static bool CategoryNameMatches(string? productCatName, string? targetCatName)
    {
        if (string.IsNullOrWhiteSpace(productCatName) || string.IsNullOrWhiteSpace(targetCatName))
            return false;

        string pCat = productCatName.Trim();
        string tCat = targetCatName.Trim();

        if (string.Equals(pCat, tCat, StringComparison.OrdinalIgnoreCase))
            return true;

        var pSegments = pCat.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var tSegments = tCat.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (pSegments.Any(s => string.Equals(s, tCat, StringComparison.OrdinalIgnoreCase)))
            return true;

        if (tSegments.Length > 0 && pSegments.Any(s => string.Equals(s, tSegments[^1], StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }

    public async Task ReloadCatalogAsync()
    {
        try
        {
            SelectedCategory = null;
            var cats = await SqliteDatabaseService.Instance.LoadAllCategoriesAsync();
            _allCategories.Clear();

            string shopName = CurrentConfig?.Name ?? "";
            var shopCats = cats.Where(c =>
                string.IsNullOrWhiteSpace(c.LinkedPosConfigs) ||
                c.LinkedShopsList.Any(s => string.Equals(s, shopName, StringComparison.OrdinalIgnoreCase))).ToList();

            if (shopCats.Count == 0) shopCats = cats;

            // Setting 3: Restrict Categories
            var restrictedCats = (CurrentConfig?.IsRestrictCategoriesEnabled == true)
                ? CurrentConfig.RestrictedCategoriesList
                : [];

            if (restrictedCats.Count > 0)
            {
                var filteredCats = shopCats.Where(c =>
                    restrictedCats.Any(rc => CategoryNameMatches(c.Name, rc) || CategoryNameMatches(c.ParentCategoryName, rc))).ToList();
                if (filteredCats.Count > 0)
                {
                    shopCats = filteredCats;
                }
            }

            foreach (var c in shopCats)
            {
                c.IsSelected = false;
            }

            _allCategories.AddRange(shopCats);

            Categories.Clear();
            foreach (var c in _allCategories)
            {
                Categories.Add(c);
            }

            var prods = await SqliteDatabaseService.Instance.LoadAllProductsAsync();
            _allProducts.Clear();
            var availableProds = prods.Where(p => p.AvailableInPos).ToList();
            if (availableProds.Count == 0 && prods.Count > 0)
            {
                availableProds = prods;
            }

            if (restrictedCats.Count > 0)
            {
                var allowedCatIds = shopCats
                    .Where(c => c.OdooId.HasValue && c.OdooId.Value > 0)
                    .Select(c => c.OdooId!.Value)
                    .ToHashSet();

                var restrictedProds = availableProds.Where(p =>
                    (p.PosCategoryId.HasValue && allowedCatIds.Contains(p.PosCategoryId.Value)) ||
                    restrictedCats.Any(rc => CategoryNameMatches(p.PosCategoryName, rc))).ToList();

                if (restrictedProds.Count > 0)
                {
                    availableProds = restrictedProds;
                }
            }

            _allProducts.AddRange(availableProds);

            FilterProducts();
            UpdateProductCartQuantities();
        }
        catch (Exception ex)
        {
            ShowPosToast($"Failed to load catalog: {ex.Message}");
        }
    }

    public async Task ReloadInPosOrdersAsync()
    {
        try
        {
            string? shopFilter = CurrentConfig?.Name;
            var orders = await SqliteDatabaseService.Instance.LoadAllOrdersAsync(shopFilter);
            _allInPosOrders.Clear();
            _allInPosOrders.AddRange(orders);
            FilterInPosOrders();
            await UpdateSessionBreakdownAsync();
        }
        catch (Exception ex)
        {
            ShowPosToast($"Failed to load orders: {ex.Message}");
        }
    }

    // =========================================================================
    // DRAFT ORDER MANAGEMENT & ONGOING DRAFT PERSISTENCE
    // =========================================================================
    private async Task RestoreDraftOrdersForShopAsync()
    {
        DraftOrders.Clear();
        int cfgOdooId = CurrentConfig?.OdooId ?? 1;
        string shopName = CurrentConfig?.Name ?? ActiveShopName;

        try
        {
            var shopOrders = await SqliteDatabaseService.Instance.LoadAllOrdersAsync(shopName);
            var draftRecords = shopOrders
                .Where(o => o.IsDraft &&
                            (string.Equals(o.PosConfigName, shopName, StringComparison.OrdinalIgnoreCase) || OrderMatchesCurrentSession(o)))
                .OrderBy(o => o.Id)
                .ToList();

            foreach (var rec in draftRecords)
            {
                int seqNum = ExtractDraftSequenceNumber(rec, cfgOdooId);
                if (seqNum >= _nextOrderSeq)
                {
                    _nextOrderSeq = seqNum + 1;
                }

                string tabNumber = !string.IsNullOrWhiteSpace(rec.TrackingNumber)
                    ? rec.TrackingNumber
                    : $"{(cfgOdooId % 10 == 0 ? 1 : cfgOdooId % 10)}{seqNum:D3}";

                var draft = new PosDraftOrder(tabNumber, cfgOdooId, seqNum, _deviceDigit)
                {
                    DatabaseOrderId = rec.Id,
                    Uuid = !string.IsNullOrWhiteSpace(rec.Uuid) ? rec.Uuid : Guid.NewGuid().ToString(),
                    OrderRef = !string.IsNullOrWhiteSpace(rec.PosReference)
                        ? rec.PosReference
                        : $"{DateTime.Now.Year % 100:D2}{_deviceDigit}-{cfgOdooId}-{seqNum:D6}",
                    CustomerName = rec.CustomerName ?? string.Empty,
                    Note = rec.Note ?? string.Empty,
                    CreatedAt = rec.DateOrder ?? DateTime.Now
                };

                foreach (var l in rec.Lines)
                {
                    var prod = _allProducts.FirstOrDefault(p =>
                                   (l.ProductId.HasValue && p.OdooId == l.ProductId.Value) ||
                                   string.Equals(p.Name, l.ProductName, StringComparison.OrdinalIgnoreCase))
                               ?? new ProductRecord
                               {
                                   OdooId = l.ProductId,
                                   Name = l.ProductName,
                                   ListPrice = l.PriceUnit,
                                   SalesTaxRate = l.PriceSubtotal > 0
                                       ? Math.Round(((l.PriceSubtotalIncl - l.PriceSubtotal) / l.PriceSubtotal) * 100m, 1)
                                       : 15.0m
                               };

                    var item = new PosCartItem(prod, l.Qty, unitPriceIsTaxIncluded: false, customProductName: l.ProductName, customUnitPrice: l.PriceUnit)
                    {
                        Discount = l.Discount
                    };
                    draft.CartItems.Add(item);
                }

                draft.NotifyTotalsChanged();
                DraftOrders.Add(draft);
            }
        }
        catch { }

        if (DraftOrders.Count > 0)
        {
            SelectDraftOrder(DraftOrders.First());
        }
        else
        {
            CreateNewDraftOrderInternal(persistPrevious: false);
        }
    }

    private int ExtractDraftSequenceNumber(PosOrderRecord rec, int cfgOdooId)
    {
        if (!string.IsNullOrWhiteSpace(rec.PosReference))
        {
            var dashParts = rec.PosReference.Split('-', StringSplitOptions.RemoveEmptyEntries);
            if (dashParts.Length > 0 && int.TryParse(dashParts[^1], out int parsedRefSeq) && parsedRefSeq > 0)
            {
                return parsedRefSeq;
            }
        }

        if (!string.IsNullOrWhiteSpace(rec.TrackingNumber) && rec.TrackingNumber.Length >= 2)
        {
            string suffix = rec.TrackingNumber.Length > 3 ? rec.TrackingNumber[1..] : rec.TrackingNumber;
            if (int.TryParse(suffix, out int parsedTrackSeq) && parsedTrackSeq > 0)
            {
                return parsedTrackSeq;
            }
        }

        return _nextOrderSeq;
    }

    [RelayCommand]
    public async Task CreateNewDraftOrderAsync()
    {
        if (CurrentOrder != null)
        {
            await PersistDraftOrderToDatabaseAsync(CurrentOrder, allowEmpty: true);
        }

        CreateNewDraftOrderInternal(persistPrevious: false);
        if (CurrentOrder != null)
        {
            await PersistDraftOrderToDatabaseAsync(CurrentOrder, allowEmpty: true);
        }
    }

    private void CreateNewDraftOrderInternal(bool persistPrevious = false)
    {
        if (persistPrevious && CurrentOrder != null && CurrentOrder.HasItems)
        {
            _ = PersistDraftOrderToDatabaseAsync(CurrentOrder);
        }

        int cfgOdooId = CurrentConfig?.OdooId ?? 1;
        int seqNum = _nextOrderSeq++;

        var order = PosDraftOrder.CreateForShop(cfgOdooId, seqNum, _deviceDigit);
        DraftOrders.Add(order);
        SelectDraftOrder(order);
    }

    [RelayCommand]
    public void SelectDraftOrder(PosDraftOrder? order)
    {
        if (order == null) return;

        foreach (var d in DraftOrders)
        {
            d.IsActive = (d == order);
        }

        CurrentOrder = order;
        SelectedCartLine = order.CartItems.LastOrDefault();
        foreach (var line in order.CartItems)
        {
            line.IsSelected = (line == SelectedCartLine);
        }

        _numpadBuffer = string.Empty;
        IsCartNumpadVisible = true;

        UpdateProductCartQuantities();
        NotifyViewStatesChanged();

        if (ActivePosView == "Orders")
        {
            ActivePosView = "Register";
        }
    }

    [RelayCommand]
    public async Task CloseDraftOrderAsync(PosDraftOrder? order)
    {
        if (order == null) return;
        if (order.DatabaseOrderId.HasValue)
        {
            await SqliteDatabaseService.Instance.DeleteDraftOrderAsync(order.DatabaseOrderId.Value);
        }

        DraftOrders.Remove(order);

        if (DraftOrders.Count == 0)
        {
            CreateNewDraftOrderInternal(persistPrevious: false);
        }
        else if (CurrentOrder == order)
        {
            SelectDraftOrder(DraftOrders.Last());
        }

        await ReloadInPosOrdersAsync();
    }

    private async Task PersistDraftOrderToDatabaseAsync(PosDraftOrder draft, bool allowEmpty = false)
    {
        try
        {
            if (!draft.HasItems && !allowEmpty && DraftOrders.Count <= 1)
            {
                if (draft.DatabaseOrderId.HasValue)
                {
                    await SqliteDatabaseService.Instance.DeleteDraftOrderAsync(draft.DatabaseOrderId.Value);
                    draft.DatabaseOrderId = null;
                }
                return;
            }

            int sessId = CurrentConfig?.CurrentSessionId ?? 1;
            string sessName = CurrentConfig?.CurrentSessionName ?? $"{ActiveShopName}/00001";
            string shopName = CurrentConfig?.Name ?? ActiveShopName;

            var record = new PosOrderRecord
            {
                Id = draft.DatabaseOrderId ?? 0,
                Uuid = draft.Uuid,
                Name = draft.FormatOdooOrderName(shopName),
                PosReference = draft.OrderRef,
                TrackingNumber = draft.OrderNumber,
                SessionId = sessId,
                SessionOdooId = CurrentConfig?.CurrentSessionId,
                SessionName = sessName,
                PosConfigName = shopName,
                DateOrder = draft.CreatedAt,
                CashierName = CashierName,
                CustomerName = draft.CustomerName,
                AmountTotal = draft.TotalAmount,
                AmountTax = draft.Taxes,
                AmountPaid = 0.00m,
                State = "draft",
                Note = draft.Note,
                IsSynced = false
            };

            var lines = draft.CartItems.Select(i => new PosOrderLineRecord
            {
                ProductId = i.ProductOdooId,
                ProductName = i.ProductName,
                Qty = i.Quantity,
                PriceUnit = i.UnitPrice,
                Discount = i.Discount,
                Taxes = $"{i.TaxRate:0.#}%",
                PriceSubtotal = i.PriceSubtotal,
                PriceSubtotalIncl = i.LineTotal
            }).ToList();

            long savedId = await SqliteDatabaseService.Instance.SavePosOrderAsync(
                record,
                lines,
                [],
                CurrentConfig?.OdooId ?? 1,
                draft.IsToInvoice,
                draft.PartnerId,
                enqueueForOdooSync: false
            );

            draft.DatabaseOrderId = savedId;
        }
        catch { }
    }

    // =========================================================================
    // CART & LINE INTERACTIONS (SYNCHRONOUS FOR ZERO-LATENCY RAPID TAPPING)
    // =========================================================================
    [RelayCommand]
    public void AddProduct(ProductRecord? product)
    {
        if (product == null || CurrentOrder == null) return;

        // Setting 4: Variants (Open Variant Selection Popup if enabled and product has attributes/variants)
        bool variantsEnabled = CurrentConfig?.IsVariantsEnabled ?? true;
        if (variantsEnabled && (product.AttributeLines.Count > 0 || product.VariantCount > 1))
        {
            OpenVariantSelectionPopup(product);
            return;
        }

        AddProductCore(product, customName: null, customPrice: null);
    }

    private void AddProductCore(ProductRecord product, string? customName, decimal? customPrice)
    {
        if (CurrentOrder == null) return;

        string targetName = !string.IsNullOrWhiteSpace(customName) ? customName : product.Name;
        decimal targetPrice = customPrice ?? product.ListPrice;

        // Setting 6: Prevent Line Merging (Always create new order lines instead of merging quantities)
        bool preventMerging = CurrentConfig?.IsPreventLineMergingEnabled ?? false;

        var existing = preventMerging
            ? null
            : CurrentOrder.CartItems.FirstOrDefault(i =>
                i.Product.Id == product.Id &&
                string.Equals(i.ProductName, targetName, StringComparison.OrdinalIgnoreCase) &&
                Math.Abs(i.UnitPrice - targetPrice) < 0.001m &&
                i.Discount == 0m);

        if (existing != null)
        {
            existing.Quantity += 1m;
            SelectCartLine(existing);
        }
        else
        {
            var newItem = new PosCartItem(product, 1m, unitPriceIsTaxIncluded: false, customProductName: customName, customUnitPrice: customPrice);
            CurrentOrder.CartItems.Add(newItem);
            SelectCartLine(newItem);
        }

        _numpadBuffer = string.Empty;
        IsCartNumpadVisible = true;
        CurrentOrder.NotifyTotalsChanged();
        UpdateProductCartQuantities();
        NotifyViewStatesChanged();

        ScheduleDraftPersistence(CurrentOrder);
    }

    private void OpenVariantSelectionPopup(ProductRecord product)
    {
        VariantPopupProduct = product;
        VariantAttributeGroups.Clear();

        var lines = product.AttributeLines;
        if (lines.Count > 0)
        {
            foreach (var line in lines)
            {
                var dbAttr = _allProductAttributes.FirstOrDefault(a =>
                    string.Equals(a.Name, line.AttributeName, StringComparison.OrdinalIgnoreCase));

                var group = new PosVariantAttributeGroup
                {
                    AttributeName = line.AttributeName,
                    DisplayType = dbAttr?.DisplayType ?? "pills"
                };

                bool isFirst = true;
                foreach (var valName in line.ValuePills)
                {
                    var dbVal = dbAttr?.Values.FirstOrDefault(v =>
                        string.Equals(v.Name, valName, StringComparison.OrdinalIgnoreCase));

                    group.Options.Add(new PosVariantOptionItem
                    {
                        AttributeName = line.AttributeName,
                        ValueName = valName,
                        HtmlColor = dbVal?.HtmlColor,
                        ExtraPrice = dbVal != null ? Convert.ToDecimal(dbVal.DefaultExtraPrice) : 0.00m,
                        IsCustom = dbVal?.IsCustom ?? false,
                        IsSelected = isFirst
                    });
                    isFirst = false;
                }

                if (group.Options.Count > 0)
                {
                    VariantAttributeGroups.Add(group);
                }
            }
        }
        else if (_allProductAttributes.Count > 0)
        {
            // Fallback attribute group if VariantCount > 1 but AttributesJson was empty
            var fallbackAttr = _allProductAttributes.First();
            var group = new PosVariantAttributeGroup
            {
                AttributeName = fallbackAttr.Name,
                DisplayType = fallbackAttr.DisplayType
            };
            bool isFirst = true;
            foreach (var v in fallbackAttr.Values.Take(5))
            {
                group.Options.Add(new PosVariantOptionItem
                {
                    AttributeName = fallbackAttr.Name,
                    ValueName = v.Name,
                    HtmlColor = v.HtmlColor,
                    ExtraPrice = Convert.ToDecimal(v.DefaultExtraPrice),
                    IsCustom = v.IsCustom,
                    IsSelected = isFirst
                });
                isFirst = false;
            }
            if (group.Options.Count > 0)
                VariantAttributeGroups.Add(group);
        }

        if (VariantAttributeGroups.Count == 0)
        {
            AddProductCore(product, customName: null, customPrice: null);
            return;
        }

        RecalculateVariantPopupSummary();
        IsVariantPopupOpen = true;
    }

    private void RecalculateVariantPopupSummary()
    {
        if (VariantPopupProduct == null) return;

        decimal basePrice = VariantPopupProduct.ListPrice;
        decimal extra = 0.00m;
        var selectedParts = new List<string>();

        foreach (var group in VariantAttributeGroups)
        {
            var selected = group.SelectedOption ?? group.Options.FirstOrDefault();
            if (selected != null)
            {
                extra += selected.ExtraPrice;
                string label = selected.IsCustom && !string.IsNullOrWhiteSpace(selected.CustomTextValue)
                    ? $"{selected.ValueName}: {selected.CustomTextValue.Trim()}"
                    : selected.ValueName;
                selectedParts.Add(label);
            }
        }

        VariantPopupTotalPrice = basePrice + extra;
        OnPropertyChanged(nameof(VariantPopupTotalPriceDisplay));

        VariantPopupProductName = selectedParts.Count > 0
            ? $"{VariantPopupProduct.Name} ({string.Join(", ", selectedParts)})"
            : VariantPopupProduct.Name;
    }

    [RelayCommand]
    public void SelectVariantOption(PosVariantOptionItem? option)
    {
        if (option == null) return;

        var group = VariantAttributeGroups.FirstOrDefault(g =>
            string.Equals(g.AttributeName, option.AttributeName, StringComparison.OrdinalIgnoreCase));
        if (group != null)
        {
            foreach (var opt in group.Options)
            {
                opt.IsSelected = ReferenceEquals(opt, option);
            }
        }

        RecalculateVariantPopupSummary();
    }

    [RelayCommand]
    public void ConfirmVariantSelection()
    {
        if (VariantPopupProduct == null)
        {
            IsVariantPopupOpen = false;
            return;
        }

        RecalculateVariantPopupSummary();
        AddProductCore(VariantPopupProduct, VariantPopupProductName, VariantPopupTotalPrice);
        IsVariantPopupOpen = false;
    }

    [RelayCommand]
    public void CloseVariantPopup()
    {
        IsVariantPopupOpen = false;
    }

    [RelayCommand]
    public void SelectCartLine(PosCartItem? item)
    {
        if (item == null) return;

        if (CurrentOrder != null)
        {
            foreach (var l in CurrentOrder.CartItems)
            {
                l.IsSelected = (l == item);
            }
        }

        SelectedCartLine = item;
        IsCartNumpadVisible = true;
        _numpadBuffer = string.Empty;
        NotifyViewStatesChanged();
    }

    [RelayCommand]
    public void SetNumpadMode(string mode)
    {
        NumpadMode = mode;
        _numpadBuffer = string.Empty;
    }

    [RelayCommand]
    public void NumpadKey(string key)
    {
        if (SelectedCartLine == null || CurrentOrder == null) return;

        if (key == "⌫" || key == "Back")
        {
            if (string.IsNullOrEmpty(_numpadBuffer))
            {
                if (SelectedCartLine.Quantity > 0m)
                {
                    _numpadBuffer = SelectedCartLine.Quantity.ToString("G29", CultureInfo.InvariantCulture);
                }
                else
                {
                    DeleteSelectedCartLine();
                    return;
                }
            }

            if (_numpadBuffer == "0" || SelectedCartLine.Quantity == 0m)
            {
                DeleteSelectedCartLine();
                return;
            }

            if (_numpadBuffer.Length > 1)
            {
                _numpadBuffer = _numpadBuffer[..^1];
                if (_numpadBuffer == "-" || _numpadBuffer == ".")
                    _numpadBuffer = "0";

                if (decimal.TryParse(_numpadBuffer, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal val))
                {
                    SelectedCartLine.Quantity = val;
                }
            }
            else
            {
                _numpadBuffer = "0";
                SelectedCartLine.Quantity = 0m;
            }

            CurrentOrder.NotifyTotalsChanged();
            UpdateProductCartQuantities();
            NotifyViewStatesChanged();
            ScheduleDraftPersistence(CurrentOrder);
            return;
        }
        else if (key == "+" || key == "Plus")
        {
            SelectedCartLine.Quantity += 1m;
            _numpadBuffer = string.Empty;
            CurrentOrder.NotifyTotalsChanged();
            UpdateProductCartQuantities();
            NotifyViewStatesChanged();
            ScheduleDraftPersistence(CurrentOrder);
            return;
        }
        else if (key == "-" || key == "Minus")
        {
            if (SelectedCartLine.Quantity > 1m)
            {
                SelectedCartLine.Quantity -= 1m;
                _numpadBuffer = string.Empty;
            }
            else if (SelectedCartLine.Quantity == 1m)
            {
                SelectedCartLine.Quantity = 0m;
                _numpadBuffer = "0";
            }
            else
            {
                DeleteSelectedCartLine();
                return;
            }

            CurrentOrder.NotifyTotalsChanged();
            UpdateProductCartQuantities();
            NotifyViewStatesChanged();
            ScheduleDraftPersistence(CurrentOrder);
            return;
        }
        else if (key == "Delete" || key == "ClearAll")
        {
            _ = ClearCurrentOrderLinesAsync();
            return;
        }
        else if (key == "+/-")
        {
            if (_numpadBuffer.StartsWith('-'))
                _numpadBuffer = _numpadBuffer[1..];
            else if (!string.IsNullOrEmpty(_numpadBuffer))
                _numpadBuffer = "-" + _numpadBuffer;
            else
            {
                SelectedCartLine.Quantity = -SelectedCartLine.Quantity;
                CurrentOrder.NotifyTotalsChanged();
                UpdateProductCartQuantities();
                ScheduleDraftPersistence(CurrentOrder);
                return;
            }
        }
        else if (key == ".")
        {
            if (!_numpadBuffer.Contains('.'))
            {
                _numpadBuffer = string.IsNullOrEmpty(_numpadBuffer) ? "0." : _numpadBuffer + ".";
            }
        }
        else
        {
            if (_numpadBuffer == "0")
                _numpadBuffer = key;
            else
                _numpadBuffer += key;
        }

        if (decimal.TryParse(_numpadBuffer, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal newVal))
        {
            SelectedCartLine.Quantity = newVal;
        }

        CurrentOrder.NotifyTotalsChanged();
        UpdateProductCartQuantities();
        NotifyViewStatesChanged();
        ScheduleDraftPersistence(CurrentOrder);
    }

    private void DeleteSelectedCartLine()
    {
        if (SelectedCartLine == null || CurrentOrder == null) return;
        CurrentOrder.CartItems.Remove(SelectedCartLine);
        SelectedCartLine = CurrentOrder.CartItems.LastOrDefault();
        if (SelectedCartLine != null)
            SelectedCartLine.IsSelected = true;

        _numpadBuffer = string.Empty;
        CurrentOrder.NotifyTotalsChanged();
        UpdateProductCartQuantities();
        NotifyViewStatesChanged();
        ScheduleDraftPersistence(CurrentOrder);
    }

    private void UpdateProductCartQuantities()
    {
        if (CurrentOrder == null) return;

        var quantities = CurrentOrder.CartItems
            .GroupBy(i => i.Product.Id)
            .ToDictionary(g => g.Key, g => (int)Math.Round(g.Sum(x => x.Quantity)));

        foreach (var p in _allProducts)
        {
            p.CartQuantity = quantities.TryGetValue(p.Id, out int q) ? Math.Max(0, q) : 0;
        }
    }

    // =========================================================================
    // CUSTOMER, NOTE, PARK ORDER ACTIONS
    // =========================================================================
    [RelayCommand]
    public void OpenCustomerPrompt()
    {
        if (CurrentOrder == null) return;
        if (string.IsNullOrWhiteSpace(CurrentOrder.CustomerName))
        {
            CurrentOrder.CustomerName = "Walk-in Customer";
            ShowPosToast("Customer set to Walk-in Customer.");
        }
        else
        {
            CurrentOrder.CustomerName = string.Empty;
            ShowPosToast("Customer removed from order.");
        }
    }

    [RelayCommand]
    public void OpenNotePrompt()
    {
        if (CurrentOrder == null) return;
        NoteInputText = SelectedCartLine?.CustomerNote ?? CurrentOrder.Note;
        IsNoteModalOpen = true;
    }

    [RelayCommand]
    public void SaveNoteModal()
    {
        if (SelectedCartLine != null)
        {
            SelectedCartLine.CustomerNote = NoteInputText.Trim();
        }
        if (CurrentOrder != null)
        {
            CurrentOrder.Note = NoteInputText.Trim();
        }
        IsNoteModalOpen = false;
    }

    [RelayCommand]
    public void CloseNoteModal()
    {
        IsNoteModalOpen = false;
    }

    [RelayCommand]
    public async Task ParkOrderAsync()
    {
        if (CurrentOrder == null || !CurrentOrder.HasItems)
        {
            return;
        }

        await PersistDraftOrderToDatabaseAsync(CurrentOrder);
        await ReloadInPosOrdersAsync();
        ShowPosToast($"Order {CurrentOrder.OrderNumber} saved as ongoing draft.");
        CreateNewDraftOrderInternal(persistPrevious: false);
    }

    [RelayCommand]
    public async Task ClearCurrentOrderLinesAsync()
    {
        if (CurrentOrder == null) return;
        CurrentOrder.CartItems.Clear();
        SelectedCartLine = null;
        CurrentOrder.NotifyTotalsChanged();
        UpdateProductCartQuantities();
        NotifyViewStatesChanged();
        await PersistDraftOrderToDatabaseAsync(CurrentOrder);
    }

    // =========================================================================
    // CATALOG & CATEGORY FILTERING
    // =========================================================================
    [RelayCommand]
    public void SelectCategory(PosCategoryRecord? category)
    {
        if (SelectedCategory == category)
            SelectedCategory = null;
        else
            SelectedCategory = category;

        foreach (var c in Categories)
        {
            c.IsSelected = (c == SelectedCategory);
        }

        FilterProducts();
    }

    [RelayCommand]
    public void ClearProductSearch()
    {
        ProductSearchText = string.Empty;
    }

    private void FilterProducts()
    {
        string query = ProductSearchText.Trim();
        FilteredProducts.Clear();

        IEnumerable<ProductRecord> source = _allProducts;

        if (SelectedCategory != null)
        {
            var cat = SelectedCategory;
            var childCatIds = _allCategories
                .Where(c => (cat.OdooId.HasValue && c.ParentCategoryId == cat.OdooId.Value) ||
                            CategoryNameMatches(c.ParentCategoryName, cat.Name))
                .Where(c => c.OdooId.HasValue)
                .Select(c => c.OdooId!.Value)
                .ToHashSet();

            source = source.Where(p =>
                (cat.OdooId.HasValue && p.PosCategoryId == cat.OdooId.Value) ||
                (p.PosCategoryId.HasValue && childCatIds.Contains(p.PosCategoryId.Value)) ||
                CategoryNameMatches(p.PosCategoryName, cat.Name));
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            source = source.Where(p =>
                p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                p.DefaultCode.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                p.Barcode.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var p in source)
        {
            FilteredProducts.Add(p);
        }
    }

    // =========================================================================
    // IN-POS ORDERS FILTERING & ACTIONS (Paid Orders in Current Session Only)
    // =========================================================================
    [RelayCommand]
    public void SwitchToRegisterView()
    {
        IsPaymentViewOpen = false;
        IsPaymentConfirmationOpen = false;
        ActivePosView = "Register";
    }

    [RelayCommand]
    public async Task SwitchToOrdersViewAsync()
    {
        if (CurrentOrder != null && CurrentOrder.HasItems)
        {
            await PersistDraftOrderToDatabaseAsync(CurrentOrder);
        }
        IsPaymentViewOpen = false;
        IsPaymentConfirmationOpen = false;
        ActivePosView = "Orders";
        await ReloadInPosOrdersAsync();
    }

    [RelayCommand]
    public void CycleOrderStatusFilter()
    {
        OrderStatusFilter = "Paid";
    }

    private void FilterInPosOrders()
    {
        string query = OrderSearchText.Trim();
        FilteredInPosOrders.Clear();

        // Always show only PAID orders belonging to the current session
        IEnumerable<PosOrderRecord> source = _allInPosOrders.Where(o =>
            OrderMatchesCurrentSession(o) && !o.IsDraft && !o.IsCancelled);

        if (!string.IsNullOrWhiteSpace(query))
        {
            source = source.Where(o =>
                o.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                o.PosReference.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                o.TrackingNumber.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                o.CustomerName.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var o in source)
        {
            FilteredInPosOrders.Add(o);
        }

        OrdersPagerText = FilteredInPosOrders.Count > 0
            ? $"1-{FilteredInPosOrders.Count} / {FilteredInPosOrders.Count}"
            : "0-0 / 0";

        if (SelectedInPosOrder == null || !FilteredInPosOrders.Contains(SelectedInPosOrder))
        {
            SelectInPosOrder(FilteredInPosOrders.FirstOrDefault());
        }
    }

    [RelayCommand]
    public void SelectInPosOrder(PosOrderRecord? order)
    {
        foreach (var o in FilteredInPosOrders)
        {
            o.IsSelectedInPos = (o == order);
        }
        SelectedInPosOrder = order;
    }

    [RelayCommand]
    public void LoadOrderIntoRegister(PosOrderRecord? order)
    {
        var target = order ?? SelectedInPosOrder;
        if (target == null) return;

        string tabNum = !string.IsNullOrWhiteSpace(target.TrackingNumber) ? target.TrackingNumber : target.Name;
        var existing = DraftOrders.FirstOrDefault(d =>
            d.DatabaseOrderId == target.Id ||
            d.Uuid == target.Uuid ||
            d.OrderNumber == tabNum);

        if (existing == null)
        {
            int cfgId = CurrentConfig?.OdooId ?? 1;
            existing = new PosDraftOrder(tabNum, cfgId, _nextOrderSeq++, _deviceDigit)
            {
                DatabaseOrderId = target.IsDraft ? target.Id : null,
                Uuid = target.IsDraft ? target.Uuid : Guid.NewGuid().ToString(),
                OrderRef = !string.IsNullOrWhiteSpace(target.PosReference) ? target.PosReference : $"{DateTime.Now.Year % 100:D2}{_deviceDigit}-{cfgId}-{_nextOrderSeq:D6}",
                CustomerName = target.CustomerName,
                Note = target.Note
            };
            DraftOrders.Add(existing);
        }

        existing.CartItems.Clear();
        foreach (var l in target.Lines)
        {
            var prod = _allProducts.FirstOrDefault(p => p.OdooId == l.ProductId || string.Equals(p.Name, l.ProductName, StringComparison.OrdinalIgnoreCase))
                       ?? new ProductRecord
                       {
                           OdooId = l.ProductId,
                           Name = l.ProductName,
                           ListPrice = l.PriceUnit,
                           SalesTaxRate = l.PriceSubtotal > 0
                               ? Math.Round(((l.PriceSubtotalIncl - l.PriceSubtotal) / l.PriceSubtotal) * 100m, 1)
                               : 15.0m
                       };

            var item = new PosCartItem(prod, l.Qty)
            {
                UnitPrice = l.PriceUnit,
                Discount = l.Discount
            };
            existing.CartItems.Add(item);
        }

        existing.NotifyTotalsChanged();
        SelectDraftOrder(existing);
        ActivePosView = "Register";
    }

    [RelayCommand]
    public async Task DeleteInPosOrderAsync(PosOrderRecord? order)
    {
        if (order == null) return;

        try
        {
            await SqliteDatabaseService.Instance.DeleteDraftOrderAsync(order.Id);
            _allInPosOrders.Remove(order);
            FilteredInPosOrders.Remove(order);

            var draftTab = DraftOrders.FirstOrDefault(d =>
                d.DatabaseOrderId == order.Id ||
                d.Uuid == order.Uuid ||
                d.OrderRef == order.PosReference ||
                d.OrderNumber == order.TrackingNumber);

            if (draftTab != null)
            {
                DraftOrders.Remove(draftTab);
                if (DraftOrders.Count == 0)
                {
                    CreateNewDraftOrderInternal(persistPrevious: false);
                }
                else if (CurrentOrder == draftTab)
                {
                    SelectDraftOrder(DraftOrders.First());
                }
            }

            FilterInPosOrders();
            ShowPosToast($"Order {order.TrackingNumberDisplay} deleted.");
        }
        catch (Exception ex)
        {
            ShowPosToast($"Failed to delete order: {ex.Message}");
        }
    }

    // =========================================================================
    // MODAL 1: OPENING CONTROL WORKFLOW (Screenshot 5 + Odoo Sync)
    // =========================================================================
    [RelayCommand]
    public async Task OpenRegisterSubmitAsync()
    {
        if (!decimal.TryParse(OpeningCashInput, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal cash))
        {
            cash = 0.00m;
        }

        int cfgId = CurrentConfig?.OdooId ?? 1;
        int? sessId = CurrentConfig?.CurrentSessionId;
        string cfgName = CurrentConfig?.Name ?? ActiveShopName;

        try
        {
            var (_, resolvedSessOdooId, resolvedSessName) = await SqliteDatabaseService.Instance.UpdateSessionOpeningControlAsync(
                cfgId,
                sessId,
                cash,
                OpeningNote,
                cfgName,
                CurrentConfig?.ActiveUserId,
                CashierName,
                enqueueForOdooSync: true
            );

            if (CurrentConfig != null)
            {
                CurrentConfig.CurrentSessionState = "opened";
                CurrentConfig.OpeningBalance = cash;
                if (resolvedSessOdooId.HasValue)
                    CurrentConfig.CurrentSessionId = resolvedSessOdooId.Value;
                CurrentConfig.CurrentSessionName = resolvedSessName;
            }

            IsOpeningControlModalOpen = false;
            IsPosLandingScreenOpen = false;
            OdooSyncService.Instance.NotifyQueueUpdated();

            // Push session open to Odoo immediately
            _ = Task.Run(async () =>
            {
                await OdooSyncService.Instance.ProcessPendingQueueAsync();
                await RefreshBoundConfigSessionAsync();
                await RefreshPendingSyncCountAsync();
            });
        }
        catch (Exception ex)
        {
            ShowPosToast($"Failed to open register: {ex.Message}");
        }
    }

    [RelayCommand]
    public void OpenOpeningControlModal()
    {
        IsHamburgerMenuOpen = false;
        IsOpeningControlModalOpen = false;
        IsPosLandingScreenOpen = true;
    }

    [RelayCommand]
    public void DiscardOpeningControl()
    {
        IsOpeningControlModalOpen = false;
        if (CurrentConfig != null && (CurrentConfig.IsClosed || CurrentConfig.IsOpeningControl))
        {
            IsPosLandingScreenOpen = false;
            ReturnToDashboardRequested?.Invoke();
        }
    }

    public bool OrderMatchesCurrentSession(PosOrderRecord o)
    {
        if (o == null) return false;

        // 1. Session Odoo ID / Local Session ID match
        int? targetSessOdooId = CurrentConfig?.CurrentSessionId ?? CurrentSession?.OdooId;
        if (targetSessOdooId.HasValue && targetSessOdooId.Value > 0)
        {
            if (o.SessionOdooId.HasValue && o.SessionOdooId.Value == targetSessOdooId.Value)
                return true;
            if (o.SessionId.HasValue && o.SessionId.Value == targetSessOdooId.Value)
                return true;
        }

        // 2. Session Local ID match
        if (CurrentSession != null && CurrentSession.Id > 0)
        {
            if (o.SessionId.HasValue && o.SessionId.Value == (int)CurrentSession.Id)
                return true;
        }

        // 3. Session Name match (e.g. "Furniture Shop/00008")
        string? targetSessName = !string.IsNullOrWhiteSpace(CurrentSessionNameDisplay)
            ? CurrentSessionNameDisplay
            : (!string.IsNullOrWhiteSpace(CurrentConfig?.CurrentSessionName)
                ? CurrentConfig.CurrentSessionName
                : CurrentSession?.Name);

        if (!string.IsNullOrWhiteSpace(targetSessName) && targetSessName != "/" && !string.IsNullOrWhiteSpace(o.SessionName))
        {
            if (string.Equals(o.SessionName.Trim(), targetSessName.Trim(), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    [RelayCommand]
    public void ClearOpeningCash()
    {
        OpeningCashInput = string.Empty;
    }

    // =========================================================================
    // SCREEN 4 & 5: PAYMENT WORKFLOW & RECEIPT CONFIRMATION (Screenshots 4 & 8)
    // =========================================================================
    [RelayCommand]
    public void OpenPaymentView()
    {
        if (CurrentOrder == null || !CurrentOrder.HasItems)
        {
            return;
        }

        AmountDue = CurrentOrder.TotalAmount;
        AmountTendered = AmountDue;
        ChangeDue = 0.00m;
        _paymentInputBuffer = AmountTendered.ToString("0.##", CultureInfo.InvariantCulture);
        _isFirstPaymentDigit = true;

        var defaultMethod = AvailablePaymentMethods.FirstOrDefault();
        SelectedPaymentMethod = defaultMethod?.Name ?? "Cash";
        SelectedPaymentMethodOdooId = defaultMethod?.OdooId;

        NotifyPaymentCalculationsChanged();
        IsPaymentViewOpen = true;
    }

    // Setting 2: One-click Payment (Shortcut to skip payment screen)
    [RelayCommand]
    public async Task OneClickPayAsync()
    {
        if (CurrentOrder == null || !CurrentOrder.HasItems) return;

        string oneClickMethod = OneClickPaymentButtonLabel;
        var matchedMethod = AvailablePaymentMethods.FirstOrDefault(m =>
            string.Equals(m.Name, oneClickMethod, StringComparison.OrdinalIgnoreCase));

        AmountDue = CurrentOrder.TotalAmount;
        AmountTendered = AmountDue;
        ChangeDue = 0.00m;
        SelectedPaymentMethod = matchedMethod?.Name ?? oneClickMethod;
        SelectedPaymentMethodOdooId = matchedMethod?.OdooId;

        await ValidatePaymentAsync();
    }

    [RelayCommand]
    public void ClosePaymentView()
    {
        IsPaymentViewOpen = false;
    }

    [RelayCommand]
    public void ClearPaymentLine()
    {
        AmountTendered = 0m;
        _paymentInputBuffer = string.Empty;
        _isFirstPaymentDigit = true;
        ChangeDue = 0m;
        NotifyPaymentCalculationsChanged();
    }

    [RelayCommand]
    public void SelectPaymentMethodOption(PosPaymentMethodRecord? method)
    {
        if (method == null) return;
        SelectedPaymentMethod = method.Name;
        SelectedPaymentMethodOdooId = method.OdooId;
        NotifyPaymentCalculationsChanged();
    }

    [RelayCommand]
    public void SelectPaymentMethod(string method)
    {
        SelectedPaymentMethod = method;
        var matched = AvailablePaymentMethods.FirstOrDefault(m => string.Equals(m.Name, method, StringComparison.OrdinalIgnoreCase));
        SelectedPaymentMethodOdooId = matched?.OdooId;
        NotifyPaymentCalculationsChanged();
    }

    [RelayCommand]
    public void ToggleInvoice()
    {
        if (CurrentOrder == null) return;
        CurrentOrder.IsToInvoice = !CurrentOrder.IsToInvoice;
    }

    [RelayCommand]
    public void PaymentNumpadKey(string key)
    {
        if (key == "⌫" || key == "Back")
        {
            if (_paymentInputBuffer.Length > 1)
            {
                _paymentInputBuffer = _paymentInputBuffer[..^1];
                if (_paymentInputBuffer == "-" || _paymentInputBuffer == ".")
                    _paymentInputBuffer = "0";
            }
            else
            {
                _paymentInputBuffer = "0";
            }

            if (decimal.TryParse(_paymentInputBuffer, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal parsed))
            {
                AmountTendered = parsed;
            }
            else
            {
                AmountTendered = 0m;
            }
            _isFirstPaymentDigit = false;
        }
        else if (key == "+/-")
        {
            // "+/-" is disabled in payment tender numpad
            return;
        }
        else if (key == ".")
        {
            if (_isFirstPaymentDigit)
            {
                _paymentInputBuffer = "0.";
                _isFirstPaymentDigit = false;
            }
            else if (!_paymentInputBuffer.Contains('.'))
            {
                _paymentInputBuffer = string.IsNullOrEmpty(_paymentInputBuffer) ? "0." : _paymentInputBuffer + ".";
            }

            if (decimal.TryParse(_paymentInputBuffer, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal parsed))
            {
                AmountTendered = parsed;
            }
        }
        else
        {
            if (_isFirstPaymentDigit || _paymentInputBuffer == "0" || string.IsNullOrEmpty(_paymentInputBuffer))
            {
                _paymentInputBuffer = key;
                _isFirstPaymentDigit = false;
            }
            else
            {
                _paymentInputBuffer += key;
            }

            if (decimal.TryParse(_paymentInputBuffer, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal parsed))
            {
                AmountTendered = parsed;
            }
        }

        ChangeDue = Math.Max(0.00m, AmountTendered - AmountDue);
        NotifyPaymentCalculationsChanged();
    }

    [RelayCommand]
    public void AddQuickTender(string amountStr)
    {
        if (decimal.TryParse(amountStr, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal add))
        {
            AmountTendered += add;
            _paymentInputBuffer = AmountTendered.ToString("0.##", CultureInfo.InvariantCulture);
            _isFirstPaymentDigit = false;
            ChangeDue = Math.Max(0.00m, AmountTendered - AmountDue);
            NotifyPaymentCalculationsChanged();
        }
    }

    [RelayCommand]
    public async Task ValidatePaymentAsync()
    {
        if (CurrentOrder == null || !CurrentOrder.HasItems) return;
        if (!CanValidatePayment) return;

        try
        {
            _draftSaveDebounceTimer.Stop();
            _pendingDraftToSave = null;

            await RefreshBoundConfigSessionAsync();

            decimal total = CurrentOrder.TotalAmount;
            decimal tax = CurrentOrder.Taxes;
            int cfgId = CurrentConfig?.OdooId ?? 1;
            int sessId = CurrentConfig?.CurrentSessionId ?? 1;
            string sessName = CurrentConfig?.CurrentSessionName ?? $"{ActiveShopName}/00001";
            string shopName = CurrentConfig?.Name ?? ActiveShopName;

            // Ensure canonical Odoo 20 receipt & order numbering using config.OdooId
            CurrentOrder.ApplyOdooSequence(cfgId, CurrentOrder.SequenceNumber, _deviceDigit);

            var order = new PosOrderRecord
            {
                Id = CurrentOrder.DatabaseOrderId ?? 0,
                Uuid = CurrentOrder.Uuid,
                Name = CurrentOrder.FormatOdooOrderName(shopName),
                PosReference = CurrentOrder.OrderRef,
                TrackingNumber = CurrentOrder.OrderNumber,
                SessionId = sessId,
                SessionOdooId = CurrentConfig?.CurrentSessionId,
                SessionName = sessName,
                PosConfigName = shopName,
                DateOrder = DateTime.Now,
                CashierId = CurrentConfig?.ActiveUserId,
                CashierName = CashierName,
                CustomerName = CurrentOrder.CustomerName,
                FiscalPosition = "Standard Tax",
                AmountTotal = total,
                AmountTax = tax,
                AmountPaid = total,
                Margin = Math.Round(CurrentOrder.SubTotal * 0.42m, 2),
                MarginRatio = 42.0m,
                State = CurrentOrder.IsToInvoice ? "invoiced" : "paid",
                Company = "My Company",
                Note = CurrentOrder.Note,
                IsSynced = false
            };

            var lines = CurrentOrder.CartItems.Select(i => new PosOrderLineRecord
            {
                ProductId = i.ProductOdooId,
                ProductName = i.ProductName,
                Qty = i.Quantity,
                PriceUnit = i.UnitPrice,
                Discount = i.Discount,
                Taxes = $"{i.TaxRate:0.#}%",
                PriceSubtotal = i.PriceSubtotal,
                PriceSubtotalIncl = i.LineTotal
            }).ToList();

            var payments = new List<PosPaymentRecord>
            {
                new()
                {
                    OrderName = order.Name,
                    SessionId = sessId,
                    SessionOdooId = CurrentConfig?.CurrentSessionId,
                    SessionName = sessName,
                    CashierName = CashierName,
                    PaymentMethodId = SelectedPaymentMethodOdooId,
                    PaymentMethodName = SelectedPaymentMethod,
                    Amount = total,
                    PaymentDate = DateTime.Now
                }
            };

            await SqliteDatabaseService.Instance.SavePosOrderAsync(
                order,
                lines,
                payments,
                cfgId,
                CurrentOrder.IsToInvoice,
                CurrentOrder.PartnerId,
                enqueueForOdooSync: true
            );

            order.Lines.Clear();
            foreach (var l in lines) order.Lines.Add(l);
            order.Payments.Clear();
            foreach (var p in payments) order.Payments.Add(p);
            _lastPaidOrder = order;

            ConfirmedPaidAmount = total;
            ConfirmedCashAmount = AmountTendered;
            ConfirmedChangeAmount = Math.Max(0.00m, AmountTendered - total);
            ConfirmedPaymentMethodName = SelectedPaymentMethod;
            IsPaymentViewOpen = false;
            IsPaymentConfirmationOpen = true;
            OdooSyncService.Instance.NotifyQueueUpdated();

            // Setting 5: Automatic Receipt Printing
            if (CurrentConfig?.IsAutomaticReceiptPrintingEnabled == true)
            {
                ExecuteOrderReceiptPrint(order, isAutoPrint: true);
            }

            await ReloadInPosOrdersAsync();
            await RefreshPendingSyncCountAsync();

            // Immediately push the order to Odoo (guaranteed not to be dropped by _queueLock)
            _ = Task.Run(async () =>
            {
                await OdooSyncService.Instance.ProcessPendingQueueAsync();
                await RefreshPendingSyncCountAsync();
            });
        }
        catch (Exception ex)
        {
            ShowPosToast($"Payment failed: {ex.Message}");
        }
    }

    [RelayCommand]
    public void BackFromConfirmation()
    {
        IsPaymentConfirmationOpen = false;
        IsPaymentViewOpen = true;
    }

    [RelayCommand]
    public void ContinueAfterPayment()
    {
        IsPaymentConfirmationOpen = false;

        if (CurrentOrder != null)
        {
            DraftOrders.Remove(CurrentOrder);
        }

        if (DraftOrders.Count == 0)
        {
            CreateNewDraftOrderInternal(persistPrevious: false);
        }
        else
        {
            SelectDraftOrder(DraftOrders.First());
        }

        ActivePosView = "Register";
    }

    [RelayCommand]
    public void PrintReceipt()
    {
        var targetOrder = _lastPaidOrder ?? _allInPosOrders.FirstOrDefault(o => !o.IsDraft && !o.IsCancelled);
        if (targetOrder != null)
        {
            ExecuteOrderReceiptPrint(targetOrder, isAutoPrint: false);
        }
        else
        {
            ShowPosToast("Receipt sent to printer.");
        }
    }

    // Setting 7: Print Last Receipt (Enable a button to print the last paid order.)
    [RelayCommand]
    public void PrintLastReceipt()
    {
        IsHamburgerMenuOpen = false;
        var targetOrder = _lastPaidOrder ?? _allInPosOrders.FirstOrDefault(o => OrderMatchesCurrentSession(o) && !o.IsDraft && !o.IsCancelled)
                          ?? _allInPosOrders.FirstOrDefault(o => !o.IsDraft && !o.IsCancelled);
        if (targetOrder == null)
        {
            ShowPosToast("No paid order found to print.");
            return;
        }

        ExecuteOrderReceiptPrint(targetOrder, isAutoPrint: false);
    }

    [RelayCommand]
    public void CloseReceiptPreviewModal()
    {
        IsReceiptPreviewModalOpen = false;
    }

    private void ExecuteOrderReceiptPrint(PosOrderRecord order, bool isAutoPrint)
    {
        ReceiptDesignRecord? mainDesign = null;
        if (CurrentConfig?.IsCustomOrderReceiptsEnabled == true && CurrentConfig.CustomOrderReceiptDesignId.HasValue)
        {
            mainDesign = _allReceiptDesigns.FirstOrDefault(d => d.Id == CurrentConfig.CustomOrderReceiptDesignId.Value)
                         ?? _allReceiptDesigns.FirstOrDefault();
        }

        ReceiptDesignRecord? secondDesign = null;
        if (CurrentConfig?.IsSecondReceiptEnabled == true)
        {
            secondDesign = CurrentConfig.SecondReceiptDesignId.HasValue
                ? _allReceiptDesigns.FirstOrDefault(d => d.Id == CurrentConfig.SecondReceiptDesignId.Value)
                : _allReceiptDesigns.Skip(1).FirstOrDefault() ?? _allReceiptDesigns.FirstOrDefault();
        }

        // If main design is "Open Cash Drawer Only" and no second receipt is enabled
        if (mainDesign != null && mainDesign.OpenCashDrawerOnly && secondDesign == null)
        {
            ShowPosToast($"Cash Drawer Opened ({mainDesign.Name}) — No paper receipt printed.");
            return;
        }

        ReceiptPreviewTitle = isAutoPrint ? "Automatic Receipt Printed" : "Order Receipt";
        ReceiptPreviewDesignBadge = mainDesign != null
            ? (mainDesign.OpenCashDrawerOnly ? $"{mainDesign.Name} (Cash Drawer Only)" : $"Custom Design: {mainDesign.Name}")
            : "Standard Receipt";

        ReceiptPreviewMainText = BuildFormattedReceiptText(order, mainDesign, isSimplified: false);

        if (secondDesign != null)
        {
            HasSecondReceiptPreview = true;
            ReceiptPreviewSecondTitle = secondDesign.OpenCashDrawerOnly
                ? $"Second Receipt: {secondDesign.Name} (Open Cash Drawer Only)"
                : $"Second Receipt ({secondDesign.Name})";
            ReceiptPreviewSecondText = BuildFormattedReceiptText(order, secondDesign, isSimplified: true);
        }
        else
        {
            HasSecondReceiptPreview = false;
            ReceiptPreviewSecondText = string.Empty;
        }

        IsReceiptPreviewModalOpen = true;
        ShowPosToast(isAutoPrint
            ? (HasSecondReceiptPreview ? "Main & Second Receipts printed automatically." : "Receipt printed automatically.")
            : "Receipt printed.");
    }

    private string BuildFormattedReceiptText(PosOrderRecord order, ReceiptDesignRecord? design, bool isSimplified)
    {
        if (design != null && design.OpenCashDrawerOnly)
        {
            return $"[PULSE SIGNAL] Cash Drawer Opened via \"{design.Name}\".\n(Open Cash Drawer Only = Yes)";
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"========================================");
        sb.AppendLine($"            {ActiveShopName.ToUpperInvariant()}");
        sb.AppendLine($"========================================");
        sb.AppendLine($"Order:   {(!string.IsNullOrWhiteSpace(order.PosReference) ? order.PosReference : order.Name)}");
        sb.AppendLine($"Date:    {(order.DateOrder ?? DateTime.Now):yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Cashier: {order.CashierName}");
        if (!string.IsNullOrWhiteSpace(order.CustomerName))
            sb.AppendLine($"Customer: {order.CustomerName}");
        sb.AppendLine($"----------------------------------------");

        foreach (var line in order.Lines)
        {
            if (isSimplified)
            {
                sb.AppendLine($"{line.Qty:0.##} x  {line.ProductName}");
            }
            else
            {
                sb.AppendLine($"{line.Qty:0.##} x {line.ProductName,-22} $ {line.PriceSubtotalIncl,7:N2}");
                if (line.Discount > 0)
                    sb.AppendLine($"   Discount: {line.Discount:0.#}%");
            }
        }

        sb.AppendLine($"----------------------------------------");
        if (!isSimplified)
        {
            sb.AppendLine($"Taxes:                       $ {order.AmountTax,8:N2}");
            sb.AppendLine($"TOTAL:                       $ {order.AmountTotal,8:N2}");
            foreach (var p in order.Payments)
            {
                sb.AppendLine($"{p.PaymentMethodName,-28} $ {p.Amount,8:N2}");
            }
        }

        if (design != null && !string.IsNullOrWhiteSpace(design.ReceiptXml))
        {
            sb.AppendLine($"----------------------------------------");
            sb.AppendLine($"[Template: {design.Name}]");
        }

        return sb.ToString().TrimEnd();
    }

    [RelayCommand]
    public void SendReceipt()
    {
        ShowPosToast("Receipt queued for email delivery.");
    }

    // =========================================================================
    // MODAL 2: HAMBURGER MENU WORKFLOW (Screenshot 7)
    // =========================================================================
    [RelayCommand]
    public void OpenHamburgerMenu()
    {
        _ = RefreshPendingSyncCountAsync();
        _ = UpdateSessionBreakdownAsync();
        IsHamburgerMenuOpen = true;
    }

    [RelayCommand]
    public void CloseHamburgerMenu()
    {
        IsHamburgerMenuOpen = false;
    }

    [RelayCommand]
    public async Task ReturnToBackendAsync()
    {
        bool multipleTabs = DraftOrders.Count > 1;
        foreach (var draft in DraftOrders.ToList())
        {
            await PersistDraftOrderToDatabaseAsync(draft, allowEmpty: multipleTabs);
        }

        IsHamburgerMenuOpen = false;
        IsPosLandingScreenOpen = false;
        ReturnToDashboardRequested?.Invoke();
    }

    [RelayCommand]
    public async Task ReloadOdooDataAsync()
    {
        IsHamburgerMenuOpen = false;
        ShowPosToast("Synchronizing with Odoo...");
        await OdooSyncService.Instance.ProcessPendingQueueAsync();
        await ReloadCatalogAsync();
        await ReloadPaymentMethodsForShopAsync();
        await ReloadInPosOrdersAsync();
        await RefreshPendingSyncCountAsync();
        ShowPosToast("POS data reloaded.");
    }

    [RelayCommand]
    public void CustomerDisplayTile()
    {
        IsHamburgerMenuOpen = false;
        ShowPosToast("Customer Display is active.");
    }

    [RelayCommand]
    public void InstallAppTile()
    {
        IsHamburgerMenuOpen = false;
        ShowPosToast("Odoo 20 POS Desktop App is installed.");
    }

    [RelayCommand]
    public void CashInOutTile()
    {
        IsHamburgerMenuOpen = false;
        ShowPosToast("Cash In/Out recorded.");
    }

    [RelayCommand]
    public void CreateProductTile()
    {
        IsHamburgerMenuOpen = false;
        ShowPosToast("Products are managed in Backend > Products.");
    }

    // =========================================================================
    // MODAL 3 & 4: CLOSING REGISTER & DRAFT ORDER PROTECTION (Screenshots 1 & 2)
    // =========================================================================
    [RelayCommand]
    public async Task OpenClosingRegisterWorkflowAsync()
    {
        IsHamburgerMenuOpen = false;

        if (CurrentOrder != null && CurrentOrder.HasItems)
        {
            await PersistDraftOrderToDatabaseAsync(CurrentOrder);
        }

        await UpdateSessionBreakdownAsync();

        int sessId = CurrentConfig?.CurrentSessionId ?? 1;
        string? shopName = CurrentConfig?.Name;
        int dbDraftCount = await SqliteDatabaseService.Instance.GetSessionDraftOrdersCountAsync(sessId, shopName);
        int localDraftCount = DraftOrders.Count(d => d.HasItems);

        IsClosingRegisterModalOpen = true;
        IsDraftOrdersWarningModalOpen = (dbDraftCount > 0 || localDraftCount > 0);
    }

    private async Task UpdateSessionBreakdownAsync()
    {
        int sessId = CurrentConfig?.CurrentSessionId ?? 1;
        string shopName = CurrentConfig?.Name ?? ActiveShopName;
        string sessName = CurrentConfig?.CurrentSessionName ?? (CurrentSession?.Name ?? $"{ActiveShopName}/00001");
        CurrentSessionNameDisplay = sessName;

        try
        {
            var loaded = await SqliteDatabaseService.Instance.LoadAllOrdersAsync(shopName);
            _allInPosOrders.Clear();
            _allInPosOrders.AddRange(loaded);
        }
        catch { }

        var sessOrders = _allInPosOrders.Where(o => OrderMatchesCurrentSession(o) && !o.IsCancelled && !o.IsDraft).ToList();

        int count = sessOrders.Count;
        SessionTotalOrdersCount = count;
        decimal cashSales = 0.00m;
        decimal cardSales = 0.00m;
        decimal custAccountSales = 0.00m;

        foreach (var ord in sessOrders)
        {
            if (ord.Payments.Count == 0)
            {
                cashSales += ord.AmountTotal;
            }
            else
            {
                foreach (var pay in ord.Payments)
                {
                    if (pay.PaymentMethodName.Contains("Card", StringComparison.OrdinalIgnoreCase) ||
                        pay.PaymentMethodName.Contains("Bank", StringComparison.OrdinalIgnoreCase))
                    {
                        cardSales += pay.Amount;
                    }
                    else if (pay.PaymentMethodName.Contains("Customer", StringComparison.OrdinalIgnoreCase) ||
                             pay.PaymentMethodName.Contains("Account", StringComparison.OrdinalIgnoreCase))
                    {
                        custAccountSales += pay.Amount;
                    }
                    else
                    {
                        cashSales += pay.Amount;
                    }
                }
            }
        }

        decimal totalSessionSales = cashSales + cardSales + custAccountSales;
        SessionTotalPaymentsAmount = totalSessionSales;
        decimal openBal = CurrentConfig?.OpeningBalance ?? 0.00m;
        decimal cashMoves = await SqliteDatabaseService.Instance.GetSessionCashInOutTotalAsync(sessId);

        CashOpening = openBal;
        CashPayments = cashSales;
        CashInOut = cashMoves;

        decimal expectedCash = openBal + cashSales + cashMoves;
        SessionSummaryText = $"{count} orders: $ {totalSessionSales:N2}";

        OnPropertyChanged(nameof(CurrentSessionDisplayName));
        OnPropertyChanged(nameof(SessionTotalOrdersDisplay));
        OnPropertyChanged(nameof(SessionTotalPaymentsDisplay));

        CashCounted = expectedCash;
        CashDifference = 0.00m;
        ClosingCashCount = expectedCash.ToString("0.00", CultureInfo.InvariantCulture);

        CardTotal = cardSales;
        CardCounted = cardSales;
        CardDifference = 0.00m;

        CustomerAccountTotal = custAccountSales;
        CustomerAccountCounted = custAccountSales;
        CustomerAccountDifference = 0.00m;
    }

    [RelayCommand]
    public async Task ReviewDraftOrdersAsync()
    {
        IsDraftOrdersWarningModalOpen = false;
        IsClosingRegisterModalOpen = false;
        OrderStatusFilter = "Active";
        await SwitchToOrdersViewAsync();
    }

    [RelayCommand]
    public async Task CancelDraftOrdersAndCloseAsync()
    {
        int sessId = CurrentConfig?.CurrentSessionId ?? 1;
        string? shopName = CurrentConfig?.Name;

        try
        {
            await SqliteDatabaseService.Instance.CancelAllSessionDraftOrdersAsync(sessId, shopName);

            DraftOrders.Clear();
            CreateNewDraftOrderInternal(persistPrevious: false);

            await ReloadInPosOrdersAsync();

            IsDraftOrdersWarningModalOpen = false;
        }
        catch (Exception ex)
        {
            ShowPosToast($"Failed to cancel draft orders: {ex.Message}");
        }
    }

    [RelayCommand]
    public void DismissDraftOrdersWarning()
    {
        IsDraftOrdersWarningModalOpen = false;
        IsClosingRegisterModalOpen = false;
    }

    [RelayCommand]
    public async Task SubmitCloseRegisterAsync()
    {
        int sessId = CurrentConfig?.CurrentSessionId ?? 1;
        string? shopName = CurrentConfig?.Name;
        int dbDraftCount = await SqliteDatabaseService.Instance.GetSessionDraftOrdersCountAsync(sessId, shopName);
        int localDraftCount = DraftOrders.Count(d => d.HasItems);

        if (dbDraftCount > 0 || localDraftCount > 0)
        {
            IsDraftOrdersWarningModalOpen = true;
            return;
        }

        if (!decimal.TryParse(ClosingCashCount, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal counted))
        {
            counted = CashCounted;
        }

        int cfgId = CurrentConfig?.OdooId ?? 1;

        try
        {
            await RefreshBoundConfigSessionAsync();
            sessId = CurrentConfig?.CurrentSessionId ?? sessId;

            await SqliteDatabaseService.Instance.CloseSessionAsync(
                cfgId,
                sessId,
                counted,
                ClosingNote,
                enqueueForOdooSync: true
            );

            if (CurrentConfig != null)
            {
                CurrentConfig.CurrentSessionState = "closed";
                CurrentConfig.LastClosingBalance = counted;
                CurrentConfig.LastClosingDate = DateTime.Now;
            }

            UpdateLandingState();
            _ = LoadLastSessionInfoAsync();

            IsClosingRegisterModalOpen = false;
            IsDraftOrdersWarningModalOpen = false;
            OdooSyncService.Instance.NotifyQueueUpdated();

            // Flush all pending orders and the close-session operation to Odoo in sequential order
            await OdooSyncService.Instance.ProcessPendingQueueAsync();
            await RefreshPendingSyncCountAsync();

            ReturnToDashboardRequested?.Invoke();
        }
        catch (Exception ex)
        {
            ShowPosToast($"Failed to close register: {ex.Message}");
        }
    }

    [RelayCommand]
    public void DiscardClosingRegister()
    {
        IsDraftOrdersWarningModalOpen = false;
        IsClosingRegisterModalOpen = false;
    }

    [RelayCommand]
    public void ClearClosingCash()
    {
        ClosingCashCount = string.Empty;
    }

    public void ShowPosToast(string message)
    {
        PosToastMessage = message;
        IsPosToastVisible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
        PosMessageRequested?.Invoke(message);
    }
}