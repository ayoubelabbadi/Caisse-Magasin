using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Caisse.Application;
using Caisse.Domain;

namespace Caisse.Desktop;

public static class AccountDialogs
{
    public static bool Login(Window? owner, IStore store) => CreateLogin(owner, store).ShowDialog() == true;
    public static AppDialog CreateLogin(Window? owner, IStore store)
    {
        var first = !store.HasAccounts();
        var dialog = new AppDialog(owner, first ? "Bienvenue · créer l’administrateur" : "Connexion à la caisse");
        dialog.Body.Children.Add(new TextBlock { Text = first ? "Créez le compte du responsable. Il pourra ensuite ajouter les caissiers et régler le seuil de remboursement." : "Choisissez votre compte et saisissez votre PIN personnel.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 18) });
        var name = new TextBox { MinHeight = 50, Margin = new Thickness(0, 8, 0, 16) };
        var users = new ComboBox { ItemsSource = store.GetUsers(), DisplayMemberPath = "Name", SelectedIndex = 0, MinHeight = 52, Margin = new Thickness(0, 8, 0, 16) };
        dialog.Body.Children.Add(new TextBlock { Text = first ? "Nom du responsable" : "Compte" }); dialog.Body.Children.Add(first ? name : users);
        dialog.Body.Children.Add(new TextBlock { Text = "PIN personnel · 6 à 12 chiffres" });
        var pin = new PasswordBox { MinHeight = 52, FontSize = 22, Margin = new Thickness(0, 8, 0, 12) }; dialog.Body.Children.Add(pin);
        var confirmPin = new PasswordBox { MinHeight = 52, FontSize = 22, Margin = new Thickness(0, 8, 0, 12) };
        if (first) { dialog.Body.Children.Add(new TextBlock { Text = "Confirmez votre PIN" }); dialog.Body.Children.Add(confirmPin); }
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.Firebrick }; dialog.Body.Children.Add(error);
        var login = new Button { Content = first ? "Créer mon compte administrateur" : "Se connecter", MinHeight = 54, Style = (Style)dialog.FindResource("Primary"), Margin = new Thickness(0, 16, 0, 0) };
        login.Click += (_, _) => {
            try {
                if (first) { if (pin.Password != confirmPin.Password) throw new InvalidOperationException("Les deux PIN doivent être identiques."); store.CreateInitialManager(name.Text, pin.Password); }
                else { if (users.SelectedItem is not UserIdentity user) throw new InvalidOperationException("Choisissez un compte."); store.Login(user.Id, pin.Password); }
                pin.Clear(); confirmPin.Clear(); dialog.DialogResult = true;
            } catch (Exception ex) { error.Text = ex.Message; pin.Clear(); }
        };
        dialog.Body.Children.Add(login); return dialog;
    }
    public static (int Id, string Pin)? Approval(Window owner, IStore store)
    {
        var dialog = new AppDialog(owner, "Autorisation du manager");
        dialog.Body.Children.Add(new TextBlock { Text = "Le cumul des remboursements de ce ticket dépasse le seuil autorisé. Un manager doit saisir son PIN pour cette opération.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) });
        var users = new ComboBox { ItemsSource = store.GetUsers().Where(u => u.IsManager), DisplayMemberPath = "Name", SelectedIndex = 0, MinHeight = 52 };
        var pin = new PasswordBox { FontSize = 22, MinHeight = 52, Margin = new Thickness(0, 12, 0, 12) };
        var button = new Button { Content = "Autoriser ce remboursement", Style = (Style)dialog.FindResource("Primary"), MinHeight = 54 };
        button.Click += (_, _) => { if (users.SelectedItem is UserIdentity && pin.Password.Length > 0) dialog.DialogResult = true; };
        dialog.Body.Children.Add(users); dialog.Body.Children.Add(pin); dialog.Body.Children.Add(button);
        if (dialog.ShowDialog() != true || users.SelectedItem is not UserIdentity manager) return null;
        var result = (manager.Id, pin.Password); pin.Clear(); return result;
    }
    public static void Manage(Window owner, IStore store)
    {
        if (store.CurrentUser?.IsManager != true) { AppDialog.Inform(owner, "Accès administrateur", "Connectez-vous avec un compte administrateur."); return; }
        var dialog = new AppDialog(owner, "Comptes & autorisations") { Width = 640 };
        var name = new TextBox { Margin = new Thickness(0, 6, 0, 10) };
        var pin = new PasswordBox { MinHeight = 50, FontSize = 22, Margin = new Thickness(0, 6, 0, 10) };
        var manager = new CheckBox { Content = "Compte administrateur", MinHeight = 48, FontSize = 17 };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        dialog.Body.Children.Add(new TextBlock { Text = "Nouveau compte · nom unique" }); dialog.Body.Children.Add(name);
        dialog.Body.Children.Add(new TextBlock { Text = "PIN initial · 6 à 12 chiffres" }); dialog.Body.Children.Add(pin); dialog.Body.Children.Add(manager);
        var add = new Button { Content = "Créer le compte", Style = (Style)dialog.FindResource("Primary"), Margin = new Thickness(0, 0, 0, 20) };
        var users = new ComboBox { ItemsSource = store.GetUsers(), DisplayMemberPath = "Name", MinHeight = 50 };
        add.Click += (_, _) => { try { store.SaveUser(name.Text, pin.Password, manager.IsChecked == true); name.Clear(); pin.Clear(); users.ItemsSource = store.GetUsers(); error.Text = "Compte créé."; } catch (Exception ex) { error.Text = ex.Message; } };
        dialog.Body.Children.Add(add); dialog.Body.Children.Add(new TextBlock { Text = "Réinitialiser un PIN · sélectionner le compte" }); dialog.Body.Children.Add(users);
        var reset = new Button { Content = "Définir un nouveau PIN…", Margin = new Thickness(0, 10, 0, 20) };
        reset.Click += (_, _) => {
            if (users.SelectedItem is not UserIdentity user) return;
            var keyboard = TouchInput.Create(dialog, "Nouveau PIN · " + user.Name, "", true, true);
            if (keyboard.ShowDialog() == true) try { store.ResetPin(user.Id, keyboard.Value); error.Text = "PIN remplacé."; } catch (Exception ex) { error.Text = ex.Message; }
        }; dialog.Body.Children.Add(reset);
        dialog.Body.Children.Add(new TextBlock { Text = "Seuil manager · cumul remboursé par ticket (0 = toute somme)" });
        var limit = new TextBox { Text = store.GetRefundLimit().ToString("0.00"), Margin = new Thickness(0, 8, 0, 12) }; TouchInput.SetMode(limit, "Numeric"); dialog.Body.Children.Add(limit);
        var save = new Button { Content = "Enregistrer le seuil", Style = (Style)dialog.FindResource("Primary") };
        save.Click += (_, _) => { try { if (!decimal.TryParse(limit.Text.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)) throw new InvalidOperationException("Montant invalide."); store.SetRefundLimit(amount); error.Text = "Seuil enregistré."; } catch (Exception ex) { error.Text = ex.Message; } };
        dialog.Body.Children.Add(save); dialog.Body.Children.Add(error); dialog.ShowDialog();
    }
}
