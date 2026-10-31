using System.Windows.Controls;
using OdooPosLogin.ViewModels;

namespace OdooPosLogin;

public partial class PosCategoryManagementView : UserControl
{
    public PosCategoriesViewModel ViewModel => (PosCategoriesViewModel)DataContext;

    public event EventHandler? CategoriesUpdated;

    public PosCategoryManagementView()
    {
        InitializeComponent();

        if (DataContext is PosCategoriesViewModel vm)
        {
            vm.CategoriesUpdated += (_, _) => CategoriesUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task ReloadCategoriesFromDatabaseAsync()
    {
        await ViewModel.ReloadCategoriesAsync();
    }

    public void ShowToast(string message, bool isError = false, bool isWarning = false)
    {
        string severity = isError ? "Error" : isWarning ? "Warning" : "Success";
        ViewModel.ShowToast(message, severity);
    }
}
