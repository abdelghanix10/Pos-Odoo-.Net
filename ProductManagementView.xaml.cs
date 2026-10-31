using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace OdooPosLogin;

public partial class ProductManagementView : UserControl
{
    private readonly List<ProductRecord> _allProducts = [];
    private readonly ObservableCollection<ProductRecord> _filteredProducts = [];

    public event EventHandler? ProductsUpdated;

    private readonly DispatcherTimer _toastTimer;
    private ProductRecord? _selectedProduct;

    public ProductManagementView()
    {
        InitializeComponent();

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            ProductToastBanner.Visibility = Visibility.Collapsed;
        };

        ProductsKanbanItemsControl.ItemsSource = _filteredProducts;

        _ = ReloadProductsFromDatabaseAsync();
    }

    public async Task ReloadProductsFromDatabaseAsync()
    {
        var products = await SqliteDatabaseService.Instance.LoadAllProductsAsync();
        _allProducts.Clear();
        _allProducts.AddRange(products);
        RefreshFilter();
    }

    private Brush GetSafeBrush(string resourceKey, Color fallbackColor)
    {
        if (TryFindResource(resourceKey) is Brush brush)
            return brush;
        return new SolidColorBrush(fallbackColor);
    }

    public void ShowToast(string message, bool isError = false, bool isWarning = false)
    {
        ProductToastText.Text = message;
        if (isWarning)
        {
            ProductToastDot.Fill = GetSafeBrush("WarningAmberBrush", Color.FromRgb(0xF5, 0x9E, 0x0B));
        }
        else if (isError)
        {
            ProductToastDot.Fill = GetSafeBrush("DangerRedBrush", Color.FromRgb(0xDC, 0x26, 0x26));
        }
        else
        {
            ProductToastDot.Fill = GetSafeBrush("ActiveGreenBrush", Color.FromRgb(0x10, 0xB9, 0x81));
        }
        ProductToastBanner.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    // =========================================================================
    // SEARCH & FILTER
    // =========================================================================
    private void RefreshFilter()
    {
        string query = SearchProductsTextBox.Text.Trim();
        _filteredProducts.Clear();

        IEnumerable<ProductRecord> source = _allProducts;
        if (!string.IsNullOrWhiteSpace(query))
        {
            source = source.Where(p =>
                p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(p.DefaultCode) && p.DefaultCode.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(p.Barcode) && p.Barcode.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(p.PosCategoryName) && p.PosCategoryName.Contains(query, StringComparison.OrdinalIgnoreCase)));
        }

        foreach (var item in source)
        {
            _filteredProducts.Add(item);
        }

        if (ProductDetailViewContainer.Visibility == Visibility.Visible)
        {
            UpdateDetailPagerVisual();
        }
    }

    private void SearchProductsTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        string q = SearchProductsTextBox.Text;
        SearchPlaceholderText.Visibility = string.IsNullOrEmpty(q) ? Visibility.Visible : Visibility.Collapsed;
        ClearSearchButton.Visibility = string.IsNullOrEmpty(q) ? Visibility.Collapsed : Visibility.Visible;
        RefreshFilter();
    }

    private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
    {
        SearchProductsTextBox.Text = string.Empty;
    }

    // =========================================================================
    // NAVIGATION: KANBAN VIEW <-> DETAIL VIEW
    // =========================================================================
    public void SwitchToListView()
    {
        ProductDetailViewContainer.Visibility = Visibility.Collapsed;
        ProductsKanbanViewContainer.Visibility = Visibility.Visible;
        BreadcrumbDetailSubPanel.Visibility = Visibility.Collapsed;
        DetailRecordPagerPanel.Visibility = Visibility.Collapsed;
    }

    public void SwitchToDetailView(ProductRecord product)
    {
        _selectedProduct = product;

        BreadcrumbCurrentProductName.Text = product.Name;
        FormProductNameDisplay.Text = product.Name;
        FormProductTypeDisplay.Text = product.ProductTypeDisplay;
        FormSalesPriceDisplay.Text = product.PriceDisplay;
        FormCostDisplay.Text = product.CostDisplay;
        FormInternalRefDisplay.Text = string.IsNullOrWhiteSpace(product.DefaultCode) ? "—" : product.DefaultCode;
        FormBarcodeDisplay.Text = product.BarcodeDisplay;
        FormCategoryDisplay.Text = product.CategoryDisplay;

        // Tags
        FormProductTagsItemsControl.ItemsSource = product.TagBadges;

        // Image
        if (product.ImageBitmap != null)
        {
            FormPreviewImage.Source = product.ImageBitmap;
            FormPreviewImage.Visibility = Visibility.Visible;
            FormPlaceholderIcon.Visibility = Visibility.Collapsed;
        }
        else
        {
            FormPreviewImage.Source = null;
            FormPreviewImage.Visibility = Visibility.Collapsed;
            FormPlaceholderIcon.Visibility = Visibility.Visible;
        }

        // Attributes Table
        FormAttributesItemsControl.ItemsSource = product.AttributeLines;
        NoAttributesNoticeText.Visibility = product.AttributeLines.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // Combos Table
        FormCombosItemsControl.ItemsSource = product.ComboLines;
        NoCombosNoticeText.Visibility = product.ComboLines.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // Reset to Tab 1
        SelectTab(1);

        ProductsKanbanViewContainer.Visibility = Visibility.Collapsed;
        ProductDetailViewContainer.Visibility = Visibility.Visible;
        BreadcrumbDetailSubPanel.Visibility = Visibility.Visible;

        UpdateDetailPagerVisual();
    }

    // =========================================================================
    // DETAIL VIEW RECORD SWITCHER (< >)
    // =========================================================================
    private void UpdateDetailPagerVisual()
    {
        if (_filteredProducts.Count == 0 || _selectedProduct == null)
        {
            DetailRecordPagerPanel.Visibility = Visibility.Collapsed;
            return;
        }

        int index = _filteredProducts.IndexOf(_selectedProduct);
        if (index < 0)
        {
            index = 0;
            _selectedProduct = _filteredProducts[0];
        }

        DetailRecordPagerPanel.Visibility = Visibility.Visible;
        DetailPagerCounterText.Text = $"{index + 1} / {_filteredProducts.Count}";
        DetailPrevRecordBtn.IsEnabled = index > 0;
        DetailNextRecordBtn.IsEnabled = index < _filteredProducts.Count - 1;
    }

    private void DetailPrevRecord_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProduct == null || _filteredProducts.Count == 0) return;
        int index = _filteredProducts.IndexOf(_selectedProduct);
        if (index > 0)
        {
            SwitchToDetailView(_filteredProducts[index - 1]);
        }
    }

    private void DetailNextRecord_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProduct == null || _filteredProducts.Count == 0) return;
        int index = _filteredProducts.IndexOf(_selectedProduct);
        if (index >= 0 && index < _filteredProducts.Count - 1)
        {
            SwitchToDetailView(_filteredProducts[index + 1]);
        }
    }

    private void ProductCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ProductRecord selectedProduct })
        {
            SwitchToDetailView(selectedProduct);
        }
    }

    private void BreadcrumbRoot_Click(object sender, RoutedEventArgs e)
    {
        SwitchToListView();
    }

    // =========================================================================
    // TABS
    // =========================================================================
    private void SelectTab(int tabIndex)
    {
        var purple = GetSafeBrush("OdooPurpleBrush", Color.FromRgb(0x71, 0x4B, 0x67));
        var gray = GetSafeBrush("SecondaryTextBrush", Color.FromRgb(0x6B, 0x72, 0x80));

        TabGeneralBtn.BorderBrush = tabIndex == 1 ? purple : Brushes.Transparent;
        TabGeneralBtn.Foreground = tabIndex == 1 ? purple : gray;

        TabAttributesBtn.BorderBrush = tabIndex == 2 ? purple : Brushes.Transparent;
        TabAttributesBtn.Foreground = tabIndex == 2 ? purple : gray;

        TabCombosBtn.BorderBrush = tabIndex == 3 ? purple : Brushes.Transparent;
        TabCombosBtn.Foreground = tabIndex == 3 ? purple : gray;

        TabGeneralContentContainer.Visibility = tabIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        TabAttributesContentContainer.Visibility = tabIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        TabCombosContentContainer.Visibility = tabIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TabGeneral_Click(object sender, RoutedEventArgs e) => SelectTab(1);
    private void TabAttributes_Click(object sender, RoutedEventArgs e) => SelectTab(2);
    private void TabCombos_Click(object sender, RoutedEventArgs e) => SelectTab(3);

    // =========================================================================
    // ODOO SYNCHRONIZATION (product.template)
    // =========================================================================
    private void SetSyncLoadingState(bool isLoading)
    {
        if (isLoading)
        {
            TopSyncProductsBtn.IsEnabled = false;
            SyncButtonText.Text = "Syncing...";
            var da = new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = TimeSpan.FromSeconds(1),
                RepeatBehavior = RepeatBehavior.Forever
            };
            TopSyncIconRotation.BeginAnimation(RotateTransform.AngleProperty, da);
        }
        else
        {
            TopSyncProductsBtn.IsEnabled = true;
            SyncButtonText.Text = "Sync from Odoo";
            TopSyncIconRotation.BeginAnimation(RotateTransform.AngleProperty, null);
            TopSyncIconRotation.Angle = 0;
        }
    }

    private async void TopSyncProductsBtn_Click(object sender, RoutedEventArgs e)
    {
        await PerformSyncFromOdooAsync();
    }

    public async Task PerformSyncFromOdooAsync()
    {
        SetSyncLoadingState(true);

        try
        {
            var config = await SqliteDatabaseService.Instance.GetOdooConfigAsync();
            if (string.IsNullOrWhiteSpace(config.ServerUrl))
            {
                ShowToast("Odoo connection not configured in Settings.", isWarning: true);
                SetSyncLoadingState(false);
                return;
            }

            var products = await OdooSyncService.Instance.FetchOdooProductsAsync(config, allowOfflineSimulation: true);

            var (imported, updated, deleted) = await SqliteDatabaseService.Instance.UpsertOdooProductsAsync(products);
            await SqliteDatabaseService.Instance.UpdateProductSyncStatusAsync(DateTime.Now, "Success");

            await ReloadProductsFromDatabaseAsync();
            ProductsUpdated?.Invoke(this, EventArgs.Empty);

            string summary = $"Sync Complete: {imported} imported, {updated} updated, {deleted} removed";
            ShowToast(summary, isError: false);
        }
        catch (Exception ex)
        {
            ShowToast($"Sync Failed: {ex.Message}", isError: true);
        }
        finally
        {
            SetSyncLoadingState(false);
        }
    }
}
