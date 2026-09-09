using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;

namespace Caisse.Desktop;

public static class TouchInput
{
    public static readonly DependencyProperty CommitCommandProperty = DependencyProperty.RegisterAttached("CommitCommand", typeof(ICommand), typeof(TouchInput));
    public static ICommand? GetCommitCommand(DependencyObject target) => (ICommand?)target.GetValue(CommitCommandProperty);
    public static void SetCommitCommand(DependencyObject target, ICommand? value) => target.SetValue(CommitCommandProperty, value);
    public static readonly DependencyProperty ModeProperty = DependencyProperty.RegisterAttached("Mode", typeof(string), typeof(TouchInput), new FrameworkPropertyMetadata("Text", FrameworkPropertyMetadataOptions.Inherits));
    public static string GetMode(DependencyObject target) => (string)target.GetValue(ModeProperty);
    public static void SetMode(DependencyObject target, string value) => target.SetValue(ModeProperty, value);
    private static bool registered, opening;
    public static void Register()
    {
        if (registered) return; registered = true;
        EventManager.RegisterClassHandler(typeof(TextBox), UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler((s, e) => { if (Open(s)) e.Handled = true; }));
        EventManager.RegisterClassHandler(typeof(TextBox), UIElement.PreviewTouchDownEvent, new EventHandler<TouchEventArgs>((s, e) => { if (Open(s)) e.Handled = true; }));
        EventManager.RegisterClassHandler(typeof(PasswordBox), UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler((s, e) => { if (Open(s)) e.Handled = true; }));
        EventManager.RegisterClassHandler(typeof(PasswordBox), UIElement.PreviewTouchDownEvent, new EventHandler<TouchEventArgs>((s, e) => { if (Open(s)) e.Handled = true; }));
    }
    private static bool Open(object? sender)
    {
        if (opening || sender is not Control field || !field.IsEnabled || GetMode(field) == "Off" || field is TextBox { IsReadOnly: true }) return false;
        var owner = Window.GetWindow(field); if (owner is null) return false;
        opening = true;
        field.Dispatcher.BeginInvoke(new Action(() => {
            try {
                var box = field as TextBox; var password = field as PasswordBox;
                var path = box is null ? "" : BindingOperations.GetBindingExpression(box, TextBox.TextProperty)?.ParentBinding.Path?.Path ?? "";
                var numeric = password is not null || GetMode(field) == "Numeric" || new[] { "Tendered", "Opening", "Counted", "MovementAmount", "EditPrice", "EditStock", "Quantity" }.Contains(path);
                var vm = owner.DataContext as MainViewModel;
                var dialog = Create(owner, password is not null ? "Votre code PIN" : numeric ? "Saisir un montant ou une quantité" : "Clavier tactile", box?.Text ?? password?.Password ?? "", numeric, password is not null, path == "Tendered" ? vm?.Total : null, vm?.Settings.Currency ?? "MAD");
                dialog.IntegerOnly = path is "Quantity" or "EditStock";
                if (dialog.ShowDialog() == true) {
                    if (box is not null) { box.Text = dialog.Value; box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource(); }
                    if (password is not null) password.Password = dialog.Value;
                    var command = GetCommitCommand(field);
                    if (command?.CanExecute(null) == true) command.Execute(null);
                }
            } finally { opening = false; }
        }));
        return true;
    }
    public static TouchKeyboard Create(Window? owner, string title, string value, bool numeric, bool secret = false, decimal? exact = null, string currency = "MAD") => new(owner, title, value, numeric, secret, exact, currency);
}

