using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using Caisse.Desktop;
using Caisse.Domain;
using Caisse.Infrastructure;

internal static class DashboardChecks
{
    public static MainViewModel Run(string output)
    {
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        var store = new SqliteStore(Path.Combine(output, $"dashboard-{Guid.NewGuid():N}.db")); store.Initialize(); store.CreateInitialManager("Amina", "123456");
        var product = store.SaveProduct(new Product { Name = "=1+1", Barcode = "000123", Price = 10, Stock = 10 });
        var first = store.OpenSession("Amina", 0);
        var sale = store.Checkout([new(product.Id, 1, 10)], "Espèces", 10);
        store.CloseSession(first.Id, 9);
        store.SaveUser("Youssef", "654321", true); store.Login(store.GetUsers().Single(u => u.Name == "Youssef").Id, "654321"); store.OpenSession("Caisse test", 20);
        store.RefundSale(sale.Id, "Retour après changement de caissier", true);
        store.Checkout([new(product.Id, 2, 10)], "Espèces", 20);
        var vm = new MainViewModel(store, new ShopSettings(), output);
        var amina = vm.Dashboard.Cashiers.Single(c => c.Name == "Amina");
        var youssef = vm.Dashboard.Cashiers.Single(c => c.Name == "Youssef");
        Check(amina.Sales == 10 && amina.Refunds == 0 && amina.Difference == -1, "Amina sales and closed discrepancy");
        Check(youssef.Sales == 20 && youssef.Refunds == 10 && youssef.Net == 10 && youssef.Cash == 10 && youssef.State == "En caisse", "Cross-cashier refund attribution");
        vm.Dashboard.Selected = amina;
        Check(vm.Dashboard.SelectedSessions.Single().Id == first.Id, "Cashier session drilldown");
        vm.Dashboard.From = DateTime.Today.AddDays(1); vm.Dashboard.To = DateTime.Today;
        Check(!vm.Dashboard.IsValid && vm.Dashboard.Cashiers.Count == 0 && vm.Dashboard.SelectedSessions.Count == 0, "Invalid date range clears results");
        vm.Dashboard.TodayCommand.Execute(null);
        Check(vm.Dashboard.Cashiers.Count == 2, "Date shortcut restores results");
        vm.Dashboard.From = DateTime.Today.AddYears(-2); vm.Dashboard.To = DateTime.Today.AddYears(-1);
        Check(vm.Dashboard.Cashiers.Count == 0, "Empty historical period");
        var now = DateTime.UtcNow;
        var rows = CashierStatistics.Build(
            [new CashSession { Id = 1, Operator = " Amina ", OpenedAt = now, ClosedAt = now }, new CashSession { Id = 2, Operator = "amina", OpenedAt = now, ClosedAt = now }],
            [new Sale { Total = 4, CreatedAt = now, CashSessionId = 1 }, new Sale { Total = 6, CreatedAt = now, CashSessionId = 2 }, new Sale { Total = 3, CreatedAt = now }], [], DateTime.Today, DateTime.Today);
        Check(rows.Count == 2 && rows.Single(c => c.Key == CashierStatistics.Key("amina")).Sales == 10 && rows.Single(c => c.Key == CashierStatistics.UnassignedKey).Sales == 3, "Name normalization and legacy unassigned sales");

        var path = Path.Combine(output, "tableaux-magasin-test.xlsx");
        StoreExport.Write(store, new ShopSettings(), path);
        using var zip = ZipFile.OpenRead(path);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XDocument Sheet(int index) { using var stream = zip.GetEntry($"xl/worksheets/sheet{index}.xml")!.Open(); return XDocument.Load(stream); }
        var stock = Sheet(1);
        XElement Cell(string address) => stock.Descendants(ns + "c").Single(c => (string?)c.Attribute("r") == address);
        Check((string?)Cell("A2").Attribute("t") == "inlineStr" && Cell("A2").Value == "000123", "Excel preserves barcode leading zeros");
        Check(Cell("B2").Value == "=1+1" && !stock.Descendants(ns + "f").Any(), "Exported names cannot become formulas");
        Check(Cell("E2").Value == "8" && Cell("F2").Value == "80", "Export contains remaining stock and valuation");
        Check(stock.Descendants(ns + "pane").Any() && stock.Descendants(ns + "autoFilter").Any(), "Excel filter and frozen header");
        Check(Sheet(6).Descendants(ns + "c").Single(c => (string?)c.Attribute("r") == "D2").Value == "Youssef", "Export attributes return to processing cashier");
        foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".xml") || e.FullName.EndsWith(".rels"))) { using var stream = entry.Open(); XDocument.Load(stream); }
        Check(zip.Entries.Count(e => e.FullName.StartsWith("xl/worksheets/")) == 11, "All export worksheets exist");
        Console.WriteLine("PASS: tableau de bord, dates, caissiers, retours croisés et export Excel typé.");
        vm.Dashboard.TodayCommand.Execute(null);
        return vm;
    }
}
