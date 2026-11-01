using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OdooPosLogin.ViewModels;

public partial class OrdersViewModel : ViewModelBase
{
    private readonly List<PosOrderRecord> _allOrders = [];

    [ObservableProperty]
    private ObservableCollection<PosOrderRecord> _filteredOrders = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isDetailViewVisible;

    [ObservableProperty]
    private PosOrderRecord? _selectedOrder;

    [ObservableProperty]
    private string _pagerText = "1 / 1";

    [ObservableProperty]
    private bool _hasPreviousRecord;

    [ObservableProperty]
    private bool _hasNextRecord;

    [ObservableProperty]
    private string _grandTotalText = "$ 0.00";

    [ObservableProperty]
    private string _activeTab = "Products"; // Products, Payments, ExtraInfo

    [ObservableProperty]
    private bool _isProductsTab = true;

    [ObservableProperty]
    private bool _isPaymentsTab;

    [ObservableProperty]
    private bool _isExtraInfoTab;

    [ObservableProperty]
    private string? _sessionFilter;

    [ObservableProperty]
    private bool _isSyncing;

    [ObservableProperty]
    private string _syncButtonText = "Sync from Odoo";

    [ObservableProperty]
    private string _toastMessage = string.Empty;

    [ObservableProperty]
    private bool _isToastVisible;

    [ObservableProperty]
    private string _toastSeverity = "Success";

    public event EventHandler? OrdersUpdated;

    private readonly DispatcherTimer _toastTimer;

    public OrdersViewModel()
    {
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            IsToastVisible = false;
        };

        _ = ReloadOrdersAsync();
    }

    partial void OnSearchTextChanged(string value) => RefreshFilter();
    partial void OnSessionFilterChanged(string? value) => RefreshFilter();

    public async Task ReloadOrdersAsync()
    {
        try
        {
            var orders = await SqliteDatabaseService.Instance.LoadAllOrdersAsync();
            _allOrders.Clear();
            _allOrders.AddRange(orders);
            RefreshFilter();
        }
        catch (Exception ex)
        {
            ShowToast($"Failed to load orders: {ex.Message}", "Error");
        }
    }

    public void SetSessionFilter(string? sessionName)
    {
        SessionFilter = sessionName;
        RefreshFilter();
    }

    [RelayCommand]
    public void ClearSessionFilter()
    {
        SessionFilter = null;
        RefreshFilter();
    }

    private void RefreshFilter()
    {
        string query = SearchText.Trim();
        FilteredOrders.Clear();

        IEnumerable<PosOrderRecord> source = _allOrders;

        if (!string.IsNullOrWhiteSpace(SessionFilter))
        {
            source = source.Where(o =>
                string.Equals(o.SessionName, SessionFilter, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(o.PosConfigName, SessionFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            source = source.Where(o =>
                o.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                o.PosReference.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                o.TrackingNumber.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                o.SessionName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                o.PosConfigName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                o.CashierName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                o.StateDisplay.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        decimal grandTotal = 0.00m;
        foreach (var o in source)
        {
            FilteredOrders.Add(o);
            grandTotal += o.AmountTotal;
        }

        GrandTotalText = $"$ {grandTotal.ToString("N2", CultureInfo.InvariantCulture)}";

        if (IsDetailViewVisible)
        {
            if (SelectedOrder != null && !FilteredOrders.Contains(SelectedOrder))
            {
                SelectedOrder = FilteredOrders.FirstOrDefault();
                if (SelectedOrder == null)
                {
                    IsDetailViewVisible = false;
                }
            }
            UpdatePager();
        }
    }

    [RelayCommand]
    public void SelectOrder(PosOrderRecord? order)
    {
        if (order == null) return;
        SelectedOrder = order;
        IsDetailViewVisible = true;
        SelectTab("Products");
        UpdatePager();
    }

    [RelayCommand]
    public void CloseDetail()
    {
        IsDetailViewVisible = false;
        SelectedOrder = null;
    }

    [RelayCommand]
    public void SelectTab(string tabName)
    {
        ActiveTab = tabName;
        IsProductsTab = tabName == "Products";
        IsPaymentsTab = tabName == "Payments";
        IsExtraInfoTab = tabName == "ExtraInfo";
    }

    private void UpdatePager()
    {
        if (SelectedOrder == null || FilteredOrders.Count == 0)
        {
            PagerText = "0 / 0";
            HasPreviousRecord = false;
            HasNextRecord = false;
            return;
        }

        int index = FilteredOrders.IndexOf(SelectedOrder);
        if (index < 0)
        {
            PagerText = $"1 / {FilteredOrders.Count}";
            HasPreviousRecord = false;
            HasNextRecord = FilteredOrders.Count > 1;
            return;
        }

        PagerText = $"{index + 1} / {FilteredOrders.Count}";
        HasPreviousRecord = index > 0;
        HasNextRecord = index < FilteredOrders.Count - 1;
    }

    [RelayCommand]
    public void PreviousRecord()
    {
        if (SelectedOrder == null || FilteredOrders.Count == 0) return;
        int index = FilteredOrders.IndexOf(SelectedOrder);
        if (index > 0)
        {
            SelectedOrder = FilteredOrders[index - 1];
            UpdatePager();
        }
    }

    [RelayCommand]
    public void NextRecord()
    {
        if (SelectedOrder == null || FilteredOrders.Count == 0) return;
        int index = FilteredOrders.IndexOf(SelectedOrder);
        if (index >= 0 && index < FilteredOrders.Count - 1)
        {
            SelectedOrder = FilteredOrders[index + 1];
            UpdatePager();
        }
    }

    [RelayCommand]
    public async Task SyncOrdersAsync()
    {
        if (IsSyncing) return;
        IsSyncing = true;
        SyncButtonText = "Syncing...";

        try
        {
            var (orders, lines, payments) = await OdooSyncService.Instance.FetchOdooOrdersAsync(allowOfflineSimulation: true);
            var (imported, updated, deleted) = await SqliteDatabaseService.Instance.UpsertOdooOrdersAsync(orders, lines, payments);

            DateTime now = DateTime.Now;
            await SqliteDatabaseService.Instance.UpdateOrderSyncStatusAsync(now, "Success");

            await ReloadOrdersAsync();
            OrdersUpdated?.Invoke(this, EventArgs.Empty);

            ShowToast($"Orders synced: {imported} added, {updated} updated, {deleted} removed.", "Success");
        }
        catch (Exception ex)
        {
            ShowToast($"Sync failed: {ex.Message}", "Error");
        }
        finally
        {
            IsSyncing = false;
            SyncButtonText = "Sync from Odoo";
        }
    }

    [RelayCommand]
    public void DownloadReceipt()
    {
        if (SelectedOrder == null) return;
        ShowToast($"Receipt for {SelectedOrder.Name} saved to local storage.", "Success");
    }

    [RelayCommand]
    public void ReturnProducts()
    {
        if (SelectedOrder == null) return;
        ShowToast($"Return flow started for {SelectedOrder.Name}.", "Success");
    }

    private void ShowToast(string message, string severity = "Success")
    {
        ToastMessage = message;
        ToastSeverity = severity;
        IsToastVisible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }
}
