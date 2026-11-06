using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OdooPosLogin.ViewModels;

public partial class SessionsViewModel : ViewModelBase
{
    public const string AllShopsOption = "All Point of Sale";
    public const string AllDatesOption = "All Dates";
    public const string DatePresetToday = "Today";
    public const string DatePresetYesterday = "Yesterday";
    public const string DatePresetLast7Days = "Last 7 Days";
    public const string DatePresetThisMonth = "This Month";
    public const string DatePresetCustom = "Custom Range";

    private readonly List<PosSessionRecord> _allSessions = [];
    private bool _suppressFilterRefresh;

    [ObservableProperty]
    private ObservableCollection<PosSessionRecord> _filteredSessions = [];

    [ObservableProperty]
    private ObservableCollection<string> _availableShops = [AllShopsOption];

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
    private bool _isListView = true;

    [ObservableProperty]
    private bool _isKanbanView;

    [ObservableProperty]
    private bool _isDetailViewVisible;

    [ObservableProperty]
    private PosSessionRecord? _selectedSession;

    public const int PageSize = 80;

    [ObservableProperty]
    private int _currentPage = 1;

    private readonly List<PosSessionRecord> _matchingSessions = [];

    [ObservableProperty]
    private string _pagerText = "0 / 0";

    [ObservableProperty]
    private string _listCounterText = "0-0 / 0";

    [ObservableProperty]
    private bool _hasPreviousRecord;

    [ObservableProperty]
    private bool _hasNextRecord;

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

    public event Action<string>? NavigateToOrdersRequested;
    public event Action<string>? NavigateToPaymentsRequested;
    public event EventHandler? SessionsUpdated;

    private readonly DispatcherTimer _toastTimer;

    public SessionsViewModel()
    {
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            IsToastVisible = false;
        };

