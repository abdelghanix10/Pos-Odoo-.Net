using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OdooPosLogin.ViewModels;

public partial class PosCategoriesViewModel : ViewModelBase
{
    private readonly List<PosCategoryRecord> _allCategories = [];

    [ObservableProperty]
    private ObservableCollection<PosCategoryRecord> _filteredCategories = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isDetailViewVisible;

    [ObservableProperty]
    private PosCategoryRecord? _selectedCategory;

    [ObservableProperty]
    private string _pagerText = "1 / 1";

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

    public event EventHandler? CategoriesUpdated;

    private readonly DispatcherTimer _toastTimer;

    public PosCategoriesViewModel()
    {
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            IsToastVisible = false;
        };

        _ = ReloadCategoriesAsync();
    }

    partial void OnSearchTextChanged(string value) => RefreshFilter();

    public async Task ReloadCategoriesAsync()
    {
        try
        {
            var categories = await SqliteDatabaseService.Instance.LoadAllCategoriesAsync();
            _allCategories.Clear();
            _allCategories.AddRange(categories);
            RefreshFilter();
        }
        catch (Exception ex)
        {
            ShowToast($"Failed to load categories: {ex.Message}", "Error");
        }
    }

    private void RefreshFilter()
    {
        string query = SearchText.Trim();
        FilteredCategories.Clear();

        IEnumerable<PosCategoryRecord> source = _allCategories;
        if (!string.IsNullOrWhiteSpace(query))
        {
            source = source.Where(c =>
                c.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(c.ParentCategoryName) && c.ParentCategoryName.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(c.LinkedPosConfigs) && c.LinkedPosConfigs.Contains(query, StringComparison.OrdinalIgnoreCase)));
        }

        foreach (var c in source)
        {
            FilteredCategories.Add(c);
        }

        if (IsDetailViewVisible)
        {
            UpdatePager();
        }
    }

    private void UpdatePager()
    {
        if (FilteredCategories.Count == 0 || SelectedCategory == null)
        {
            PagerText = "0 / 0";
            HasPreviousRecord = false;
            HasNextRecord = false;
            return;
        }

        int index = FilteredCategories.IndexOf(SelectedCategory);
        if (index < 0)
        {
            index = 0;
            SelectedCategory = FilteredCategories[0];
        }

        PagerText = $"{index + 1} / {FilteredCategories.Count}";
        HasPreviousRecord = index > 0;
        HasNextRecord = index < FilteredCategories.Count - 1;
    }

    [RelayCommand]
    private void SelectCategory(PosCategoryRecord? category)
    {
        if (category == null) return;
        SelectedCategory = category;
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
        if (SelectedCategory == null || FilteredCategories.Count == 0) return;
        int index = FilteredCategories.IndexOf(SelectedCategory);
        if (index > 0)
        {
            SelectCategory(FilteredCategories[index - 1]);
        }
    }

    [RelayCommand]
    private void NextRecord()
    {
        if (SelectedCategory == null || FilteredCategories.Count == 0) return;
        int index = FilteredCategories.IndexOf(SelectedCategory);
        if (index >= 0 && index < FilteredCategories.Count - 1)
        {
            SelectCategory(FilteredCategories[index + 1]);
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
        IsSyncing = true;
        SyncButtonText = "Syncing...";

        try
        {
            var config = await SqliteDatabaseService.Instance.GetOdooConfigAsync();
            if (string.IsNullOrWhiteSpace(config.ServerUrl))
            {
                ShowToast("Odoo connection not configured in Settings.", "Warning");
                return;
            }

            var categories = await OdooSyncService.Instance.FetchOdooCategoriesAsync(config, allowOfflineSimulation: true);
            var (imported, updated, deleted) = await SqliteDatabaseService.Instance.UpsertOdooCategoriesAsync(categories);
            await SqliteDatabaseService.Instance.UpdateCategorySyncStatusAsync(DateTime.Now, "Success");

            await ReloadCategoriesAsync();
            CategoriesUpdated?.Invoke(this, EventArgs.Empty);

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
