using System.Windows.Controls;
using OdooPosLogin.ViewModels;

namespace OdooPosLogin;

public partial class ProductTagManagementView : UserControl
{
    public ProductTagsViewModel ViewModel => (ProductTagsViewModel)DataContext;

    public event EventHandler? TagsUpdated;

    public ProductTagManagementView()
    {
        InitializeComponent();

        if (DataContext is ProductTagsViewModel vm)
        {
            vm.TagsUpdated += (_, _) => TagsUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task ReloadTagsFromDatabaseAsync()
    {
        await ViewModel.ReloadTagsAsync();
    }

    public void ShowToast(string message, bool isError = false, bool isWarning = false)
    {
        string severity = isError ? "Error" : isWarning ? "Warning" : "Success";
        ViewModel.ShowToast(message, severity);
    }
}
