using Caisse.Application;
using Caisse.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Caisse.Infrastructure;

public sealed partial class SqliteStore(string path) : IStore
{
    public void Initialize()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var db = new StoreDb(path);
        db.Database.EnsureCreated();
        UpgradeSchema();
    }

    public List<Product> GetProducts()
    {
        using var db = new StoreDb(path);
        return db.Products.AsNoTracking().OrderBy(p => p.Name).ToList();
    }

    public Product SaveProduct(Product input)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.Barcode))
            throw new InvalidOperationException("Le nom et le code-barres sont obligatoires.");
        Money.Validate(input.Price, "Prix");
        if (input.Stock < 0) throw new InvalidOperationException("Le stock ne peut pas être négatif.");
        using var db = new StoreDb(path);
        using var transaction = db.Database.BeginTransaction();
        RequireUser(db, true);
        var barcode = input.Barcode.Trim();
        if (db.Products.Any(p => p.Barcode == barcode && p.Id != input.Id))
            throw new InvalidOperationException("Ce code-barres appartient déjà à un produit.");
        var product = input.Id == 0 ? new Product() : db.Products.Single(p => p.Id == input.Id);
        var difference = input.Stock - product.Stock;
        product.Name = input.Name.Trim();
        product.Barcode = barcode;
        product.Category = string.IsNullOrWhiteSpace(input.Category) ? "Divers" : input.Category.Trim();
        product.Price = input.Price;
        product.Stock = input.Stock;
        if (input.Id == 0) db.Products.Add(product);
        db.SaveChanges();
        if (difference != 0)
            db.StockMovements.Add(new StockMovement { ProductId = product.Id, Quantity = difference, Reason = "Ajustement catalogue", CreatedAt = DateTime.UtcNow });
        db.SaveChanges();
        Audit(db, "Catalogue / stock", System.Text.Json.JsonSerializer.Serialize(new { product.Id, product.Name, product.Price, product.Stock, Adjustment = difference })); db.SaveChanges();
        transaction.Commit();
        return product;
    }

    public Sale Checkout(IReadOnlyList<CartRequest> cart, string paymentMethod, decimal tendered, decimal discount = 0)
    {
        if (cart.Count == 0) throw new InvalidOperationException("Le panier est vide.");
        if (paymentMethod is not ("Espèces" or "Carte (terminal externe)"))
            throw new InvalidOperationException("Mode de paiement inconnu.");
        Money.Validate(tendered, "Montant reçu");
        Money.Validate(discount, "Remise");
        using var db = new StoreDb(path);
        using var transaction = db.Database.BeginTransaction();
        var session = RequireOpenSession(db);
        var sale = new Sale { Number = $"V-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}", CreatedAt = DateTime.UtcNow, PaymentMethod = paymentMethod, CashSessionId = session.Id, Discount = discount };
        foreach (var group in cart.GroupBy(c => c.ProductId))
        {
            if (group.Any(c => c.Quantity <= 0)) throw new InvalidOperationException("Quantité invalide.");
            var quantity = group.Sum(c => c.Quantity);
            var product = db.Products.SingleOrDefault(p => p.Id == group.Key)
                ?? throw new InvalidOperationException("Un produit n'existe plus.");
            if (group.Any(c => c.ExpectedPrice != product.Price))
                throw new InvalidOperationException($"Le prix de {product.Name} a changé. Retirez-le puis ajoutez-le au panier.");
            if (quantity > product.Stock) throw new InvalidOperationException($"Stock insuffisant : {product.Name} ({product.Stock} disponible).");
            product.Stock -= quantity;
            sale.Lines.Add(new SaleLine { ProductId = product.Id, ProductName = product.Name, Barcode = product.Barcode, UnitPrice = product.Price, Quantity = quantity });
            db.StockMovements.Add(new StockMovement { ProductId = product.Id, Quantity = -quantity, Reason = sale.Number, CreatedAt = sale.CreatedAt });
        }
        var subtotal = sale.Lines.Sum(l => l.Total);
        if (discount > subtotal) throw new InvalidOperationException("La remise dépasse le montant du panier.");
        sale.Total = subtotal - discount;
        Money.Validate(sale.Total, "Total");
        if (paymentMethod == "Espèces" && tendered < sale.Total)
            throw new InvalidOperationException("Le montant reçu est inférieur au total.");
        sale.Tendered = paymentMethod == "Espèces" ? tendered : sale.Total;
        sale.Change = sale.Tendered - sale.Total;
        db.Sales.Add(sale);
        db.SaveChanges();
        Audit(db, "Vente", System.Text.Json.JsonSerializer.Serialize(new { sale.Number, sale.Total, sale.Discount, sale.PaymentMethod }), session); db.SaveChanges();
        transaction.Commit();
        return sale;
    }

    public List<Sale> GetSales()
    {
        using var db = new StoreDb(path);
        return db.Sales.Include(s => s.Lines).AsNoTracking().OrderByDescending(s => s.CreatedAt).ToList();
    }

    public void Backup(string destination)
    {
        if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Choisissez un fichier différent de la base active.");
        using var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destination }.ToString());
        source.Open();
        target.Open();
        source.BackupDatabase(target);
    }

    public void LoadDemoProducts()
    {
        using (var authorizationDb = new StoreDb(path)) RequireUser(authorizationDb, true);
        if (GetProducts().Count != 0) throw new InvalidOperationException("Les exemples sont réservés à un catalogue vide.");
        Product[] products = [
            new() { Name = "Lait entier · 1 L", Barcode = "DEMO001", Category = "Produits frais", Price = 9.50m, Stock = 40 },
            new() { Name = "Pain de campagne", Barcode = "DEMO002", Category = "Boulangerie", Price = 5m, Stock = 25 },
            new() { Name = "Eau minérale · 1,5 L", Barcode = "DEMO003", Category = "Boissons", Price = 6m, Stock = 60 },
            new() { Name = "Café moulu · 250 g", Barcode = "DEMO004", Category = "Épicerie", Price = 32m, Stock = 18 },
            new() { Name = "Huile d’olive · 1 L", Barcode = "DEMO005", Category = "Épicerie", Price = 85m, Stock = 12 },
            new() { Name = "Riz long · 1 kg", Barcode = "DEMO006", Category = "Épicerie", Price = 18.50m, Stock = 30 },
            new() { Name = "Jus d’orange · 1 L", Barcode = "DEMO007", Category = "Boissons", Price = 16m, Stock = 20 },
            new() { Name = "Yaourt nature · 4 pots", Barcode = "DEMO008", Category = "Produits frais", Price = 12m, Stock = 8 }
        ];
        using var db = new StoreDb(path);
        using var transaction = db.Database.BeginTransaction();
        db.Products.AddRange(products);
        db.SaveChanges();
        db.StockMovements.AddRange(products.Select(p => new StockMovement { ProductId = p.Id, Quantity = p.Stock, Reason = "Stock de démonstration", CreatedAt = DateTime.UtcNow }));
        db.SaveChanges();
        transaction.Commit();
    }
}
