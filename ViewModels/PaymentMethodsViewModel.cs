using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OdooPosLogin.ViewModels;

public partial class PaymentMethodsViewModel : ViewModelBase
{
    private readonly List<PosPaymentMethodRecord> _allMethods = [];

    [ObservableProperty]
    private ObservableCollection<PosPaymentMethodRecord> _filteredMethods = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isListView = true;

    [ObservableProperty]
    private bool _isKanbanView;

    [ObservableProperty]
    private bool _isDetailViewVisible;

    [ObservableProperty]
    private PosPaymentMethodRecord? _selectedMethod;

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

    public event EventHandler? PaymentMethodsUpdated;

    private readonly DispatcherTimer _toastTimer;

    public PaymentMethodsViewModel()
    {
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            IsToastVisible = false;
        };

        _ = ReloadPaymentMethodsAsync();
    }

    partial void OnSearchTextChanged(string value) => RefreshFilter();

    public void ResetToMainList()
    {
        IsDetailViewVisible = false;
        UpdatePager();
    }

    public async Task ReloadPaymentMethodsAsync(bool resetDetailView = false)
    {
        try
        {
            if (resetDetailView)
            {
                IsDetailViewVisible = false;
            }

            var methods = await SqliteDatabaseService.Instance.LoadAllPaymentMethodsAsync();
            _allMethods.Clear();
            _allMethods.AddRange(methods);
            RefreshFilter();
        }
        catch (Exception ex)
        {
            ShowToast($"Failed to load payment methods: {ex.Message}", "Error");
        }
    }

    private void RefreshFilter()
    {
        string query = SearchText.Trim();
        FilteredMethods.Clear();

        IEnumerable<PosPaymentMethodRecord> source = _allMethods;
        if (!string.IsNullOrWhiteSpace(query))
        {
            source = source.Where(m =>
                m.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                m.JournalName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                m.CompanyName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                m.LinkedPosConfigs.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                m.MethodTypeDisplay.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var m in source)
        {
            FilteredMethods.Add(m);
        }

        IsEmptyStateVisible = FilteredMethods.Count == 0;
        ListCounterText = FilteredMethods.Count > 0
            ? $"1-{FilteredMethods.Count} / {FilteredMethods.Count}"
            : "0-0 / 0";

        if (IsDetailViewVisible)
        {
            if (SelectedMethod != null && !FilteredMethods.Contains(SelectedMethod))
            {
                SelectedMethod = FilteredMethods.FirstOrDefault();
                if (SelectedMethod == null)
                {
                    IsDetailViewVisible = false;
                }
            }
        }

        UpdatePager();
    }

    private void UpdatePager()
    {
        if (FilteredMethods.Count == 0 || SelectedMethod == null)
        {
            PagerText = "0 / 0";
            HasPreviousRecord = false;
            HasNextRecord = false;
            return;
        }

        int index = FilteredMethods.IndexOf(SelectedMethod);
        if (index < 0)
        {
            index = 0;
            SelectedMethod = FilteredMethods[0];
        }

        PagerText = $"{index + 1} / {FilteredMethods.Count}";
        HasPreviousRecord = index > 0;
        HasNextRecord = index < FilteredMethods.Count - 1;
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
    private void SelectPaymentMethod(PosPaymentMethodRecord? method)
    {
        if (method == null) return;
        SelectedMethod = method;
        IsDetailViewVisible = true;
        UpdatePager();
    }

    [RelayCommand]
    private void BackToList()
    {
        IsDetailViewVisible = false;
    }

    [RelayCommand]
    private void PreviousRecord()
    {
        if (SelectedMethod == null || FilteredMethods.Count == 0) return;
        int index = FilteredMethods.IndexOf(SelectedMethod);
        if (index > 0)
        {
            SelectedMethod = FilteredMethods[index - 1];
            UpdatePager();
        }
    }

    [RelayCommand]
    private void NextRecord()
    {
        if (SelectedMethod == null || FilteredMethods.Count == 0) return;
        int index = FilteredMethods.IndexOf(SelectedMethod);
        if (index >= 0 && index < FilteredMethods.Count - 1)
        {
            SelectedMethod = FilteredMethods[index + 1];
            UpdatePager();
        }
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

            var methods = await OdooSyncService.Instance.FetchOdooPaymentMethodsAsync(config);
            var (imported, updated, deleted) = await SqliteDatabaseService.Instance.UpsertOdooPaymentMethodsAsync(methods);
            await SqliteDatabaseService.Instance.UpdatePaymentMethodSyncStatusAsync(DateTime.Now, "Success");

            await ReloadPaymentMethodsAsync();
            PaymentMethodsUpdated?.Invoke(this, EventArgs.Empty);

            ShowToast($"Payment methods synced: {imported} imported, {updated} updated, {deleted} removed.", "Success");
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

    public void ShowToast(string message, string severity = "Success")
    {
        ToastMessage = message;
        ToastSeverity = severity;
        IsToastVisible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }
}