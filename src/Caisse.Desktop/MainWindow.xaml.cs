using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Threading;
using Caisse.Domain;
using Microsoft.Win32;

namespace Caisse.Desktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel model;
    public MainWindow(MainViewModel model)
    {
        InitializeComponent();
        this.model = model;
        DataContext = model;
        var previousRole = model.IsManager;
        model.PropertyChanged += (_, e) => {
            if (e.PropertyName == nameof(MainViewModel.IsManager) && previousRole != model.IsManager) {
                previousRole = model.IsManager;
                SelectWorkspace();
            }
        };
        var workArea = SystemParameters.WorkArea;
        MinWidth = Math.Min(1120, workArea.Width);
        MinHeight = Math.Min(760, workArea.Height);
        Width = Math.Min(1380, workArea.Width);
        Height = Math.Min(880, workArea.Height);
        SizeChanged += (_, _) => FitContent();
        model.ReceiptRequested += ShowReceipt;
        model.Retail.Confirm = message => AppDialog.Confirm(this, "Confirmer l’opération", message);
        model.Retail.RequestApproval = () => AccountDialogs.Approval(this, model.Store);
        model.Retail.BeforeClose = () => { if (model.Cart.Count == 0) return true; if (!AppDialog.Confirm(this, "Panier en cours", "Annuler le panier avant de clôturer le shift ?")) return false; model.CancelCart(); return true; };
        model.Retail.RefundReceiptRequested += refund => ShowDocument(ReceiptDocument.CreateRefund(refund, model.Settings), "Remboursement #" + refund.Id);
        model.Retail.ShiftOpened += SelectWorkspace;
        Loaded += (_, _) => SelectWorkspace();
        PreviewKeyDown += (_, e) => {
            if (model.IsCashier && (e.Key is Key.F2 or Key.F3)) {
                MainTabs.SelectedIndex = 0;
                if (e.Key == Key.F2) FocusScanner();
                else Dispatcher.BeginInvoke(new Action(() => { SearchBox.Focus(); SearchBox.SelectAll(); }), DispatcherPriority.Input);
                e.Handled = true;
            }
        };
        MainTabs.SelectionChanged += (_, e) => {
            if (e.Source == MainTabs && MainTabs.SelectedIndex == 0)
                Dispatcher.BeginInvoke(FocusScanner, DispatcherPriority.Input);
        };
        // Restore scanner readiness after cart buttons, while preserving deliberate text entry.
        AddHandler(Button.ClickEvent, new RoutedEventHandler((_, _) => {
            if (MainTabs.SelectedIndex == 0) Dispatcher.BeginInvoke(FocusScanner, DispatcherPriority.Input);
        }));
        Closing += (_, e) => {
            if (model.Cart.Count > 0) {
                if (!AppDialog.Confirm(this, "Panier non encaissé", "Quitter et abandonner le panier en cours ?")) { e.Cancel = true; return; }
                try { model.CancelCart(); } catch (Exception ex) { model.Status = ex.Message; e.Cancel = true; }
            }
        };
    }
    private void Accounts_Click(object sender, RoutedEventArgs e) { AccountDialogs.Manage(this, model.Store); model.Refresh(); }
    private void SelectWorkspace()
    {
        MainTabs.SelectedIndex = model.IsManager ? 5 : model.Retail.IsOpen ? 0 : 4;
        Title = "Caisse · " + model.WorkspaceTitle;
        if (model.IsCashier && model.Retail.IsOpen) FocusScanner();
    }
    private void Payment_Click(object sender, RoutedEventArgs e)
    {
        if (model.Cart.Count == 0 || !model.Retail.IsOpen) { model.Status = "Ouvrez votre shift et ajoutez des articles avant d’encaisser."; return; }
        new PaymentDialog(this, model).ShowDialog(); FocusScanner();
    }
    private void Lock_Click(object sender, RoutedEventArgs e)
    {
        if (model.Cart.Count > 0 && !AppDialog.Confirm(this, "Panier en cours", "Annuler ce panier puis verrouiller la caisse ?")) return;
        model.CancelCart(); model.Store.Logout();
        while (!AccountDialogs.Login(this, model.Store)) {
            if (AppDialog.Confirm(this, "Quitter", "Quitter l’application ? Le shift reste ouvert et pourra être repris après connexion.")) { Close(); return; }
        }
        model.Refresh();
        model.Search = ""; model.Barcode = ""; model.Status = "";
        model.Retail.SaleSearch = ""; model.Retail.SelectedSale = null;
        SelectWorkspace();
    }
    private void FocusScanner()
    {
        if (model.IsCashier && IsActive && IsEnabled && MainTabs.SelectedIndex == 0) { BarcodeBox.Focus(); BarcodeBox.SelectAll(); }
    }
    private void FocusScanner_Click(object sender, RoutedEventArgs e) => FocusScanner();
    private void BarcodeBox_KeyDown(object sender, KeyEventArgs e)
    {
        // Some keyboard-mode readers use Tab instead of Enter as their suffix.
        if (e.Key == Key.Tab && !string.IsNullOrWhiteSpace(model.Barcode)) {
            model.ScanCommand.Execute(null); e.Handled = true;
        }
    }
    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        SearchBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        if (!e.IsRepeat) model.AddSearchResultCommand.Execute(null);
        e.Handled = true;
    }
    private void FitContent()
    {
        var width = Math.Max(1, ActualWidth - 18);
        var height = Math.Max(1, ActualHeight - 40);
        var scale = Math.Min(1, Math.Min(width / 1120, height / 720));
        MainContent.LayoutTransform = new ScaleTransform(scale, scale);
    }
    private void Backup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Copie de restauration (*.db)|*.db", FileName = $"caisse-sauvegarde-{DateTime.Now:yyyyMMdd-HHmmss}.db" };
        if (dialog.ShowDialog(this) == true) model.Backup(dialog.FileName);
    }
    private void ExportData_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Classeur Excel (*.xlsx)|*.xlsx", FileName = $"magasin-{DateTime.Now:yyyyMMdd-HHmmss}.xlsx" };
        if (dialog.ShowDialog(this) == true) model.ExportData(dialog.FileName);
    }
    private void ShowReceipt(Sale sale)
    {
        ShowDocument(ReceiptDocument.Create(sale, model.Settings), sale.Number);
    }
    private void ShowDocument(FlowDocument document, string reference)
    {
        new ReceiptWindow(document, reference) { Owner = this }.ShowDialog();
        FocusScanner();
    }

    private void Discount_Click(object sender, RoutedEventArgs e)
    {
        if (model.Cart.Count == 0) { model.Status = "Ajoutez un produit avant d’appliquer une remise."; return; }
        var dialog = new AppDialog(this, "Remise sur le panier");
        var panel = dialog.Body;
        panel.Children.Add(new TextBlock { Text = "Remise sur la vente", FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) });
        var kind = new ComboBox { ItemsSource = new[] { "Pourcentage (%)", "Montant (" + model.Settings.Currency + ")" }, SelectedIndex = 0, Margin = new Thickness(0, 0, 0, 12) };
        var input = new TextBox { Text = "0", Margin = new Thickness(0, 0, 0, 12) };
        TouchInput.SetMode(input, "Numeric");
        var error = new TextBlock { Text = "Saisir 0 pour supprimer la remise.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        var apply = new Button { Content = "Appliquer", IsDefault = true, Style = (Style)FindResource("Primary") };
        apply.Click += (_, _) => {
            try {
                if (!decimal.TryParse(input.Text.Trim().Replace(',', '.'), System.Globalization.NumberStyles.AllowDecimalPoint | System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var value)) throw new InvalidOperationException("Montant invalide.");
                model.ApplyDiscount(value, kind.SelectedIndex == 0); dialog.Close();
            } catch (Exception ex) { error.Text = ex.Message; }
        };
        panel.Children.Add(kind); panel.Children.Add(input); panel.Children.Add(error); panel.Children.Add(apply);
        dialog.Loaded += (_, _) => { input.Focus(); input.SelectAll(); }; dialog.ShowDialog();
    }
}
