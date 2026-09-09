using Caisse.Domain;
using Caisse.Infrastructure;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Caisse.Tests;

public sealed class RetailTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "CaisseTests", Guid.NewGuid().ToString("N"));
    private readonly SqliteStore store;
    public RetailTests() { store = new SqliteStore(Path.Combine(directory, "test.db")); store.Initialize(); store.CreateInitialManager("Amina", "123456"); }
    private Product Product() => store.SaveProduct(new Product { Name = "Café", Barcode = "001", Price = 10.25m, Stock = 10 });

    [Fact]
    public void SaleRequiresAnOpenSession()
    {
        var p = Product();
        Assert.Throws<InvalidOperationException>(() => store.Checkout([new(p.Id, 1, p.Price)], "Espèces", 20m));
        Assert.Empty(store.GetSales()); Assert.Equal(10, store.GetProducts().Single().Stock);
    }

    [Fact]
    public void OnlyOneSessionCanBeOpenAndClosedSessionCannotBeReused()
    {
        var session = store.OpenSession("Amina", 100);
        Assert.Throws<InvalidOperationException>(() => store.OpenSession("Ali", 50));
        store.CloseSession(session.Id, 100);
        Assert.Throws<InvalidOperationException>(() => store.CloseSession(session.Id, 100));
        Assert.Throws<InvalidOperationException>(() => store.AddCashMovement(session.Id, 10, "Trop tard"));
        Assert.NotEqual(session.Id, store.OpenSession("Ali", 100).Id);
    }

    [Fact]
    public void DiscountIsPersistedAndCashUsesNetTotalNotTendered()
    {
        var session = store.OpenSession("Amina", 100); var p = Product();
        var sale = store.Checkout([new(p.Id, 2, p.Price)], "Espèces", 50, 2.05m);
        Assert.Equal(18.45m, sale.Total); Assert.Equal(31.55m, sale.Change);
        Assert.Equal(2.05m, store.GetSales().Single().Discount);
        Assert.Equal(118.45m, store.GetExpectedCash(session.Id));
    }

    [Fact]
    public void ExcessiveDiscountRollsBackStock()
    {
        store.OpenSession("A", 0); var p = Product();
        Assert.Throws<InvalidOperationException>(() => store.Checkout([new(p.Id, 1, p.Price)], "Espèces", 20, 11));
        Assert.Equal(10, store.GetProducts().Single().Stock); Assert.Empty(store.GetSales());
    }

    [Fact]
    public void SessionReconcilesSalesMovementsRefundsAndExcludesCards()
    {
        var session = store.OpenSession("Amina", 100); var p = Product();
        var cash = store.Checkout([new(p.Id, 2, p.Price)], "Espèces", 50, 2.05m);
        store.Checkout([new(p.Id, 1, p.Price)], "Carte (terminal externe)", 0);
        store.AddCashMovement(session.Id, 20, "Appoint"); store.AddCashMovement(session.Id, -10, "Dépôt coffre");
        store.RefundSale(cash.Id, "Retour client", true);
        var closed = store.CloseSession(session.Id, 108);
        Assert.Equal(110m, closed.ExpectedAtClose); Assert.Equal(-2m, closed.Difference);
        Assert.Equal(110m, store.GetExpectedCash(session.Id));
        Assert.Equal(9, store.GetProducts().Single().Stock);
    }

    [Fact]
    public void RefundCannotBeRepeatedAndKeepsOriginalTicket()
    {
        store.OpenSession("A", 0); var p = Product();
        var sale = store.Checkout([new(p.Id, 3, p.Price)], "Espèces", 40, 3m);
        var refund = store.RefundSale(sale.Id, "Retour", true);
        Assert.Equal(27.75m, refund.Amount); Assert.Equal(10, store.GetProducts().Single().Stock);
        Assert.Throws<InvalidOperationException>(() => store.RefundSale(sale.Id, "Encore", true));
        Assert.Single(store.GetRefunds()); Assert.Single(store.GetSales());
        Assert.Single(store.GetStockMovements(), m => m.Reason == "Retour " + sale.Number);
    }

    [Fact]
    public void DamagedReturnDoesNotRestock()
    {
        store.OpenSession("A", 0); var p = Product();
        var sale = store.Checkout([new(p.Id, 1, p.Price)], "Espèces", 20);
        store.RefundSale(sale.Id, "Produit endommagé", false);
        Assert.Equal(9, store.GetProducts().Single().Stock);
    }

    [Fact]
    public void RefundOfOldSaleBelongsToCurrentSession()
    {
        var first = store.OpenSession("A", 0); var p = Product();
        var sale = store.Checkout([new(p.Id, 1, p.Price)], "Espèces", 20);
        store.CloseSession(first.Id, sale.Total);
        var next = store.OpenSession("B", 100);
        var refund = store.RefundSale(sale.Id, "Retour lendemain", true);
        Assert.Equal(next.Id, refund.CashSessionId);
        Assert.Equal(89.75m, store.GetExpectedCash(next.Id));
        Assert.Equal(10.25m, store.GetExpectedCash(first.Id));
    }

    [Fact]
    public void CashOutAndRefundCannotExceedAvailableCash()
    {
        var session = store.OpenSession("A", 0); var p = Product();
        var sale = store.Checkout([new(p.Id, 1, p.Price)], "Espèces", 20);
        store.AddCashMovement(session.Id, -sale.Total, "Dépôt coffre");
        Assert.Throws<InvalidOperationException>(() => store.AddCashMovement(session.Id, -1, "Sortie"));
        Assert.Throws<InvalidOperationException>(() => store.RefundSale(sale.Id, "Retour", true));
        Assert.Empty(store.GetRefunds()); Assert.Equal(9, store.GetProducts().Single().Stock);
    }

    [Fact]
    public void ReasonsAndValidOpeningAmountsAreRequired()
    {
        Assert.Throws<InvalidOperationException>(() => store.OpenSession("", 100));
        Assert.Throws<InvalidOperationException>(() => store.OpenSession("A", -1));
        var session = store.OpenSession("A", 100); var p = Product();
        var sale = store.Checkout([new(p.Id, 1, p.Price)], "Espèces", 20);
        Assert.Throws<InvalidOperationException>(() => store.AddCashMovement(session.Id, 5, ""));
        Assert.Throws<InvalidOperationException>(() => store.RefundSale(sale.Id, "", true));
        Assert.Throws<InvalidOperationException>(() => store.CloseSession(session.Id, 1.001m));
    }

    [Fact]
    public void UpgradePreservesLegacySalesAndCreatesBackupOnce()
    {
        var legacyPath = Path.Combine(directory, "legacy.db");
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = legacyPath }.ToString()))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE Products (Id INTEGER PRIMARY KEY, Barcode TEXT NOT NULL, Name TEXT NOT NULL, Category TEXT NOT NULL, Price INTEGER NOT NULL, Stock INTEGER NOT NULL);
                CREATE TABLE Sales (Id INTEGER PRIMARY KEY, Number TEXT NOT NULL, CreatedAt TEXT NOT NULL, PaymentMethod TEXT NOT NULL, Total INTEGER NOT NULL, Tendered INTEGER NOT NULL, Change INTEGER NOT NULL);
                CREATE TABLE SaleLine (Id INTEGER PRIMARY KEY, SaleId INTEGER NOT NULL, ProductId INTEGER NOT NULL, ProductName TEXT NOT NULL, Barcode TEXT NOT NULL, UnitPrice INTEGER NOT NULL, Quantity INTEGER NOT NULL);
                CREATE TABLE StockMovements (Id INTEGER PRIMARY KEY, ProductId INTEGER NOT NULL, Quantity INTEGER NOT NULL, Reason TEXT NOT NULL, CreatedAt TEXT NOT NULL);
                INSERT INTO Products VALUES (1, '001', 'Ancien produit', 'Divers', 1235, 9);
                INSERT INTO Sales VALUES (1, 'V-ANCIENNE', '2026-09-01 10:00:00', 'Espèces', 1235, 2000, 765);
                INSERT INTO SaleLine VALUES (1, 1, 1, 'Ancien produit', '001', 1235, 1);
                """;
            command.ExecuteNonQuery();
        }
        var upgraded = new SqliteStore(legacyPath); upgraded.Initialize(); upgraded.Initialize();
        var sale = upgraded.GetSales().Single();
        Assert.Equal("V-ANCIENNE", sale.Number); Assert.Equal(12.35m, sale.Total); Assert.Equal(0m, sale.Discount); Assert.Null(sale.CashSessionId);
        Assert.Equal(12.35m, sale.Lines.Single().UnitPrice); Assert.Equal(9, upgraded.GetProducts().Single().Stock);
        Assert.Single(Directory.GetFiles(Path.Combine(directory, "backups"), "avant-mise-a-jour-*.db"));
        upgraded.CreateInitialManager("Manager test", "123456"); upgraded.OpenSession("Après mise à jour", 100); upgraded.RefundSale(sale.Id, "Ancien ticket", true);
        Assert.Equal(10, upgraded.GetProducts().Single().Stock);
    }

    [Fact]
    public void BackupIncludesSessionsDiscountsAndRefunds()
    {
        var session = store.OpenSession("A", 100); var p = Product();
        var sale = store.Checkout([new(p.Id, 1, p.Price)], "Espèces", 20, 1m);
        store.RefundSale(sale.Id, "Retour", true); store.CloseSession(session.Id, 100);
        var backup = Path.Combine(directory, "copy.db"); store.Backup(backup);
        var restored = new SqliteStore(backup); restored.Initialize();
        Assert.Single(restored.GetRefunds()); Assert.Equal(1m, restored.GetSales().Single().Discount);
        Assert.Equal(100m, restored.GetSessions().Single().ExpectedAtClose);
    }

    public void Dispose() { SqliteConnection.ClearAllPools(); Directory.Delete(directory, true); }
}
