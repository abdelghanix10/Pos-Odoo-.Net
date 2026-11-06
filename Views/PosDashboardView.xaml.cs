using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
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

    private void HeaderActionsMenuBtn_Click(object sender, RoutedEventArgs e)
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

    private static void CloseParentPopup(DependencyObject? sender)
    {
        DependencyObject? current = sender;
        while (current != null)
        {
            if (current is Popup popup)
            {
                popup.IsOpen = false;
                return;
            }
            current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
        }
    }

    private void PopoverViewOrders_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is PosConfigRecord config)
        {
            CloseParentPopup(elem);
            VM.ViewOrdersCommand.Execute(config);
        }
    }

    private void PopoverViewSessions_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is PosConfigRecord config)
        {
            CloseParentPopup(elem);
            VM.ViewSessionsCommand.Execute(config);
        }
    }

    private void PopoverReportingOrders_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is PosConfigRecord config)
        {
            CloseParentPopup(elem);
            VM.ReportingOrdersCommand.Execute(config);
        }
    }

    private void PopoverConfigure_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is PosConfigRecord config)
        {
            CloseParentPopup(elem);
            VM.ConfigureShopCommand.Execute(config);
        }
    }
}