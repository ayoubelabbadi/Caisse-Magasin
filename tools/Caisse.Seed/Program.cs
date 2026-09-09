using Caisse.Domain;
using Caisse.Infrastructure;

var directory = Environment.GetEnvironmentVariable("CAISSE_DATA_DIR")
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CaisseMagasin");
var path = Path.Combine(directory, "caisse.db");
if (!File.Exists(path)) throw new InvalidOperationException("Base de caisse introuvable : " + path);
var store = new SqliteStore(path);
Product[] examples = [
    new() { Barcode = "EXEMPLE101", Name = "Sucre en poudre · 1 kg", Category = "Épicerie", Price = 12m, Stock = 40 },
    new() { Barcode = "EXEMPLE102", Name = "Farine de blé · 1 kg", Category = "Épicerie", Price = 9m, Stock = 30 },
    new() { Barcode = "EXEMPLE103", Name = "Pâtes spaghetti · 500 g", Category = "Épicerie", Price = 8.50m, Stock = 36 },
    new() { Barcode = "EXEMPLE104", Name = "Lentilles · 1 kg", Category = "Épicerie", Price = 24m, Stock = 20 },
    new() { Barcode = "EXEMPLE105", Name = "Pois chiches · 1 kg", Category = "Épicerie", Price = 22m, Stock = 20 },
    new() { Barcode = "EXEMPLE106", Name = "Thé vert · 200 g", Category = "Épicerie", Price = 18m, Stock = 24 },
    new() { Barcode = "EXEMPLE107", Name = "Sel fin · 1 kg", Category = "Épicerie", Price = 4m, Stock = 30 },
    new() { Barcode = "EXEMPLE108", Name = "Concentré de tomate · 140 g", Category = "Conserves", Price = 6.50m, Stock = 48 },
    new() { Barcode = "EXEMPLE109", Name = "Sardines en conserve · 125 g", Category = "Conserves", Price = 7m, Stock = 48 },
    new() { Barcode = "EXEMPLE110", Name = "Thon en conserve · 160 g", Category = "Conserves", Price = 14m, Stock = 24 },
    new() { Barcode = "EXEMPLE111", Name = "Biscuits au chocolat · 150 g", Category = "Biscuits & confiserie", Price = 10m, Stock = 30 },
    new() { Barcode = "EXEMPLE112", Name = "Chocolat au lait · 100 g", Category = "Biscuits & confiserie", Price = 12m, Stock = 24 },
    new() { Barcode = "EXEMPLE113", Name = "Soda cola · 1 L", Category = "Boissons", Price = 10m, Stock = 36 },
    new() { Barcode = "EXEMPLE114", Name = "Eau gazeuse · 1 L", Category = "Boissons", Price = 8m, Stock = 24 },
    new() { Barcode = "EXEMPLE115", Name = "Savon de toilette · 100 g", Category = "Hygiène", Price = 5m, Stock = 40 },
    new() { Barcode = "EXEMPLE116", Name = "Dentifrice · 75 ml", Category = "Hygiène", Price = 15m, Stock = 20 },
    new() { Barcode = "EXEMPLE117", Name = "Shampooing · 250 ml", Category = "Hygiène", Price = 25m, Stock = 18 },
    new() { Barcode = "EXEMPLE118", Name = "Liquide vaisselle · 750 ml", Category = "Entretien", Price = 13m, Stock = 24 },
    new() { Barcode = "EXEMPLE119", Name = "Lessive en poudre · 1 kg", Category = "Entretien", Price = 28m, Stock = 16 },
    new() { Barcode = "EXEMPLE120", Name = "Mouchoirs · boîte de 100", Category = "Hygiène", Price = 9m, Stock = 30 }
];
Console.WriteLine("Base : " + path);
var existing = store.GetProducts();
Console.WriteLine($"Produits existants : {existing.Count}");
foreach (var p in existing) Console.WriteLine($"  {p.Barcode} | {p.Name} | stock {p.Stock}");
if (!args.Contains("--apply")) return;
var backupDirectory = Path.GetFullPath(Path.Combine("artifacts", "stock-backups"));
Directory.CreateDirectory(backupDirectory);
var backup = Path.Combine(backupDirectory, $"avant-ajout-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.db");
store.Backup(backup);
using var db = new StoreDb(path);
using var transaction = db.Database.BeginTransaction();
var current = db.Products.ToList();
var additions = examples.Where(p => !current.Any(e => e.Barcode.Equals(p.Barcode, StringComparison.OrdinalIgnoreCase) || e.Name.Equals(p.Name, StringComparison.OrdinalIgnoreCase))).ToList();
db.Products.AddRange(additions);
db.SaveChanges();
db.StockMovements.AddRange(additions.Select(p => new StockMovement { ProductId = p.Id, Quantity = p.Stock, Reason = "Ajout de produits d’exemple — prix et quantités fictifs", CreatedAt = DateTime.UtcNow }));
db.SaveChanges();
transaction.Commit();
var verified = store.GetProducts();
if (verified.Count != current.Count + additions.Count || additions.Any(p => !verified.Any(v => v.Id == p.Id && v.Stock == p.Stock && v.Price == p.Price))) throw new InvalidOperationException("La vérification après ajout a échoué.");
Console.WriteLine($"Vérifié : {additions.Count} produits ajoutés, {additions.Sum(p => p.Stock)} unités ; {verified.Count} produits au total.");
Console.WriteLine("Sauvegarde : " + backup);
