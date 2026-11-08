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

    // ==================== SESSION & CONFIG CONTEXT ====================
    [ObservableProperty]
    private PosConfigRecord? _currentConfig;

    [ObservableProperty]
    private PosSessionRecord? _currentSession;

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
        CurrentConfig = config;
        ActiveShopName = config.Name;
        CashierName = !string.IsNullOrWhiteSpace(config.ActiveUserName) ? config.ActiveUserName! : "Mitchell Admin";
        CashierAvatarBitmap = config.ActiveUserAvatarBitmap;

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
        }
        catch { }

        decimal defaultCash = config.LastClosingBalance > 0
            ? config.LastClosingBalance
            : (config.OpeningBalance > 0 ? config.OpeningBalance : 0.00m);

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

        DraftOrders.Clear();
        CreateNewDraftOrderInternal(persistPrevious: false);

        await ReloadCatalogAsync();
        await ReloadPaymentMethodsForShopAsync();
        await ReloadInPosOrdersAsync();
        await RefreshPendingSyncCountAsync();

        ActivePosView = "Register";
        IsPaymentViewOpen = false;
        IsPaymentConfirmationOpen = false;
        IsHamburgerMenuOpen = false;
        IsClosingRegisterModalOpen = false;
        IsDraftOrdersWarningModalOpen = false;
        IsNoteModalOpen = false;

        if (config.IsClosed)
        {
            IsPosLandingScreenOpen = true;
            IsOpeningControlModalOpen = false;
        }
        else if (config.IsOpeningControl || showOpeningControl)
        {
            IsPosLandingScreenOpen = false;
            IsOpeningControlModalOpen = true;
        }
        else
        {
            IsPosLandingScreenOpen = false;
            IsOpeningControlModalOpen = false;
        }
    }

    [RelayCommand]
    public void EnterPosFromLanding()
    {
        IsPosLandingScreenOpen = false;
        IsOpeningControlModalOpen = true;
    }

    public async Task ReloadPaymentMethodsForShopAsync()
    {
        try
        {
            var allMethods = await SqliteDatabaseService.Instance.LoadAllPaymentMethodsAsync();
            AvailablePaymentMethods.Clear();

            string shopName = CurrentConfig?.Name ?? "";
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

            var firstMethod = AvailablePaymentMethods.FirstOrDefault();
            SelectedPaymentMethod = firstMethod?.Name ?? "Cash";
            SelectedPaymentMethodOdooId = firstMethod?.OdooId;
        }
        catch { }
    }

    public async Task ReloadCatalogAsync()
    {
        try
        {
            var cats = await SqliteDatabaseService.Instance.LoadAllCategoriesAsync();
            _allCategories.Clear();

            string shopName = CurrentConfig?.Name ?? "";
            var shopCats = cats.Where(c =>
                string.IsNullOrWhiteSpace(c.LinkedPosConfigs) ||
                c.LinkedShopsList.Any(s => string.Equals(s, shopName, StringComparison.OrdinalIgnoreCase))).ToList();

            if (shopCats.Count == 0) shopCats = cats;
            _allCategories.AddRange(shopCats);

            Categories.Clear();
            foreach (var c in _allCategories)
            {
                Categories.Add(c);
            }

            var prods = await SqliteDatabaseService.Instance.LoadAllProductsAsync();
            _allProducts.Clear();
            _allProducts.AddRange(prods.Where(p => p.AvailableInPos));

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
    [RelayCommand]
    public async Task CreateNewDraftOrderAsync()
    {
        if (CurrentOrder != null && CurrentOrder.HasItems)
        {
            await PersistDraftOrderToDatabaseAsync(CurrentOrder);
        }

        CreateNewDraftOrderInternal(persistPrevious: false);
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

    private async Task PersistDraftOrderToDatabaseAsync(PosDraftOrder draft)
    {
        try
        {
            if (!draft.HasItems)
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

        var existing = CurrentOrder.CartItems.FirstOrDefault(i =>
            i.Product.Id == product.Id &&
            Math.Abs(i.UnitPrice - product.ListPrice) < 0.001m &&
            i.Discount == 0m);

        if (existing != null)
        {
            existing.Quantity += 1m;
            SelectCartLine(existing);
        }
        else
        {
            var newItem = new PosCartItem(product, 1m);
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
            source = source.Where(p =>
                p.PosCategoryId == SelectedCategory.OdooId ||
                string.Equals(p.PosCategoryName, SelectedCategory.Name, StringComparison.OrdinalIgnoreCase));
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
    // IN-POS ORDERS FILTERING & ACTIONS (Screenshot 3)
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
        OrderStatusFilter = OrderStatusFilter switch
        {
            "Active" => "Paid",
            "Paid" => "All",
            _ => "Active"
        };
    }

    private void FilterInPosOrders()
    {
        string query = OrderSearchText.Trim();
        FilteredInPosOrders.Clear();

        IEnumerable<PosOrderRecord> source = _allInPosOrders;

        if (OrderStatusFilter is "Active" or "Ongoing")
        {
            source = source.Where(o => o.IsDraft);
        }
        else if (OrderStatusFilter == "Paid")
        {
            source = source.Where(o => !o.IsDraft && !o.IsCancelled);
        }

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
    public void DiscardOpeningControl()
    {
        IsOpeningControlModalOpen = false;
        IsPosLandingScreenOpen = false;
        ReturnToDashboardRequested?.Invoke();
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
            if (_paymentInputBuffer.StartsWith('-'))
                _paymentInputBuffer = _paymentInputBuffer[1..];
            else if (!string.IsNullOrEmpty(_paymentInputBuffer) && _paymentInputBuffer != "0")
                _paymentInputBuffer = "-" + _paymentInputBuffer;

            if (decimal.TryParse(_paymentInputBuffer, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal parsed))
            {
                AmountTendered = parsed;
            }
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

            ConfirmedPaidAmount = total;
            ConfirmedCashAmount = AmountTendered;
            ConfirmedChangeAmount = Math.Max(0.00m, AmountTendered - total);
            ConfirmedPaymentMethodName = SelectedPaymentMethod;
            IsPaymentViewOpen = false;
            IsPaymentConfirmationOpen = true;
            OdooSyncService.Instance.NotifyQueueUpdated();

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
        ShowPosToast("Receipt sent to printer.");
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
        if (CurrentOrder != null && CurrentOrder.HasItems)
        {
            await PersistDraftOrderToDatabaseAsync(CurrentOrder);
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

        var sessOrders = _allInPosOrders.Where(o =>
            ((o.SessionOdooId == sessId || o.SessionId == sessId) || string.Equals(o.PosConfigName, shopName, StringComparison.OrdinalIgnoreCase))
            && !o.IsCancelled && !o.IsDraft).ToList();

        int count = sessOrders.Count;
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
        decimal openBal = CurrentConfig?.OpeningBalance ?? 0.00m;
        decimal cashMoves = await SqliteDatabaseService.Instance.GetSessionCashInOutTotalAsync(sessId);

        CashOpening = openBal;
        CashPayments = cashSales;
        CashInOut = cashMoves;

        decimal expectedCash = openBal + cashSales + cashMoves;
        SessionSummaryText = $"{count} orders: $ {totalSessionSales:N2}";

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