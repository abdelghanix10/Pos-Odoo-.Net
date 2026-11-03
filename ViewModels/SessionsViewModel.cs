using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OdooPosLogin.ViewModels;

public partial class SessionsViewModel : ViewModelBase
{
    private readonly List<PosSessionRecord> _allSessions = [];

    [ObservableProperty]
    private ObservableCollection<PosSessionRecord> _filteredSessions = [];

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

    partial void OnSearchTextChanged(string value) => RefreshFilter();

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
            RefreshFilter();
        }
        catch (Exception ex)
        {
            ShowToast($"Failed to load sessions: {ex.Message}", "Error");
        }
    }

    private void RefreshFilter()
    {
        string query = SearchText.Trim();
        FilteredSessions.Clear();

        IEnumerable<PosSessionRecord> source = _allSessions;
        if (!string.IsNullOrWhiteSpace(query))
        {
            source = source.Where(s =>
                s.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                s.PosConfigName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                s.UserName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                s.StateDisplay.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var s in source)
        {
            FilteredSessions.Add(s);
        }

        IsEmptyStateVisible = FilteredSessions.Count == 0;
        ListCounterText = FilteredSessions.Count > 0
            ? $"1-{FilteredSessions.Count} / {FilteredSessions.Count}"
            : "0-0 / 0";

        if (IsDetailViewVisible)
        {
            if (SelectedSession != null)
            {
                var match = FilteredSessions.FirstOrDefault(s => s.OdooId == SelectedSession.OdooId && s.Name == SelectedSession.Name);
                SelectedSession = match ?? FilteredSessions.FirstOrDefault();
                if (SelectedSession == null)
                {
                    IsDetailViewVisible = false;
                }
            }
        }

        UpdatePager();
    }

    [RelayCommand]
    private void SwitchToList()
    {
        IsListView = true;
        IsKanbanView = false;
        IsDetailViewVisible = false;
    }

    [RelayCommand]
    private void SwitchToKanban()
    {
        IsKanbanView = true;
        IsListView = false;
        IsDetailViewVisible = false;
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
    }

    private void UpdatePager()
    {
        if (SelectedSession == null || FilteredSessions.Count == 0)
        {
            PagerText = "0 / 0";
            HasPreviousRecord = false;
            HasNextRecord = false;
            return;
        }

        int index = FilteredSessions.IndexOf(SelectedSession);
        if (index < 0)
        {
            index = 0;
            SelectedSession = FilteredSessions[0];
        }

        PagerText = $"{index + 1} / {FilteredSessions.Count}";
        HasPreviousRecord = index > 0;
        HasNextRecord = index < FilteredSessions.Count - 1;
    }

    [RelayCommand]
    public void PreviousRecord()
    {
        if (SelectedSession == null || FilteredSessions.Count == 0) return;
        int index = FilteredSessions.IndexOf(SelectedSession);
        if (index > 0)
        {
            SelectedSession = FilteredSessions[index - 1];
            UpdatePager();
        }
    }

    [RelayCommand]
    public void NextRecord()
    {
        if (SelectedSession == null || FilteredSessions.Count == 0) return;
        int index = FilteredSessions.IndexOf(SelectedSession);
        if (index >= 0 && index < FilteredSessions.Count - 1)
        {
            SelectedSession = FilteredSessions[index + 1];
            UpdatePager();
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