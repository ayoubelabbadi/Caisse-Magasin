using System.Windows.Controls;
namespace Caisse.Desktop;
public partial class DashboardPanel : UserControl
{
    public DashboardPanel() => InitializeComponent();
    private void ChartBar_Click(object sender, System.Windows.RoutedEventArgs e) => AppDialog.Inform(System.Windows.Window.GetWindow(this), "Ventes sur la période", ((Button)sender).Tag?.ToString() ?? "");
}
