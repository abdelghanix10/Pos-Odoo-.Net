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

    public const int PageSize = 80;

    [ObservableProperty]
    private int _currentPage = 1;

    private readonly List<PosPaymentMethodRecord> _matchingMethods = [];

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

        _matchingMethods.Clear();
        foreach (var m in source)
        {
            _matchingMethods.Add(m);
        }

        IsEmptyStateVisible = _matchingMethods.Count == 0;
        CurrentPage = 1;
        ApplyPaging();

        if (IsDetailViewVisible)
        {
            if (SelectedMethod != null && !_matchingMethods.Contains(SelectedMethod))
            {
                SelectedMethod = _matchingMethods.FirstOrDefault();
                if (SelectedMethod == null)
                {
                    IsDetailViewVisible = false;
                }
            }
        }

        UpdatePager();
    }

    private void ApplyPaging()
    {
        FilteredMethods.Clear();
        var pageItems = _matchingMethods
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize);
        foreach (var m in pageItems)
        {
            FilteredMethods.Add(m);
        }
        UpdatePager();
    }

    private void UpdatePager()
    {
        if (IsDetailViewVisible)
        {
            if (_matchingMethods.Count == 0 || SelectedMethod == null)
            {
                PagerText = "0 / 0";
                HasPreviousRecord = false;
                HasNextRecord = false;
                return;
            }

            int index = _matchingMethods.IndexOf(SelectedMethod);
            if (index < 0)
            {
                index = 0;
                SelectedMethod = _matchingMethods[0];
            }

            PagerText = $"{index + 1} / {_matchingMethods.Count}";
            HasPreviousRecord = index > 0;
            HasNextRecord = index < _matchingMethods.Count - 1;
        }
        else
        {
            int total = _matchingMethods.Count;
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
        UpdatePager();
    }

    [RelayCommand]
    private void PreviousRecord()
    {
        if (IsDetailViewVisible)
        {
            if (SelectedMethod == null || _matchingMethods.Count == 0) return;
            int index = _matchingMethods.IndexOf(SelectedMethod);
            if (index > 0)
            {
                SelectedMethod = _matchingMethods[index - 1];
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
    private void NextRecord()
    {
        if (IsDetailViewVisible)
        {
            if (SelectedMethod == null || _matchingMethods.Count == 0) return;
            int index = _matchingMethods.IndexOf(SelectedMethod);
            if (index >= 0 && index < _matchingMethods.Count - 1)
            {
                SelectedMethod = _matchingMethods[index + 1];
                UpdatePager();
            }
        }
        else
        {
            int maxPage = (int)Math.Ceiling((double)_matchingMethods.Count / PageSize);
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