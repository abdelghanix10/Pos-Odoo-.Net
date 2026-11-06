using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OdooPosLogin.ViewModels;

public partial class PaymentsViewModel : ViewModelBase
{
    public const string AllShopsOption = "All Point of Sale";
    public const string AllSessionsOption = "All Sessions";
    public const string AllDatesOption = "All Dates";
    public const string DatePresetToday = "Today";
    public const string DatePresetYesterday = "Yesterday";
    public const string DatePresetLast7Days = "Last 7 Days";
    public const string DatePresetThisMonth = "This Month";
    public const string DatePresetCustom = "Custom Range";

    private readonly List<PosPaymentRecord> _allPayments = [];
    private readonly List<PosConfigRecord> _allConfigs = [];
    private readonly List<PosSessionRecord> _allSessions = [];
    private bool _suppressFilterRefresh;

    [ObservableProperty]
    private ObservableCollection<PaymentGroupRecord> _groupedPayments = [];

    [ObservableProperty]
    private ObservableCollection<PosPaymentRecord> _flatPayments = [];

    [ObservableProperty]
    private ObservableCollection<string> _availableShops = [AllShopsOption];

    [ObservableProperty]
    private ObservableCollection<string> _availableSessions = [AllSessionsOption];

    [ObservableProperty]
    private ObservableCollection<string> _datePresets = [
        AllDatesOption,
        DatePresetToday,
        DatePresetYesterday,
        DatePresetLast7Days,
        DatePresetThisMonth,
        DatePresetCustom
    ];

    [ObservableProperty]
    private string _selectedShopFilter = AllShopsOption;

    [ObservableProperty]
    private string _selectedSessionFilter = AllSessionsOption;

    [ObservableProperty]
    private string _selectedDatePreset = AllDatesOption;

    [ObservableProperty]
    private DateTime? _startDate;

    [ObservableProperty]
    private DateTime? _endDate;

    [ObservableProperty]
    private bool _isCustomDateRange;

    [ObservableProperty]
    private bool _hasActiveFilter;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isDetailViewVisible;

    [ObservableProperty]
    private PosPaymentRecord? _selectedPayment;

    public const int PageSize = 80;

    [ObservableProperty]
    private int _currentPage = 1;

    private readonly List<PosPaymentRecord> _matchingPayments = [];

    [ObservableProperty]
    private string _pagerText = "0 / 0";

    [ObservableProperty]
    private string _listCounterText = "0-0 / 0";

    [ObservableProperty]
    private bool _hasPreviousRecord;

    [ObservableProperty]
    private bool _hasNextRecord;

    [ObservableProperty]
    private string _grandTotalText = "$ 0.00";

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

    [ObservableProperty]
    private bool _isEmptyStateVisible;

    public event EventHandler? PaymentsUpdated;

    private readonly DispatcherTimer _toastTimer;

    public PaymentsViewModel()
    {
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            IsToastVisible = false;
        };

