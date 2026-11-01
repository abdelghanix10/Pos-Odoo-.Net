using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using OdooPosLogin.ViewModels;

namespace OdooPosLogin;

public partial class PosDashboardView : UserControl
{
    public PosDashboardViewModel VM => (PosDashboardViewModel)DataContext;

    public PosDashboardView()
    {
        InitializeComponent();
    }

    public async Task ReloadConfigsFromDatabaseAsync()
    {
        await VM.ReloadConfigsAsync();
    }

    private void CardMenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.ContextMenu != null)
        {
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.Placement = PlacementMode.Bottom;
            btn.ContextMenu.IsOpen = true;
        }
    }

    private void ShopActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is PosConfigRecord config)
        {
            VM.OpenRegisterCommand.Execute(config);
        }
    }

    private void MenuItem_ViewOrders_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is PosConfigRecord config)
        {
            VM.ViewOrdersCommand.Execute(config);
        }
    }

    private void MenuItem_ViewSessions_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is PosConfigRecord config)
        {
            VM.ViewSessionsCommand.Execute(config);
        }
    }

    private void MenuItem_ViewPayments_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is PosConfigRecord config)
        {
            VM.ViewPaymentsCommand.Execute(config);
        }
    }
}