public sealed class TouchKeyboard : AppDialog
{
    private readonly TextBox entry;
    private readonly TextBlock masked = new();
    private readonly PasswordBox? secretEntry;
    private readonly bool secret;
    private bool upper = true;
    public string Value => entry.Text;
    public bool IntegerOnly { get; set; }
    public TouchKeyboard(Window? owner, string title, string value, bool numeric, bool secret, decimal? exact, string currency) : base(owner, title)
    {
        this.secret = secret;
        TouchInput.SetMode(this, "Off"); Width = Math.Min(numeric ? 580 : 1000, SystemParameters.WorkArea.Width - 30);
        entry = new TextBox { Text = value, FontSize = 26, MinHeight = 58, Margin = new Thickness(0, 0, 0, 14) };
        if (secret) { entry.Visibility = Visibility.Collapsed; secretEntry = new PasswordBox { Password = value, FontSize = 28, MaxLength = 12, MinHeight = 58, Margin = new Thickness(0, 0, 0, 16) }; secretEntry.PasswordChanged += (_, _) => { if (entry.Text != secretEntry.Password) entry.Text = secretEntry.Password; }; Body.Children.Add(secretEntry); }
        entry.TextChanged += (_, _) => { masked.Text = new string('●', entry.Text.Length); if (secretEntry is not null && secretEntry.Password != entry.Text) secretEntry.Password = entry.Text; };
        Body.Children.Add(entry);
        if (numeric) {
            if (exact.HasValue) {
                var quick = new UniformGrid { Columns = 5, Margin = new Thickness(0, 0, 0, 10) };
                foreach (var amount in new[] { exact.Value, 20m, 50m, 100m, 200m }.Select((v, i) => (v, i))) {
                    var b = Key(amount.i == 0 ? "Exact" : amount.v + " " + (currency == "MAD" ? "DH" : currency), () => SetValue(amount.v.ToString("0.00", CultureInfo.GetCultureInfo("fr-FR")))); b.FontSize = 15; quick.Children.Add(b);
                }
                Body.Children.Add(quick);
            }
            var keys = new UniformGrid { Columns = 3 };
            foreach (var text in new[] { "7", "8", "9", "4", "5", "6", "1", "2", "3", "00", "0", "," }) keys.Children.Add(Key(text, () => Insert(text)));
            Body.Children.Add(keys);
        } else {
            foreach (var row in new[] { "1234567890", "AZERTYUIOP", "QSDFGHJKLM", "WXCVBNéèàç", "@.-_'/?!:;" }) {
                var keys = new UniformGrid { Columns = row.Length };
                foreach (var character in row) { var text = character.ToString(); keys.Children.Add(Key(text, () => Insert(upper ? text : text.ToLowerInvariant()))); }
                Body.Children.Add(keys);
            }
            var extra = new UniformGrid { Columns = 2 }; extra.Children.Add(Key("Maj / min", () => upper = !upper)); extra.Children.Add(Key("Espace", () => Insert(" "))); Body.Children.Add(extra);
        }
        var error = new TextBlock { Foreground = System.Windows.Media.Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };
        Body.Children.Add(error);
        var actions = new UniformGrid { Columns = 3, Margin = new Thickness(0, 8, 0, 0) };
        actions.Children.Add(Key("⌫ Correction", Backspace)); actions.Children.Add(Key("Effacer", () => SetValue("")));
        var validate = Key("Valider", () => {
            if (IntegerOnly && string.IsNullOrEmpty(entry.Text)) SetValue("0");
            if (IntegerOnly && entry.Text.Any(c => !char.IsAsciiDigit(c))) { error.Text = "Saisissez une quantité entière."; return; }
            if (numeric && !secret && (entry.Text.Count(c => c is ',' or '.') > 1 || entry.Text.Any(c => !char.IsAsciiDigit(c) && c is not ',' and not '.'))) { error.Text = "Utilisez des chiffres et une seule virgule."; return; }
            if (secret && entry.Text.Any(c => !char.IsAsciiDigit(c))) { error.Text = "Le PIN contient uniquement des chiffres."; return; }
            DialogResult = true;
        }); validate.IsDefault = true; validate.Style = (Style)FindResource("Primary"); actions.Children.Add(validate); Body.Children.Add(actions);
        Loaded += (_, _) => { if (secretEntry is not null) secretEntry.Focus(); else { entry.Focus(); entry.SelectAll(); } };
    }
    private Button Key(string text, Action action)
    {
        var button = new Button { Content = text, FontSize = 20, MinHeight = 54, Padding = new Thickness(6), Margin = new Thickness(3), Focusable = false };
        button.Click += (_, _) => action(); return button;
    }
    public void Insert(string text)
    {
        if (secret) { if (entry.Text.Length + text.Length <= 12 && text.All(char.IsAsciiDigit)) entry.Text += text; return; }
        var start = entry.SelectionStart; entry.SelectedText = text; entry.Select(start + text.Length, 0);
    }
    public void Backspace()
    {
        if (secret) { if (entry.Text.Length > 0) entry.Text = entry.Text[..^1]; return; }
        if (entry.SelectionLength > 0) entry.SelectedText = "";
        else if (entry.SelectionStart > 0) { var start = entry.SelectionStart; entry.Text = entry.Text.Remove(start - 1, 1); entry.Select(start - 1, 0); }
    }
    public void SetValue(string value) { entry.Text = value; entry.Select(entry.Text.Length, 0); }
}
