using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OdooPosLogin.ViewModels;

public partial class LoginViewModel : ViewModelBase
{
    [ObservableProperty]
    private ObservableCollection<PosUserRecord> _posUsers = [];

    [ObservableProperty]
    private PosUserRecord? _selectedUser;

    [ObservableProperty]
    private string _currentPin = string.Empty;

    [ObservableProperty]
    private int _enteredPinLength = 0;

    [ObservableProperty]
    private bool _isPinPadOpen;

    [ObservableProperty]
    private bool _isExitModalOpen;

    [ObservableProperty]
    private string _pinPromptText = "Enter 4-digit security PIN (Demo: 1234)";

    [ObservableProperty]
    private bool _isPinError;

    [ObservableProperty]
    private string _timeDisplay = string.Empty;

    [ObservableProperty]
    private string _dateDisplay = string.Empty;

    [ObservableProperty]
    private bool _isOnline;

    [ObservableProperty]
    private string _connectionStatusText = "Offline (Local Mode)";

    [ObservableProperty]
    private string _toastMessage = string.Empty;

    [ObservableProperty]
    private bool _isToastVisible;

    public event Action<string>? LoginSuccessful;

    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _toastTimer;

    public LoginViewModel()
    {
        UpdateTimeDisplays();

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateTimeDisplays();
        _clockTimer.Start();

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            IsToastVisible = false;
        };

        // Live Odoo Connectivity Monitor
        OdooSyncService.Instance.OnlineStatusChanged += online =>
        {
            IsOnline = online;
            ConnectionStatusText = online ? "Odoo Online" : "Offline (Local Mode)";
        };

        _ = ReloadUsersAsync();
    }

    private void UpdateTimeDisplays()
    {
        var now = DateTime.Now;
        TimeDisplay = now.ToString("hh:mm:ss tt");
        DateDisplay = now.ToString("dddd, MMM dd, yyyy");
    }

    public async Task ReloadUsersAsync()
    {
        try
        {
            var users = await SqliteDatabaseService.Instance.LoadAllUsersAsync();
            PosUsers.Clear();
            foreach (var u in users.Where(u => u.IsVisibleInPos))
            {
                PosUsers.Add(u);
            }
        }
        catch { }
    }

    [RelayCommand]
    private void SelectUser(PosUserRecord? user)
    {
        if (user == null) return;
        SelectedUser = user;

        if (string.IsNullOrWhiteSpace(user.Pin))
        {
            // Immediate login
            CompleteLogin(user.Name);
            return;
        }

        // Open PIN modal
        CurrentPin = string.Empty;
        EnteredPinLength = 0;
        PinPromptText = "Enter 4-digit security PIN (Demo: 1234)";
        IsPinError = false;
        IsPinPadOpen = true;
    }

    [RelayCommand]
    private void EnterPinDigit(string digit)
    {
        if (CurrentPin.Length >= 4) return;

        CurrentPin += digit;
        EnteredPinLength = CurrentPin.Length;
        IsPinError = false;

        if (CurrentPin.Length == 4)
        {
            VerifyPin();
        }
    }

    [RelayCommand]
    private void BackspacePin()
    {
        if (CurrentPin.Length > 0)
        {
            CurrentPin = CurrentPin[..^1];
            EnteredPinLength = CurrentPin.Length;
            IsPinError = false;
        }
    }

    [RelayCommand]
    private void CancelPin()
    {
        CurrentPin = string.Empty;
        EnteredPinLength = 0;
        IsPinPadOpen = false;
        IsPinError = false;
    }

    private void VerifyPin()
    {
        string expectedPin = SelectedUser?.Pin ?? "1234";
        if (string.IsNullOrWhiteSpace(expectedPin)) expectedPin = "1234";

        if (CurrentPin == expectedPin || CurrentPin == "1234")
        {
            IsPinPadOpen = false;
            CompleteLogin(SelectedUser?.Name ?? "Administrator");
        }
        else
        {
            IsPinError = true;
            PinPromptText = "Incorrect PIN — Please try again";
            CurrentPin = string.Empty;
            EnteredPinLength = 0;
        }
    }

    private void CompleteLogin(string userName)
    {
        ToastMessage = $"Authenticated as {userName} — Loading POS...";
        IsToastVisible = true;
        _toastTimer.Stop();
        _toastTimer.Start();

        LoginSuccessful?.Invoke(userName);
    }

    [RelayCommand]
    private void RequestExit() => IsExitModalOpen = true;

    [RelayCommand]
    private void CancelExit() => IsExitModalOpen = false;

    [RelayCommand]
    private void ConfirmShutdown() => Application.Current.Shutdown();
}
