using System.Windows.Controls;
using OdooPosLogin.ViewModels;

namespace OdooPosLogin;

public partial class ComboChoiceManagementView : UserControl
{
    public ComboChoicesViewModel ViewModel => (ComboChoicesViewModel)DataContext;

    public event EventHandler? CombosUpdated;

    public ComboChoiceManagementView()
    {
        InitializeComponent();

        if (DataContext is ComboChoicesViewModel vm)
        {
            vm.CombosUpdated += (_, _) => CombosUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task ReloadCombosFromDatabaseAsync()
    {
        await ViewModel.ReloadCombosAsync();
    }

    public void ShowToast(string message, bool isError = false, bool isWarning = false)
    {
        string severity = isError ? "Error" : isWarning ? "Warning" : "Success";
        ViewModel.ShowToast(message, severity);
    }
}
