using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Caisse.Desktop;

public class AppDialog : Window
{
    public StackPanel Body { get; } = new() { Margin = new Thickness(26, 20, 26, 24) };

    public AppDialog(Window? owner, string title)
    {
        Style = (Style)FindResource(typeof(Window));
        Owner = owner; Title = title; Width = 520; SizeToContent = SizeToContent.Height;
        MaxHeight = SystemParameters.WorkArea.Height - 40;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false;
        var root = new Grid(); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new());
        var header = new Grid { Background = new SolidColorBrush(Color.FromRgb(16, 61, 64)) };
        header.Children.Add(new TextBlock { Text = title, Foreground = Brushes.White, FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(26, 22, 58, 22), TextWrapping = TextWrapping.Wrap });
        var close = new Button { Content = "×", Foreground = Brushes.White, Background = Brushes.Transparent, BorderThickness = new Thickness(0), FontSize = 22, Padding = new Thickness(8, 2, 8, 2), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        close.Click += (_, _) => Close(); header.Children.Add(close);
        header.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        root.Children.Add(header);
        var scroll = new ScrollViewer { Content = Body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); root.Children.Add(scroll);
        var surface = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(18), BorderBrush = new SolidColorBrush(Color.FromRgb(206, 224, 218)), BorderThickness = new Thickness(1), Child = root };
        surface.SizeChanged += (_, _) => root.Clip = new RectangleGeometry(new Rect(0, 0, root.ActualWidth, root.ActualHeight), 17, 17);
        Content = surface;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
    }

    public static AppDialog CreateConfirmation(Window? owner, string title, string message)
    {
        var dialog = new AppDialog(owner, title);
        dialog.Body.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 15, LineHeight = 25, Margin = new Thickness(0, 0, 0, 24) });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Annuler", IsCancel = true, IsDefault = true, MinWidth = 110 };
        var confirm = new Button { Content = "Confirmer", Style = (Style)dialog.FindResource("Primary"), MinWidth = 130, Margin = new Thickness(0) };
        cancel.Click += (_, _) => dialog.DialogResult = false;
        confirm.Click += (_, _) => dialog.DialogResult = true;
        actions.Children.Add(cancel); actions.Children.Add(confirm); dialog.Body.Children.Add(actions);
        dialog.Loaded += (_, _) => cancel.Focus();
        return dialog;
    }

    public static bool Confirm(Window owner, string title, string message) => CreateConfirmation(owner, title, message).ShowDialog() == true;

    public static void Inform(Window? owner, string title, string message)
    {
        var dialog = new AppDialog(owner, title);
        dialog.Body.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 24) });
        var close = new Button { Content = "Compris", IsDefault = true, IsCancel = true, Style = (Style)dialog.FindResource("Primary"), HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => dialog.Close(); dialog.Body.Children.Add(close); dialog.ShowDialog();
    }
}
