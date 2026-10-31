using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace OdooPosLogin;

public partial class ProductTagManagementView : UserControl
{
    private readonly List<ProductTagRecord> _allTags = [];
    private readonly ObservableCollection<ProductTagRecord> _filteredTags = [];

    public event EventHandler? TagsUpdated;

    private readonly DispatcherTimer _toastTimer;
    private ProductTagRecord? _selectedTag;

    public ProductTagManagementView()
    {
        InitializeComponent();

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            TagToastBanner.Visibility = Visibility.Collapsed;
        };

        TagsListItemsControl.ItemsSource = _filteredTags;

        _ = ReloadTagsFromDatabaseAsync();
    }

    public async Task ReloadTagsFromDatabaseAsync()
    {
        var tags = await SqliteDatabaseService.Instance.LoadAllTagsAsync();
        _allTags.Clear();
        _allTags.AddRange(tags);
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
        TagToastText.Text = message;
        if (isWarning)
        {
            TagToastDot.Fill = GetSafeBrush("WarningAmberBrush", Color.FromRgb(0xF5, 0x9E, 0x0B));
        }
        else if (isError)
        {
            TagToastDot.Fill = GetSafeBrush("DangerRedBrush", Color.FromRgb(0xDC, 0x26, 0x26));
        }
        else
        {
            TagToastDot.Fill = GetSafeBrush("ActiveGreenBrush", Color.FromRgb(0x10, 0xB9, 0x81));
        }
        TagToastBanner.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    // =========================================================================
    // SEARCH & FILTER
    // =========================================================================
    private void RefreshFilter()
    {
        string query = SearchTagsTextBox.Text.Trim();
        _filteredTags.Clear();

        IEnumerable<ProductTagRecord> source = _allTags;
        if (!string.IsNullOrWhiteSpace(query))
        {
            source = source.Where(t =>
                t.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                t.ColorNameDisplay.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var item in source)
        {
            _filteredTags.Add(item);
        }
    }

    private void SearchTagsTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        string q = SearchTagsTextBox.Text;
        SearchPlaceholderText.Visibility = string.IsNullOrEmpty(q) ? Visibility.Visible : Visibility.Collapsed;
        ClearSearchButton.Visibility = string.IsNullOrEmpty(q) ? Visibility.Collapsed : Visibility.Visible;
        RefreshFilter();
    }

    private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
    {
        SearchTagsTextBox.Text = string.Empty;
    }

    // =========================================================================
    // NAVIGATION: LIST VIEW <-> DETAIL VIEW
    // =========================================================================
    public void SwitchToListView()
    {
        TagDetailViewContainer.Visibility = Visibility.Collapsed;
        TagListViewContainer.Visibility = Visibility.Visible;
        BreadcrumbDetailSubPanel.Visibility = Visibility.Collapsed;
    }

    public void SwitchToDetailView(ProductTagRecord tag)
    {
        _selectedTag = tag;

        BreadcrumbCurrentTagName.Text = tag.Name;
        FormTagNameDisplay.Text = tag.Name;
        FormRelatedProductsCountText.Text = tag.ProductsCountDisplay;

        FormColorDot.Fill = tag.ColorBrush;
        FormColorNameDisplay.Text = tag.ColorNameDisplay;

        FormBadgePreviewBorder.Background = tag.SoftBackgroundBrush;
        FormBadgePreviewBorder.BorderBrush = tag.ColorBrush;
        FormBadgePreviewDot.Fill = tag.ColorBrush;
        FormBadgePreviewText.Text = tag.Name;
        FormBadgePreviewText.Foreground = tag.ColorBrush;

        TagListViewContainer.Visibility = Visibility.Collapsed;
        TagDetailViewContainer.Visibility = Visibility.Visible;
        BreadcrumbDetailSubPanel.Visibility = Visibility.Visible;
    }

    private void TagRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ProductTagRecord selectedTag })
        {
            SwitchToDetailView(selectedTag);
        }
    }

    private void BreadcrumbRoot_Click(object sender, RoutedEventArgs e)
    {
        SwitchToListView();
    }

    // =========================================================================
    // ODOO SYNCHRONIZATION (product.tag)
    // =========================================================================
    private void SetSyncLoadingState(bool isLoading)
    {
        if (isLoading)
        {
            TopSyncTagsBtn.IsEnabled = false;
            SyncButtonText.Text = "Syncing...";
            var spinAnim = new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = TimeSpan.FromSeconds(0.9),
                RepeatBehavior = RepeatBehavior.Forever
            };
            TagSyncIconRotation.BeginAnimation(RotateTransform.AngleProperty, spinAnim);
        }
        else
        {
            TagSyncIconRotation.BeginAnimation(RotateTransform.AngleProperty, null);
            TagSyncIconRotation.Angle = 0;
            SyncButtonText.Text = "Sync from Odoo";
            TopSyncTagsBtn.IsEnabled = true;
        }
    }

    private async void SyncTagsBtn_Click(object sender, RoutedEventArgs e)
    {
        await TriggerOdooTagsSyncAsync();
    }

    public async Task TriggerOdooTagsSyncAsync()
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

            // Fetch live product.tag from Odoo
            var odooTags = await OdooSyncService.Instance.FetchOdooTagsAsync(config, allowOfflineSimulation: false);

            // Reconcile and save in SQLite
            var (imported, updated, deleted) = await SqliteDatabaseService.Instance.UpsertOdooTagsAsync(odooTags);

            DateTime now = DateTime.Now;
            string statusMsg = deleted > 0
                ? $"Sync Complete: {imported} imported, {updated} updated, {deleted} removed."
                : $"Sync Complete: {imported} imported, {updated} updated ({odooTags.Count} tags).";

            await SqliteDatabaseService.Instance.UpdateTagSyncStatusAsync(now, statusMsg);

            await ReloadTagsFromDatabaseAsync();
            TagsUpdated?.Invoke(this, EventArgs.Empty);

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