        _ = ReloadSessionsAsync();
    }

    partial void OnSearchTextChanged(string value)
    {
        if (!_suppressFilterRefresh) RefreshFilter();
    }

    partial void OnSelectedShopFilterChanged(string value)
    {
        if (!_suppressFilterRefresh) RefreshFilter();
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

    public async Task ReloadSessionsAsync(bool resetDetailView = false)
    {
        try
        {
            if (resetDetailView)
            {
                IsDetailViewVisible = false;
            }

            var sessions = await SqliteDatabaseService.Instance.LoadAllSessionsAsync();
            _allSessions.Clear();
            _allSessions.AddRange(sessions);
            RebuildShopFilterOptions();
            RefreshFilter();
        }
        catch (Exception ex)
        {
            ShowToast($"Failed to load sessions: {ex.Message}", "Error");
        }
    }

    private void RebuildShopFilterOptions()
    {
        _suppressFilterRefresh = true;
        try
        {
            string prev = SelectedShopFilter;
            var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in _allSessions)
            {
                if (!string.IsNullOrWhiteSpace(s.PosConfigName)) set.Add(s.PosConfigName.Trim());
            }

            AvailableShops.Clear();
            AvailableShops.Add(AllShopsOption);
            foreach (var shop in set)
            {
                AvailableShops.Add(shop);
            }

            SelectedShopFilter = AvailableShops.Contains(prev) ? prev : AllShopsOption;
        }
        finally
        {
            _suppressFilterRefresh = false;
        }
    }

    [RelayCommand]
    public void ClearFilters()
    {
        _suppressFilterRefresh = true;
        try
        {
            SelectedShopFilter = AllShopsOption;
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

    private void RefreshFilter()
    {
        string query = SearchText.Trim();
        FilteredSessions.Clear();

        IEnumerable<PosSessionRecord> source = _allSessions;

        bool hasShop = !string.IsNullOrWhiteSpace(SelectedShopFilter) && SelectedShopFilter != AllShopsOption;
        bool hasDate = SelectedDatePreset != AllDatesOption || StartDate.HasValue || EndDate.HasValue;
        HasActiveFilter = hasShop || hasDate || !string.IsNullOrWhiteSpace(query);

        if (hasShop)
        {
            source = source.Where(s => string.Equals(s.PosConfigName, SelectedShopFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (StartDate.HasValue || EndDate.HasValue)
        {
            source = source.Where(s =>
            {
                if (!s.StartAt.HasValue) return false;
                var dt = s.StartAt.Value.Date;
                if (StartDate.HasValue && dt < StartDate.Value.Date) return false;
                if (EndDate.HasValue && dt > EndDate.Value.Date) return false;
                return true;
            });
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            source = source.Where(s =>
                s.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                s.PosConfigName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                s.UserName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                s.StateDisplay.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        _matchingSessions.Clear();
        foreach (var s in source)
        {
            _matchingSessions.Add(s);
        }

        IsEmptyStateVisible = _matchingSessions.Count == 0;
        CurrentPage = 1;
        ApplyPaging();

        if (IsDetailViewVisible)
        {
            if (SelectedSession != null)
            {
                var match = _matchingSessions.FirstOrDefault(s => s.OdooId == SelectedSession.OdooId && s.Name == SelectedSession.Name);
                SelectedSession = match ?? _matchingSessions.FirstOrDefault();
                if (SelectedSession == null)
                {
                    IsDetailViewVisible = false;
                }
            }
        }

        UpdatePager();
    }

    private void ApplyPaging()
    {
        FilteredSessions.Clear();
        var pageItems = _matchingSessions
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize);
        foreach (var s in pageItems)
        {
            FilteredSessions.Add(s);
        }
        UpdatePager();
    }

    [RelayCommand]
    private void SwitchToList()
    {
        IsListView = true;
        IsKanbanView = false;
        IsDetailViewVisible = false;
        UpdatePager();
    }

    [RelayCommand]
    private void SwitchToKanban()
    {
        IsKanbanView = true;
        IsListView = false;
        IsDetailViewVisible = false;
        UpdatePager();
    }

    [RelayCommand]
    public void SelectSession(PosSessionRecord? session)
    {
        if (session == null) return;
        SelectedSession = session;
        IsDetailViewVisible = true;
        UpdatePager();
    }

    [RelayCommand]
    public void CloseDetail()
    {
        IsDetailViewVisible = false;
        SelectedSession = null;
        UpdatePager();
    }

    private void UpdatePager()
    {
        if (IsDetailViewVisible)
        {
            if (SelectedSession == null || _matchingSessions.Count == 0)
            {
                PagerText = "0 / 0";
                HasPreviousRecord = false;
                HasNextRecord = false;
                return;
            }

            int index = _matchingSessions.IndexOf(SelectedSession);
            if (index < 0)
            {
                index = 0;
                SelectedSession = _matchingSessions[0];
            }

            PagerText = $"{index + 1} / {_matchingSessions.Count}";
            HasPreviousRecord = index > 0;
            HasNextRecord = index < _matchingSessions.Count - 1;
        }
        else
        {
            int total = _matchingSessions.Count;
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
            if (SelectedSession == null || _matchingSessions.Count == 0) return;
            int index = _matchingSessions.IndexOf(SelectedSession);
            if (index > 0)
            {
                SelectedSession = _matchingSessions[index - 1];
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
            if (SelectedSession == null || _matchingSessions.Count == 0) return;
            int index = _matchingSessions.IndexOf(SelectedSession);
            if (index >= 0 && index < _matchingSessions.Count - 1)
            {
                SelectedSession = _matchingSessions[index + 1];
                UpdatePager();
            }
        }
        else
        {
            int maxPage = (int)Math.Ceiling((double)_matchingSessions.Count / PageSize);
            if (CurrentPage < maxPage)
            {
                CurrentPage++;
                ApplyPaging();
            }
        }
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
    }

    [RelayCommand]
    public async Task SyncSessionsAsync()
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

            var sessionsDto = await OdooSyncService.Instance.FetchOdooSessionsAsync(config);
            var (imported, updated, deleted) = await SqliteDatabaseService.Instance.UpsertOdooSessionsAsync(sessionsDto);

            await SqliteDatabaseService.Instance.UpdateSessionSyncStatusAsync(DateTime.Now, "Success");

            await ReloadSessionsAsync();
            SessionsUpdated?.Invoke(this, EventArgs.Empty);

            ShowToast($"Sessions synced: {imported} added, {updated} updated, {deleted} removed.", "Success");
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
    public void ContinueSelling()
    {
        if (SelectedSession == null) return;
        ShowToast($"Register '{SelectedSession.PosConfigName}' is active and ready.", "Success");
    }

    [RelayCommand]
    public void ViewOrders()
    {
        if (SelectedSession == null) return;
        NavigateToOrdersRequested?.Invoke(SelectedSession.Name);
    }

    [RelayCommand]
    public void ViewPayments()
    {
        if (SelectedSession == null) return;
        NavigateToPaymentsRequested?.Invoke(SelectedSession.Name);
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