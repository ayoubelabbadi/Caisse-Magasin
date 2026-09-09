using Caisse.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Caisse.Infrastructure;

public sealed class StoreDb(string path) : DbContext
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<CashSession> CashSessions => Set<CashSession>();
    public DbSet<CashMovement> CashMovements => Set<CashMovement>();
    public DbSet<Refund> Refunds => Set<Refund>();
    public DbSet<UserAccount> Users => Set<UserAccount>();
    public DbSet<StorePolicy> Policies => Set<StorePolicy>();
    public DbSet<AuditEntry> Audit => Set<AuditEntry>();
    public DbSet<RefundLine> RefundLines => Set<RefundLine>();

    protected override void OnConfiguring(DbContextOptionsBuilder options) =>
        options.UseSqlite(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = path }.ToString());

    protected override void OnModelCreating(ModelBuilder model)
    {
        var cents = new ValueConverter<decimal, long>(v => (long)(v * 100m), v => v / 100m);
        model.Entity<Product>().HasIndex(p => p.Barcode).IsUnique();
        model.Entity<Product>().Property(p => p.Price).HasConversion(cents);
        model.Entity<Sale>().HasIndex(s => s.Number).IsUnique();
        model.Entity<Sale>().Property(s => s.Total).HasConversion(cents);
        model.Entity<Sale>().Property(s => s.Tendered).HasConversion(cents);
        model.Entity<Sale>().Property(s => s.Change).HasConversion(cents);
        model.Entity<Sale>().Property(s => s.Discount).HasConversion(cents);
        model.Entity<CashSession>().Property(s => s.OpeningAmount).HasConversion(cents);
        model.Entity<CashSession>().Property(s => s.CountedAmount).HasConversion(cents);
        model.Entity<CashSession>().Property(s => s.ExpectedAtClose).HasConversion(cents);
        model.Entity<CashSession>().Ignore(s => s.Difference);
        model.Entity<CashSession>().Ignore(s => s.State);
        model.Entity<CashMovement>().Property(s => s.Amount).HasConversion(cents);
        model.Entity<Refund>().Property(s => s.Amount).HasConversion(cents);
        model.Entity<Refund>().HasIndex(s => s.SaleId);
        model.Entity<Refund>().HasMany(s => s.Lines).WithOne().HasForeignKey(l => l.RefundId);
        model.Entity<RefundLine>().Property(s => s.Amount).HasConversion(cents);
        model.Entity<StorePolicy>().Property(s => s.RefundLimit).HasConversion(cents);
        model.Entity<UserAccount>().HasIndex(s => s.Name).IsUnique();
        model.Entity<SaleLine>().Property(s => s.UnitPrice).HasConversion(cents);
        model.Entity<SaleLine>().Ignore(s => s.Total);
        model.Entity<Sale>().HasMany(s => s.Lines).WithOne().HasForeignKey(l => l.SaleId);
    }
}
