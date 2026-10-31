using System.Windows.Controls;
using OdooPosLogin.ViewModels;

namespace OdooPosLogin;

public partial class ProductManagementView : UserControl
{
    public ProductsViewModel ViewModel => (ProductsViewModel)DataContext;

    public event EventHandler? ProductsUpdated;

    public ProductManagementView()
    {
        InitializeComponent();

        if (DataContext is ProductsViewModel vm)
        {
            vm.ProductsUpdated += (_, _) => ProductsUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task ReloadProductsFromDatabaseAsync()
    {
        await ViewModel.ReloadProductsAsync();
    }

    public void ShowToast(string message, bool isError = false, bool isWarning = false)
    {
        string severity = isError ? "Error" : isWarning ? "Warning" : "Success";
        ViewModel.ShowToast(message, severity);
    }
}
