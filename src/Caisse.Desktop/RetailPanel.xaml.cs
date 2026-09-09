using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Caisse.Desktop;

public partial class RetailPanel : UserControl
{
    public RetailPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, e) => {
            if (e.OldValue is RetailViewModel previous) previous.PropertyChanged -= RoleChanged;
            if (e.NewValue is RetailViewModel current) current.PropertyChanged += RoleChanged;
            RetailTabs.SelectedIndex = 0;
        };
    }
    private void RoleChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RetailViewModel.IsManager) && DataContext is RetailViewModel { IsManager: false } && RetailTabs.SelectedIndex > 1)
            RetailTabs.SelectedIndex = 0;
    }
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Rapport CSV (*.csv)|*.csv", FileName = $"ventes-{DateTime.Now:yyyyMMdd-HHmmss}.csv" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) ((RetailViewModel)DataContext).ExportReport(dialog.FileName);
    }
    private void Reason_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || ReasonChoice.SelectedItem as string != "Autre motif") return;
        var vm = (RetailViewModel)DataContext;
        var keyboard = TouchInput.Create(Window.GetWindow(this), "Expliquez le motif du remboursement", vm.RefundReason, false);
        if (keyboard.ShowDialog() == true) vm.RefundReason = keyboard.Value;
    }
    private void RefundDetails_Click(object sender, RoutedEventArgs e)
    {
        var vm = (RetailViewModel)DataContext;
        if (vm.SelectedRefund is not { } refund) { vm.Status = "Sélectionnez un remboursement dans l’historique."; return; }
        var dialog = new AppDialog(Window.GetWindow(this), "Historique du remboursement #" + refund.Id) { Width = 760 };
        dialog.Body.Children.Add(new TextBlock { Text = $"Ticket : {refund.SaleNumber}\nDemandeur : {refund.RequestedBy}\nManager : {(string.IsNullOrEmpty(refund.ApprovedBy) ? "Non requis / ancien retour" : refund.ApprovedBy)}\nCaisse : {refund.Register} · shift #{refund.CashSessionId}\nDate : {refund.CreatedAt.ToLocalTime():dd/MM/yyyy HH:mm:ss}\nMontant : {refund.Amount:N2} · {refund.PaymentMethod}\nMotif : {refund.ReasonCode} · {refund.Reason}\nStock : {(refund.Restock ? "Quantités remises en stock" : "Stock inchangé")}", TextWrapping = TextWrapping.Wrap, FontSize = 17, Margin = new Thickness(0, 0, 0, 16) });
        foreach (var line in refund.Lines) dialog.Body.Children.Add(new TextBlock { Text = $"{line.Quantity} × {line.ProductName}  —  {line.Amount:N2}", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 5), FontSize = 17 });
        var print = new Button { Content = "Ouvrir le justificatif", Command = vm.RefundReceiptCommand, Margin = new Thickness(0, 16, 0, 0) }; dialog.Body.Children.Add(print); dialog.ShowDialog();
    }
}
