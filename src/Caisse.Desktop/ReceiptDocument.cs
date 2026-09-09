using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using Caisse.Domain;

namespace Caisse.Desktop;

public static class ReceiptDocument
{
    private static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(23, 43, 53));
    private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(85, 103, 112));
    private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(8, 105, 91));
    private static readonly Brush Rule = new SolidColorBrush(Color.FromRgb(218, 227, 229));
    private static string Number(decimal value) => value.ToString("N2", CultureInfo.GetCultureInfo("fr-FR"));
    private static FlowDocument Paper() => new()
    {
        FontFamily = new FontFamily("Segoe UI"), FontSize = 12, Foreground = Ink,
        Background = Brushes.White, PagePadding = new Thickness(22), ColumnWidth = 10000,
        Language = XmlLanguage.GetLanguage("fr-FR"), TextAlignment = TextAlignment.Left
    };
    private static Paragraph Text(string text, double size = 12, bool bold = false, Brush? color = null) => new(new Run(text))
    {
        FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        Foreground = color ?? Ink, Margin = new Thickness(0, 0, 0, 5), KeepTogether = true
    };
    private static void Header(FlowDocument document, ShopSettings settings, string title, string reference, DateTime date)
    {
        var name = Text(settings.Name, 25, true, Accent); name.TextAlignment = TextAlignment.Center;
        name.Margin = new Thickness(0, 2, 0, 8); document.Blocks.Add(name);
        var kind = Text(title, 10, true, Muted); kind.TextAlignment = TextAlignment.Center;
        kind.Margin = new Thickness(0, 0, 0, 20); document.Blocks.Add(kind);
        document.Blocks.Add(Text(reference, 10, true));
        document.Blocks.Add(Text(date.ToLocalTime().ToString("dd MMMM yyyy · HH:mm", CultureInfo.GetCultureInfo("fr-FR")), 11, color: Muted));
        document.Blocks.Add(new Paragraph { BorderBrush = Rule, BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(0, 5, 0, 12), FontSize = 1 });
    }
    public static FlowDocument Create(Sale sale, ShopSettings settings)
    {
        var document = Paper();
        Header(document, settings, "TICKET DE VENTE", sale.Number, sale.CreatedAt);
        var items = new Table { CellSpacing = 0, Margin = new Thickness(0, 0, 0, 14) };
        items.Columns.Add(new TableColumn { Width = GridLength.Auto });
        items.Columns.Add(new TableColumn { Width = new GridLength(32) });
        items.Columns.Add(new TableColumn { Width = new GridLength(82) });
        var rows = new TableRowGroup(); items.RowGroups.Add(rows);
        var heading = new TableRow();
        heading.Cells.Add(Cell("ARTICLE", false, true)); heading.Cells.Add(Cell("QTÉ", true, true));
        heading.Cells.Add(Cell("MONTANT", true, true)); rows.Rows.Add(heading);
        foreach (var item in sale.Lines)
        {
            var row = new TableRow(); var product = Cell(item.ProductName);
            product.Blocks.Add(Text(Number(item.UnitPrice) + " " + settings.Currency + " / unité", 10, color: Muted));
            row.Cells.Add(product); row.Cells.Add(Cell(item.Quantity.ToString(CultureInfo.InvariantCulture), true));
            row.Cells.Add(Cell(Number(item.Total), true)); rows.Rows.Add(row);
        }
        document.Blocks.Add(items);
        document.Blocks.Add(Text($"{sale.Lines.Sum(l => l.Quantity)} article(s) · Prix en {settings.Currency}", 10, color: Muted));
        var summary = new Section { Margin = new Thickness(0, 10, 0, 0) };
        if (sale.Discount > 0)
        {
            summary.Blocks.Add(Pair("Sous-total", Number(sale.Total + sale.Discount) + " " + settings.Currency));
            summary.Blocks.Add(Pair("Remise", "−" + Number(sale.Discount) + " " + settings.Currency));
        }
        summary.Blocks.Add(Pair("TOTAL", Number(sale.Total) + " " + settings.Currency, true));
        summary.Blocks.Add(Pair("Paiement", sale.PaymentMethod));
        summary.Blocks.Add(Pair("Montant reçu", Number(sale.Tendered) + " " + settings.Currency));
        summary.Blocks.Add(Pair("Monnaie rendue", Number(sale.Change) + " " + settings.Currency));
        document.Blocks.Add(summary); Footer(document, "Merci de votre visite !", "À bientôt dans votre magasin."); return document;
    }
    private static TableCell Cell(string value, bool right = false, bool heading = false)
    {
        var paragraph = Text(value, heading ? 9 : 12, true, heading ? Muted : Ink);
        paragraph.TextAlignment = right ? TextAlignment.Right : TextAlignment.Left;
        return new TableCell(paragraph) { Padding = new Thickness(0, 8, right ? 0 : 8, 8), BorderBrush = Rule, BorderThickness = new Thickness(0, 0, 0, 0.5) };
    }
    private static Table Pair(string label, string value, bool total = false)
    {
        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, total ? 8 : 0, 0, total ? 12 : 3) };
        table.Columns.Add(new TableColumn { Width = GridLength.Auto });
        table.Columns.Add(new TableColumn { Width = GridLength.Auto });
        var group = new TableRowGroup(); var row = new TableRow(); group.Rows.Add(row); table.RowGroups.Add(group);
        if (total) row.Background = new SolidColorBrush(Color.FromRgb(234, 242, 239));
        var left = Text(label, total ? 13 : 11, total, total ? Accent : Muted);
        var right = Text(value, total ? 21 : 11, true, total ? Accent : Ink); right.TextAlignment = TextAlignment.Right;
        row.Cells.Add(new TableCell(left) { Padding = new Thickness(total ? 10 : 0, total ? 12 : 3, 6, total ? 10 : 0) });
        row.Cells.Add(new TableCell(right) { Padding = new Thickness(0, total ? 8 : 3, total ? 10 : 0, total ? 10 : 0) });
        return table;
    }
    private static void Footer(FlowDocument document, string title, string subtitle)
    {
        var footer = new Section { BorderBrush = Rule, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 16, 0, 0), Margin = new Thickness(0, 16, 0, 0) };
        var thanks = Text(title, 13, true); thanks.TextAlignment = TextAlignment.Center;
        var detail = Text(subtitle, 10, color: Muted); detail.TextAlignment = TextAlignment.Center;
        footer.Blocks.Add(thanks); footer.Blocks.Add(detail); document.Blocks.Add(footer);
    }
    public static FlowDocument CreateRefund(Refund refund, ShopSettings settings)
    {
        var document = Paper();
        Header(document, settings, "JUSTIFICATIF DE REMBOURSEMENT", "Remboursement #" + refund.Id, refund.CreatedAt);
        document.Blocks.Add(Text("TICKET D’ORIGINE", 9, true, Muted)); document.Blocks.Add(Text(refund.SaleNumber, 11, true));
        document.Blocks.Add(Pair("REMBOURSÉ", Number(refund.Amount) + " " + settings.Currency, true));
        document.Blocks.Add(Pair("Paiement", refund.PaymentMethod)); document.Blocks.Add(Pair("Remise en stock", refund.Restock ? "Oui" : "Non"));
        if (!string.IsNullOrWhiteSpace(refund.RequestedBy)) document.Blocks.Add(Text("Caissier : " + refund.RequestedBy));
        if (!string.IsNullOrWhiteSpace(refund.ApprovedBy)) document.Blocks.Add(Text("Autorisé par : " + refund.ApprovedBy));
        foreach (var line in refund.Lines) document.Blocks.Add(Text($"{line.Quantity} × {line.ProductName} · {Number(line.Amount)} {settings.Currency}"));
        document.Blocks.Add(Text("MOTIF DU RETOUR", 9, true, Muted)); document.Blocks.Add(Text(refund.Reason));
        Footer(document, "Retour enregistré", "Conservez ce justificatif avec votre ticket d’origine."); return document;
    }
}
