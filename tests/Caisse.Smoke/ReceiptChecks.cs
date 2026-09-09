using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Caisse.Desktop;
using Caisse.Domain;

internal static class ReceiptChecks
{
    public static void Run(string output)
    {
        var settings = new ShopSettings();
        var sale = new Sale { Number = "V-20260907-201055-53D38B", CreatedAt = DateTime.UtcNow, Total = 59, Tendered = 70, Change = 11,
            Lines = [
                new() { ProductName = "Pâtes spaghetti · 500 g", Quantity = 1, UnitPrice = 8.50m },
                new() { ProductName = "Concentré de tomate · 140 g", Quantity = 1, UnitPrice = 6.50m },
                new() { ProductName = "Chocolat au lait · 100 g", Quantity = 1, UnitPrice = 12 },
                new() { ProductName = "Eau gazeuse · 1 L", Quantity = 1, UnitPrice = 8 },
                new() { ProductName = "Dentifrice · 75 ml", Quantity = 1, UnitPrice = 15 },
                new() { ProductName = "Farine de blé · 1 kg", Quantity = 1, UnitPrice = 9 }
            ] };
        var document = ReceiptDocument.Create(sale, settings);
        var preview = new ReceiptWindow(ReceiptDocument.Create(sale, settings), sale.Number);
        var previewContent = (FrameworkElement)preview.Content;
        previewContent.Measure(new Size(560, 800)); previewContent.Arrange(new Rect(0, 0, 560, 800)); previewContent.UpdateLayout();
        var text = new TextRange(document.ContentStart, document.ContentEnd).Text;
        if (!text.Contains("59,00 MAD") || !text.Contains("11,00 MAD") || !text.Contains("Farine de blé")) throw new Exception("Ticket : montant ou article manquant.");
        var widthBefore = document.PageWidth;
        Save(ReceiptWindow.CopyForPrint(document, 420, 900), Path.Combine(output, "ticket-style.png"));
        Save(ReceiptWindow.CopyForPrint(document, 280, 1100), Path.Combine(output, "ticket-etroit.png"));
        if (!document.PageWidth.Equals(widthBefore)) throw new Exception("L’impression a modifié l’aperçu.");
        var refund = ReceiptDocument.CreateRefund(new Refund { Id = 12, SaleNumber = sale.Number, Amount = 59, CreatedAt = DateTime.UtcNow, PaymentMethod = "Espèces", Reason = "Retour des articles à la demande du client.", Restock = true }, settings);
        Save(ReceiptWindow.CopyForPrint(refund, 420, 650), Path.Combine(output, "ticket-retour-style.png"));
        sale.Discount = 5.90m; sale.Total = 53.10m; sale.Change = 16.90m;
        var discounted = ReceiptDocument.Create(sale, settings);
        if (!new TextRange(discounted.ContentStart, discounted.ContentEnd).Text.Contains("−5,90 MAD")) throw new Exception("Remise absente du ticket.");
        for (var i = 0; i < 50; i++) sale.Lines.Add(new SaleLine { ProductName = "Produit avec une désignation volontairement longue pour vérifier le retour à la ligne · grand format", Quantity = 2, UnitPrice = 12.50m });
        var longDocument = ReceiptWindow.CopyForPrint(ReceiptDocument.Create(sale, settings), 280, 600);
        var paginator = ((IDocumentPaginatorSource)longDocument).DocumentPaginator;
        paginator.ComputePageCount();
        if (paginator.PageCount < 2) throw new Exception("Pagination du ticket long incorrecte.");
        for (var i = 0; i < paginator.PageCount; i++) if (paginator.GetPage(i) == DocumentPage.Missing) throw new Exception("Page du ticket manquante.");
        Console.WriteLine("PASS: ticket français, remise, retour, impression étroite et pagination longue.");
    }

    private static void Save(FlowDocument document, string path)
    {
        var paginator = ((IDocumentPaginatorSource)document).DocumentPaginator; paginator.ComputePageCount();
        if (paginator.PageCount != 1) throw new Exception("Le ticket de référence devrait tenir sur une page : " + path);
        var bitmap = new RenderTargetBitmap((int)document.PageWidth, (int)document.PageHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(paginator.GetPage(0).Visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
