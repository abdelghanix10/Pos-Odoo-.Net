using System.Windows.Controls;
using OdooPosLogin.ViewModels;

namespace OdooPosLogin;

public partial class CompanyManagementView : UserControl
{
    public CompaniesViewModel VM => (CompaniesViewModel)DataContext;

    public event EventHandler? CompaniesUpdated;

    public CompanyManagementView()
    {
        InitializeComponent();

        if (DataContext is CompaniesViewModel vm)
        {
            vm.CompaniesUpdated += (_, _) => CompaniesUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task ReloadCompaniesFromDatabaseAsync(bool resetDetailView = true)
    {
        await VM.ReloadCompaniesAsync(resetDetailView);
    }
}