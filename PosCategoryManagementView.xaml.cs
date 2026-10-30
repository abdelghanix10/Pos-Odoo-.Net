using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace OdooPosLogin;

public partial class PosCategoryManagementView : UserControl
{
    private readonly List<PosCategoryRecord> _allCategories = [];
    private readonly ObservableCollection<PosCategoryRecord> _filteredCategories = [];

    public event EventHandler? CategoriesUpdated;

    private readonly DispatcherTimer _toastTimer;
    private PosCategoryRecord? _editingCategory;
    private bool _isCreatingNew = false;
    private int _selectedColorIndex = 0;
    private string? _stagedImageBase64;
    private BitmapImage? _stagedImageBitmap;

    public record ColorSwatchItem(int Index, string Name, Brush Brush);

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

        // Initialize 12 Odoo Color Swatches
        PopulateColorSwatches();

        // Load Categories from local SQLite database
        _ = ReloadCategoriesFromDatabaseAsync();
    }

    private void PopulateColorSwatches()
    {
        var swatches = new List<ColorSwatchItem>();
        for (int i = 0; i <= 11; i++)
        {
            string hex = PosCategoryRecord.GetOdooColorHex(i);
            string name = i == 0 ? "Default Gray" : $"Color {i}";
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            swatches.Add(new ColorSwatchItem(i, name, brush));
        }
        ColorSwatchesItemsControl.ItemsSource = swatches;
    }

    public async Task ReloadCategoriesFromDatabaseAsync()
    {
        var categories = await SqliteDatabaseService.Instance.LoadAllCategoriesAsync();
        _allCategories.Clear();
        _allCategories.AddRange(categories);
        RefreshFilter();
        PopulateParentCategoryComboBox();
    }

    private void PopulateParentCategoryComboBox()
    {
        string? currentSelection = FormParentCategoryComboBox.SelectedItem as string;

        FormParentCategoryComboBox.Items.Clear();
        FormParentCategoryComboBox.Items.Add("(None)");

        foreach (var cat in _allCategories)
        {
            if (_editingCategory != null && cat.Id == _editingCategory.Id)
                continue; // Cannot be parent of itself

            if (!string.IsNullOrWhiteSpace(cat.Name) && !FormParentCategoryComboBox.Items.Contains(cat.Name))
            {
                FormParentCategoryComboBox.Items.Add(cat.Name);
            }
        }

        if (!string.IsNullOrEmpty(currentSelection) && FormParentCategoryComboBox.Items.Contains(currentSelection))
        {
            FormParentCategoryComboBox.SelectedItem = currentSelection;
        }
        else
        {
            FormParentCategoryComboBox.SelectedIndex = 0;
        }
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

        UpdatePagerDisplay();
    }

    private void UpdatePagerDisplay()
    {
        if (CategoryDetailViewContainer.Visibility == Visibility.Visible)
        {
            if (_editingCategory != null)
            {
                int index = _filteredCategories.IndexOf(_editingCategory);
                CategoriesPagerText.Text = index >= 0
                    ? $"{index + 1} / {_filteredCategories.Count}"
                    : $"1 / {_filteredCategories.Count}";
            }
            else
            {
                CategoriesPagerText.Text = $"New / {_filteredCategories.Count}";
            }
        }
        else
        {
            int count = _filteredCategories.Count;
            CategoriesPagerText.Text = count > 0 ? $"1-{count} / {count}" : "0 / 0";
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
    // VIEW SWITCHING (LIST VIEW <-> DETAIL/FORM VIEW)
    // =========================================================================
    public void SwitchToListView()
    {
        CategoryDetailViewContainer.Visibility = Visibility.Collapsed;
        CategoryListViewContainer.Visibility = Visibility.Visible;

        BreadcrumbDetailSubPanel.Visibility = Visibility.Collapsed;
        FormActionButtonsPanel.Visibility = Visibility.Collapsed;

        ViewListButton.Background = (Brush)FindResource("OdooPurpleSoftBrush");
        ViewFormButton.Background = Brushes.Transparent;

        UpdatePagerDisplay();
    }

    public void SwitchToFormView(PosCategoryRecord? categoryToEdit = null)
    {
        _editingCategory = categoryToEdit;
        _isCreatingNew = categoryToEdit == null;

        PopulateParentCategoryComboBox();

        if (categoryToEdit != null)
        {
            BreadcrumbCurrentCategoryName.Text = categoryToEdit.Name;
            FormCategoryNameTextBox.Text = categoryToEdit.Name;

            if (!string.IsNullOrEmpty(categoryToEdit.ParentCategoryName) && FormParentCategoryComboBox.Items.Contains(categoryToEdit.ParentCategoryName))
            {
                FormParentCategoryComboBox.SelectedItem = categoryToEdit.ParentCategoryName;
            }
            else
            {
                FormParentCategoryComboBox.SelectedIndex = 0;
            }

            _selectedColorIndex = categoryToEdit.ColorIndex;
            FormCurrentColorDot.Fill = categoryToEdit.ColorBrush;

            FormSequenceTextBox.Text = categoryToEdit.Sequence.ToString();
            FormLinkedConfigsTextBox.Text = categoryToEdit.LinkedPosConfigs ?? "";

            _stagedImageBase64 = categoryToEdit.ImageBase64;
            _stagedImageBitmap = categoryToEdit.ImageBitmap;
            UpdateImagePreview();

            DeleteCategoryButton.Visibility = Visibility.Visible;
        }
        else
        {
            BreadcrumbCurrentCategoryName.Text = "New Category";
            FormCategoryNameTextBox.Text = string.Empty;
            FormParentCategoryComboBox.SelectedIndex = 0;

            _selectedColorIndex = 0;
            FormCurrentColorDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(PosCategoryRecord.GetOdooColorHex(0)));

            FormSequenceTextBox.Text = "10";
            FormLinkedConfigsTextBox.Text = "Furniture Shop";

            _stagedImageBase64 = null;
            _stagedImageBitmap = null;
            UpdateImagePreview();

            DeleteCategoryButton.Visibility = Visibility.Collapsed;
        }

        CategoryListViewContainer.Visibility = Visibility.Collapsed;
        CategoryDetailViewContainer.Visibility = Visibility.Visible;

        BreadcrumbDetailSubPanel.Visibility = Visibility.Visible;
        FormActionButtonsPanel.Visibility = Visibility.Visible;

        ViewFormButton.Background = (Brush)FindResource("OdooPurpleSoftBrush");
        ViewListButton.Background = Brushes.Transparent;

        UpdatePagerDisplay();
    }

    private void CategoryRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PosCategoryRecord selectedCategory })
        {
            SwitchToFormView(selectedCategory);
        }
    }

    private void NewCategoryButton_Click(object sender, RoutedEventArgs e)
    {
        SwitchToFormView(null);
    }

    private void BreadcrumbRoot_Click(object sender, RoutedEventArgs e)
    {
        SwitchToListView();
    }

    private void ViewListButton_Click(object sender, RoutedEventArgs e)
    {
        SwitchToListView();
    }

    private void ViewFormButton_Click(object sender, RoutedEventArgs e)
    {
        if (_filteredCategories.Count > 0)
        {
            SwitchToFormView(_editingCategory ?? _filteredCategories.First());
        }
        else
        {
            SwitchToFormView(null);
        }
    }

    // =========================================================================
    // COLOR PALETTE SELECTION
    // =========================================================================
    private void ColorSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int index })
        {
            _selectedColorIndex = index;
            string hex = PosCategoryRecord.GetOdooColorHex(index);
            FormCurrentColorDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }
    }

    // =========================================================================
    // IMAGE UPLOADER (88x88)
    // =========================================================================
    private void FormImageContainer_Click(object sender, MouseButtonEventArgs e)
    {
        UploadCategoryImage();
    }

    private void FormImageContainer_Click(object sender, RoutedEventArgs e)
    {
        UploadCategoryImage();
    }

    private void UploadCategoryImage()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Category Image",
            Filter = "Image Files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(dialog.FileName);
                _stagedImageBase64 = Convert.ToBase64String(bytes);
                _stagedImageBitmap = PosImageUtils.Base64ToBitmapImage(_stagedImageBase64);
                UpdateImagePreview();
            }
            catch (Exception ex)
            {
                ShowToast($"Failed to load image: {ex.Message}", isError: true);
            }
        }
    }

    private void FormRemoveImage_Click(object sender, RoutedEventArgs e)
    {
        _stagedImageBase64 = null;
        _stagedImageBitmap = null;
        UpdateImagePreview();
    }

    private void UpdateImagePreview()
    {
        if (_stagedImageBitmap != null)
        {
            FormPreviewImage.Source = _stagedImageBitmap;
            FormPreviewImage.Visibility = Visibility.Visible;
            FormPlaceholderIcon.Visibility = Visibility.Collapsed;
            FormRemoveImageButton.Visibility = Visibility.Visible;
        }
        else
        {
            FormPreviewImage.Source = null;
            FormPreviewImage.Visibility = Visibility.Collapsed;
            FormPlaceholderIcon.Visibility = Visibility.Visible;
            FormRemoveImageButton.Visibility = Visibility.Collapsed;
        }
    }

    // =========================================================================
    // PAGER NAVIGATION
    // =========================================================================
    private void PagerPrevButton_Click(object sender, RoutedEventArgs e)
    {
        if (CategoryDetailViewContainer.Visibility == Visibility.Visible && _filteredCategories.Count > 0)
        {
            int currentIdx = _editingCategory != null ? _filteredCategories.IndexOf(_editingCategory) : 0;
            if (currentIdx > 0)
            {
                SwitchToFormView(_filteredCategories[currentIdx - 1]);
            }
        }
    }

    private void PagerNextButton_Click(object sender, RoutedEventArgs e)
    {
        if (CategoryDetailViewContainer.Visibility == Visibility.Visible && _filteredCategories.Count > 0)
        {
            int currentIdx = _editingCategory != null ? _filteredCategories.IndexOf(_editingCategory) : -1;
            if (currentIdx < _filteredCategories.Count - 1)
            {
                SwitchToFormView(_filteredCategories[currentIdx + 1]);
            }
        }
    }

    // =========================================================================
    // FORM SAVE / DISCARD / DELETE
    // =========================================================================
    private async void SaveCategoryButton_Click(object sender, RoutedEventArgs e)
    {
        string name = FormCategoryNameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ShowToast("Category Name is required.", isWarning: true);
            return;
        }

        string? parentName = FormParentCategoryComboBox.SelectedItem as string;
        if (parentName == "(None)") parentName = null;

        int? parentId = null;
        if (!string.IsNullOrEmpty(parentName))
        {
            var pCat = _allCategories.FirstOrDefault(c => c.Name.Equals(parentName, StringComparison.OrdinalIgnoreCase));
            parentId = pCat?.OdooId ?? (int?)pCat?.Id;
        }

        int sequence = 10;
        int.TryParse(FormSequenceTextBox.Text.Trim(), out sequence);

        string linkedConfigs = FormLinkedConfigsTextBox.Text.Trim();
        string colorHex = PosCategoryRecord.GetOdooColorHex(_selectedColorIndex);

        var record = _editingCategory ?? new PosCategoryRecord();
        record.Name = name;
        record.ParentCategoryId = parentId;
        record.ParentCategoryName = parentName;
        record.LinkedPosConfigs = linkedConfigs;
        record.ColorIndex = _selectedColorIndex;
        record.ColorHex = colorHex;
        record.Sequence = sequence;
        record.ImageBase64 = _stagedImageBase64;
        record.ImageBitmap = _stagedImageBitmap;

        try
        {
            await SqliteDatabaseService.Instance.SaveCategoryAsync(record);
            await ReloadCategoriesFromDatabaseAsync();
            CategoriesUpdated?.Invoke(this, EventArgs.Empty);

            ShowToast($"Category \"{name}\" saved successfully.");
            SwitchToListView();
        }
        catch (Exception ex)
        {
            ShowToast($"Failed to save: {ex.Message}", isError: true);
        }
    }

    private void DiscardCategoryButton_Click(object sender, RoutedEventArgs e)
    {
        SwitchToListView();
    }

    private async void DeleteCategoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_editingCategory == null) return;

        string deletedName = _editingCategory.Name;
        try
        {
            await SqliteDatabaseService.Instance.DeleteCategoryAsync(_editingCategory.Id);
            await ReloadCategoriesFromDatabaseAsync();
            CategoriesUpdated?.Invoke(this, EventArgs.Empty);

            ShowToast($"Category \"{deletedName}\" deleted.");
            SwitchToListView();
        }
        catch (Exception ex)
        {
            ShowToast($"Failed to delete: {ex.Message}", isError: true);
        }
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
