using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace OdooPosLogin;

public class PosSettingsViewModel : INotifyPropertyChanged
{
    private PosConfigRecord? _selectedConfig;
    private bool _isLoading;
    private string _statusMessage = string.Empty;
    private bool _isReceiptDesignsSubTabSelected;

    // 1. Payment Methods
    private string? _paymentMethodToAdd;

    // 2. One-click Payment (Single Select, Synced from Odoo)
    private bool _isOneClickPaymentEnabled;
    private string? _oneClickMethodToAdd;
    private string _selectedOneClickPaymentMethod = "Cash";

    // 3. Restrict Categories
    private bool _isRestrictCategoriesEnabled;
    private string? _categoryToAdd;

    // 4. Variants
    private bool _isVariantsEnabled = true;

    // 5. Automatic Receipt Printing
    private bool _isAutomaticReceiptPrintingEnabled;

    // 6. Prevent Line Merging
    private bool _isPreventLineMergingEnabled;

    // 7. Print Last Receipt
    private bool _isPrintLastReceiptEnabled;

    // 8. Custom Order Receipts
    private bool _isCustomOrderReceiptsEnabled;
    private ReceiptDesignRecord? _selectedCustomOrderReceiptDesign;

    // 9. Hide Opening Cash Control
    private bool _isHideOpeningCashControlEnabled;
    private string _defaultOpeningCashAmountText = "0.00";

    // 10. Second Receipt
    private bool _isSecondReceiptEnabled;
    private ReceiptDesignRecord? _selectedSecondReceiptDesign;

    // Receipt Designs Editor
    private ReceiptDesignRecord? _editingReceiptDesign;
    private string _designEditorName = string.Empty;
    private bool _designEditorOpenCashDrawerOnly;
    private string _designEditorReceiptXml = string.Empty;

    private List<string> _allPaymentMethodNames = [];
    private List<string> _allCategoryNames = [];

    public ObservableCollection<PosConfigRecord> PosConfigs { get; } = [];
    public ObservableCollection<string> SelectedPaymentMethodTags { get; } = [];
    public ObservableCollection<string> AvailablePaymentMethodsToAdd { get; } = [];

    public ObservableCollection<string> SelectedOneClickPaymentTags { get; } = [];
    public ObservableCollection<string> AvailableOneClickMethodsToAdd { get; } = [];
    public ObservableCollection<string> AvailableOneClickMethods { get; } = [];

    public ObservableCollection<string> SelectedCategoryTags { get; } = [];
    public ObservableCollection<string> AvailableCategoriesToAdd { get; } = [];

    public ObservableCollection<ReceiptDesignRecord> ReceiptDesigns { get; } = [];

    public event Action? NavigateToPaymentMethodsRequested;
    public event Action? NavigateToCategoriesRequested;
    public event Action? NavigateToAttributesRequested;
    public event Action? NavigateToReceiptDesignsRequested;
    public event Action? SettingsSaved;

    public ICommand SaveSettingsCommand { get; }
    public ICommand DiscardSettingsCommand { get; }
    public ICommand RemovePaymentMethodTagCommand { get; }
    public ICommand AddPaymentMethodTagCommand { get; }
    public ICommand RemoveOneClickMethodTagCommand { get; }
    public ICommand AddOneClickMethodTagCommand { get; }
    public ICommand RemoveCategoryTagCommand { get; }
    public ICommand AddCategoryTagCommand { get; }

    public ICommand OpenPaymentMethodsLinkCommand { get; }
    public ICommand OpenCategoriesLinkCommand { get; }
    public ICommand OpenAttributesLinkCommand { get; }
    public ICommand OpenReceiptDesignsTabCommand { get; }
    public ICommand OpenPosSettingsTabCommand { get; }

    public ICommand SelectReceiptDesignCommand { get; }
    public ICommand NewReceiptDesignCommand { get; }
    public ICommand SaveReceiptDesignCommand { get; }
    public ICommand DeleteReceiptDesignCommand { get; }
    public ICommand LoadDefaultXmlTemplateCommand { get; }

