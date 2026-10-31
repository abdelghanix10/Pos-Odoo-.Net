using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace OdooPosLogin;

public partial class ComboChoiceManagementView : UserControl
{
    private readonly List<ComboChoiceRecord> _allCombos = [];
    private readonly ObservableCollection<ComboChoiceRecord> _filteredCombos = [];

    public event EventHandler? CombosUpdated;

    private readonly DispatcherTimer _toastTimer;
    private ComboChoiceRecord? _selectedCombo;

    public ComboChoiceManagementView()
    {
        InitializeComponent();

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            ComboToastBanner.Visibility = Visibility.Collapsed;
        };

        ComboListItemsControl.ItemsSource = _filteredCombos;

        _ = ReloadCombosFromDatabaseAsync();
    }

    public async Task ReloadCombosFromDatabaseAsync()
    {
        var combos = await SqliteDatabaseService.Instance.LoadAllComboChoicesAsync();
        _allCombos.Clear();
        _allCombos.AddRange(combos);
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
        ComboToastText.Text = message;
        if (isWarning)
        {
            ComboToastDot.Fill = GetSafeBrush("WarningAmberBrush", Color.FromRgb(0xF5, 0x9E, 0x0B));
        }
        else if (isError)
        {
            ComboToastDot.Fill = GetSafeBrush("DangerRedBrush", Color.FromRgb(0xDC, 0x26, 0x26));
        }
        else
        {
            ComboToastDot.Fill = GetSafeBrush("ActiveGreenBrush", Color.FromRgb(0x10, 0xB9, 0x81));
        }
        ComboToastBanner.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    // =========================================================================
    // SEARCH & FILTER
    // =========================================================================
    private void RefreshFilter()
    {
        string query = SearchCombosTextBox.Text.Trim();
        _filteredCombos.Clear();

        IEnumerable<ComboChoiceRecord> source = _allCombos;
        if (!string.IsNullOrWhiteSpace(query))
        {
            source = source.Where(c =>
                c.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                c.IncludedProductNames.Any(p => p.Contains(query, StringComparison.OrdinalIgnoreCase)));
        }

        foreach (var item in source)
        {
            _filteredCombos.Add(item);
        }

        if (ComboDetailViewContainer.Visibility == Visibility.Visible)
        {
            UpdateDetailPagerVisual();
        }
    }

    private void SearchCombosTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        string q = SearchCombosTextBox.Text;
        SearchPlaceholderText.Visibility = string.IsNullOrEmpty(q) ? Visibility.Visible : Visibility.Collapsed;
        ClearSearchButton.Visibility = string.IsNullOrEmpty(q) ? Visibility.Collapsed : Visibility.Visible;
        RefreshFilter();
    }

    private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
    {
        SearchCombosTextBox.Text = string.Empty;
    }

    // =========================================================================
    // NAVIGATION: LIST VIEW <-> DETAIL VIEW
    // =========================================================================
    public void SwitchToListView()
    {
        ComboDetailViewContainer.Visibility = Visibility.Collapsed;
        ComboListViewContainer.Visibility = Visibility.Visible;
        BreadcrumbDetailSubPanel.Visibility = Visibility.Collapsed;
        DetailRecordPagerPanel.Visibility = Visibility.Collapsed;
    }

    public void SwitchToDetailView(ComboChoiceRecord combo)
    {
        _selectedCombo = combo;

        BreadcrumbCurrentComboName.Text = combo.Name;
        FormComboNameDisplay.Text = combo.Name;
        FormComboLinesItemsControl.ItemsSource = combo.Lines;

        ComboListViewContainer.Visibility = Visibility.Collapsed;
        ComboDetailViewContainer.Visibility = Visibility.Visible;
        BreadcrumbDetailSubPanel.Visibility = Visibility.Visible;

        UpdateDetailPagerVisual();
    }

    // =========================================================================
    // DETAIL VIEW RECORD SWITCHER (< >)
    // =========================================================================
    private void UpdateDetailPagerVisual()
    {
        if (_filteredCombos.Count == 0 || _selectedCombo == null)
        {
            DetailRecordPagerPanel.Visibility = Visibility.Collapsed;
            return;
        }

        int index = _filteredCombos.IndexOf(_selectedCombo);
        if (index < 0)
        {
            index = 0;
            _selectedCombo = _filteredCombos[0];
        }

        DetailRecordPagerPanel.Visibility = Visibility.Visible;
        DetailPagerCounterText.Text = $"{index + 1} / {_filteredCombos.Count}";
        DetailPrevRecordBtn.IsEnabled = index > 0;
        DetailNextRecordBtn.IsEnabled = index < _filteredCombos.Count - 1;
    }

    private void DetailPrevRecord_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCombo == null || _filteredCombos.Count == 0) return;
        int index = _filteredCombos.IndexOf(_selectedCombo);
        if (index > 0)
        {
            SwitchToDetailView(_filteredCombos[index - 1]);
        }
    }

    private void DetailNextRecord_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCombo == null || _filteredCombos.Count == 0) return;
        int index = _filteredCombos.IndexOf(_selectedCombo);
        if (index >= 0 && index < _filteredCombos.Count - 1)
        {
            SwitchToDetailView(_filteredCombos[index + 1]);
        }
    }

    private void ComboRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ComboChoiceRecord selectedCombo })
        {
            SwitchToDetailView(selectedCombo);
        }
    }

    private void BreadcrumbRoot_Click(object sender, RoutedEventArgs e)
    {
        SwitchToListView();
    }

    // =========================================================================
    // ODOO SYNCHRONIZATION (pos.combo & pos.combo.line)
    // =========================================================================
    private void SetSyncLoadingState(bool isLoading)
    {
        if (isLoading)
        {
            TopSyncCombosBtn.IsEnabled = false;
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
            TopSyncCombosBtn.IsEnabled = true;
            SyncButtonText.Text = "Sync from Odoo";
            TopSyncIconRotation.BeginAnimation(RotateTransform.AngleProperty, null);
            TopSyncIconRotation.Angle = 0;
        }
    }

    private async void TopSyncCombosBtn_Click(object sender, RoutedEventArgs e)
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

            var (combos, lines) = await OdooSyncService.Instance.FetchOdooComboChoicesAsync(config, allowOfflineSimulation: true);

            var (imported, updated, deleted) = await SqliteDatabaseService.Instance.UpsertOdooCombosAsync(combos, lines);
            await SqliteDatabaseService.Instance.UpdateComboSyncStatusAsync(DateTime.Now, "Success");

            await ReloadCombosFromDatabaseAsync();
            CombosUpdated?.Invoke(this, EventArgs.Empty);

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
