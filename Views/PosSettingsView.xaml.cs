using System.Windows.Controls;

namespace OdooPosLogin;

public partial class PosSettingsView : UserControl
{
    public PosSettingsViewModel VM { get; } = new();

    public PosSettingsView()
    {
        InitializeComponent();
        DataContext = VM;
    }
}
