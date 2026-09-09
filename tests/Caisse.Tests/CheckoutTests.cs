using Caisse.Domain;
using Caisse.Infrastructure;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Caisse.Tests;

public sealed class CheckoutTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "CaisseTests", Guid.NewGuid().ToString("N"));
    private readonly SqliteStore store;
    public CheckoutTests() { store = new SqliteStore(Path.Combine(directory, "test.db")); store.Initialize(); store.CreateInitialManager("Amina", "123456"); store.OpenSession("Tests", 0); }
    private Product Product(string code = "001", decimal price = 12.35m, int stock = 10) => store.SaveProduct(new Product { Name = "Article " + code, Barcode = code, Price = price, Stock = stock });

    [Fact]
    public void CashSalePersistsExactAmountsAndStock()
    {
        var p = Product();
        var sale = store.Checkout([new(p.Id, 3, p.Price)], "Espèces", 50m);
        Assert.Equal(37.05m, sale.Total); Assert.Equal(12.95m, sale.Change);
        Assert.Equal(7, store.GetProducts().Single().Stock);
        Assert.Equal(sale.Number, store.GetSales().Single().Number);
        using var db = new StoreDb(Path.Combine(directory, "test.db"));
        Assert.Contains(db.StockMovements, m => m.Reason == sale.Number && m.Quantity == -3);
    }

    [Fact]
    public void InsufficientPaymentLeavesNoSaleAndStockUnchanged()
    {
        var p = Product();
        Assert.Throws<InvalidOperationException>(() => store.Checkout([new(p.Id, 2, p.Price)], "Espèces", 1m));
        Assert.Empty(store.GetSales()); Assert.Equal(10, store.GetProducts().Single().Stock);
    }

    [Fact]
    public void LaterLineFailureRollsBackWholeCart()
    {
        var a = Product("a"); var b = Product("b", stock: 0);
        Assert.Throws<InvalidOperationException>(() => store.Checkout([new(a.Id, 2, a.Price), new(b.Id, 1, b.Price)], "Espèces", 100m));
        Assert.Equal(10, store.GetProducts().Single(p => p.Id == a.Id).Stock); Assert.Empty(store.GetSales());
    }

    [Fact]
    public void HistoricalPriceSurvivesCatalogChanges()
    {
        var p = Product();
        store.Checkout([new(p.Id, 1, p.Price)], "Espèces", p.Price);
        p.Price = 99m; p.Name = "Nouveau nom"; p.Stock = 9; store.SaveProduct(p);
        var line = store.GetSales().Single().Lines.Single();
        Assert.Equal(12.35m, line.UnitPrice); Assert.Equal("Article 001", line.ProductName);
    }

    [Fact]
    public void ChangedPriceRequiresCartRefresh()
    {
        var p = Product(); var oldPrice = p.Price; p.Price = 20m; store.SaveProduct(p);
        Assert.Throws<InvalidOperationException>(() => store.Checkout([new(p.Id, 1, oldPrice)], "Espèces", 100m));
        Assert.Empty(store.GetSales());
    }

    [Fact]
    public void CardSaleRecordsExactTotalWithoutCashChange()
    {
        var p = Product(); var sale = store.Checkout([new(p.Id, 1, p.Price)], "Carte (terminal externe)", 0m);
        Assert.Equal(sale.Total, sale.Tendered); Assert.Equal(0m, sale.Change);
    }

    [Fact]
    public void DuplicateCartRowsCannotBypassStockLimit()
    {
        var p = Product(stock: 3);
        Assert.Throws<InvalidOperationException>(() => store.Checkout([new(p.Id, 2, p.Price), new(p.Id, 2, p.Price)], "Espèces", 100m));
        Assert.Equal(3, store.GetProducts().Single().Stock);
    }

    [Fact]
    public void DuplicateBarcodeAndFractionalCentsAreRejected()
    {
        Product();
        Assert.Throws<InvalidOperationException>(() => Product());
        Assert.Throws<InvalidOperationException>(() => Product("002", 1.001m));
        Assert.Single(store.GetProducts());
    }

    [Fact]
    public void EmptyAndNegativeQuantityCartsAreRejected()
    {
        var p = Product();
        Assert.Throws<InvalidOperationException>(() => store.Checkout([], "Espèces", 100m));
        Assert.Throws<InvalidOperationException>(() => store.Checkout([new(p.Id, -1, p.Price)], "Espèces", 100m));
        Assert.Empty(store.GetSales());
    }

    [Fact]
    public void BackupCanBeOpenedWithSalesAndStockIntact()
    {
        var p = Product(); store.Checkout([new(p.Id, 1, p.Price)], "Espèces", 20m);
        var backupPath = Path.Combine(directory, "backup.db"); store.Backup(backupPath);
        var restored = new SqliteStore(backupPath); restored.Initialize();
        Assert.Equal(9, restored.GetProducts().Single().Stock); Assert.Single(restored.GetSales());
        Assert.Throws<InvalidOperationException>(() => store.Backup(Path.Combine(directory, "test.db")));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(directory, true);
    }
}
