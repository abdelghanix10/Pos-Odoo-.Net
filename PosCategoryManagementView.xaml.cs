using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace OdooPosLogin;

public partial class PosCategoryManagementView : UserControl
{
    private readonly List<PosCategoryRecord> _allCategories = [];
    private readonly ObservableCollection<PosCategoryRecord> _filteredCategories = [];

    public event EventHandler? CategoriesUpdated;

    private readonly DispatcherTimer _toastTimer;
    private PosCategoryRecord? _selectedCategory;

    public PosCategoryManagementView()
    {
        InitializeComponent();

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            CategoryToastBanner.Visibility = Visibility.Collapsed;
        };

        CategoriesListItemsControl.ItemsSource = _filteredCategories;

        // Load Categories from local SQLite database
        _ = ReloadCategoriesFromDatabaseAsync();
    }

    public async Task ReloadCategoriesFromDatabaseAsync()
    {
        var categories = await SqliteDatabaseService.Instance.LoadAllCategoriesAsync();
        _allCategories.Clear();
        _allCategories.AddRange(categories);
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
        CategoryToastText.Text = message;
        if (isWarning)
        {
            CategoryToastDot.Fill = GetSafeBrush("WarningAmberBrush", Color.FromRgb(0xF5, 0x9E, 0x0B));
        }
        else if (isError)
        {
            CategoryToastDot.Fill = GetSafeBrush("DangerRedBrush", Color.FromRgb(0xDC, 0x26, 0x26));
        }
        else
        {
            CategoryToastDot.Fill = GetSafeBrush("ActiveGreenBrush", Color.FromRgb(0x10, 0xB9, 0x81));
        }
        CategoryToastBanner.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    // =========================================================================
    // SEARCH & FILTER
    // =========================================================================
    private void RefreshFilter()
    {
        string query = SearchCategoriesTextBox.Text.Trim();
        _filteredCategories.Clear();

        IEnumerable<PosCategoryRecord> source = _allCategories;
        if (!string.IsNullOrWhiteSpace(query))
        {
            source = source.Where(c =>
                c.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(c.ParentCategoryName) && c.ParentCategoryName.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(c.LinkedPosConfigs) && c.LinkedPosConfigs.Contains(query, StringComparison.OrdinalIgnoreCase)));
        }

        foreach (var item in source)
        {
            _filteredCategories.Add(item);
        }

        if (CategoryDetailViewContainer.Visibility == Visibility.Visible)
        {
            UpdateDetailPagerVisual();
        }
    }

    private void SearchCategoriesTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        string q = SearchCategoriesTextBox.Text;
        SearchPlaceholderText.Visibility = string.IsNullOrEmpty(q) ? Visibility.Visible : Visibility.Collapsed;
        ClearSearchButton.Visibility = string.IsNullOrEmpty(q) ? Visibility.Collapsed : Visibility.Visible;
        RefreshFilter();
    }

    private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
    {
        SearchCategoriesTextBox.Text = string.Empty;
    }

    // =========================================================================
    // VIEW SWITCHING (LIST VIEW <-> DETAIL VIEW)
    // =========================================================================
    public void SwitchToListView()
    {
        CategoryDetailViewContainer.Visibility = Visibility.Collapsed;
        CategoryListViewContainer.Visibility = Visibility.Visible;
        BreadcrumbDetailSubPanel.Visibility = Visibility.Collapsed;
        DetailRecordPagerPanel.Visibility = Visibility.Collapsed;
    }

    public void SwitchToDetailView(PosCategoryRecord category)
    {
        _selectedCategory = category;

        BreadcrumbCurrentCategoryName.Text = category.Name;
        FormCategoryNameDisplay.Text = category.Name;
        FormParentCategoryDisplay.Text = category.ParentCategoryDisplay;
        FormCurrentColorDot.Fill = category.ColorBrush;
        FormColorNameDisplay.Text = category.ColorIndex == 0 ? "Default Gray" : $"Color {category.ColorIndex}";
        FormLinkedShopsItemsControl.ItemsSource = category.LinkedShopsList;

        if (category.ImageBitmap != null)
        {
            FormPreviewImage.Source = category.ImageBitmap;
            FormPreviewImage.Visibility = Visibility.Visible;
            FormPlaceholderIcon.Visibility = Visibility.Collapsed;
        }
        else
        {
            FormPreviewImage.Source = null;
            FormPreviewImage.Visibility = Visibility.Collapsed;
            FormPlaceholderIcon.Visibility = Visibility.Visible;
        }

        CategoryListViewContainer.Visibility = Visibility.Collapsed;
        CategoryDetailViewContainer.Visibility = Visibility.Visible;
        BreadcrumbDetailSubPanel.Visibility = Visibility.Visible;

        UpdateDetailPagerVisual();
    }

    // =========================================================================
    // DETAIL VIEW RECORD SWITCHER (< >)
    // =========================================================================
    private void UpdateDetailPagerVisual()
    {
        if (_filteredCategories.Count == 0 || _selectedCategory == null)
        {
            DetailRecordPagerPanel.Visibility = Visibility.Collapsed;
            return;
        }

        int index = _filteredCategories.IndexOf(_selectedCategory);
        if (index < 0)
        {
            index = 0;
            _selectedCategory = _filteredCategories[0];
        }

        DetailRecordPagerPanel.Visibility = Visibility.Visible;
        DetailPagerCounterText.Text = $"{index + 1} / {_filteredCategories.Count}";
        DetailPrevRecordBtn.IsEnabled = index > 0;
        DetailNextRecordBtn.IsEnabled = index < _filteredCategories.Count - 1;
    }

    private void DetailPrevRecord_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCategory == null || _filteredCategories.Count == 0) return;
        int index = _filteredCategories.IndexOf(_selectedCategory);
        if (index > 0)
        {
            SwitchToDetailView(_filteredCategories[index - 1]);
        }
    }

    private void DetailNextRecord_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCategory == null || _filteredCategories.Count == 0) return;
        int index = _filteredCategories.IndexOf(_selectedCategory);
        if (index >= 0 && index < _filteredCategories.Count - 1)
        {
            SwitchToDetailView(_filteredCategories[index + 1]);
        }
    }

    private void CategoryRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PosCategoryRecord selectedCategory })
        {
            SwitchToDetailView(selectedCategory);
        }
    }

    private void BreadcrumbRoot_Click(object sender, RoutedEventArgs e)
    {
        SwitchToListView();
    }

    // =========================================================================
    // ODOO SYNCHRONIZATION (pos.category)
    // =========================================================================
    private void SetSyncLoadingState(bool isLoading)
    {
        if (isLoading)
        {
            TopSyncCategoriesBtn.IsEnabled = false;
            SyncButtonText.Text = "Syncing...";
            var spinAnim = new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = TimeSpan.FromSeconds(0.9),
                RepeatBehavior = RepeatBehavior.Forever
            };
            CategorySyncIconRotation.BeginAnimation(RotateTransform.AngleProperty, spinAnim);
        }
        else
        {
            CategorySyncIconRotation.BeginAnimation(RotateTransform.AngleProperty, null);
            CategorySyncIconRotation.Angle = 0;
            SyncButtonText.Text = "Sync from Odoo";
            TopSyncCategoriesBtn.IsEnabled = true;
        }
    }

    private async void SyncCategoriesBtn_Click(object sender, RoutedEventArgs e)
    {
        await TriggerOdooCategoriesSyncAsync();
    }

    public async Task TriggerOdooCategoriesSyncAsync()
    {
        try
        {
            SetSyncLoadingState(true);

            var config = await SqliteDatabaseService.Instance.GetOdooConfigAsync();
            if (string.IsNullOrWhiteSpace(config.ServerUrl))
            {
                ShowToast("Cannot synchronize: Odoo Server URL is not configured. Please open Settings.", isWarning: true);
                return;
            }

            // Fetch live pos.category records from Odoo
            var odooCategories = await OdooSyncService.Instance.FetchOdooCategoriesAsync(config, allowOfflineSimulation: false);

            // Reconcile in local SQLite: upsert matching categories, delete categories not in Odoo
            var (imported, updated, deleted) = await SqliteDatabaseService.Instance.UpsertOdooCategoriesAsync(odooCategories);

            DateTime now = DateTime.Now;
            string statusMsg = deleted > 0
                ? $"Sync Complete: {imported} imported, {updated} updated, {deleted} removed."
                : $"Sync Complete: {imported} imported, {updated} updated.";

            await SqliteDatabaseService.Instance.UpdateCategorySyncStatusAsync(now, statusMsg);

            await ReloadCategoriesFromDatabaseAsync();
            CategoriesUpdated?.Invoke(this, EventArgs.Empty);

            ShowToast(statusMsg);
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
