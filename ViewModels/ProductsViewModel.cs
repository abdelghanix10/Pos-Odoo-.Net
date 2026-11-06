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

    public const int PageSize = 80;

    [ObservableProperty]
    private int _currentPage = 1;

    private readonly List<ProductRecord> _matchingProducts = [];

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

        _matchingProducts.Clear();
        foreach (var p in source)
        {
            _matchingProducts.Add(p);
        }

        IsEmptyStateVisible = _matchingProducts.Count == 0;
        CurrentPage = 1;
        ApplyPaging();

        if (IsDetailViewVisible)
        {
            if (SelectedProduct != null)
            {
                var match = _matchingProducts.FirstOrDefault(p => p.OdooId == SelectedProduct.OdooId && p.Name == SelectedProduct.Name);
                SelectedProduct = match ?? _matchingProducts.FirstOrDefault();
                if (SelectedProduct == null)
                {
                    IsDetailViewVisible = false;
                }
            }
        }

        UpdatePager();
    }

    private void ApplyPaging()
    {
        FilteredProducts.Clear();
        var pageItems = _matchingProducts
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize);
        foreach (var p in pageItems)
        {
            FilteredProducts.Add(p);
        }
        UpdatePager();
    }

    private void UpdatePager()
    {
        if (IsDetailViewVisible)
        {
            if (_matchingProducts.Count == 0 || SelectedProduct == null)
            {
                PagerText = "0 / 0";
                HasPreviousRecord = false;
                HasNextRecord = false;
                return;
            }

            int index = _matchingProducts.IndexOf(SelectedProduct);
            if (index < 0)
            {
                index = 0;
                SelectedProduct = _matchingProducts[0];
            }

            PagerText = $"{index + 1} / {_matchingProducts.Count}";
            HasPreviousRecord = index > 0;
            HasNextRecord = index < _matchingProducts.Count - 1;
        }
        else
        {
            int total = _matchingProducts.Count;
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
    private void SwitchToKanban()
    {
        IsKanbanView = true;
        IsListView = false;
        IsDetailViewVisible = false;
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
        UpdatePager();
    }

    [RelayCommand]
    private void PreviousRecord()
    {
        if (IsDetailViewVisible)
        {
            if (SelectedProduct == null || _matchingProducts.Count == 0) return;
            int index = _matchingProducts.IndexOf(SelectedProduct);
            if (index > 0)
            {
                SelectedProduct = _matchingProducts[index - 1];
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
            if (SelectedProduct == null || _matchingProducts.Count == 0) return;
            int index = _matchingProducts.IndexOf(SelectedProduct);
            if (index >= 0 && index < _matchingProducts.Count - 1)
            {
                SelectedProduct = _matchingProducts[index + 1];
                UpdatePager();
            }
        }
        else
        {
            int maxPage = (int)Math.Ceiling((double)_matchingProducts.Count / PageSize);
            if (CurrentPage < maxPage)
            {
                CurrentPage++;
                ApplyPaging();
            }
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