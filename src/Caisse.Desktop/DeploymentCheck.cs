using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Caisse.Domain;
using Caisse.Infrastructure;

namespace Caisse.Desktop;

internal static class DeploymentCheck
{
    public static void Run(string output)
    {
        var directory = Path.Combine(Path.GetFullPath(output), "verification-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var store = new SqliteStore(Path.Combine(directory, "check.db")); store.Initialize(); store.CreateInitialManager("Amina", "123456");
        var session = store.OpenSession("Vérification", 100);
        var product = store.SaveProduct(new Product { Name = "Article de vérification", Barcode = "CHECK", Price = 12.50m, Stock = 2 });
        var sale = store.Checkout([new(product.Id, 1, product.Price)], "Espèces", 20);
        if (sale.Change != 7.50m || store.GetProducts().Single().Stock != 1 || store.GetExpectedCash(session.Id) != 112.50m) throw new InvalidOperationException("Vérification SQLite échouée.");
        var manager = store.CurrentUser!; store.Logout(); store.Login(manager.Id, "123456");
        store.SetRefundLimit(0);
        var returned = store.RefundPartial(sale.Id, [new(sale.Lines.Single().Id, 1)], "Produit retourné", "", true, manager.Id, "123456");
        if (returned.ApprovedById != manager.Id || returned.Lines.Single().Quantity != 1 || store.GetExpectedCash(session.Id) != 100m || !store.GetAudit().Any(a => a.Action == "Remboursement")) throw new InvalidOperationException("Vérification PIN / retour / journal échouée.");
        var document = ReceiptWindow.CopyForPrint(ReceiptDocument.Create(sale, new ShopSettings()), 280, 900);
        var paginator = ((IDocumentPaginatorSource)document).DocumentPaginator; paginator.ComputePageCount();
        if (paginator.PageCount < 1) throw new InvalidOperationException("Vérification du ticket échouée.");
        var window = new MainWindow(new MainViewModel(store, new ShopSettings(), directory));
        StoreExport.Write(store, new ShopSettings(), Path.Combine(directory, "tableaux.xlsx"));
        if (!File.Exists(Path.Combine(directory, "tableaux.xlsx"))) throw new InvalidOperationException("Export Excel indisponible.");
        window.WindowState = WindowState.Maximized;
        if (window.WindowState != WindowState.Maximized) throw new InvalidOperationException("Agrandissement échoué.");
        window.WindowState = WindowState.Normal;
        if (window.WindowState != WindowState.Normal) throw new InvalidOperationException("Restauration échouée.");
        if (window.WindowStyle != WindowStyle.SingleBorderWindow || window.ResizeMode != ResizeMode.CanResize) throw new InvalidOperationException("Commandes Windows indisponibles.");
        File.WriteAllText(Path.Combine(Path.GetFullPath(output), "verification-ok.txt"), $"OK : démarrage autonome, PIN, shift, WPF, SQLite, vente, remboursement autorisé, journal, ticket, export Excel et commandes de fenêtre.\n{RuntimeInformation.FrameworkDescription}\n{RuntimeInformation.OSArchitecture}\nBase de test : {directory}");
    }
}
