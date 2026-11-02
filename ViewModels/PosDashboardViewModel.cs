using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OdooPosLogin.ViewModels;

public partial class PosDashboardViewModel : ViewModelBase
{
    private readonly List<PosConfigRecord> _allConfigs = [];

    [ObservableProperty]
    private ObservableCollection<PosConfigRecord> _filteredConfigs = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

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
    private int _totalShopsCount;

    [ObservableProperty]
    private int _activeSessionsCount;

    [ObservableProperty]
    private bool _isEmptyStateVisible;

    public event Action<PosConfigRecord>? OpenRegisterRequested;
    public event Action<string>? ViewOrdersRequested;
    public event Action<string>? ViewSessionsRequested;
    public event Action<string>? ViewPaymentsRequested;
    public event EventHandler? ConfigsUpdated;

    private readonly DispatcherTimer _toastTimer;

    public PosDashboardViewModel()
    {
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            IsToastVisible = false;
        };

        _ = ReloadConfigsAsync();
    }

    partial void OnSearchTextChanged(string value) => RefreshFilter();

    public async Task ReloadConfigsAsync()
    {
        try
        {
            var configs = await SqliteDatabaseService.Instance.LoadAllPosConfigsAsync();
            _allConfigs.Clear();
            _allConfigs.AddRange(configs);
            RefreshFilter();
            ConfigsUpdated?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ShowToast($"Failed to load POS configurations: {ex.Message}", "Error");
        }
    }

    private void RefreshFilter()
    {
        string query = SearchText.Trim();
        FilteredConfigs.Clear();

        IEnumerable<PosConfigRecord> source = _allConfigs;
        if (!string.IsNullOrWhiteSpace(query))
        {
            source = source.Where(c =>
                c.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (c.CurrentSessionName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (c.ActiveUserName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        foreach (var c in source)
        {
            FilteredConfigs.Add(c);
        }

        TotalShopsCount = _allConfigs.Count;
        ActiveSessionsCount = _allConfigs.Count(c => !c.IsClosed);
        IsEmptyStateVisible = FilteredConfigs.Count == 0;
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
    }

    [RelayCommand]
    public async Task SyncFromOdooAsync()
    {
        if (IsSyncing) return;

        try
        {
            IsSyncing = true;
            SyncButtonText = "Syncing Shops...";

            var config = await SqliteDatabaseService.Instance.GetOdooConfigAsync();
            if (string.IsNullOrWhiteSpace(config.ServerUrl) || string.IsNullOrWhiteSpace(config.DatabaseName))
            {
                ShowToast("Odoo connection is not configured in Settings.", "Warning");
                return;
            }

            var odooConfigs = await OdooSyncService.Instance.FetchOdooPosConfigsAsync(config);
            var (imported, updated, deleted) = await SqliteDatabaseService.Instance.UpsertOdooPosConfigsAsync(odooConfigs);

            string statusMsg = $"Sync Complete: {imported} imported, {updated} updated, {deleted} removed.";
            await SqliteDatabaseService.Instance.UpdatePosConfigSyncStatusAsync(DateTime.Now, statusMsg);

            await ReloadConfigsAsync();
            ShowToast(statusMsg, "Success");
        }
        catch (Exception ex)
        {
            ShowToast($"Sync Failed: {ex.Message}", "Error");
        }
        finally
        {
            IsSyncing = false;
            SyncButtonText = "Sync from Odoo";
        }
    }

    [RelayCommand]
    private void OpenRegister(PosConfigRecord? config)
    {
        if (config == null) return;
        OpenRegisterRequested?.Invoke(config);
    }

    [RelayCommand]
    private void ViewOrders(PosConfigRecord? config)
    {
        if (config == null) return;
        ViewOrdersRequested?.Invoke(config.Name);
    }

    [RelayCommand]
    private void ViewSessions(PosConfigRecord? config)
    {
        if (config == null) return;
        ViewSessionsRequested?.Invoke(config.Name);
    }

    [RelayCommand]
    private void ViewPayments(PosConfigRecord? config)
    {
        if (config == null) return;
        ViewPaymentsRequested?.Invoke(config.Name);
    }

    public void ShowToast(string message, string severity = "Success")
    {
        ToastMessage = message;
        ToastSeverity = severity;
        IsToastVisible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }
}