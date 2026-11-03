using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OdooPosLogin.ViewModels;

public partial class ProductsViewModel : ViewModelBase
{
    private readonly List<ProductRecord> _allProducts = [];

    [ObservableProperty]
    private ObservableCollection<ProductRecord> _filteredProducts = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isKanbanView = true;

    [ObservableProperty]
    private bool _isListView;

    [ObservableProperty]
    private bool _isDetailViewVisible;

    [ObservableProperty]
    private ProductRecord? _selectedProduct;

    [ObservableProperty]
    private int _activeTabIndex = 1; // 1: General Info, 2: Attributes & Variants, 3: Sales/Combos, 4: Point of Sale

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

    public event EventHandler? ProductsUpdated;

    private readonly DispatcherTimer _toastTimer;

    public ProductsViewModel()
    {
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            IsToastVisible = false;
        };

        _ = ReloadProductsAsync();
    }

    partial void OnSearchTextChanged(string value)
    {
        RefreshFilter();
    }

    public void ResetToMainList()
    {
        IsDetailViewVisible = false;
        UpdatePager();
    }

    public async Task ReloadProductsAsync(bool resetDetailView = false)
    {
        try
        {
            if (resetDetailView)
            {
                IsDetailViewVisible = false;
            }

            var products = await SqliteDatabaseService.Instance.LoadAllProductsAsync();
            _allProducts.Clear();
            _allProducts.AddRange(products);
            RefreshFilter();
        }
        catch (Exception ex)
        {
            ShowToast($"Failed to load products: {ex.Message}", "Error");
        }
    }

    private void RefreshFilter()
    {
        string query = SearchText.Trim();
        FilteredProducts.Clear();

        IEnumerable<ProductRecord> source = _allProducts;
        if (!string.IsNullOrWhiteSpace(query))
        {
            source = source.Where(p =>
                p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(p.DefaultCode) && p.DefaultCode.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(p.Barcode) && p.Barcode.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(p.PosCategoryName) && p.PosCategoryName.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(p.InternalCategoryName) && p.InternalCategoryName.Contains(query, StringComparison.OrdinalIgnoreCase)));
        }

        foreach (var p in source)
        {
            FilteredProducts.Add(p);
        }

        IsEmptyStateVisible = FilteredProducts.Count == 0;
        ListCounterText = FilteredProducts.Count > 0
            ? $"1-{FilteredProducts.Count} / {FilteredProducts.Count}"
            : "0-0 / 0";

        if (IsDetailViewVisible)
        {
            if (SelectedProduct != null)
            {
                var match = FilteredProducts.FirstOrDefault(p => p.OdooId == SelectedProduct.OdooId && p.Name == SelectedProduct.Name);
                SelectedProduct = match ?? FilteredProducts.FirstOrDefault();
                if (SelectedProduct == null)
                {
                    IsDetailViewVisible = false;
                }
            }
        }

        UpdatePager();
    }

    private void UpdatePager()
    {
        if (FilteredProducts.Count == 0 || SelectedProduct == null)
        {
            PagerText = "0 / 0";
            HasPreviousRecord = false;
            HasNextRecord = false;
            return;
        }

        int index = FilteredProducts.IndexOf(SelectedProduct);
        if (index < 0)
        {
            index = 0;
            SelectedProduct = FilteredProducts[0];
        }

        PagerText = $"{index + 1} / {FilteredProducts.Count}";
        HasPreviousRecord = index > 0;
        HasNextRecord = index < FilteredProducts.Count - 1;
    }

    [RelayCommand]
    private void SwitchToKanban()
    {
        IsKanbanView = true;
        IsListView = false;
        IsDetailViewVisible = false;
    }

    [RelayCommand]
    private void SwitchToList()
    {
        IsListView = true;
        IsKanbanView = false;
        IsDetailViewVisible = false;
    }

    [RelayCommand]
    public void SelectProduct(ProductRecord? product)
    {
        if (product == null) return;
        SelectedProduct = product;
        ActiveTabIndex = 1;
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
        if (SelectedProduct == null || FilteredProducts.Count == 0) return;
        int index = FilteredProducts.IndexOf(SelectedProduct);
        if (index > 0)
        {
            SelectedProduct = FilteredProducts[index - 1];
            UpdatePager();
        }
    }

    [RelayCommand]
    private void NextRecord()
    {
        if (SelectedProduct == null || FilteredProducts.Count == 0) return;
        int index = FilteredProducts.IndexOf(SelectedProduct);
        if (index >= 0 && index < FilteredProducts.Count - 1)
        {
            SelectedProduct = FilteredProducts[index + 1];
            UpdatePager();
        }
    }

    [RelayCommand]
    private void SelectTab(string tabIndexStr)
    {
        if (int.TryParse(tabIndexStr, out int tabIndex))
        {
            ActiveTabIndex = tabIndex;
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

            var products = await OdooSyncService.Instance.FetchOdooProductsAsync(config);
            var (imported, updated, deleted) = await SqliteDatabaseService.Instance.UpsertOdooProductsAsync(products);
            await SqliteDatabaseService.Instance.UpdateProductSyncStatusAsync(DateTime.Now, "Success");

            await ReloadProductsAsync();
            ProductsUpdated?.Invoke(this, EventArgs.Empty);

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