        _ = ReloadPaymentsAsync();
    }

    partial void OnSearchTextChanged(string value)
    {
        if (!_suppressFilterRefresh) RefreshFilter();
    }

    partial void OnSelectedShopFilterChanged(string value)
    {
        if (_suppressFilterRefresh) return;
        RebuildAvailableSessions();
        RefreshFilter();
    }

    partial void OnSelectedSessionFilterChanged(string value)
    {
        if (_suppressFilterRefresh) return;
        RefreshFilter();
    }

    partial void OnSelectedDatePresetChanged(string value)
    {
        if (_suppressFilterRefresh) return;
        _suppressFilterRefresh = true;
        try
        {
            var today = DateTime.Today;
            switch (value)
            {
                case DatePresetToday:
                    StartDate = today;
                    EndDate = today;
                    IsCustomDateRange = false;
                    break;
                case DatePresetYesterday:
                    StartDate = today.AddDays(-1);
                    EndDate = today.AddDays(-1);
                    IsCustomDateRange = false;
                    break;
                case DatePresetLast7Days:
                    StartDate = today.AddDays(-7);
                    EndDate = today;
                    IsCustomDateRange = false;
                    break;
                case DatePresetThisMonth:
                    StartDate = new DateTime(today.Year, today.Month, 1);
                    EndDate = today;
                    IsCustomDateRange = false;
                    break;
                case DatePresetCustom:
                    IsCustomDateRange = true;
                    StartDate ??= today;
                    EndDate ??= today;
                    break;
                default: // All Dates
                    StartDate = null;
                    EndDate = null;
                    IsCustomDateRange = false;
                    break;
            }
        }
        finally
        {
            _suppressFilterRefresh = false;
        }
        RefreshFilter();
    }

    partial void OnStartDateChanged(DateTime? value)
    {
        if (!_suppressFilterRefresh) RefreshFilter();
    }

    partial void OnEndDateChanged(DateTime? value)
    {
        if (!_suppressFilterRefresh) RefreshFilter();
    }

    public void ResetToMainList()
    {
        IsDetailViewVisible = false;
        UpdatePager();
    }

    public async Task ReloadPaymentsAsync(bool resetDetailView = false)
    {
        try
        {
            if (resetDetailView)
            {
                IsDetailViewVisible = false;
            }

            var configs = await SqliteDatabaseService.Instance.LoadAllPosConfigsAsync();
            _allConfigs.Clear();
            _allConfigs.AddRange(configs);

            var sessions = await SqliteDatabaseService.Instance.LoadAllSessionsAsync();
            _allSessions.Clear();
            _allSessions.AddRange(sessions);

            var payments = await SqliteDatabaseService.Instance.LoadAllPaymentsAsync();
            _allPayments.Clear();
            _allPayments.AddRange(payments);

            RebuildFilterOptions();
            RefreshFilter();
        }
        catch (Exception ex)
        {
            ShowToast($"Failed to load payments: {ex.Message}", "Error");
        }
    }

    private void RebuildFilterOptions()
    {
        _suppressFilterRefresh = true;
        try
        {
            string prevShop = SelectedShopFilter;
            string prevSession = SelectedSessionFilter;

            var shopNames = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in _allConfigs)
            {
                if (!string.IsNullOrWhiteSpace(c.Name)) shopNames.Add(c.Name.Trim());
            }
            foreach (var s in _allSessions)
            {
                if (!string.IsNullOrWhiteSpace(s.PosConfigName)) shopNames.Add(s.PosConfigName.Trim());
            }
            foreach (var p in _allPayments)
            {
                string shop = ExtractShopFromPayment(p);
                if (!string.IsNullOrWhiteSpace(shop)) shopNames.Add(shop);
            }

            AvailableShops.Clear();
            AvailableShops.Add(AllShopsOption);
            foreach (var shop in shopNames)
            {
                AvailableShops.Add(shop);
            }

            SelectedShopFilter = AvailableShops.Contains(prevShop) ? prevShop : AllShopsOption;
            RebuildAvailableSessions(prevSession);
        }
        finally
        {
            _suppressFilterRefresh = false;
        }
    }

    private void RebuildAvailableSessions(string? preferredSession = null)
    {
        string targetSession = preferredSession ?? SelectedSessionFilter;
        string activeShop = SelectedShopFilter;
        bool filterByShop = !string.IsNullOrWhiteSpace(activeShop) && activeShop != AllShopsOption;

        var sessionNames = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var s in _allSessions)
        {
            if (string.IsNullOrWhiteSpace(s.Name) || s.Name == "/") continue;
            if (!filterByShop || string.Equals(s.PosConfigName, activeShop, StringComparison.OrdinalIgnoreCase) ||
                s.Name.StartsWith(activeShop + "/", StringComparison.OrdinalIgnoreCase))
            {
                sessionNames.Add(s.Name.Trim());
            }
        }

        foreach (var p in _allPayments)
        {
            if (string.IsNullOrWhiteSpace(p.SessionName) || p.SessionName == "/") continue;
            if (!filterByShop || PaymentMatchesShop(p, activeShop))
            {
                sessionNames.Add(p.SessionName.Trim());
            }
        }

        _suppressFilterRefresh = true;
        try
        {
            AvailableSessions.Clear();
            AvailableSessions.Add(AllSessionsOption);
            foreach (var sess in sessionNames)
            {
                AvailableSessions.Add(sess);
            }

            SelectedSessionFilter = AvailableSessions.Contains(targetSession) ? targetSession : AllSessionsOption;
        }
        finally
        {
            _suppressFilterRefresh = false;
        }
    }

    private static string ExtractShopFromPayment(PosPaymentRecord p)
    {
        if (!string.IsNullOrWhiteSpace(p.SessionName) && p.SessionName.Contains('/'))
        {
            return p.SessionName.Split('/')[0].Trim();
        }
        if (!string.IsNullOrWhiteSpace(p.OrderName) && p.OrderName.Contains(" - "))
        {
            return p.OrderName.Split(" - ")[0].Trim();
        }
        return string.Empty;
    }

    private bool PaymentMatchesShop(PosPaymentRecord p, string shopName)
    {
        if (string.IsNullOrWhiteSpace(shopName) || shopName == AllShopsOption) return true;

        string extracted = ExtractShopFromPayment(p);
        if (string.Equals(extracted, shopName, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrWhiteSpace(p.SessionName) &&
            p.SessionName.StartsWith(shopName, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrWhiteSpace(p.OrderName) &&
            p.OrderName.StartsWith(shopName, StringComparison.OrdinalIgnoreCase))
            return true;

        var matchedSession = _allSessions.FirstOrDefault(s =>
            (p.SessionOdooId.HasValue && s.OdooId == p.SessionOdooId) ||
            (!string.IsNullOrWhiteSpace(p.SessionName) && string.Equals(s.Name, p.SessionName, StringComparison.OrdinalIgnoreCase)));

        return matchedSession != null && string.Equals(matchedSession.PosConfigName, shopName, StringComparison.OrdinalIgnoreCase);
    }

    public void SetSessionFilter(string? filterValue)
    {
        _suppressFilterRefresh = true;
        try
        {
            SessionFilter = filterValue;
            if (string.IsNullOrWhiteSpace(filterValue))
            {
                SelectedShopFilter = AllShopsOption;
                RebuildAvailableSessions(AllSessionsOption);
            }
            else if (AvailableShops.Any(s => string.Equals(s, filterValue, StringComparison.OrdinalIgnoreCase)))
            {
                SelectedShopFilter = AvailableShops.First(s => string.Equals(s, filterValue, StringComparison.OrdinalIgnoreCase));
                RebuildAvailableSessions(AllSessionsOption);
            }
            else
            {
                SelectedShopFilter = AllShopsOption;
                RebuildAvailableSessions(filterValue);
                if (!AvailableSessions.Contains(filterValue))
                {
                    AvailableSessions.Add(filterValue);
                }
                SelectedSessionFilter = filterValue;
            }
        }
        finally
        {
            _suppressFilterRefresh = false;
        }
        RefreshFilter();
    }

    [RelayCommand]
    public void ClearSessionFilter()
    {
        ClearFilters();
    }

    [RelayCommand]
    public void ClearFilters()
    {
        _suppressFilterRefresh = true;
        try
        {
            SessionFilter = null;
            SelectedShopFilter = AllShopsOption;
            RebuildAvailableSessions(AllSessionsOption);
            SelectedDatePreset = AllDatesOption;
            StartDate = null;
            EndDate = null;
            IsCustomDateRange = false;
            SearchText = string.Empty;
        }
        finally
        {
            _suppressFilterRefresh = false;
        }
        RefreshFilter();
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
    }

    private void RefreshFilter()
    {
        string query = SearchText.Trim();
        FlatPayments.Clear();
        GroupedPayments.Clear();

        IEnumerable<PosPaymentRecord> source = _allPayments;

        bool hasShop = !string.IsNullOrWhiteSpace(SelectedShopFilter) && SelectedShopFilter != AllShopsOption;
        bool hasSess = !string.IsNullOrWhiteSpace(SelectedSessionFilter) && SelectedSessionFilter != AllSessionsOption;
        bool hasDate = SelectedDatePreset != AllDatesOption || StartDate.HasValue || EndDate.HasValue;
        HasActiveFilter = hasShop || hasSess || hasDate || !string.IsNullOrWhiteSpace(query);

        if (hasShop)
        {
            source = source.Where(p => PaymentMatchesShop(p, SelectedShopFilter));
        }

        if (hasSess)
        {
            source = source.Where(p =>
                string.Equals(p.SessionName, SelectedSessionFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (StartDate.HasValue || EndDate.HasValue)
        {
            source = source.Where(p =>
            {
                if (!p.PaymentDate.HasValue) return false;
                var dt = p.PaymentDate.Value.Date;
                if (StartDate.HasValue && dt < StartDate.Value.Date) return false;
                if (EndDate.HasValue && dt > EndDate.Value.Date) return false;
                return true;
            });
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            source = source.Where(p =>
                p.OrderName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                p.PaymentMethodName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                p.SessionName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                p.CashierName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                p.AmountDisplay.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        decimal grandTotal = 0.00m;
        _matchingPayments.Clear();
        foreach (var p in source)
        {
            _matchingPayments.Add(p);
            grandTotal += p.Amount;
        }

        GrandTotalText = $"$ {grandTotal.ToString("N2", CultureInfo.InvariantCulture)}";
        IsEmptyStateVisible = _matchingPayments.Count == 0;
        CurrentPage = 1;
        ApplyPaging();

        if (IsDetailViewVisible)
        {
            if (SelectedPayment != null && !_matchingPayments.Contains(SelectedPayment))
            {
                SelectedPayment = _matchingPayments.FirstOrDefault();
                if (SelectedPayment == null)
                {
                    IsDetailViewVisible = false;
                }
            }
        }

        UpdatePager();
    }

    private void ApplyPaging()
    {
        FlatPayments.Clear();
        GroupedPayments.Clear();

        var pageItems = _matchingPayments
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize)
            .ToList();

        foreach (var p in pageItems)
        {
            FlatPayments.Add(p);
        }

        // Group by Payment Method + Shop/PaymentMethodId (matching Screenshot 8 where each Shop's Cash journal has its own Cash group)
        var groups = pageItems
            .GroupBy(p => new
            {
                Method = string.IsNullOrWhiteSpace(p.PaymentMethodName) ? "Cash" : p.PaymentMethodName,
                Key = p.PaymentMethodId.HasValue ? p.PaymentMethodId.Value.ToString() : ExtractShopFromPayment(p)
            });

        foreach (var g in groups)
        {
            var groupRecord = new PaymentGroupRecord
            {
                GroupName = g.Key.Method,
                Subtotal = g.Sum(p => p.Amount),
                IsExpanded = true
            };
            foreach (var item in g)
            {
                groupRecord.Payments.Add(item);
            }
            GroupedPayments.Add(groupRecord);
        }

        UpdatePager();
    }

    [RelayCommand]
    public void ToggleGroup(PaymentGroupRecord? group)
    {
        if (group == null) return;
        group.IsExpanded = !group.IsExpanded;
    }

    [RelayCommand]
    public void SelectPayment(PosPaymentRecord? payment)
    {
        if (payment == null) return;
        SelectedPayment = payment;
        IsDetailViewVisible = true;
        UpdatePager();
    }

    [RelayCommand]
    public void CloseDetail()
    {
        IsDetailViewVisible = false;
        SelectedPayment = null;
        UpdatePager();
    }

    private void UpdatePager()
    {
        if (IsDetailViewVisible)
        {
            if (SelectedPayment == null || _matchingPayments.Count == 0)
            {
                PagerText = "0 / 0";
                HasPreviousRecord = false;
                HasNextRecord = false;
                return;
            }

            int index = _matchingPayments.IndexOf(SelectedPayment);
            if (index < 0)
            {
                index = 0;
                SelectedPayment = _matchingPayments[0];
            }

            PagerText = $"{index + 1} / {_matchingPayments.Count}";
            HasPreviousRecord = index > 0;
            HasNextRecord = index < _matchingPayments.Count - 1;
        }
        else
        {
            int total = _matchingPayments.Count;
            int maxPage = total > 0 ? (int)Math.Ceiling((double)total / PageSize) : 1;
            if (CurrentPage > maxPage) CurrentPage = maxPage;
            if (CurrentPage < 1) CurrentPage = 1;

            if (total == 0)
            {
                ListCounterText = "0-0 / 0";
                HasPreviousRecord = false;
                HasNextRecord = false;
            }
            else
            {
                int start = (CurrentPage - 1) * PageSize + 1;
                int end = Math.Min(CurrentPage * PageSize, total);
                ListCounterText = $"{start}-{end} / {total}";
                HasPreviousRecord = CurrentPage > 1;
                HasNextRecord = CurrentPage < maxPage;
            }
        }
    }

    [RelayCommand]
    public void PreviousRecord()
    {
        if (IsDetailViewVisible)
        {
            if (SelectedPayment == null || _matchingPayments.Count == 0) return;
            int index = _matchingPayments.IndexOf(SelectedPayment);
            if (index > 0)
            {
                SelectedPayment = _matchingPayments[index - 1];
                UpdatePager();
            }
        }
        else
        {
            if (CurrentPage > 1)
            {
                CurrentPage--;
                ApplyPaging();
            }
        }
    }

    [RelayCommand]
    public void NextRecord()
    {
        if (IsDetailViewVisible)
        {
            if (SelectedPayment == null || _matchingPayments.Count == 0) return;
            int index = _matchingPayments.IndexOf(SelectedPayment);
            if (index >= 0 && index < _matchingPayments.Count - 1)
            {
                SelectedPayment = _matchingPayments[index + 1];
                UpdatePager();
            }
        }
        else
        {
            int maxPage = (int)Math.Ceiling((double)_matchingPayments.Count / PageSize);
            if (CurrentPage < maxPage)
            {
                CurrentPage++;
                ApplyPaging();
            }
        }
    }

    [RelayCommand]
    public async Task SyncPaymentsAsync()
    {
        if (IsSyncing) return;
        IsSyncing = true;
        SyncButtonText = "Syncing...";

        try
        {
            var config = await SqliteDatabaseService.Instance.GetOdooConfigAsync();
            if (string.IsNullOrWhiteSpace(config.ServerUrl) || string.IsNullOrWhiteSpace(config.DatabaseName))
            {
                ShowToast("Odoo connection is not configured in Settings.", "Warning");
                return;
            }

            var paymentsDto = await OdooSyncService.Instance.FetchOdooPaymentsAsync(config);
            var (imported, updated, deleted) = await SqliteDatabaseService.Instance.UpsertOdooPaymentsAsync(paymentsDto);

            await SqliteDatabaseService.Instance.UpdatePaymentSyncStatusAsync(DateTime.Now, "Success");

            await ReloadPaymentsAsync();
            PaymentsUpdated?.Invoke(this, EventArgs.Empty);

            ShowToast($"Payments synced: {imported} added, {updated} updated, {deleted} removed.", "Success");
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

    private void ShowToast(string message, string severity = "Success")
    {
        ToastMessage = message;
        ToastSeverity = severity;
        IsToastVisible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }
}