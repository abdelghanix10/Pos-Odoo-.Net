using System.Windows.Controls;
using OdooPosLogin.ViewModels;

namespace OdooPosLogin;

public partial class ProductAttributeManagementView : UserControl
{
    public ProductAttributesViewModel ViewModel => (ProductAttributesViewModel)DataContext;

    public event EventHandler? AttributesUpdated;

    public ProductAttributeManagementView()
    {
        InitializeComponent();

        if (DataContext is ProductAttributesViewModel vm)
        {
            vm.AttributesUpdated += (_, _) => AttributesUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task ReloadAttributesFromDatabaseAsync(bool resetDetailView = true)
    {
        await ViewModel.ReloadAttributesAsync(resetDetailView);
    }

    public void ShowToast(string message, bool isError = false, bool isWarning = false)
    {
        string severity = isError ? "Error" : isWarning ? "Warning" : "Success";
        ViewModel.ShowToast(message, severity);
    }
}