    public PosSettingsViewModel()
    {
        SaveSettingsCommand = new RelayCommand(async _ => await SaveCurrentConfigSettingsAsync(showFeedback: true));
        DiscardSettingsCommand = new RelayCommand(async _ => await LoadDataAsync(_selectedConfig?.OdooId));

        RemovePaymentMethodTagCommand = new RelayCommand(p =>
        {
            if (p is string name && SelectedPaymentMethodTags.Remove(name))
            {
                RefreshAvailableDropdowns();
                _ = SaveCurrentConfigSettingsAsync(showFeedback: false);
            }
        });

        AddPaymentMethodTagCommand = new RelayCommand(p =>
        {
            string? name = p as string ?? PaymentMethodToAdd;
            if (!string.IsNullOrWhiteSpace(name) && !SelectedPaymentMethodTags.Contains(name))
            {
                SelectedPaymentMethodTags.Add(name);
                PaymentMethodToAdd = null;
                RefreshAvailableDropdowns();
                _ = SaveCurrentConfigSettingsAsync(showFeedback: false);
            }
        });

        RemoveOneClickMethodTagCommand = new RelayCommand(p =>
        {
            if (p is string name && SelectedOneClickPaymentTags.Remove(name))
            {
                RefreshAvailableDropdowns();
                _ = SaveCurrentConfigSettingsAsync(showFeedback: false);
            }
        });

        AddOneClickMethodTagCommand = new RelayCommand(p =>
        {
            string? name = p as string ?? OneClickMethodToAdd;
            if (!string.IsNullOrWhiteSpace(name) && !SelectedOneClickPaymentTags.Contains(name))
            {
                SelectedOneClickPaymentTags.Add(name);
                OneClickMethodToAdd = null;
                RefreshAvailableDropdowns();
                _ = SaveCurrentConfigSettingsAsync(showFeedback: false);
            }
        });

        RemoveCategoryTagCommand = new RelayCommand(p =>
        {
            if (p is string name && SelectedCategoryTags.Remove(name))
            {
                RefreshAvailableDropdowns();
                _ = SaveCurrentConfigSettingsAsync(showFeedback: false);
            }
        });

        AddCategoryTagCommand = new RelayCommand(p =>
        {
            string? name = p as string ?? CategoryToAdd;
            if (!string.IsNullOrWhiteSpace(name) && !SelectedCategoryTags.Contains(name))
            {
                SelectedCategoryTags.Add(name);
                CategoryToAdd = null;
                RefreshAvailableDropdowns();
                _ = SaveCurrentConfigSettingsAsync(showFeedback: false);
            }
        });

        OpenPaymentMethodsLinkCommand = new RelayCommand(_ => NavigateToPaymentMethodsRequested?.Invoke());
        OpenCategoriesLinkCommand = new RelayCommand(_ => NavigateToCategoriesRequested?.Invoke());
        OpenAttributesLinkCommand = new RelayCommand(_ => NavigateToAttributesRequested?.Invoke());
        OpenReceiptDesignsTabCommand = new RelayCommand(_ =>
        {
            IsReceiptDesignsSubTabSelected = true;
            NavigateToReceiptDesignsRequested?.Invoke();
        });
        OpenPosSettingsTabCommand = new RelayCommand(_ => IsReceiptDesignsSubTabSelected = false);

        SelectReceiptDesignCommand = new RelayCommand(p =>
        {
            if (p is ReceiptDesignRecord design)
            {
                EditingReceiptDesign = design;
            }
        });

        NewReceiptDesignCommand = new RelayCommand(_ => CreateNewReceiptDesign());
        SaveReceiptDesignCommand = new RelayCommand(async _ => await SaveReceiptDesignAsync());
        DeleteReceiptDesignCommand = new RelayCommand(async p => await DeleteReceiptDesignAsync(p as ReceiptDesignRecord ?? EditingReceiptDesign));
        LoadDefaultXmlTemplateCommand = new RelayCommand(_ =>
        {
            DesignEditorReceiptXml = ReceiptDesignRecord.GetDefaultXmlTemplate();
        });
    }

