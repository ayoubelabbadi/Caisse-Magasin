using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

namespace Caisse.Desktop;

public sealed class PaymentDialog : AppDialog
{
    public PaymentDialog(Window? owner, MainViewModel model) : base(owner, "Encaisser la vente")
    {
        DataContext = model; Width = 620;
        var total = new TextBlock { FontSize = 34, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 18) }; total.SetBinding(TextBlock.TextProperty, new Binding("TotalText")); Body.Children.Add(total);
        var methods = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 16) };
        foreach (var item in new[] { ("Espèces", "IsCash"), ("Carte bancaire", "IsCard") }) {
            var choice = new RadioButton { Content = item.Item1, Style = (Style)FindResource("PaymentChoice"), Margin = new Thickness(4), MinHeight = 56 }; choice.SetBinding(RadioButton.IsCheckedProperty, new Binding(item.Item2)); methods.Children.Add(choice);
        }
        Body.Children.Add(methods);
        var cash = new StackPanel(); cash.SetBinding(VisibilityProperty, new Binding("IsCash") { Converter = new BooleanToVisibilityConverter() });
        cash.Children.Add(new TextBlock { Text = "Montant donné par le client", FontSize = 18 });
        var amount = new TextBox { FontSize = 28, MinHeight = 60, Margin = new Thickness(0, 8, 0, 12) }; TouchInput.SetMode(amount, "Numeric");
        amount.SetBinding(TextBox.TextProperty, new Binding("Tendered") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }); cash.Children.Add(amount);
        var exact = new Button { Content = "Montant exact", Margin = new Thickness(0, 0, 0, 10), Command = model.ExactCommand }; cash.Children.Add(exact);
        var quick = new UniformGrid { Columns = 4 };
        foreach (var value in new[] { 20, 50, 100, 200 }) quick.Children.Add(new Button { Content = value + " " + (model.Settings.Currency == "MAD" ? "DH" : model.Settings.Currency), Command = model.QuickTenderCommand, CommandParameter = value, Margin = new Thickness(3), MinHeight = 56 });
        cash.Children.Add(quick);
        cash.Children.Add(new TextBlock { Text = "Monnaie à rendre", FontSize = 18, Margin = new Thickness(0, 16, 0, 6) });
        var change = new TextBlock { FontSize = 32, FontWeight = FontWeights.Bold, Foreground = (System.Windows.Media.Brush)FindResource("Accent") }; change.SetBinding(TextBlock.TextProperty, "ChangeText"); cash.Children.Add(change); Body.Children.Add(cash);
        var card = new TextBlock { Text = "Validez d’abord le paiement sur le terminal bancaire. Le bouton ci-dessous enregistre ensuite la vente dans la caisse.", TextWrapping = TextWrapping.Wrap, FontSize = 17, Margin = new Thickness(0, 0, 0, 14) }; card.SetBinding(VisibilityProperty, new Binding("IsCard") { Converter = new BooleanToVisibilityConverter() }); Body.Children.Add(card);
        var error = new TextBlock { Foreground = System.Windows.Media.Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) }; Body.Children.Add(error);
        var pay = new Button { Content = "Valider l’encaissement", MinHeight = 60, Style = (Style)FindResource("Primary"), Margin = new Thickness(0, 18, 0, 0) };
        pay.Click += (_, _) => { model.CheckoutCommand.Execute(null); if (model.Cart.Count == 0) DialogResult = true; else error.Text = model.Status; }; Body.Children.Add(pay);
    }
}
