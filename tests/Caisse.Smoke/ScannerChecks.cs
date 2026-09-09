using System.IO;
using Caisse.Desktop;
using Caisse.Domain;
using Caisse.Infrastructure;

internal static class ScannerChecks
{
    public static void Run(string output)
    {
        var store = new SqliteStore(Path.Combine(output, $"scanner-{Guid.NewGuid():N}.db"));
        store.Initialize(); store.CreateInitialManager("Amina", "123456");
        for (var i = 0; i < 65; i++)
            store.SaveProduct(new Product { Name = $"Article {i:D3}", Barcode = $"{i:D13}", Category = i % 2 == 0 ? "A" : "B", Price = 5, Stock = 2 });
        var shift = store.OpenSession("Caisse test", 0);
        var vm = new MainViewModel(store, new ShopSettings(), output);
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        Check(vm.Products.Count == 24, "Catalogue page size");
        var first = vm.Products.First().Id;
        vm.NextProductsCommand.Execute(null);
        Check(vm.Products.Count == 24 && vm.Products.First().Id != first, "Next product page");
        vm.NextProductsCommand.Execute(null);
        Check(vm.Products.Count == 17 && !vm.NextProductsCommand.CanExecute(null), "Final product page");
        vm.Search = "Article 064";
        Check(vm.Products.Count == 1, "Search beyond first page");
        vm.Category = "B";
        Check(vm.Products.Count == 0, "Empty search results");
        vm.Barcode = "0000000000000"; vm.ScanCommand.Execute(null);
        Check(vm.Cart.Count == 1 && vm.Barcode == "" && vm.Search == "Article 064", "Scan bypasses search and category, preserves leading zeros");
        vm.Barcode = "0000000000000"; vm.ScanCommand.Execute(null);
        Check(vm.Cart.Single().Quantity == 2, "Repeated scan increments quantity");
        vm.Barcode = "0000000000000"; vm.ScanCommand.Execute(null);
        Check(vm.ScanError && vm.Cart.Single().Quantity == 2, "Scan respects available stock");
        vm.Barcode = "000000000"; vm.ScanCommand.Execute(null);
        Check(vm.ScanError && vm.Cart.Count == 1 && vm.Barcode == "", "Partial barcode must not match");
        vm.Barcode = "0000000000001"; vm.ScanCommand.Execute(null);
        Check(!vm.ScanError && vm.Cart.Count == 2, "Next valid scan recovers after failure");
        vm.Barcode = "  "; vm.ScanCommand.Execute(null);
        Check(vm.Cart.Count == 2, "Empty scanner suffix ignored");
        vm.EditName = "Produit sans code"; vm.EditPrice = "12"; vm.EditStock = "3"; vm.EditCategory = "Accessoires";
        vm.SaveCommand.Execute(null);
        Check(vm.Catalog.Any(p => p.Name == "Produit sans code" && p.Barcode.StartsWith("REF-")), "Automatic reference for unbarcoded item");
        vm.Category = "Tous les produits"; vm.Search = "Produit sans code";
        vm.AddCommand.Execute(vm.Products.Single());
        Check(vm.Cart.Count == 3, "Manual search fallback adds unbarcoded product");
        Check(vm.EditorCategories.Contains("Accessoires") && !vm.EditorCategories.Contains("Tous les produits"), "Editor category list includes custom categories only");

        vm.IsCard = true;
        Check(!vm.IsCash && vm.PaymentMethod == "Carte (terminal externe)", "Card choice maps to stored payment method");
        vm.Tendered = ""; vm.CheckoutCommand.Execute(null);
        Check(vm.Cart.Count == 0 && vm.Sales.Single().PaymentMethod == "Carte (terminal externe)" && vm.Retail.Expected == 0, "Card checkout without cash tender leaves cash drawer unchanged");
        vm.IsCash = true; Check(!vm.IsCard, "Cash choice resets card selection");
        vm.Search = "Article"; vm.AddSearchResultCommand.Execute(null);
        Check(vm.Cart.Count == 0 && vm.Status.Contains("Plusieurs produits"), "Ambiguous search does not add an arbitrary product");
        vm.Search = "introuvable"; vm.AddSearchResultCommand.Execute(null);
        Check(vm.Cart.Count == 0, "Unknown search does not add a product");
        var window = new MainWindow(vm) { ShowInTaskbar = false, Left = -10000, Top = -10000 };
        window.Show();
        var searchBox = (System.Windows.Controls.TextBox)window.FindName("SearchBox");
        // Submit immediately, before the delayed search binding has updated.
        searchBox.Text = "Article 064";
        searchBox.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
            System.Windows.PresentationSource.FromVisual(window), Environment.TickCount, System.Windows.Input.Key.Enter)
            { RoutedEvent = System.Windows.UIElement.PreviewKeyDownEvent });
        Check(vm.Cart.Single().Product.Name == "Article 064", "Physical Enter flushes delayed search and adds the matching product");
        TouchInput.GetCommitCommand(searchBox)!.Execute(null);
        Check(vm.Cart.Single().Quantity == 2, "Touch validation uses the same add command");
        vm.AddSearchResultCommand.Execute(null);
        Check(vm.Cart.Single().Quantity == 2 && vm.Status.Contains("Stock disponible"), "Search addition respects stock");
        vm.CancelCart(); window.Close();
        store.CloseSession(shift.Id, store.GetExpectedCash(shift.Id));
        vm.Refresh(); vm.Search = "Article 063"; vm.AddSearchResultCommand.Execute(null);
        Check(vm.Cart.Count == 0 && vm.Status.Contains("shift"), "Search addition requires the cashier shift");
    }
}