    public bool IsReceiptDesignsSubTabSelected
    {
        get => _isReceiptDesignsSubTabSelected;
        set
        {
            if (_isReceiptDesignsSubTabSelected != value)
            {
                _isReceiptDesignsSubTabSelected = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PosSettingsSubTabVisibility));
                OnPropertyChanged(nameof(ReceiptDesignsSubTabVisibility));
            }
        }
    }

    public Visibility PosSettingsSubTabVisibility => _isReceiptDesignsSubTabSelected ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ReceiptDesignsSubTabVisibility => _isReceiptDesignsSubTabSelected ? Visibility.Visible : Visibility.Collapsed;

    public PosConfigRecord? SelectedConfig
    {
        get => _selectedConfig;
        set
        {
            if (_selectedConfig != value)
            {
                _selectedConfig = value;
                OnPropertyChanged();
                LoadConfigIntoForm(_selectedConfig);
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            if (_statusMessage != value)
            {
                _statusMessage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasStatusMessage));
            }
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(_statusMessage);

    public string? PaymentMethodToAdd
    {
        get => _paymentMethodToAdd;
        set
        {
            if (_paymentMethodToAdd != value)
            {
                _paymentMethodToAdd = value;
                OnPropertyChanged();
                if (!string.IsNullOrWhiteSpace(value) && !SelectedPaymentMethodTags.Contains(value))
                {
                    SelectedPaymentMethodTags.Add(value);
                    _paymentMethodToAdd = null;
                    OnPropertyChanged();
                    RefreshAvailableDropdowns();
                    _ = SaveCurrentConfigSettingsAsync(showFeedback: false);
                }
            }
        }
    }

    public bool IsOneClickPaymentEnabled
    {
        get => _isOneClickPaymentEnabled;
        set
        {
            if (_isOneClickPaymentEnabled != value)
            {
                _isOneClickPaymentEnabled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(OneClickPaymentDetailsVisibility));
                AutoSaveIfLoaded();
            }
        }
    }

    public Visibility OneClickPaymentDetailsVisibility => _isOneClickPaymentEnabled ? Visibility.Visible : Visibility.Collapsed;

    public string SelectedOneClickPaymentMethod
    {
        get => _selectedOneClickPaymentMethod;
        set
        {
            if (_selectedOneClickPaymentMethod != value)
            {
                _selectedOneClickPaymentMethod = value ?? "Cash";
                OnPropertyChanged();
            }
        }
    }

    public string? OneClickMethodToAdd
    {
        get => _oneClickMethodToAdd;
        set
        {
            if (_oneClickMethodToAdd != value)
            {
                _oneClickMethodToAdd = value;
                OnPropertyChanged();
                if (!string.IsNullOrWhiteSpace(value) && !SelectedOneClickPaymentTags.Contains(value))
                {
                    SelectedOneClickPaymentTags.Add(value);
                    _oneClickMethodToAdd = null;
                    OnPropertyChanged();
                    RefreshAvailableDropdowns();
                    _ = SaveCurrentConfigSettingsAsync(showFeedback: false);
                }
            }
        }
    }

    public bool IsRestrictCategoriesEnabled
    {
        get => _isRestrictCategoriesEnabled;
        set
        {
            if (_isRestrictCategoriesEnabled != value)
            {
                _isRestrictCategoriesEnabled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RestrictCategoriesDetailsVisibility));
                AutoSaveIfLoaded();
            }
        }
    }

    public Visibility RestrictCategoriesDetailsVisibility => _isRestrictCategoriesEnabled ? Visibility.Visible : Visibility.Collapsed;

    public string? CategoryToAdd
    {
        get => _categoryToAdd;
        set
        {
            if (_categoryToAdd != value)
            {
                _categoryToAdd = value;
                OnPropertyChanged();
                if (!string.IsNullOrWhiteSpace(value) && !SelectedCategoryTags.Contains(value))
                {
                    SelectedCategoryTags.Add(value);
                    _categoryToAdd = null;
                    OnPropertyChanged();
                    RefreshAvailableDropdowns();
                    _ = SaveCurrentConfigSettingsAsync(showFeedback: false);
                }
            }
        }
    }

    public bool IsVariantsEnabled
    {
        get => _isVariantsEnabled;
        set
        {
            if (_isVariantsEnabled != value)
            {
                _isVariantsEnabled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(VariantsDetailsVisibility));
                AutoSaveIfLoaded();
            }
        }
    }

    public Visibility VariantsDetailsVisibility => _isVariantsEnabled ? Visibility.Visible : Visibility.Collapsed;

    public bool IsAutomaticReceiptPrintingEnabled
    {
        get => _isAutomaticReceiptPrintingEnabled;
        set
        {
            if (_isAutomaticReceiptPrintingEnabled != value)
            {
                _isAutomaticReceiptPrintingEnabled = value;
                OnPropertyChanged();
                AutoSaveIfLoaded();
            }
        }
    }

    public bool IsPreventLineMergingEnabled
    {
        get => _isPreventLineMergingEnabled;
        set
        {
            if (_isPreventLineMergingEnabled != value)
            {
                _isPreventLineMergingEnabled = value;
                OnPropertyChanged();
                AutoSaveIfLoaded();
            }
        }
    }

    public bool IsPrintLastReceiptEnabled
    {
        get => _isPrintLastReceiptEnabled;
        set
        {
            if (_isPrintLastReceiptEnabled != value)
            {
                _isPrintLastReceiptEnabled = value;
                OnPropertyChanged();
                AutoSaveIfLoaded();
            }
        }
    }

    public bool IsCustomOrderReceiptsEnabled
    {
        get => _isCustomOrderReceiptsEnabled;
        set
        {
            if (_isCustomOrderReceiptsEnabled != value)
            {
                _isCustomOrderReceiptsEnabled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CustomOrderReceiptsDetailsVisibility));
                if (_isCustomOrderReceiptsEnabled && _selectedCustomOrderReceiptDesign == null && ReceiptDesigns.Count > 0)
                {
                    SelectedCustomOrderReceiptDesign = ReceiptDesigns[0];
                }
                AutoSaveIfLoaded();
            }
        }
    }

    public Visibility CustomOrderReceiptsDetailsVisibility => _isCustomOrderReceiptsEnabled ? Visibility.Visible : Visibility.Collapsed;

    public ReceiptDesignRecord? SelectedCustomOrderReceiptDesign
    {
        get => _selectedCustomOrderReceiptDesign;
        set
        {
            if (_selectedCustomOrderReceiptDesign != value)
            {
                _selectedCustomOrderReceiptDesign = value;
                OnPropertyChanged();
                AutoSaveIfLoaded();
            }
        }
    }

    public bool IsHideOpeningCashControlEnabled
    {
        get => _isHideOpeningCashControlEnabled;
        set
        {
            if (_isHideOpeningCashControlEnabled != value)
            {
                _isHideOpeningCashControlEnabled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HideOpeningCashControlDetailsVisibility));
                AutoSaveIfLoaded();
            }
        }
    }

    public Visibility HideOpeningCashControlDetailsVisibility => _isHideOpeningCashControlEnabled ? Visibility.Visible : Visibility.Collapsed;

    public string DefaultOpeningCashAmountText
    {
        get => _defaultOpeningCashAmountText;
        set
        {
            if (_defaultOpeningCashAmountText != value)
            {
                _defaultOpeningCashAmountText = value;
                OnPropertyChanged();
                AutoSaveIfLoaded();
            }
        }
    }

    public bool IsSecondReceiptEnabled
    {
        get => _isSecondReceiptEnabled;
        set
        {
            if (_isSecondReceiptEnabled != value)
            {
                _isSecondReceiptEnabled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SecondReceiptDetailsVisibility));
                if (_isSecondReceiptEnabled && _selectedSecondReceiptDesign == null && ReceiptDesigns.Count > 0)
                {
                    SelectedSecondReceiptDesign = ReceiptDesigns.Count > 1 ? ReceiptDesigns[1] : ReceiptDesigns[0];
                }
                AutoSaveIfLoaded();
            }
        }
    }

    public Visibility SecondReceiptDetailsVisibility => _isSecondReceiptEnabled ? Visibility.Visible : Visibility.Collapsed;

    public ReceiptDesignRecord? SelectedSecondReceiptDesign
    {
        get => _selectedSecondReceiptDesign;
        set
        {
            if (_selectedSecondReceiptDesign != value)
            {
                _selectedSecondReceiptDesign = value;
                OnPropertyChanged();
                AutoSaveIfLoaded();
            }
        }
    }

    // =========================================================================
    // RECEIPT DESIGNS EDITOR PROPERTIES
    // =========================================================================
    public ReceiptDesignRecord? EditingReceiptDesign
    {
        get => _editingReceiptDesign;
        set
        {
            if (_editingReceiptDesign != value)
            {
                foreach (var item in ReceiptDesigns)
                    item.IsSelected = ReferenceEquals(item, value);

                _editingReceiptDesign = value;
                OnPropertyChanged();
                if (value != null)
                {
                    DesignEditorName = value.Name;
                    DesignEditorOpenCashDrawerOnly = value.OpenCashDrawerOnly;
                    DesignEditorReceiptXml = value.ReceiptXml;
                }
            }
        }
    }

    public string DesignEditorName
    {
        get => _designEditorName;
        set { if (_designEditorName != value) { _designEditorName = value; OnPropertyChanged(); } }
    }

    public bool DesignEditorOpenCashDrawerOnly
    {
        get => _designEditorOpenCashDrawerOnly;
        set
        {
            if (_designEditorOpenCashDrawerOnly != value)
            {
                _designEditorOpenCashDrawerOnly = value;
                OnPropertyChanged();
            }
        }
    }

    public string DesignEditorReceiptXml
    {
        get => _designEditorReceiptXml;
        set { if (_designEditorReceiptXml != value) { _designEditorReceiptXml = value; OnPropertyChanged(); } }
    }

    private void AutoSaveIfLoaded()
    {
        if (!_isLoading && _selectedConfig != null)
        {
            _ = SaveCurrentConfigSettingsAsync(showFeedback: false);
        }
    }

    public async Task LoadDataAsync(int? targetConfigOdooId = null)
    {
        _isLoading = true;
        try
        {
            var db = SqliteDatabaseService.Instance;

            // Load Payment Methods
            var paymentMethods = await db.LoadAllPaymentMethodsAsync();
            _allPaymentMethodNames = paymentMethods
                .Select(m => m.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (_allPaymentMethodNames.Count == 0)
            {
                _allPaymentMethodNames = ["Cash", "Card", "Customer Account", "Online Payment"];
            }

            // Load Categories
            var categories = await db.LoadAllCategoriesAsync();
            _allCategoryNames = categories
                .Select(c => c.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (_allCategoryNames.Count == 0)
            {
                _allCategoryNames = ["Upper body", "Lower body", "Others", "Desks", "Chairs"];
            }

            // Load Receipt Designs
            var designs = await db.LoadAllReceiptDesignsAsync();
            ReceiptDesigns.Clear();
            foreach (var d in designs)
            {
                ReceiptDesigns.Add(d);
            }
            if (EditingReceiptDesign == null && ReceiptDesigns.Count > 0)
            {
                EditingReceiptDesign = ReceiptDesigns[0];
            }

            // Load POS Configs
            var configs = await db.LoadAllPosConfigsAsync();
            int? previousId = targetConfigOdooId ?? _selectedConfig?.OdooId;
            PosConfigs.Clear();
            foreach (var cfg in configs)
            {
                PosConfigs.Add(cfg);
            }

            var match = previousId.HasValue
                ? PosConfigs.FirstOrDefault(c => c.OdooId == previousId.Value) ?? PosConfigs.FirstOrDefault()
                : PosConfigs.FirstOrDefault();

            _selectedConfig = match;
            OnPropertyChanged(nameof(SelectedConfig));
            LoadConfigIntoForm(_selectedConfig);
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void LoadConfigIntoForm(PosConfigRecord? config)
    {
        bool prevLoading = _isLoading;
        _isLoading = true;
        try
        {
            SelectedPaymentMethodTags.Clear();
            SelectedOneClickPaymentTags.Clear();
            SelectedCategoryTags.Clear();

            if (config == null)
            {
                RefreshAvailableDropdowns();
                return;
            }

            var payMethods = config.SelectedPaymentMethodsList;
            if (payMethods.Count == 0)
                payMethods = ["Cash", "Card", "Customer Account"];

            foreach (var pm in payMethods)
                SelectedPaymentMethodTags.Add(pm);

            IsOneClickPaymentEnabled = config.IsOneClickPaymentEnabled;
            var oneClickMethods = config.OneClickPaymentMethodsList;
            if (oneClickMethods.Count == 0)
                oneClickMethods = ["Cash"];
            SelectedOneClickPaymentTags.Add(oneClickMethods[0]);
            SelectedOneClickPaymentMethod = oneClickMethods[0];

            IsRestrictCategoriesEnabled = config.IsRestrictCategoriesEnabled;
            var restrictedCats = config.RestrictedCategoriesList;
            foreach (var rc in restrictedCats)
                SelectedCategoryTags.Add(rc);

            IsVariantsEnabled = config.IsVariantsEnabled;
            IsAutomaticReceiptPrintingEnabled = config.IsAutomaticReceiptPrintingEnabled;
            IsPreventLineMergingEnabled = config.IsPreventLineMergingEnabled;
            IsPrintLastReceiptEnabled = config.IsPrintLastReceiptEnabled;

            IsCustomOrderReceiptsEnabled = config.IsCustomOrderReceiptsEnabled;
            SelectedCustomOrderReceiptDesign = config.CustomOrderReceiptDesignId.HasValue
                ? ReceiptDesigns.FirstOrDefault(d => d.Id == config.CustomOrderReceiptDesignId.Value) ?? ReceiptDesigns.FirstOrDefault()
                : ReceiptDesigns.FirstOrDefault();

            IsHideOpeningCashControlEnabled = config.IsHideOpeningCashControlEnabled;
            DefaultOpeningCashAmountText = config.DefaultOpeningCashAmount.ToString("0.00", CultureInfo.InvariantCulture);

            IsSecondReceiptEnabled = config.IsSecondReceiptEnabled;
            SelectedSecondReceiptDesign = config.SecondReceiptDesignId.HasValue
                ? ReceiptDesigns.FirstOrDefault(d => d.Id == config.SecondReceiptDesignId.Value) ?? ReceiptDesigns.Skip(1).FirstOrDefault() ?? ReceiptDesigns.FirstOrDefault()
                : ReceiptDesigns.Skip(1).FirstOrDefault() ?? ReceiptDesigns.FirstOrDefault();

            RefreshAvailableDropdowns();
        }
        finally
        {
            _isLoading = prevLoading;
        }
    }

    private void RefreshAvailableDropdowns()
    {
        AvailablePaymentMethodsToAdd.Clear();
        foreach (var m in _allPaymentMethodNames)
        {
            if (!SelectedPaymentMethodTags.Any(x => string.Equals(x, m, StringComparison.OrdinalIgnoreCase)))
                AvailablePaymentMethodsToAdd.Add(m);
        }

        AvailableOneClickMethodsToAdd.Clear();
        AvailableOneClickMethods.Clear();
        var pool = SelectedPaymentMethodTags.Count > 0 ? SelectedPaymentMethodTags.ToList() : _allPaymentMethodNames;
        foreach (var m in pool)
        {
            AvailableOneClickMethods.Add(m);
            if (!SelectedOneClickPaymentTags.Any(x => string.Equals(x, m, StringComparison.OrdinalIgnoreCase)))
                AvailableOneClickMethodsToAdd.Add(m);
        }
        if (!string.IsNullOrWhiteSpace(SelectedOneClickPaymentMethod) &&
            !AvailableOneClickMethods.Any(x => string.Equals(x, SelectedOneClickPaymentMethod, StringComparison.OrdinalIgnoreCase)))
        {
            AvailableOneClickMethods.Insert(0, SelectedOneClickPaymentMethod);
        }

        AvailableCategoriesToAdd.Clear();
        foreach (var c in _allCategoryNames)
        {
            if (!SelectedCategoryTags.Any(x => string.Equals(x, c, StringComparison.OrdinalIgnoreCase)))
                AvailableCategoriesToAdd.Add(c);
        }
    }

    public async Task SaveCurrentConfigSettingsAsync(bool showFeedback = true)
    {
        if (_selectedConfig == null) return;

        // Settings 1, 2, 3, 4, 5 are synced with Odoo and read-only in the program.
        // Only local settings (6, 7, 8, 9, 10) are persisted from the program.
        _selectedConfig.IsPreventLineMergingEnabled = IsPreventLineMergingEnabled;
        _selectedConfig.IsPrintLastReceiptEnabled = IsPrintLastReceiptEnabled;
        _selectedConfig.IsCustomOrderReceiptsEnabled = IsCustomOrderReceiptsEnabled;
        _selectedConfig.CustomOrderReceiptDesignId = SelectedCustomOrderReceiptDesign?.Id;
        _selectedConfig.IsHideOpeningCashControlEnabled = IsHideOpeningCashControlEnabled;
        if (decimal.TryParse(DefaultOpeningCashAmountText.Replace("$", "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal defCash) && defCash >= 0m)
        {
            _selectedConfig.DefaultOpeningCashAmount = defCash;
        }
        _selectedConfig.IsSecondReceiptEnabled = IsSecondReceiptEnabled;
        _selectedConfig.SecondReceiptDesignId = SelectedSecondReceiptDesign?.Id;

        await SqliteDatabaseService.Instance.SavePosConfigSettingsAsync(_selectedConfig);
        SettingsSaved?.Invoke();

        if (showFeedback)
        {
            StatusMessage = $"Saved settings for {_selectedConfig.Name}";
            _ = ClearStatusAfterDelayAsync();
        }
    }

    private void CreateNewReceiptDesign()
    {
        var newDesign = new ReceiptDesignRecord
        {
            Id = 0,
            Name = $"Custom Receipt {ReceiptDesigns.Count + 1}",
            OpenCashDrawerOnly = false,
            ReceiptXml = ReceiptDesignRecord.GetDefaultXmlTemplate(),
            UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
        };
        ReceiptDesigns.Add(newDesign);
        EditingReceiptDesign = newDesign;
    }

    private async Task SaveReceiptDesignAsync()
    {
        if (EditingReceiptDesign == null)
        {
            CreateNewReceiptDesign();
        }

        if (EditingReceiptDesign == null) return;

        EditingReceiptDesign.Name = string.IsNullOrWhiteSpace(DesignEditorName) ? "Custom Receipt" : DesignEditorName.Trim();
        EditingReceiptDesign.OpenCashDrawerOnly = DesignEditorOpenCashDrawerOnly;
        EditingReceiptDesign.ReceiptXml = DesignEditorReceiptXml ?? string.Empty;
        EditingReceiptDesign.UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        await SqliteDatabaseService.Instance.SaveReceiptDesignAsync(EditingReceiptDesign);
        StatusMessage = $"Saved Receipt Design \"{EditingReceiptDesign.Name}\"";
        _ = ClearStatusAfterDelayAsync();
    }

    private async Task DeleteReceiptDesignAsync(ReceiptDesignRecord? design)
    {
        if (design == null) return;
        if (design.Id > 0)
        {
            await SqliteDatabaseService.Instance.DeleteReceiptDesignAsync(design.Id);
        }
        ReceiptDesigns.Remove(design);
        EditingReceiptDesign = ReceiptDesigns.FirstOrDefault();
        StatusMessage = $"Deleted Receipt Design \"{design.Name}\"";
        _ = ClearStatusAfterDelayAsync();
    }

    private async Task ClearStatusAfterDelayAsync()
    {
        await Task.Delay(3000);
        StatusMessage = string.Empty;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private sealed class RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) : ICommand
    {
        public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;
        public void Execute(object? parameter) => execute(parameter);
        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}
