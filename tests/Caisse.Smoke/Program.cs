using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Caisse.Desktop;
using Caisse.Infrastructure;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var output = Path.GetFullPath(args.Length > 0 ? args[0] : "artifacts");
            Directory.CreateDirectory(output);
            var store = new SqliteStore(Path.Combine(output, $"smoke-{Guid.NewGuid():N}.db"));
            store.Initialize(); store.CreateInitialManager("Amina", "123456"); store.LoadDemoProducts();
            var app = new App(false); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            TouchInput.Register();
            ScannerChecks.Run(output);
            RoleChecks.Run(output);
            var dashboardExample = DashboardChecks.Run(output);
            ReceiptChecks.Run(output);
            var errors = new BindingErrors(); PresentationTraceSources.DataBindingSource.Listeners.Add(errors);
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
            var vm = new MainViewModel(store, new ShopSettings(), output);
            vm.Retail.Opening = "100"; vm.Retail.OpenCommand.Execute(null);
            vm.Barcode = "DEMO001"; vm.ScanCommand.Execute(null);
            vm.PlusCommand.Execute(vm.Cart.Single());
            vm.AddCommand.Execute(vm.Catalog.First(p => p.Barcode == "DEMO004"));
            vm.Tendered = "100";
            if (vm.Total != 51m || vm.Cart.Count != 2) throw new Exception("Panier incorrect.");
            var window = new MainWindow(vm);
            var loginPreview = AccountDialogs.CreateLogin(null, store);
            Render((FrameworkElement)loginPreview.Content, 520, 440, Path.Combine(output, "connexion-pin.png"));
            TouchActivationChecks(vm);
            var content = (FrameworkElement)window.Content;
            Render(content, 1360, 790, Path.Combine(output, "caisse.png"));
            var payment = new PaymentDialog(null, vm);
            Render((FrameworkElement)payment.Content, 620, 720, Path.Combine(output, "paiement-tactile.png"));
            var numeric = TouchInput.Create(null, "Montant donné par le client", "", true, false, vm.Total);
            numeric.Insert("1"); numeric.Insert("00"); numeric.Insert(","); numeric.Insert("5"); numeric.Backspace(); numeric.Insert("25");
            if (numeric.Value != "100,25") throw new Exception("Pavé numérique : saisie ou correction incorrecte.");
            Render((FrameworkElement)numeric.Content, 580, 640, Path.Combine(output, "pave-numerique.png"));
            var keyboard = TouchInput.Create(null, "Recherche produit · clavier AZERTY", "", false);
            keyboard.Insert("Café"); keyboard.Insert(" "); keyboard.Insert("bio"); keyboard.Backspace(); keyboard.Insert("o");
            if (keyboard.Value != "Café bio") throw new Exception("Clavier texte : saisie ou correction incorrecte.");
            Render((FrameworkElement)keyboard.Content, 1000, 730, Path.Combine(output, "clavier-azerty.png"));
            var pinPad = TouchInput.Create(null, "PIN manager", "", true, true); pinPad.Insert("123456"); pinPad.Insert(",");
            if (pinPad.Value != "123456") throw new Exception("Le PIN doit refuser les caractères non numériques.");
            vm.IsCard = true;
            Render(content, 1120, 720, Path.Combine(output, "carte-bancaire.png"));
            vm.IsCash = true;
            var tabs = Find<TabControl>(content)!;
            tabs.SelectedIndex = 1;
            Render(content, 1360, 790, Path.Combine(output, "catalogue.png"));
            tabs.SelectedIndex = 3;
            Render(content, 1120, 720, Path.Combine(output, "donnees-compact.png"));
            tabs.SelectedIndex = 5;
            content.UpdateLayout();
            var dashboardPanel = Find<DashboardPanel>(content)!;
            dashboardPanel.DataContext = dashboardExample.Dashboard;
            Render(content, 1360, 790, Path.Combine(output, "dashboard.png"));
            Render(content, 1120, 720, Path.Combine(output, "dashboard-compact.png"));
            var confirmation = AppDialog.CreateConfirmation(null, "Confirmer le remboursement", "Enregistrer le remboursement intégral du ticket V-20260908 : 80,00 MAD ?\nRendez cette somme au client en espèces.\nRemise en stock : oui\nMotif : Article retourné");
            Render((FrameworkElement)confirmation.Content, 520, 330, Path.Combine(output, "confirmation.png"));
            DialogChecks();
            tabs.SelectedIndex = 0;
            Render(content, 1120, 720, Path.Combine(output, "caisse-compact.png"));
            // Use an independent VM so receipt dialogs are not opened by the smoke test.
            var checkout = new MainViewModel(store, new ShopSettings(), output);
            checkout.Barcode = "DEMO001"; checkout.ScanCommand.Execute(null); checkout.ExactCommand.Execute(null); checkout.CheckoutCommand.Execute(null);
            if (checkout.Cart.Count != 0 || checkout.Sales.Count != 1 || checkout.Catalog.First(p => p.Barcode == "DEMO001").Stock != 39)
                throw new Exception("Échec du parcours d’encaissement : " + checkout.Status);
            var receipt = ReceiptDocument.Create(checkout.Sales.Single(), checkout.Settings);
            receipt.PageWidth = 430; receipt.PageHeight = 620;
            var paginator = ((IDocumentPaginatorSource)receipt).DocumentPaginator;
            paginator.ComputePageCount();
            if (paginator.PageCount < 1 || !new TextRange(receipt.ContentStart, receipt.ContentEnd).Text.Contains(checkout.Sales.Single().Number))
                throw new Exception("Ticket vide.");
            var ticketBitmap = new RenderTargetBitmap(430, 620, 96, 96, PixelFormats.Pbgra32);
            var paper = new DrawingVisual();
            using (var context = paper.RenderOpen()) context.DrawRectangle(Brushes.White, null, new Rect(0, 0, 430, 620));
            ticketBitmap.Render(paper); ticketBitmap.Render(paginator.GetPage(0).Visual);
            var ticketEncoder = new PngBitmapEncoder(); ticketEncoder.Frames.Add(BitmapFrame.Create(ticketBitmap));
            using (var ticketStream = File.Create(Path.Combine(output, "ticket.png"))) ticketEncoder.Save(ticketStream);
            vm.Retail.RefreshCommand.Execute(null);
            vm.ApplyDiscount(10m, true);
            if (vm.DiscountAmount != 5.10m || vm.Total != 45.90m) throw new Exception("Remise incorrecte.");
            tabs.SelectedIndex = 4;
            Render(content, 1360, 880, Path.Combine(output, "gestion-session.png"));
            var retailPanel = Find<RetailPanel>(content)!;
            var retailTabs = Find<TabControl>(retailPanel)!;
            retailTabs.SelectedIndex = 1;
            vm.Retail.SelectedSale = vm.Retail.Sales.Single(); vm.Retail.RefundReason = "Retour de test";
            Render(content, 1360, 880, Path.Combine(output, "gestion-retours.png"));
            // Exercise confirmations in an independent VM without dialog handlers.
            checkout.Retail.Confirm = _ => true;
            checkout.Retail.SelectedSale = checkout.Retail.Sales.Single(); checkout.Retail.RefundReason = "Retour de test";
            foreach (var item in checkout.Retail.RefundItems) item.Quantity = item.Remaining;
            checkout.Retail.RefundCommand.Execute(null);
            if (checkout.Retail.Refunds.Count != 1 || checkout.Retail.Expected != 100m) throw new Exception("Retour incorrect : " + checkout.Retail.Status);
            checkout.Retail.Counted = "99"; checkout.Retail.CloseCommand.Execute(null);
            if (checkout.Retail.IsOpen || checkout.Retail.Sessions.Single().Difference != -1m) throw new Exception("Clôture incorrecte.");
            vm.Retail.RefreshCommand.Execute(null);
            retailTabs.SelectedIndex = 2;
            Render(content, 1360, 880, Path.Combine(output, "gestion-rapports.png"));
            if (vm.Retail.Report.Sum(r => r.Amount) != 0m || vm.Retail.Report.Count != 2) throw new Exception("Rapport incorrect.");
            vm.Retail.ExportReport(Path.Combine(output, "rapport-test.csv"));
            if (!File.ReadAllText(Path.Combine(output, "rapport-test.csv")).Contains("Retour")) throw new Exception("Export vide.");
            retailTabs.SelectedIndex = 3;
            Render(content, 1120, 720, Path.Combine(output, "gestion-stock-compact.png"));
            AutomaticBackup.CreateDaily(store, output);
            if (!File.Exists(Path.Combine(output, "backups", $"caisse-{DateTime.Now:yyyy-MM-dd}.db"))) throw new Exception("Sauvegarde quotidienne absente.");
            if (errors.Messages.Count > 0) throw new Exception(string.Join(Environment.NewLine, errors.Messages));
            Console.WriteLine("PASS: chargement WPF, liaisons, panier, scan, encaissement et rendus aux deux tailles.");
            app.Shutdown();
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static T? Find<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T value) return value;
            if (Find<T>(child) is { } nested) return nested;
        }
        return null;
    }

    private static void DialogChecks()
    {
        foreach (var accept in new[] { false, true })
        {
            var dialog = AppDialog.CreateConfirmation(null, "Confirmation de test", "Une action de test, sans modification des données.");
            dialog.WindowStartupLocation = WindowStartupLocation.Manual; dialog.Left = -10000; dialog.Top = -10000; dialog.ShowActivated = false;
            dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(new Action(() => {
                var actions = dialog.Body.Children.OfType<StackPanel>().Last();
                actions.Children.OfType<Button>().Single(b => (string)b.Content == (accept ? "Confirmer" : "Annuler")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }));
            if ((dialog.ShowDialog() == true) != accept) throw new Exception("Résultat incorrect de la confirmation personnalisée.");
        }
    }

    private static void TouchActivationChecks(MainViewModel model)
    {
        var active = true; var opened = 0; var value = "125,50";
        EventManager.RegisterClassHandler(typeof(TouchKeyboard), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) => {
            if (!active) return;
            var keyboard = (TouchKeyboard)sender; opened++; keyboard.SetValue(value); keyboard.DialogResult = true;
        }));
        var panel = new StackPanel(); var number = new TextBox(); number.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding("Tendered") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged });
        var search = new TextBox(); search.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding("Search") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged });
        var barcode = new TextBox(); TouchInput.SetMode(barcode, "Off"); var pin = new PasswordBox();
        panel.Children.Add(number); panel.Children.Add(search); panel.Children.Add(barcode); panel.Children.Add(pin);
        var host = new Window { Content = panel, DataContext = model, Width = 400, Height = 300, Left = -10000, Top = -10000, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
        host.Show();
        void Touch(System.Windows.UIElement control) { control.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, System.Windows.Input.MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent }); System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); }
        Touch(number); if (model.Tendered != "125,50" || opened != 1) throw new Exception("Activation automatique du pavé numérique incorrecte.");
        value = "Café"; Touch(search); if (model.Search != "Café" || opened != 2) throw new Exception("Activation du clavier texte incorrecte.");
        Touch(barcode); if (opened != 2) throw new Exception("Le lecteur de code-barres ne doit pas ouvrir de clavier.");
        value = "654321"; Touch(pin); if (pin.Password != value || opened != 3) throw new Exception("Clavier PIN incorrect.");
        active = false; host.Close(); model.Search = ""; model.Tendered = "100";
    }

    private static void Render(FrameworkElement content, int width, int height, string path)
    {
        content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }

    private sealed class BindingErrors : TraceListener
    {
        public List<string> Messages { get; } = [];
        public override void Write(string? message) { if (!string.IsNullOrEmpty(message)) Messages.Add(message); }
        public override void WriteLine(string? message) => Write(message);
    }

}
