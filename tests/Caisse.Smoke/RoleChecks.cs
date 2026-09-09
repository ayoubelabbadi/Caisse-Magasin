using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Caisse.Desktop;
using Caisse.Domain;
using Caisse.Infrastructure;

internal static class RoleChecks
{
    public static void Run(string output)
    {
        var store = new SqliteStore(Path.Combine(output, $"roles-{Guid.NewGuid():N}.db"));
        store.Initialize(); store.CreateInitialManager("Admin", "918273");
        var admin = store.CurrentUser!.Id;
        store.SaveUser("Employé", "123987", false);
        var cashier = store.GetUsers().Single(u => !u.IsManager).Id;
        var product = store.SaveProduct(new Product { Name = "Article rôle", Barcode = "ROLE", Price = 10, Stock = 10 });
        var adminShift = store.OpenSession("Test", 0);
        store.Checkout([new(product.Id, 1, 10)], "Espèces", 10);
        store.CloseSession(adminShift.Id, 10);
        var vm = new MainViewModel(store, new ShopSettings(), output);
        var window = new MainWindow(vm) { ShowInTaskbar = false, Left = -10000, Top = -10000 };
        window.Show();
        var tabs = (TabControl)window.FindName("MainTabs");
        void Flush() => window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        Flush();
        Check(tabs.SelectedIndex == 5 && ((TabItem)tabs.Items[0]).Visibility == Visibility.Collapsed, "Admin opens dashboard and has no checkout tab");
        Check(((TabItem)tabs.Items[1]).Visibility == Visibility.Visible, "Admin sees stock");
        Capture(window, Path.Combine(output, "espace-administrateur.png"));
        var panel = (RetailPanel)((TabItem)tabs.Items[4]).Content;
        var retailTabs = (TabControl)panel.FindName("RetailTabs");
        retailTabs.SelectedIndex = 2; tabs.SelectedIndex = 6;
        store.Login(cashier, "123987"); vm.Refresh(); Flush();
        Check(tabs.SelectedIndex == 4 && retailTabs.SelectedIndex == 0, "Role change leaves previous admin screens");
        Check(new[] {1, 3, 5, 6}.All(i => ((TabItem)tabs.Items[i]).Visibility == Visibility.Collapsed), "Cashier admin tabs are hidden");
        Check(new[] {0, 2, 4}.All(i => ((TabItem)tabs.Items[i]).Visibility == Visibility.Visible), "Cashier sees operational tabs");
        Check(new[] {2, 3}.All(i => ((TabItem)retailTabs.Items[i]).Visibility == Visibility.Collapsed), "Cashier reports and stock journal are hidden");
        Check(vm.Sales.Count == 0 && vm.Retail.Sessions.Count == 0 && vm.Dashboard.Cashiers.Count == 0 && vm.Retail.Report.Count == 0 && vm.AuditLog.Count == 0, "Cashier has no other employee history or admin aggregates");
        Capture(window, Path.Combine(output, "shift-caissier.png"));
        var forbidden = Path.Combine(output, "forbidden-export.xlsx"); vm.ExportData(forbidden);
        Check(!File.Exists(forbidden), "Cashier cannot export through direct viewmodel action");
        vm.Retail.OpenCommand.Execute(null); Flush();
        Check(tabs.SelectedIndex == 0 && vm.Retail.IsOpen, "Cashier shift opens checkout");
        Capture(window, Path.Combine(output, "espace-caissier.png"));
        vm.Barcode = "ROLE"; vm.ScanCommand.Execute(null); vm.Tendered = "10";
        // Use the store here to avoid opening a receipt dialog in the test window.
        vm.CancelCart(); store.Checkout([new(product.Id, 1, 10)], "Espèces", 10); vm.Refresh();
        Check(vm.Sales.Count == 1 && vm.Retail.Sessions.All(s => s.UserId == cashier), "Cashier history contains only own activity");
        vm.Retail.SaleSearch = store.GetSales().Last().Number;
        Check(vm.Retail.Sales.Count == 1, "Cashier can find an original ticket from another shift for a return");
        store.Login(admin, "918273"); vm.Refresh(); Flush();
        Check(tabs.SelectedIndex == 5 && vm.Sales.Count == 2 && ((TabItem)tabs.Items[6]).Visibility == Visibility.Visible, "Admin access and complete history restored after role switch");
        window.Close();
        Console.WriteLine("PASS: espaces par rôle, changement de compte, historique personnel et export interdit au caissier.");
    }
    private static void Capture(Window window, string path)
    {
        window.UpdateLayout();
        var content = (FrameworkElement)window.Content;
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
}
