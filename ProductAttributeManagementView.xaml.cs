using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace OdooPosLogin;

public partial class ProductAttributeManagementView : UserControl
{
    private readonly List<ProductAttributeRecord> _allAttributes = [];
    private readonly ObservableCollection<ProductAttributeRecord> _filteredAttributes = [];

    public event EventHandler? AttributesUpdated;

    private readonly DispatcherTimer _toastTimer;
    private ProductAttributeRecord? _selectedAttribute;

    public ProductAttributeManagementView()
    {
        InitializeComponent();

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            AttributeToastBanner.Visibility = Visibility.Collapsed;
        };

        AttributesListItemsControl.ItemsSource = _filteredAttributes;

        _ = ReloadAttributesFromDatabaseAsync();
    }

    public async Task ReloadAttributesFromDatabaseAsync()
    {
        var attributes = await SqliteDatabaseService.Instance.LoadAllAttributesAsync();
        _allAttributes.Clear();
        _allAttributes.AddRange(attributes);
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
        AttributeToastText.Text = message;
        if (isWarning)
        {
            AttributeToastDot.Fill = GetSafeBrush("WarningAmberBrush", Color.FromRgb(0xF5, 0x9E, 0x0B));
        }
        else if (isError)
        {
            AttributeToastDot.Fill = GetSafeBrush("DangerRedBrush", Color.FromRgb(0xDC, 0x26, 0x26));
        }
        else
        {
            AttributeToastDot.Fill = GetSafeBrush("ActiveGreenBrush", Color.FromRgb(0x10, 0xB9, 0x81));
        }
        AttributeToastBanner.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    // =========================================================================
    // SEARCH & FILTER
    // =========================================================================
    private void RefreshFilter()
    {
        string query = SearchAttributesTextBox.Text.Trim();
        _filteredAttributes.Clear();

        IEnumerable<ProductAttributeRecord> source = _allAttributes;
        if (!string.IsNullOrWhiteSpace(query))
        {
            source = source.Where(a =>
                a.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                a.DisplayTypeFormatted.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                a.CreateVariantFormatted.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var item in source)
        {
            _filteredAttributes.Add(item);
        }
    }

    private void SearchAttributesTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        string q = SearchAttributesTextBox.Text;
        SearchPlaceholderText.Visibility = string.IsNullOrEmpty(q) ? Visibility.Visible : Visibility.Collapsed;
        ClearSearchButton.Visibility = string.IsNullOrEmpty(q) ? Visibility.Collapsed : Visibility.Visible;
        RefreshFilter();
    }

    private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
    {
        SearchAttributesTextBox.Text = string.Empty;
    }

    // =========================================================================
    // NAVIGATION: LIST VIEW <-> DETAIL VIEW
    // =========================================================================
    public void SwitchToListView()
    {
        AttributeDetailViewContainer.Visibility = Visibility.Collapsed;
        AttributeListViewContainer.Visibility = Visibility.Visible;
        BreadcrumbDetailSubPanel.Visibility = Visibility.Collapsed;
    }

    public void SwitchToDetailView(ProductAttributeRecord attribute)
    {
        _selectedAttribute = attribute;

        BreadcrumbCurrentAttributeName.Text = attribute.Name;
        FormAttributeNameDisplay.Text = attribute.Name;
        FormDisplayTypeDisplay.Text = attribute.DisplayTypeFormatted;
        FormRelatedProductsCountText.Text = attribute.ProductsCountDisplay;

        // Update read-only radio indicators for Variant Creation
        var purpleBrush = GetSafeBrush("OdooPurpleBrush", Color.FromRgb(0x71, 0x4B, 0x67));
        var grayBorderBrush = GetSafeBrush("InputBorderBrush", Color.FromRgb(0xD1, 0xD5, 0xDB));

        RadioInstantlyInner.Visibility = attribute.IsCreateInstantly ? Visibility.Visible : Visibility.Collapsed;
        RadioInstantlyOuter.BorderBrush = attribute.IsCreateInstantly ? purpleBrush : grayBorderBrush;

        RadioDynamicallyInner.Visibility = attribute.IsCreateDynamically ? Visibility.Visible : Visibility.Collapsed;
        RadioDynamicallyOuter.BorderBrush = attribute.IsCreateDynamically ? purpleBrush : grayBorderBrush;

        RadioNeverInner.Visibility = attribute.IsCreateNever ? Visibility.Visible : Visibility.Collapsed;
        RadioNeverOuter.BorderBrush = attribute.IsCreateNever ? purpleBrush : grayBorderBrush;

        // Set Values
        AttributeValuesItemsControl.ItemsSource = attribute.Values;

        AttributeListViewContainer.Visibility = Visibility.Collapsed;
        AttributeDetailViewContainer.Visibility = Visibility.Visible;
        BreadcrumbDetailSubPanel.Visibility = Visibility.Visible;
    }

    private void AttributeRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ProductAttributeRecord selectedAttribute })
        {
            SwitchToDetailView(selectedAttribute);
        }
    }

    private void BreadcrumbRoot_Click(object sender, RoutedEventArgs e)
    {
        SwitchToListView();
    }

    // =========================================================================
    // ODOO SYNCHRONIZATION (product.attribute & product.attribute.value)
    // =========================================================================
    private void SetSyncLoadingState(bool isLoading)
    {
        if (isLoading)
        {
            TopSyncAttributesBtn.IsEnabled = false;
            SyncButtonText.Text = "Syncing...";
            var spinAnim = new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = TimeSpan.FromSeconds(0.9),
                RepeatBehavior = RepeatBehavior.Forever
            };
            AttrSyncIconRotation.BeginAnimation(RotateTransform.AngleProperty, spinAnim);
        }
        else
        {
            AttrSyncIconRotation.BeginAnimation(RotateTransform.AngleProperty, null);
            AttrSyncIconRotation.Angle = 0;
            SyncButtonText.Text = "Sync from Odoo";
            TopSyncAttributesBtn.IsEnabled = true;
        }
    }

    private async void SyncAttributesBtn_Click(object sender, RoutedEventArgs e)
    {
        await TriggerOdooAttributesSyncAsync();
    }

    public async Task TriggerOdooAttributesSyncAsync()
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

            // Fetch live product.attribute & product.attribute.value from Odoo
            var (odooAttrs, odooVals) = await OdooSyncService.Instance.FetchOdooAttributesAsync(config, allowOfflineSimulation: false);

            // Reconcile and save in SQLite
            var (aImp, aUpd, aDel, vImp, vUpd, vDel) = await SqliteDatabaseService.Instance.UpsertOdooAttributesAsync(odooAttrs, odooVals);

            DateTime now = DateTime.Now;
            string statusMsg = $"Sync Complete: {aImp + aUpd} attributes, {vImp + vUpd} values synced.";
            if (aDel > 0 || vDel > 0)
            {
                statusMsg += $" ({aDel} attributes, {vDel} values removed)";
            }

            await SqliteDatabaseService.Instance.UpdateAttributeSyncStatusAsync(now, statusMsg);

            await ReloadAttributesFromDatabaseAsync();
            AttributesUpdated?.Invoke(this, EventArgs.Empty);

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
