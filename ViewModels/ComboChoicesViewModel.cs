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

    public const int PageSize = 80;

    [ObservableProperty]
    private int _currentPage = 1;

    private readonly List<ComboChoiceRecord> _matchingCombos = [];

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

        _matchingCombos.Clear();
        foreach (var c in source)
        {
            _matchingCombos.Add(c);
        }

        IsEmptyStateVisible = _matchingCombos.Count == 0;
        CurrentPage = 1;
        ApplyPaging();

        if (IsDetailViewVisible)
        {
            if (SelectedCombo != null)
            {
                var match = _matchingCombos.FirstOrDefault(c => c.OdooId == SelectedCombo.OdooId && c.Name == SelectedCombo.Name);
                SelectedCombo = match ?? _matchingCombos.FirstOrDefault();
                if (SelectedCombo == null)
                {
                    IsDetailViewVisible = false;
                }
            }
        }

        UpdatePager();
    }

    private void ApplyPaging()
    {
        FilteredCombos.Clear();
        var pageItems = _matchingCombos
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize);
        foreach (var c in pageItems)
        {
            FilteredCombos.Add(c);
        }
        UpdatePager();
    }

    private void UpdatePager()
    {
        if (IsDetailViewVisible)
        {
            if (_matchingCombos.Count == 0 || SelectedCombo == null)
            {
                PagerText = "0 / 0";
                HasPreviousRecord = false;
                HasNextRecord = false;
                return;
            }

            int index = _matchingCombos.IndexOf(SelectedCombo);
            if (index < 0)
            {
                index = 0;
                SelectedCombo = _matchingCombos[0];
            }

            PagerText = $"{index + 1} / {_matchingCombos.Count}";
            HasPreviousRecord = index > 0;
            HasNextRecord = index < _matchingCombos.Count - 1;
        }
        else
        {
            int total = _matchingCombos.Count;
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
        UpdatePager();
    }

    [RelayCommand]
    private void PreviousRecord()
    {
        if (IsDetailViewVisible)
        {
            if (SelectedCombo == null || _matchingCombos.Count == 0) return;
            int index = _matchingCombos.IndexOf(SelectedCombo);
            if (index > 0)
            {
                SelectCombo(_matchingCombos[index - 1]);
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
            if (SelectedCombo == null || _matchingCombos.Count == 0) return;
            int index = _matchingCombos.IndexOf(SelectedCombo);
            if (index >= 0 && index < _matchingCombos.Count - 1)
            {
                SelectCombo(_matchingCombos[index + 1]);
            }
        }
        else
        {
            int maxPage = (int)Math.Ceiling((double)_matchingCombos.Count / PageSize);
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