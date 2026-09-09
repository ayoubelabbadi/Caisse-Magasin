using Caisse.Domain;
using Caisse.Infrastructure;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Caisse.Tests;

public sealed class SecurityRefundTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "CaisseSecurity", Guid.NewGuid().ToString("N"));
    private readonly SqliteStore store;
    private readonly int managerId, cashierId;
    public SecurityRefundTests()
    {
        store = new SqliteStore(Path.Combine(directory, "test.db")); store.Initialize();
        store.CreateInitialManager("Manager", "918273"); managerId = store.CurrentUser!.Id;
        store.SaveUser("Caissier", "123987", false); cashierId = store.GetUsers().Single(u => !u.IsManager).Id;
    }
    private Product Product(decimal price = 10m, int stock = 20) => store.SaveProduct(new Product { Name = "Produit", Barcode = Guid.NewGuid().ToString("N"), Price = price, Stock = stock });
    [Fact]
    public void AuthenticationCannotBeReplacedByAClaimedName()
    {
        store.Logout(); Assert.Throws<InvalidOperationException>(() => store.OpenSession("Manager", 10));
        Assert.Throws<InvalidOperationException>(() => store.Login(cashierId, "000000")); Assert.Null(store.CurrentUser);
        store.Login(cashierId, "123987");
        var shift = store.OpenSession("Caisse 02", 30);
        Assert.Equal("Caissier", shift.Operator); Assert.Equal(cashierId, shift.UserId); Assert.Equal("Caisse 02", shift.Register);
        store.Logout(); store.Login(managerId, "918273");
        Assert.Throws<InvalidOperationException>(() => store.AddCashMovement(shift.Id, 1, "Autre compte"));
        store.CloseSession(shift.Id, 30);
    }
    [Fact]
    public void RestartRequiresPinAndResumesTheSameShift()
    {
        var product = Product(); store.Login(cashierId, "123987"); var shift = store.OpenSession("Caisse 01", 100);
        var restarted = new SqliteStore(Path.Combine(directory, "test.db")); restarted.Initialize();
        Assert.Null(restarted.CurrentUser);
        Assert.Throws<InvalidOperationException>(() => restarted.Checkout([new(product.Id, 1, product.Price)], "Espèces", 10));
        restarted.Login(cashierId, "123987");
        var sale = restarted.Checkout([new(product.Id, 1, product.Price)], "Espèces", 10);
        Assert.Equal(shift.Id, sale.CashSessionId); Assert.Single(restarted.GetSessions());
    }
    [Fact]
    public void CashierCannotChangeAccountsPolicyOrCatalog()
    {
        store.Login(cashierId, "123987");
        Assert.Throws<InvalidOperationException>(() => store.SaveUser("Intrus", "987654", true));
        Assert.Throws<InvalidOperationException>(() => store.SaveUser("Employé interdit", "987654", false));
        Assert.Throws<InvalidOperationException>(() => store.ResetPin(managerId, "987654"));
        Assert.Throws<InvalidOperationException>(() => store.SetRefundLimit(999999));
        Assert.Throws<InvalidOperationException>(() => Product());
        Assert.Throws<InvalidOperationException>(() => store.GetAudit());
        Assert.Throws<InvalidOperationException>(() => store.CreateInitialManager("Intrus", "987654"));
    }
    [Fact]
    public void PinHashesAreSaltedAndAttemptsLockTheAccount()
    {
        using (var db = new StoreDb(Path.Combine(directory, "test.db"))) {
            Assert.All(db.Users.ToList(), u => { Assert.NotEqual("123987", u.PinHash); Assert.True(u.Salt.Length > 10); });
        }
        for (var i = 0; i < 5; i++) Assert.Throws<InvalidOperationException>(() => store.Login(cashierId, "000000"));
        Assert.Throws<InvalidOperationException>(() => store.Login(cashierId, "123987"));
        store.ResetPin(cashierId, "123987"); Assert.Equal(cashierId, store.Login(cashierId, "123987").Id);
    }
    [Fact]
    public void PartialRefundsRequireManagerAtCumulativeThresholdAndPreserveDetails()
    {
        var product = Product(); store.SetRefundLimit(10);
        store.Login(cashierId, "123987"); var shift = store.OpenSession("Caisse 01", 100);
        var sale = store.Checkout([new(product.Id, 3, 10)], "Espèces", 30);
        var line = sale.Lines.Single();
        var first = store.RefundPartial(sale.Id, [new(line.Id, 1)], "Produit retourné", "", true);
        Assert.Equal(10, first.Amount); Assert.Null(first.ApprovedById);
        Assert.Throws<InvalidOperationException>(() => store.RefundPartial(sale.Id, [new(line.Id, 1)], "Produit retourné", "", true));
        Assert.Throws<InvalidOperationException>(() => store.RefundPartial(sale.Id, [new(line.Id, 1)], "Produit retourné", "", true, managerId, "000000"));
        Assert.Single(store.GetRefunds());
        var approved = store.RefundPartial(sale.Id, [new(line.Id, 1)], "Produit défectueux", "", false, managerId, "918273");
        Assert.Equal(cashierId, approved.RequestedById); Assert.Equal(managerId, approved.ApprovedById); Assert.Equal(shift.Id, approved.CashSessionId);
        Assert.Equal("Caisse 01", approved.Register); Assert.Equal("Manager", approved.ApprovedBy); Assert.Equal("Produit", approved.Lines.Single().ProductName);
        Assert.False(approved.Restock); Assert.Equal(18, store.GetProducts().Single().Stock);
        Assert.Throws<InvalidOperationException>(() => store.RefundPartial(sale.Id, [new(line.Id, 2)], "Produit retourné", "", true, managerId, "918273"));
        Assert.Throws<InvalidOperationException>(() => store.RefundPartial(sale.Id, [new(line.Id, 1)], "Autre motif", " ", true, managerId, "918273"));
        store.Login(managerId, "918273");
        Assert.Equal(2, store.GetAudit().Count(a => a.Action == "Remboursement"));
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "test.db") }.ToString()); connection.Open();
        foreach (var sql in new[] { "DELETE FROM Refunds", "UPDATE RefundLines SET Quantity=99", "DELETE FROM Audit" }) { using var command = connection.CreateCommand(); command.CommandText = sql; Assert.Throws<SqliteException>(() => command.ExecuteNonQuery()); }
    }
    [Fact]
    public void RoundingAcrossPartialReturnsNeverExceedsTheDiscountedSale()
    {
        var a = Product(.05m); var b = Product(.07m); store.OpenSession("Caisse 01", 100);
        var sale = store.Checkout([new(a.Id, 3, a.Price), new(b.Id, 2, b.Price)], "Espèces", 1, .08m);
        var all = new List<Refund>();
        foreach (var line in sale.Lines) for (var i = 0; i < line.Quantity; i++) all.Add(store.RefundPartial(sale.Id, [new(line.Id, 1)], "Erreur de prix", "", true));
        Assert.Equal(sale.Total, all.Sum(r => r.Amount)); Assert.Equal(5, all.Sum(r => r.Lines.Sum(l => l.Quantity)));
        Assert.All(store.GetProducts(), p => Assert.Equal(20, p.Stock));
        Assert.Throws<InvalidOperationException>(() => store.RefundPartial(sale.Id, [new(sale.Lines[0].Id, 1)], "Produit retourné", "", true));
    }
    [Fact]
    public void V2UpgradePreservesOldSessionAndRequiresManagerClosure()
    {
        var legacy = Path.Combine(directory, "legacy-v2.db");
        using (var connection = new SqliteConnection($"Data Source={legacy}")) {
            connection.Open(); using var command = connection.CreateCommand(); command.CommandText = """
                CREATE TABLE Products(Id INTEGER PRIMARY KEY,Barcode TEXT NOT NULL,Name TEXT NOT NULL,Category TEXT NOT NULL,Price INTEGER NOT NULL,Stock INTEGER NOT NULL);
                CREATE TABLE Sales(Id INTEGER PRIMARY KEY,Number TEXT NOT NULL,CreatedAt TEXT NOT NULL,PaymentMethod TEXT NOT NULL,Total INTEGER NOT NULL,Tendered INTEGER NOT NULL,Change INTEGER NOT NULL,Discount INTEGER NOT NULL,CashSessionId INTEGER NULL);
                CREATE TABLE SaleLine(Id INTEGER PRIMARY KEY,SaleId INTEGER NOT NULL,ProductId INTEGER NOT NULL,ProductName TEXT NOT NULL,Barcode TEXT NOT NULL,UnitPrice INTEGER NOT NULL,Quantity INTEGER NOT NULL);
                CREATE TABLE StockMovements(Id INTEGER PRIMARY KEY,ProductId INTEGER NOT NULL,Quantity INTEGER NOT NULL,Reason TEXT NOT NULL,CreatedAt TEXT NOT NULL);
                CREATE TABLE CashSessions(Id INTEGER PRIMARY KEY,Operator TEXT NOT NULL,OpenedAt TEXT NOT NULL,ClosedAt TEXT NULL,OpeningAmount INTEGER NOT NULL,CountedAmount INTEGER NULL,ExpectedAtClose INTEGER NULL);
                INSERT INTO CashSessions VALUES(1,'Ancien caissier','2026-09-01 10:00:00',NULL,10000,NULL,NULL);
                PRAGMA user_version=2;
                """; command.ExecuteNonQuery();
        }
        var upgraded = new SqliteStore(legacy); upgraded.Initialize(); upgraded.Initialize();
        Assert.Single(Directory.GetFiles(Path.Combine(directory, "backups"), "avant-mise-a-jour-*.db"));
        upgraded.CreateInitialManager("Responsable", "987321");
        var session = upgraded.GetSessions().Single(); Assert.Equal("Ancien caissier", session.Operator); Assert.Null(session.UserId);
        Assert.Throws<InvalidOperationException>(() => upgraded.AddCashMovement(session.Id, 1, "Bloqué"));
        upgraded.CloseSession(session.Id, 100);
        Assert.Equal("Responsable", upgraded.OpenSession("Caisse 01", 100).Operator);
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); Directory.Delete(directory, true); }
}
