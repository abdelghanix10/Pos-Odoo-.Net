using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OdooPosLogin.ViewModels;

public partial class ProductAttributesViewModel : ViewModelBase
{
    private readonly List<ProductAttributeRecord> _allAttributes = [];
    private readonly List<ProductRecord> _allProducts = [];

    [ObservableProperty]
    private ObservableCollection<ProductAttributeRecord> _filteredAttributes = [];

    [ObservableProperty]
    private ObservableCollection<ProductRecord> _associatedProducts = [];

    [ObservableProperty]
    private string _associatedProductsCountDisplay = "0 Products";

    [ObservableProperty]
    private bool _hasNoAssociatedProducts = true;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isDetailViewVisible;

    [ObservableProperty]
    private ProductAttributeRecord? _selectedAttribute;

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

    public event EventHandler? AttributesUpdated;

    private readonly DispatcherTimer _toastTimer;

    public ProductAttributesViewModel()
    {
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            IsToastVisible = false;
        };

        _ = ReloadAttributesAsync();
    }

    partial void OnSearchTextChanged(string value) => RefreshFilter();

    public void ResetToMainList()
    {
        IsDetailViewVisible = false;
        UpdatePager();
    }

    public async Task ReloadAttributesAsync(bool resetDetailView = false)
    {
        try
        {
            if (resetDetailView)
            {
                IsDetailViewVisible = false;
            }

            var attributes = await SqliteDatabaseService.Instance.LoadAllAttributesAsync();
            _allAttributes.Clear();
            _allAttributes.AddRange(attributes);

            var products = await SqliteDatabaseService.Instance.LoadAllProductsAsync();
            _allProducts.Clear();
            _allProducts.AddRange(products);

            RefreshFilter();
            UpdateAssociatedProducts();
        }
        catch (Exception ex)
        {
            ShowToast($"Failed to load attributes: {ex.Message}", "Error");
        }
    }

    private void RefreshFilter()
    {
        string query = SearchText.Trim();
        FilteredAttributes.Clear();

        IEnumerable<ProductAttributeRecord> source = _allAttributes;
        if (!string.IsNullOrWhiteSpace(query))
        {
            source = source.Where(a =>
                a.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                a.DisplayTypeFormatted.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                a.CreateVariantFormatted.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var a in source)
        {
            FilteredAttributes.Add(a);
        }

        IsEmptyStateVisible = FilteredAttributes.Count == 0;
        ListCounterText = FilteredAttributes.Count > 0
            ? $"1-{FilteredAttributes.Count} / {FilteredAttributes.Count}"
            : "0-0 / 0";

        if (IsDetailViewVisible)
        {
            if (SelectedAttribute != null)
            {
                var match = FilteredAttributes.FirstOrDefault(a => a.OdooId == SelectedAttribute.OdooId);
                SelectedAttribute = match ?? FilteredAttributes.FirstOrDefault();
                if (SelectedAttribute == null)
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
        if (SelectedAttribute == null)
        {
            AssociatedProductsCountDisplay = "0 Products";
            HasNoAssociatedProducts = true;
            return;
        }

        var matched = _allProducts
            .Where(p => p.HasAttribute(SelectedAttribute.Name))
            .ToList();

        foreach (var prod in matched)
        {
            AssociatedProducts.Add(prod);
        }

        if (AssociatedProducts.Count > 0 && SelectedAttribute.NumberRelatedProducts == 0)
        {
            SelectedAttribute.NumberRelatedProducts = AssociatedProducts.Count;
        }

        int count = Math.Max(AssociatedProducts.Count, SelectedAttribute.NumberRelatedProducts);
        AssociatedProductsCountDisplay = $"{count} {(count == 1 ? "Product" : "Products")}";
        HasNoAssociatedProducts = AssociatedProducts.Count == 0;
    }

    private void UpdatePager()
    {
        if (FilteredAttributes.Count == 0 || SelectedAttribute == null)
        {
            PagerText = "0 / 0";
            HasPreviousRecord = false;
            HasNextRecord = false;
            return;
        }

        int index = FilteredAttributes.IndexOf(SelectedAttribute);
        if (index < 0)
        {
            index = 0;
            SelectedAttribute = FilteredAttributes[0];
        }

        PagerText = $"{index + 1} / {FilteredAttributes.Count}";
        HasPreviousRecord = index > 0;
        HasNextRecord = index < FilteredAttributes.Count - 1;
    }

    [RelayCommand]
    private async Task SelectAttribute(ProductAttributeRecord? attribute)
    {
        if (attribute == null) return;
        SelectedAttribute = attribute;

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
        if (SelectedAttribute == null || FilteredAttributes.Count == 0) return;
        int index = FilteredAttributes.IndexOf(SelectedAttribute);
        if (index > 0)
        {
            SelectedAttribute = FilteredAttributes[index - 1];
            UpdatePager();
            UpdateAssociatedProducts();
        }
    }

    [RelayCommand]
    private void NextRecord()
    {
        if (SelectedAttribute == null || FilteredAttributes.Count == 0) return;
        int index = FilteredAttributes.IndexOf(SelectedAttribute);
        if (index >= 0 && index < FilteredAttributes.Count - 1)
        {
            SelectedAttribute = FilteredAttributes[index + 1];
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

            var (attrs, vals) = await OdooSyncService.Instance.FetchOdooAttributesAsync(config);
            var (aImp, aUpd, aDel, vImp, vUpd, vDel) = await SqliteDatabaseService.Instance.UpsertOdooAttributesAsync(attrs, vals);
            await SqliteDatabaseService.Instance.UpdateAttributeSyncStatusAsync(DateTime.Now, "Success");

            await ReloadAttributesAsync();
            AttributesUpdated?.Invoke(this, EventArgs.Empty);

            string summary = $"Sync Complete: {aImp} imported, {aUpd} updated, {aDel} removed ({vals.Count} values)";
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