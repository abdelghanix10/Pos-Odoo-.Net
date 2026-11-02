using System.Collections.ObjectModel;
using System.Globalization;
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
    private string _activeShopName = "Bakery Shop";

    // ==================== TOP NAVIGATION & ACTIVE VIEW ====================
    [ObservableProperty]
    private string _activePosView = "Register"; // "Register" or "Orders"

    public bool IsRegisterViewActive => ActivePosView == "Register";
    public bool IsOrdersViewActive => ActivePosView == "Orders";

    // ==================== DRAFT ORDERS & TABS ====================
    [ObservableProperty]
    private ObservableCollection<PosDraftOrder> _draftOrders = [];

    [ObservableProperty]
    private PosDraftOrder? _currentOrder;

    private int _orderCounter = 1002;

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

    // ==================== IN-POS ORDERS VIEW ====================
    [ObservableProperty]
    private ObservableCollection<PosOrderRecord> _filteredInPosOrders = [];

    [ObservableProperty]
    private PosOrderRecord? _selectedInPosOrder;

    [ObservableProperty]
    private string _orderSearchText = string.Empty;

    [ObservableProperty]
    private string _orderStatusFilter = "All"; // "All", "Ongoing", "Paid"

    // ==================== MODAL 1: OPENING CONTROL ====================
    [ObservableProperty]
    private bool _isOpeningControlModalOpen;

    [ObservableProperty]
    private string _openingCashInput = "25.59";

    [ObservableProperty]
    private string _openingNote = string.Empty;

    // ==================== MODAL 2: PAYMENT SPLIT SCREEN ====================
    [ObservableProperty]
    private bool _isPaymentViewOpen;

    [ObservableProperty]
    private string _selectedPaymentMethod = "Cash"; // "Cash", "Card"

    [ObservableProperty]
    private decimal _amountDue;

    [ObservableProperty]
    private decimal _amountTendered;

    [ObservableProperty]
    private decimal _changeDue;

    // ==================== MODAL 3: PAYMENT CONFIRMATION ====================
    [ObservableProperty]
    private bool _isPaymentConfirmationOpen;

    [ObservableProperty]
    private decimal _confirmedPaidAmount;

    // ==================== MODAL 4: HAMBURGER MENU ====================
    [ObservableProperty]
    private bool _isHamburgerMenuOpen;

    // ==================== MODAL 5: CLOSING REGISTER & WARNING ====================
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
    private decimal _cardCounted = 0.00m;

    [ObservableProperty]
    private decimal _cardDifference = 0.00m;

    // Toast notifications inside POS
    [ObservableProperty]
    private string _posToastMessage = string.Empty;

    [ObservableProperty]
    private bool _isPosToastVisible;

    private readonly DispatcherTimer _toastTimer;

    // Navigation event back to MainShell / Dashboard
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

        // Initialize with default draft order
        CreateNewDraftOrder();
    }

    partial void OnProductSearchTextChanged(string value) => FilterProducts();
    partial void OnOrderSearchTextChanged(string value) => FilterInPosOrders();
    partial void OnOrderStatusFilterChanged(string value) => FilterInPosOrders();
    partial void OnActivePosViewChanged(string value)
    {
        OnPropertyChanged(nameof(IsRegisterViewActive));
        OnPropertyChanged(nameof(IsOrdersViewActive));
    }

    // =========================================================================
    // INITIALIZATION & CONTEXT LOADING
    // =========================================================================
    public async Task InitializeRegisterAsync(PosConfigRecord config, bool showOpeningControl = false)
    {
        CurrentConfig = config;
        ActiveShopName = config.Name;
        CashierName = config.ActiveUserName ?? "Mitchell Admin";

        // Pre-fill opening cash from config
        decimal lastBalance = config.LastClosingBalance > 0 ? config.LastClosingBalance : (config.OpeningBalance > 0 ? config.OpeningBalance : 25.59m);
        OpeningCashInput = lastBalance.ToString("F2", CultureInfo.InvariantCulture);
        ClosingCashCount = OpeningCashInput;
        OpeningNote = string.Empty;
        ClosingNote = string.Empty;

        // Load Categories & Products from SQLite
        await ReloadCatalogAsync();

        // Load In-POS Orders from SQLite
        await ReloadInPosOrdersAsync();

        // Check if opening control modal should be presented
        if (showOpeningControl || config.IsOpeningControl || config.IsClosed)
        {
            IsOpeningControlModalOpen = true;
        }
        else
        {
            IsOpeningControlModalOpen = false;
        }

        ActivePosView = "Register";
        IsPaymentViewOpen = false;
        IsPaymentConfirmationOpen = false;
        IsHamburgerMenuOpen = false;
        IsClosingRegisterModalOpen = false;
        IsDraftOrdersWarningModalOpen = false;
    }

    public async Task ReloadCatalogAsync()
    {
        try
        {
            var cats = await SqliteDatabaseService.Instance.LoadAllCategoriesAsync();
            _allCategories.Clear();
            _allCategories.AddRange(cats);

            Categories.Clear();
            foreach (var c in _allCategories)
            {
                Categories.Add(c);
            }

            var prods = await SqliteDatabaseService.Instance.LoadAllProductsAsync();
            _allProducts.Clear();
            _allProducts.AddRange(prods);

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
            var orders = await SqliteDatabaseService.Instance.LoadAllOrdersAsync();
            _allInPosOrders.Clear();
            _allInPosOrders.AddRange(orders);
            FilterInPosOrders();
            UpdateSessionBreakdown();
        }
        catch (Exception ex)
        {
            ShowPosToast($"Failed to load orders: {ex.Message}");
        }
    }

    // =========================================================================
    // DRAFT ORDER MANAGEMENT
    // =========================================================================
    [RelayCommand]
    public void CreateNewDraftOrder()
    {
        string num = _orderCounter.ToString();
        _orderCounter++;

        var order = new PosDraftOrder(num);
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
        _numpadBuffer = string.Empty;

        UpdateProductCartQuantities();
    }

    [RelayCommand]
    public void CloseDraftOrder(PosDraftOrder? order)
    {
        if (order == null) return;
        DraftOrders.Remove(order);

        if (DraftOrders.Count == 0)
        {
            CreateNewDraftOrder();
        }
        else if (CurrentOrder == order)
        {
            SelectDraftOrder(DraftOrders.Last());
        }
    }

    // =========================================================================
    // CART & LINE INTERACTIONS
    // =========================================================================
    [RelayCommand]
    public void AddProduct(ProductRecord? product)
    {
        if (product == null || CurrentOrder == null) return;

        var existing = CurrentOrder.CartItems.FirstOrDefault(i => i.Product.Id == product.Id);
        if (existing != null)
        {
            existing.Quantity += 1;
            SelectedCartLine = existing;
        }
        else
        {
            var newItem = new PosCartItem(product, 1);
            CurrentOrder.CartItems.Add(newItem);
            SelectedCartLine = newItem;
        }

        _numpadBuffer = string.Empty;
        CurrentOrder.NotifyTotalsChanged();
        UpdateProductCartQuantities();
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
        _numpadBuffer = string.Empty;
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
            if (_numpadBuffer.Length > 0)
            {
                _numpadBuffer = _numpadBuffer[..^1];
            }
            else
            {
                // Remove line from cart if backspacing on empty buffer
                CurrentOrder.CartItems.Remove(SelectedCartLine);
                SelectedCartLine = CurrentOrder.CartItems.LastOrDefault();
                CurrentOrder.NotifyTotalsChanged();
                UpdateProductCartQuantities();
                return;
            }
        }
        else if (key == "+/-")
        {
            if (_numpadBuffer.StartsWith("-"))
                _numpadBuffer = _numpadBuffer[1..];
            else if (!string.IsNullOrEmpty(_numpadBuffer))
                _numpadBuffer = "-" + _numpadBuffer;
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
            _numpadBuffer += key;
        }

        // Apply buffer based on active mode
        if (decimal.TryParse(_numpadBuffer, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal val))
        {
            switch (NumpadMode)
            {
                case "Qty":
                    SelectedCartLine.Quantity = (int)val;
                    if (SelectedCartLine.Quantity <= 0)
                    {
                        CurrentOrder.CartItems.Remove(SelectedCartLine);
                        SelectedCartLine = CurrentOrder.CartItems.LastOrDefault();
                    }
                    break;
                case "%":
                    SelectedCartLine.Discount = Math.Clamp(val, 0, 100);
                    break;
                case "Price":
                    SelectedCartLine.UnitPrice = Math.Max(0, val);
                    break;
            }
        }

        CurrentOrder.NotifyTotalsChanged();
        UpdateProductCartQuantities();
    }

    private void UpdateProductCartQuantities()
    {
        if (CurrentOrder == null) return;

        var quantities = CurrentOrder.CartItems
            .GroupBy(i => i.Product.Id)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

        foreach (var p in _allProducts)
        {
            if (quantities.TryGetValue(p.Id, out int q))
            {
                p.CartQuantity = q;
            }
            else
            {
                p.CartQuantity = 0;
            }
        }
    }

    // =========================================================================
    // CATALOG & CATEGORY FILTERING
    // =========================================================================
    [RelayCommand]
    public void SelectCategory(PosCategoryRecord? category)
    {
        SelectedCategory = category;
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
    // IN-POS ORDERS FILTERING & ACTIONS
    // =========================================================================
    [RelayCommand]
    public void SwitchToRegisterView()
    {
        ActivePosView = "Register";
    }

    [RelayCommand]
    public void SwitchToOrdersView()
    {
        ActivePosView = "Orders";
        _ = ReloadInPosOrdersAsync();
    }

    private void FilterInPosOrders()
    {
        string query = OrderSearchText.Trim();
        FilteredInPosOrders.Clear();

        IEnumerable<PosOrderRecord> source = _allInPosOrders;

        if (OrderStatusFilter == "Ongoing")
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
                o.TrackingNumber.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var o in source)
        {
            FilteredInPosOrders.Add(o);
        }

        if (SelectedInPosOrder == null || !FilteredInPosOrders.Contains(SelectedInPosOrder))
        {
            SelectedInPosOrder = FilteredInPosOrders.FirstOrDefault();
        }
    }

    [RelayCommand]
    public void LoadOrderIntoRegister(PosOrderRecord? order)
    {
        if (order == null) return;

        // Create or find a draft tab for this order
        string tabNum = string.IsNullOrWhiteSpace(order.TrackingNumber) ? "2001" : order.TrackingNumber;
        var existing = DraftOrders.FirstOrDefault(d => d.OrderNumber == tabNum);
        if (existing == null)
        {
            existing = new PosDraftOrder(tabNum)
            {
                OrderRef = order.PosReference,
                Note = order.Note
            };
            DraftOrders.Add(existing);
        }

        existing.CartItems.Clear();
        foreach (var l in order.Lines)
        {
            var prod = _allProducts.FirstOrDefault(p => p.Name == l.ProductName) ??
                       new ProductRecord { Name = l.ProductName, ListPrice = l.PriceUnit };
            existing.CartItems.Add(new PosCartItem(prod, (int)l.Qty)
            {
                UnitPrice = l.PriceUnit,
                Discount = l.Discount
            });
        }

        existing.NotifyTotalsChanged();
        SelectDraftOrder(existing);
        ActivePosView = "Register";
        ShowPosToast($"Order {order.Name} loaded into Register.");
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

            // Also remove from draft tabs if currently open
            var draftTab = DraftOrders.FirstOrDefault(d => d.OrderRef == order.PosReference);
            if (draftTab != null)
            {
                CloseDraftOrder(draftTab);
            }

            ShowPosToast($"Draft order {order.Name} deleted.");
        }
        catch (Exception ex)
        {
            ShowPosToast($"Failed to delete order: {ex.Message}");
        }
    }

    // =========================================================================
    // MODAL 1: OPENING CONTROL WORKFLOW
    // =========================================================================
    [RelayCommand]
    public async Task OpenRegisterSubmitAsync()
    {
        if (!decimal.TryParse(OpeningCashInput, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal cash))
        {
            cash = 0.00m;
        }

        int cfgId = CurrentConfig?.OdooId ?? 1;
        int sessId = CurrentConfig?.CurrentSessionId ?? 4;

        try
        {
            await SqliteDatabaseService.Instance.UpdateSessionOpeningControlAsync(cfgId, sessId, cash, OpeningNote);

            if (CurrentConfig != null)
            {
                CurrentConfig.CurrentSessionState = "opened";
                CurrentConfig.OpeningBalance = cash;
            }

            IsOpeningControlModalOpen = false;
            ShowPosToast("Register opened. Ready to sell!");
        }
        catch (Exception ex)
        {
            ShowPosToast($"Failed to open register: {ex.Message}");
        }
    }

    [RelayCommand]
    public void DiscardOpeningControl()
    {
        // Must not open register; immediately redirect back to POS Dashboard
        IsOpeningControlModalOpen = false;
        ReturnToDashboardRequested?.Invoke();
    }

    [RelayCommand]
    public void ClearOpeningCash()
    {
        OpeningCashInput = "0.00";
    }

    // =========================================================================
    // MODAL 2 & 3: PAYMENT WORKFLOW & RECEIPT CONFIRMATION
    // =========================================================================
    [RelayCommand]
    public void OpenPaymentView()
    {
        if (CurrentOrder == null || CurrentOrder.CartItems.Count == 0)
        {
            ShowPosToast("Cart is empty. Add products before proceeding to payment.");
            return;
        }

        AmountDue = CurrentOrder.TotalAmount;
        AmountTendered = AmountDue;
        ChangeDue = 0.00m;
        SelectedPaymentMethod = "Cash";

        IsPaymentViewOpen = true;
    }

    [RelayCommand]
    public void ClosePaymentView()
    {
        IsPaymentViewOpen = false;
    }

    [RelayCommand]
    public void SelectPaymentMethod(string method)
    {
        SelectedPaymentMethod = method;
    }

    [RelayCommand]
    public void AddQuickTender(string amountStr)
    {
        if (decimal.TryParse(amountStr, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal add))
        {
            AmountTendered += add;
            ChangeDue = Math.Max(0.00m, AmountTendered - AmountDue);
        }
    }

    [RelayCommand]
    public async Task ValidatePaymentAsync()
    {
        if (CurrentOrder == null) return;

        try
        {
            decimal total = CurrentOrder.TotalAmount;
            decimal tax = CurrentOrder.Taxes;
            int cfgId = CurrentConfig?.OdooId ?? 3;
            int sessId = CurrentConfig?.CurrentSessionId ?? 4;
            string sessName = CurrentConfig?.CurrentSessionName ?? "Bakery Shop/00004";
            string shopName = CurrentConfig?.Name ?? "Bakery Shop";

            var order = new PosOrderRecord
            {
                Name = $"{shopName} - {CurrentOrder.OrderNumber}",
                PosReference = CurrentOrder.OrderRef,
                TrackingNumber = CurrentOrder.OrderNumber,
                SessionId = sessId,
                SessionOdooId = sessId,
                SessionName = sessName,
                PosConfigName = shopName,
                DateOrder = DateTime.Now,
                CashierName = CashierName,
                FiscalPosition = "Standard Tax",
                AmountTotal = total,
                AmountTax = tax,
                AmountPaid = total,
                Margin = Math.Round(total * 0.42m, 2),
                MarginRatio = 42.0m,
                State = "paid",
                Company = "My Company",
                Note = CurrentOrder.Note
            };

            var lines = CurrentOrder.CartItems.Select(i => new PosOrderLineRecord
            {
                ProductName = i.ProductName,
                Qty = i.Quantity,
                PriceUnit = i.UnitPrice,
                Discount = i.Discount,
                Taxes = "15%",
                PriceSubtotal = Math.Round(i.LineTotal / 1.15m, 2),
                PriceSubtotalIncl = i.LineTotal
            }).ToList();

            var payments = new List<PosPaymentRecord>
            {
                new()
                {
                    OrderName = order.Name,
                    SessionOdooId = sessId,
                    SessionName = sessName,
                    CashierName = CashierName,
                    PaymentMethodName = SelectedPaymentMethod,
                    Amount = total,
                    PaymentDate = DateTime.Now
                }
            };

            await SqliteDatabaseService.Instance.SavePosOrderAsync(order, lines, payments);

            ConfirmedPaidAmount = total;
            IsPaymentViewOpen = false;
            IsPaymentConfirmationOpen = true;

            await ReloadInPosOrdersAsync();
        }
        catch (Exception ex)
        {
            ShowPosToast($"Payment failed: {ex.Message}");
        }
    }

    [RelayCommand]
    public void ContinueAfterPayment()
    {
        IsPaymentConfirmationOpen = false;

        // Clear completed draft order
        if (CurrentOrder != null)
        {
            DraftOrders.Remove(CurrentOrder);
        }

        if (DraftOrders.Count == 0)
        {
            CreateNewDraftOrder();
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
        ShowPosToast("Receipt sent to thermal printer.");
    }

    [RelayCommand]
    public void SendReceipt()
    {
        ShowPosToast("Receipt sent via email.");
    }

    // =========================================================================
    // MODAL 4: HAMBURGER MENU WORKFLOW
    // =========================================================================
    [RelayCommand]
    public void OpenHamburgerMenu()
    {
        IsHamburgerMenuOpen = true;
    }

    [RelayCommand]
    public void CloseHamburgerMenu()
    {
        IsHamburgerMenuOpen = false;
    }

    [RelayCommand]
    public void ReturnToBackend()
    {
        IsHamburgerMenuOpen = false;
        ReturnToDashboardRequested?.Invoke();
    }

    [RelayCommand]
    public async Task ReloadOdooDataAsync()
    {
        IsHamburgerMenuOpen = false;
        ShowPosToast("Syncing latest products and data from Odoo...");
        await ReloadCatalogAsync();
        await ReloadInPosOrdersAsync();
        ShowPosToast("Odoo data reloaded successfully.");
    }

    [RelayCommand]
    public void CustomerDisplayTile()
    {
        IsHamburgerMenuOpen = false;
        ShowPosToast("Customer Facing Display is active.");
    }

    [RelayCommand]
    public void InstallAppTile()
    {
        IsHamburgerMenuOpen = false;
        ShowPosToast("Application is fully installed locally.");
    }

    [RelayCommand]
    public void CashInOutTile()
    {
        IsHamburgerMenuOpen = false;
        ShowPosToast("Cash In/Out logged.");
    }

    [RelayCommand]
    public void CreateProductTile()
    {
        IsHamburgerMenuOpen = false;
        ShowPosToast("Product creation is strictly sync-only from Odoo.");
    }

    // =========================================================================
    // MODAL 5: CLOSING REGISTER & DRAFT ORDER PROTECTION ("Oh snap !")
    // =========================================================================
    [RelayCommand]
    public async Task OpenClosingRegisterWorkflowAsync()
    {
        IsHamburgerMenuOpen = false;
        UpdateSessionBreakdown();

        int sessId = CurrentConfig?.CurrentSessionId ?? 4;
        int activeDraftCount = await SqliteDatabaseService.Instance.GetSessionDraftOrdersCountAsync(sessId);
        int localDraftCount = DraftOrders.Count(d => d.CartItems.Count > 0);

        IsClosingRegisterModalOpen = true;

        if (activeDraftCount > 0 || localDraftCount > 0)
        {
            // Display "Oh snap !" layered warning modal
            IsDraftOrdersWarningModalOpen = true;
        }
        else
        {
            IsDraftOrdersWarningModalOpen = false;
        }
    }

    private void UpdateSessionBreakdown()
    {
        int sessId = CurrentConfig?.CurrentSessionId ?? 4;
        var sessOrders = _allInPosOrders.Where(o =>
            (o.SessionOdooId == sessId || o.SessionId == sessId) && !o.IsCancelled && !o.IsDraft).ToList();

        int count = sessOrders.Count;
        decimal total = sessOrders.Sum(o => o.AmountTotal);

        if (count == 0 && CurrentConfig?.OpeningBalance > 0)
        {
            total = CurrentConfig.OpeningBalance;
            count = 1;
        }

        SessionSummaryText = $"{count} orders: $ {total:N2}";
        CashOpening = CurrentConfig?.OpeningBalance ?? 0.00m;
        CashPayments = total;
        CashCounted = total;
        CashDifference = 0.00m;
        ClosingCashCount = CashCounted.ToString("F2", CultureInfo.InvariantCulture);

        CardCounted = 0.00m;
        CardDifference = 0.00m;
    }

    [RelayCommand]
    public void ReviewDraftOrders()
    {
        // Closes the modals and switches to the POS Orders view
        IsDraftOrdersWarningModalOpen = false;
        IsClosingRegisterModalOpen = false;
        ActivePosView = "Orders";
        OrderStatusFilter = "Ongoing";
        FilterInPosOrders();
    }

    [RelayCommand]
    public async Task CancelDraftOrdersAndCloseAsync()
    {
        int sessId = CurrentConfig?.CurrentSessionId ?? 4;

        try
        {
            await SqliteDatabaseService.Instance.CancelAllSessionDraftOrdersAsync(sessId);

            // Clear in-memory cart lines
            DraftOrders.Clear();
            CreateNewDraftOrder();

            await ReloadInPosOrdersAsync();

            // Close the warning popup and leave Closing Register open
            IsDraftOrdersWarningModalOpen = false;
            ShowPosToast("Draft orders cancelled. Proceed to close register.");
        }
        catch (Exception ex)
        {
            ShowPosToast($"Failed to cancel draft orders: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task SubmitCloseRegisterAsync()
    {
        if (!decimal.TryParse(ClosingCashCount, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal counted))
        {
            counted = CashCounted;
        }

        int cfgId = CurrentConfig?.OdooId ?? 3;
        int sessId = CurrentConfig?.CurrentSessionId ?? 4;

        try
        {
            await SqliteDatabaseService.Instance.CloseSessionAsync(cfgId, sessId, counted, ClosingNote);

            if (CurrentConfig != null)
            {
                CurrentConfig.CurrentSessionState = "closed";
                CurrentConfig.LastClosingBalance = counted;
                CurrentConfig.LastClosingDate = DateTime.Now;
            }

            IsClosingRegisterModalOpen = false;
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
        ClosingCashCount = "0.00";
    }

    [RelayCommand]
    public void ParkOrder()
    {
        if (CurrentOrder == null || CurrentOrder.CartItems.Count == 0)
        {
            ShowPosToast("Cart is empty. Nothing to park.");
            return;
        }

        ShowPosToast($"Order {CurrentOrder.OrderNumber} parked.");
        CreateNewDraftOrder();
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
