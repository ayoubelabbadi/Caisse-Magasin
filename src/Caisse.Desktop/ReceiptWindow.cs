using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Xml;

namespace Caisse.Desktop;

public sealed class ReceiptWindow : Window
{
    private readonly FlowDocument document;
    public ReceiptWindow(FlowDocument document, string reference)
    {
        this.document = document;
        Title = "Ticket · " + reference; Width = 580; Height = 850; MinWidth = 490; MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 14;
        var layout = new Grid { Background = new SolidColorBrush(Color.FromRgb(232, 238, 240)) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new StackPanel { Margin = new Thickness(24, 18, 24, 16) };
        header.Children.Add(new TextBlock { Text = "Aperçu du ticket", FontSize = 23, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(23, 43, 53)) });
        header.Children.Add(new TextBlock { Text = reference, Foreground = Brushes.SlateGray, FontSize = 12, Margin = new Thickness(0, 5, 0, 0), TextWrapping = TextWrapping.Wrap });
        layout.Children.Add(header);
        var paper = new Border
        {
            MaxWidth = 460, Background = Brushes.White, Margin = new Thickness(24, 0, 24, 20),
            BorderBrush = new SolidColorBrush(Color.FromRgb(213, 223, 225)), BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 14, ShadowDepth = 3, Opacity = 0.10 },
            Child = new FlowDocumentScrollViewer { Document = document, IsToolBarVisible = false, Background = Brushes.White, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
        };
        Grid.SetRow(paper, 1); layout.Children.Add(paper);
        var actions = new DockPanel { Margin = new Thickness(24, 0, 24, 18), LastChildFill = false };
        var close = new Button { Content = "Fermer", IsCancel = true, Padding = new Thickness(18, 12, 18, 12) };
        close.Click += (_, _) => Close(); actions.Children.Add(close);
        var print = new Button { Content = "Imprimer le ticket", Padding = new Thickness(22, 12, 22, 12), Style = (Style)FindResource("Primary"), Margin = new Thickness(0), IsDefault = true };
        DockPanel.SetDock(print, Dock.Right); actions.Children.Add(print);
        print.Click += (_, _) => Print(reference);
        Grid.SetRow(actions, 2); layout.Children.Add(actions); Content = layout;
    }

    public static FlowDocument CopyForPrint(FlowDocument original, double width, double height)
    {
        using var reader = XmlReader.Create(new StringReader(XamlWriter.Save(original)));
        var copy = (FlowDocument)XamlReader.Load(reader);
        copy.PageWidth = Math.Min(420, width); copy.PageHeight = height;
        copy.PagePadding = new Thickness(copy.PageWidth < 320 ? 10 : 22);
        return copy;
    }

    private void Print(string reference)
    {
        try
        {
            var dialog = new PrintDialog();
            if (dialog.ShowDialog() != true) return;
            var copy = CopyForPrint(document, dialog.PrintableAreaWidth, dialog.PrintableAreaHeight);
            dialog.PrintDocument(((IDocumentPaginatorSource)copy).DocumentPaginator, reference);
        }
        catch (Exception ex)
        {
            AppDialog.Inform(this, "Impression", "Impression impossible. L’opération reste enregistrée.\n" + ex.Message);
        }
    }
}
