using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OdooPosLogin.ViewModels;

public partial class ComboChoicesViewModel : ViewModelBase
{
    private readonly List<ComboChoiceRecord> _allCombos = [];

    [ObservableProperty]
    private ObservableCollection<ComboChoiceRecord> _filteredCombos = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isListView = true;

    [ObservableProperty]
    private bool _isKanbanView;

    [ObservableProperty]
    private bool _isDetailViewVisible;

    [ObservableProperty]
    private ComboChoiceRecord? _selectedCombo;

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

    public event EventHandler? CombosUpdated;

    private readonly DispatcherTimer _toastTimer;

    public ComboChoicesViewModel()
    {
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            IsToastVisible = false;
        };

        _ = ReloadCombosAsync();
    }

    partial void OnSearchTextChanged(string value) => RefreshFilter();

    public void ResetToMainList()
    {
        IsDetailViewVisible = false;
        UpdatePager();
    }

    public async Task ReloadCombosAsync(bool resetDetailView = false)
    {
        try
        {
            if (resetDetailView)
            {
                IsDetailViewVisible = false;
            }

            var combos = await SqliteDatabaseService.Instance.LoadAllComboChoicesAsync();
            _allCombos.Clear();
            _allCombos.AddRange(combos);
            RefreshFilter();
        }
        catch (Exception ex)
        {
            ShowToast($"Failed to load combo choices: {ex.Message}", "Error");
        }
    }

    private void RefreshFilter()
    {
        string query = SearchText.Trim();
        FilteredCombos.Clear();

        IEnumerable<ComboChoiceRecord> source = _allCombos;
        if (!string.IsNullOrWhiteSpace(query))
        {
            source = source.Where(c =>
                c.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                c.IncludedProductNames.Any(p => p.Contains(query, StringComparison.OrdinalIgnoreCase)));
        }

        foreach (var c in source)
        {
            FilteredCombos.Add(c);
        }

        IsEmptyStateVisible = FilteredCombos.Count == 0;
        ListCounterText = FilteredCombos.Count > 0
            ? $"1-{FilteredCombos.Count} / {FilteredCombos.Count}"
            : "0-0 / 0";

        if (IsDetailViewVisible)
        {
            if (SelectedCombo != null)
            {
                var match = FilteredCombos.FirstOrDefault(c => c.OdooId == SelectedCombo.OdooId && c.Name == SelectedCombo.Name);
                SelectedCombo = match ?? FilteredCombos.FirstOrDefault();
                if (SelectedCombo == null)
                {
                    IsDetailViewVisible = false;
                }
            }
        }

        UpdatePager();
    }

    private void UpdatePager()
    {
        if (FilteredCombos.Count == 0 || SelectedCombo == null)
        {
            PagerText = "0 / 0";
            HasPreviousRecord = false;
            HasNextRecord = false;
            return;
        }

        int index = FilteredCombos.IndexOf(SelectedCombo);
        if (index < 0)
        {
            index = 0;
            SelectedCombo = FilteredCombos[0];
        }

        PagerText = $"{index + 1} / {FilteredCombos.Count}";
        HasPreviousRecord = index > 0;
        HasNextRecord = index < FilteredCombos.Count - 1;
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
    private void SelectCombo(ComboChoiceRecord? combo)
    {
        if (combo == null) return;
        SelectedCombo = combo;
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
        if (SelectedCombo == null || FilteredCombos.Count == 0) return;
        int index = FilteredCombos.IndexOf(SelectedCombo);
        if (index > 0)
        {
            SelectCombo(FilteredCombos[index - 1]);
        }
    }

    [RelayCommand]
    private void NextRecord()
    {
        if (SelectedCombo == null || FilteredCombos.Count == 0) return;
        int index = FilteredCombos.IndexOf(SelectedCombo);
        if (index >= 0 && index < FilteredCombos.Count - 1)
        {
            SelectCombo(FilteredCombos[index + 1]);
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

            var (combos, lines) = await OdooSyncService.Instance.FetchOdooComboChoicesAsync(config);
            var (imported, updated, deleted) = await SqliteDatabaseService.Instance.UpsertOdooCombosAsync(combos, lines);
            await SqliteDatabaseService.Instance.UpdateComboSyncStatusAsync(DateTime.Now, "Success");

            await ReloadCombosAsync();
            CombosUpdated?.Invoke(this, EventArgs.Empty);

            string summary = $"Sync Complete: {imported} imported, {updated} updated, {deleted} removed";
            ShowToast(summary, "Success");
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