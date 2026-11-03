using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OdooPosLogin.ViewModels;

public partial class PosCategoriesViewModel : ViewModelBase
{
    private readonly List<PosCategoryRecord> _allCategories = [];
    private readonly List<ProductRecord> _allProducts = [];

    [ObservableProperty]
    private ObservableCollection<PosCategoryRecord> _filteredCategories = [];

    [ObservableProperty]
    private ObservableCollection<ProductRecord> _associatedProducts = [];

    [ObservableProperty]
    private string _associatedProductsCountDisplay = "0 Products";

    [ObservableProperty]
    private bool _hasNoAssociatedProducts = true;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isListView = true;

    [ObservableProperty]
    private bool _isKanbanView;

    [ObservableProperty]
    private bool _isDetailViewVisible;

    [ObservableProperty]
    private PosCategoryRecord? _selectedCategory;

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

    public void ResetToMainList()
    {
        IsDetailViewVisible = false;
        UpdatePager();
    }

    public async Task ReloadCategoriesAsync(bool resetDetailView = false)
    {
        try
        {
            if (resetDetailView)
            {
                IsDetailViewVisible = false;
            }

            var categories = await SqliteDatabaseService.Instance.LoadAllCategoriesAsync();
            _allCategories.Clear();
            _allCategories.AddRange(categories);

            var products = await SqliteDatabaseService.Instance.LoadAllProductsAsync();
            _allProducts.Clear();
            _allProducts.AddRange(products);

            RefreshFilter();
            UpdateAssociatedProducts();
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

        IsEmptyStateVisible = FilteredCategories.Count == 0;
        ListCounterText = FilteredCategories.Count > 0
            ? $"1-{FilteredCategories.Count} / {FilteredCategories.Count}"
            : "0-0 / 0";

        if (IsDetailViewVisible)
        {
            if (SelectedCategory != null)
            {
                var match = FilteredCategories.FirstOrDefault(c => c.OdooId == SelectedCategory.OdooId && c.Name == SelectedCategory.Name);
                SelectedCategory = match ?? FilteredCategories.FirstOrDefault();
                if (SelectedCategory == null)
                {
                    IsDetailViewVisible = false;
                }
            }
        }

        UpdatePager();
        UpdateAssociatedProducts();
    }

    private void UpdateAssociatedProducts()
    {
        AssociatedProducts.Clear();
        if (SelectedCategory == null)
        {
            AssociatedProductsCountDisplay = "0 Products";
            HasNoAssociatedProducts = true;
            return;
        }

        var matched = _allProducts
            .Where(p => p.HasPosCategory(SelectedCategory.OdooId, SelectedCategory.Name))
            .ToList();

        foreach (var prod in matched)
        {
            AssociatedProducts.Add(prod);
        }

        AssociatedProductsCountDisplay = $"{AssociatedProducts.Count} {(AssociatedProducts.Count == 1 ? "Product" : "Products")}";
        HasNoAssociatedProducts = AssociatedProducts.Count == 0;
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
    private async Task SelectCategory(PosCategoryRecord? category)
    {
        if (category == null) return;
        SelectedCategory = category;

        if (_allProducts.Count == 0)
        {
            var prods = await SqliteDatabaseService.Instance.LoadAllProductsAsync();
            _allProducts.Clear();
            _allProducts.AddRange(prods);
        }

        IsDetailViewVisible = true;
        UpdatePager();
        UpdateAssociatedProducts();
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
            SelectedCategory = FilteredCategories[index - 1];
            UpdatePager();
            UpdateAssociatedProducts();
        }
    }

    [RelayCommand]
    private void NextRecord()
    {
        if (SelectedCategory == null || FilteredCategories.Count == 0) return;
        int index = FilteredCategories.IndexOf(SelectedCategory);
        if (index >= 0 && index < FilteredCategories.Count - 1)
        {
            SelectedCategory = FilteredCategories[index + 1];
            UpdatePager();
            UpdateAssociatedProducts();
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

            var categories = await OdooSyncService.Instance.FetchOdooCategoriesAsync(config);
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