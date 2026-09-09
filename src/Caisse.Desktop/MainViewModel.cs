using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Caisse.Application;
using Caisse.Domain;

namespace Caisse.Desktop;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; Notify(name); return true;
    }
}

public sealed class RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null) : ICommand
{
    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => execute(parameter);
    public event EventHandler? CanExecuteChanged { add => CommandManager.RequerySuggested += value; remove => CommandManager.RequerySuggested -= value; }
}

public sealed class CartItem(Product product) : Observable
{
    public Product Product { get; } = product;
    private int quantity = 1;
    public int Quantity { get => quantity; set { Set(ref quantity, value); Notify(nameof(Total)); } }
    public decimal Total => Product.Price * Quantity;
}

public sealed class MainViewModel : Observable
{
    private readonly IStore store;
    public IStore Store => store;
    public bool IsManager => store.CurrentUser?.IsManager == true;
    public bool IsCashier => store.CurrentUser is { IsManager: false };
    public string WorkspaceTitle => IsManager ? "Administration du magasin" : "Espace caissier";
    public string HistoryTitle => IsManager ? "Toutes les ventes" : "Mes ventes";
    public string ManagementTitle => IsManager ? "Suivi du magasin" : "Mon shift & retours";
    public string AccountLabel => (store.CurrentUser?.Name ?? "Verrouillé") + " · " + Settings.RegisterName;
    public ObservableCollection<AuditEntry> AuditLog { get; } = [];
    public ShopSettings Settings { get; }
    public string DataDirectory { get; }
    public RetailViewModel Retail { get; }
    public DashboardViewModel Dashboard { get; }
    private decimal discountValue;
    private bool discountPercent;
    public decimal Subtotal => Cart.Sum(l => l.Total);
    public decimal DiscountAmount => Math.Min(Subtotal, discountPercent ? decimal.Round(Subtotal * discountValue / 100m, 2, MidpointRounding.AwayFromZero) : discountValue);
    public string DiscountLabel => DiscountAmount == 0 ? "Remise…" : $"Remise : −{Amount(DiscountAmount)}";
    public string TodayLabel => DateTime.Now.ToString("dddd dd MMMM yyyy");
    public ObservableCollection<Product> Products { get; } = [];
    public ObservableCollection<Product> Catalog { get; } = [];
    public ObservableCollection<CartItem> Cart { get; } = [];
    public ObservableCollection<Sale> Sales { get; } = [];
    public ObservableCollection<string> Categories { get; } = ["Tous les produits"];
    public ObservableCollection<string> EditorCategories { get; } = [];
    public string[] PaymentMethods { get; } = ["Espèces", "Carte (terminal externe)"];
    public event Action<Sale>? ReceiptRequested;
    private Dictionary<string, Product> barcodeIndex = new(StringComparer.Ordinal);
    private string barcode = "", scanFeedback = "Prêt à scanner · un passage, un article";
    private bool scanError;
    private int productPage, matchingProducts;
    private const int PageSize = 24;
    public string Barcode { get => barcode; set => Set(ref barcode, value); }
    public string ScanFeedback { get => scanFeedback; private set => Set(ref scanFeedback, value); }
    public bool ScanError { get => scanError; private set => Set(ref scanError, value); }
    private string search = "", category = "Tous les produits", status = "", tendered = "", paymentMethod = "Espèces";
    private Product? selectedProduct;
    private Sale? selectedSale;
    private int editId;
    private string editName = "", editBarcode = "", editCategory = "Épicerie", editPrice = "", editStock = "0";

    public string Search { get => search; set { if (Set(ref search, value)) { productPage = 0; Filter(); } } }
    public string Category { get => category; set { if (Set(ref category, value)) { productPage = 0; Filter(); } } }
    public string Status { get => status; set => Set(ref status, value); }
    public string Tendered { get => tendered; set { Set(ref tendered, value); Notify(nameof(ChangeText)); } }
    public string PaymentMethod { get => paymentMethod; set { Set(ref paymentMethod, value); Notify(nameof(IsCash)); Notify(nameof(IsCard)); Notify(nameof(ChangeText)); } }
    public bool IsCash { get => PaymentMethod == "Espèces"; set { if (value) PaymentMethod = "Espèces"; } }
    public bool IsCard { get => !IsCash; set { if (value) PaymentMethod = "Carte (terminal externe)"; } }
    public Product? SelectedProduct { get => selectedProduct; set { Set(ref selectedProduct, value); if (value is not null) Edit(value); } }
    public Sale? SelectedSale { get => selectedSale; set => Set(ref selectedSale, value); }
    public string EditName { get => editName; set => Set(ref editName, value); }
    public string EditBarcode { get => editBarcode; set => Set(ref editBarcode, value); }
    public string EditCategory { get => editCategory; set => Set(ref editCategory, value); }
    public string EditPrice { get => editPrice; set => Set(ref editPrice, value); }
    public string EditStock { get => editStock; set => Set(ref editStock, value); }
    public string EditorTitle => editId == 0 ? "Nouveau produit" : "Modifier le produit";
    public decimal Total => Subtotal - DiscountAmount;
    public string TotalText => Amount(Total);
    public string CartCount => $"{Cart.Sum(l => l.Quantity)} article(s)";
    public string ChangeText => IsCash && TryMoney(Tendered, out var paid) ? Amount(Math.Max(0, paid - Total)) : Amount(0);
    public string DailyTotal => Amount(Sales.Where(s => s.CreatedAt.ToLocalTime().Date == DateTime.Today).Sum(s => s.Total) - Retail.Refunds.Where(r => r.CreatedAt.ToLocalTime().Date == DateTime.Today).Sum(r => r.Amount));
    public string DailyCount => $"{Sales.Count(s => s.CreatedAt.ToLocalTime().Date == DateTime.Today)} vente(s) aujourd’hui";
    public string StockAlert => $"{Catalog.Count(p => p.Stock <= 5)} produit(s) à réapprovisionner";
    public string ProductCount => matchingProducts == 0 ? "Aucun produit trouvé · essayez un autre nom ou une référence" : $"{matchingProducts} produits · page {productPage + 1} / {Math.Max(1, (matchingProducts + PageSize - 1) / PageSize)}";
    public bool IsCatalogEmpty => Catalog.Count == 0;
    public bool IsCartEmpty => Cart.Count == 0;
    public ICommand AddCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand PlusCommand { get; }
    public ICommand MinusCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand ExactCommand { get; }
    public ICommand QuickTenderCommand { get; }
    public ICommand CheckoutCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand NewCommand { get; }
    public ICommand DemoCommand { get; }
    public ICommand ReceiptCommand { get; }
    public ICommand ScanCommand { get; }
    public ICommand AddSearchResultCommand { get; }
    public ICommand PreviousProductsCommand { get; }
    public ICommand NextProductsCommand { get; }

    public MainViewModel(IStore store, ShopSettings settings, string directory)
    {
        this.store = store; Settings = settings; DataDirectory = directory;
        Retail = new RetailViewModel(store, settings, Refresh);
        Dashboard = new DashboardViewModel(store, settings, () => Run(Refresh));
        AddCommand = new RelayCommand(p => Run(() => Add((Product)p!)), p => p is Product product && product.Stock > 0);
        PlusCommand = new RelayCommand(p => Run(() => Add(((CartItem)p!).Product)));
        MinusCommand = new RelayCommand(p => Run(() => { var item = (CartItem)p!; store.RecordActivity("Quantité diminuée", item.Product.Name + " · avant " + item.Quantity); if (item.Quantity == 1) Cart.Remove(item); else item.Quantity--; CartChanged(); }));
        RemoveCommand = new RelayCommand(p => Run(() => { var item = (CartItem)p!; store.RecordActivity("Retrait article", item.Product.Name + " × " + item.Quantity); Cart.Remove(item); CartChanged(); }));
        ClearCommand = new RelayCommand(_ => Run(CancelCart), _ => Cart.Count > 0);
        ExactCommand = new RelayCommand(_ => Tendered = Total.ToString("0.00"), _ => Cart.Count > 0 && IsCash);
        QuickTenderCommand = new RelayCommand(p => Tendered = Convert.ToString(p, CultureInfo.InvariantCulture) ?? "", _ => Cart.Count > 0 && IsCash);
        CheckoutCommand = new RelayCommand(_ => Run(Checkout), _ => Cart.Count > 0 && Retail.IsOpen);
        SaveCommand = new RelayCommand(_ => Run(Save), _ => IsManager);
        NewCommand = new RelayCommand(_ => NewProduct());
        DemoCommand = new RelayCommand(_ => Run(() => { store.LoadDemoProducts(); Refresh(); Status = "Catalogue de démonstration chargé. Les ventes seront enregistrées dans cette base."; }), _ => IsManager && IsCatalogEmpty);
        ReceiptCommand = new RelayCommand(_ => { if (SelectedSale is not null) ReceiptRequested?.Invoke(SelectedSale); }, _ => SelectedSale is not null);
        ScanCommand = new RelayCommand(_ => Scan());
        AddSearchResultCommand = new RelayCommand(_ => Run(AddSearchResult));
        PreviousProductsCommand = new RelayCommand(_ => { productPage--; Filter(); }, _ => productPage > 0);
        NextProductsCommand = new RelayCommand(_ => { productPage++; Filter(); }, _ => (productPage + 1) * PageSize < matchingProducts);
        Refresh();
    }

    private void AddSearchResult()
    {
        var term = Search.Trim();
        if (term.Length == 0) { Status = "Saisissez le nom ou la référence du produit."; return; }
        var matches = Catalog.Where(p => (Category == "Tous les produits" || p.Category == Category) &&
            (p.Name.Contains(term, StringComparison.OrdinalIgnoreCase) || p.Barcode.Contains(term, StringComparison.OrdinalIgnoreCase))).ToList();
        var exact = matches.Where(p => p.Barcode.Equals(term, StringComparison.Ordinal)).ToList();
        if (exact.Count == 0) exact = matches.Where(p => p.Name.Equals(term, StringComparison.OrdinalIgnoreCase)).ToList();
        var product = exact.Count == 1 ? exact[0] : matches.Count == 1 ? matches[0] : null;
        if (product is null) { Status = matches.Count == 0 ? "Aucun produit trouvé." : "Plusieurs produits correspondent : touchez le produit à ajouter au panier."; return; }
        Add(product);
    }

    private void Scan()
    {
        var code = Barcode.Trim();
        Barcode = "";
        if (code.Length == 0) return;
        try
        {
            if (!barcodeIndex.TryGetValue(code, out var product))
                throw new InvalidOperationException($"Code {code} inconnu · rescanner ou rechercher le produit (F3).");
            Add(product);
            ScanError = false;
            ScanFeedback = $"✓ {product.Name} · {Amount(product.Price)} · quantité {Cart.First(l => l.Product.Id == product.Id).Quantity}";
        }
        catch (InvalidOperationException ex)
        {
            ScanError = true;
            ScanFeedback = ex.Message;
            Status = ex.Message;
        }
    }

    public string Amount(decimal value) => $"{value:N2} {Settings.Currency}";
    public void CancelCart()
    {
        if (Cart.Count > 0) store.RecordActivity("Annulation panier", System.Text.Json.JsonSerializer.Serialize(new { Total, DiscountAmount, Items = Cart.Select(i => new { i.Product.Name, i.Quantity, i.Total }) }));
        Cart.Clear(); discountValue = 0; Tendered = ""; CartChanged();
    }
    public void ApplyDiscount(decimal value, bool percent)
    {
        Money.Validate(value, "Remise");
        if (percent && value > 100) throw new InvalidOperationException("La remise doit être comprise entre 0 et 100 %.");
        if (!percent && value > Subtotal) throw new InvalidOperationException("La remise dépasse le panier.");
        store.RecordActivity("Remise panier", $"Valeur {value} · pourcentage {percent} · sous-total {Subtotal}");
        discountValue = value; discountPercent = percent; CartChanged();
        Status = DiscountAmount == 0 ? "Remise supprimée." : "Remise appliquée : " + Amount(DiscountAmount);
    }
    public void Run(Action action)
    {
        try { action(); }
        catch (Exception ex) { Status = ex is InvalidOperationException or FormatException or OverflowException ? ex.Message : "L’opération a échoué : " + ex.GetBaseException().Message; }
    }
    private void RequireAdministrator() { if (!IsManager) throw new InvalidOperationException("Accès réservé à l’administrateur."); }
    public void Backup(string path) => Run(() => { RequireAdministrator(); store.Backup(path); Status = "Copie de restauration enregistrée. Conservez-la sur un support distinct."; });
    public void ExportData(string path) => Run(() => { RequireAdministrator(); StoreExport.Write(store, Settings, path); Status = "Classeur Excel enregistré : stock, ventes, caissiers et sessions."; });

    private void Add(Product product)
    {
        if (!Retail.IsOpen) throw new InvalidOperationException("Démarrez votre shift dans Gestion avant de remplir le panier.");
        var item = Cart.FirstOrDefault(l => l.Product.Id == product.Id);
        var stock = Catalog.First(p => p.Id == product.Id).Stock;
        if ((item?.Quantity ?? 0) >= stock) throw new InvalidOperationException("Stock disponible atteint pour " + product.Name + ".");
        store.RecordActivity("Ajout article", product.Name + " · prix " + product.Price);
        if (item is null) Cart.Add(new CartItem(product)); else item.Quantity++;
        CartChanged(); Status = product.Name + " ajouté au panier.";
    }
    private void CartChanged()
    {
        if (Cart.Count == 0) discountValue = 0;
        Notify(nameof(DiscountLabel)); Notify(nameof(DiscountAmount)); Notify(nameof(Subtotal));
        Notify(nameof(Total)); Notify(nameof(TotalText)); Notify(nameof(CartCount)); Notify(nameof(ChangeText)); Notify(nameof(IsCartEmpty));
        CommandManager.InvalidateRequerySuggested();
    }
    private void Checkout()
    {
        var paid = 0m;
        if (IsCash && !TryMoney(Tendered, out paid)) throw new InvalidOperationException("Saisissez le montant reçu ou cliquez sur « Montant exact ».");
        var sale = store.Checkout(Cart.Select(l => new CartRequest(l.Product.Id, l.Quantity, l.Product.Price)).ToList(), PaymentMethod, paid, DiscountAmount);
        Cart.Clear(); Tendered = ""; CartChanged(); Refresh();
        SelectedSale = Sales.First(s => s.Id == sale.Id);
        Status = $"Vente {sale.Number} enregistrée. Monnaie à rendre : {Amount(sale.Change)}. Ticket disponible dans l’historique.";
        ReceiptRequested?.Invoke(sale);
    }
    private void Save()
    {
        if (!TryMoney(EditPrice, out var price)) throw new InvalidOperationException("Prix invalide. Exemple : 12,50.");
        if (!int.TryParse(EditStock, out var stock)) throw new InvalidOperationException("Le stock doit être un nombre entier.");
        store.SaveProduct(new Product { Id = editId, Name = EditName, Barcode = string.IsNullOrWhiteSpace(EditBarcode) ? "REF-" + Guid.NewGuid().ToString("N") : EditBarcode, Category = EditCategory, Price = price, Stock = stock });
        Refresh(); NewProduct(); Status = "Produit enregistré.";
    }
    private void Edit(Product product)
    {
        editId = product.Id; EditName = product.Name; EditBarcode = product.Barcode; EditCategory = product.Category;
        EditPrice = product.Price.ToString("0.00"); EditStock = product.Stock.ToString(); Notify(nameof(EditorTitle));
    }
    private void NewProduct()
    {
        selectedProduct = null; Notify(nameof(SelectedProduct)); editId = 0;
        EditName = ""; EditBarcode = ""; EditCategory = "Épicerie"; EditPrice = ""; EditStock = "0"; Notify(nameof(EditorTitle));
    }
    public void Refresh()
    {
        Catalog.Clear(); foreach (var p in store.GetProducts()) Catalog.Add(p);
        barcodeIndex = Catalog.ToDictionary(p => p.Barcode, StringComparer.Ordinal);
        var previousCategory = Category;
        Categories.Clear(); Categories.Add("Tous les produits");
        foreach (var c in Catalog.Select(p => p.Category).Distinct().Order()) Categories.Add(c);
        EditorCategories.Clear();
        foreach (var c in Catalog.Select(p => p.Category).Concat(new[] { "Épicerie", "Boissons", "Produits frais", "Boulangerie", "Fruits & légumes", "Hygiène & beauté", "Entretien", "Divers" }).Distinct(StringComparer.OrdinalIgnoreCase).Order()) EditorCategories.Add(c);
        Category = Categories.Contains(previousCategory) ? previousCategory : "Tous les produits";
        var ownSessions = store.GetSessions().Where(s => s.UserId == store.CurrentUser?.Id).Select(s => s.Id).ToHashSet();
        Sales.Clear(); foreach (var s in store.GetSales().Where(s => IsManager || (s.CashSessionId.HasValue && ownSessions.Contains(s.CashSessionId.Value)))) Sales.Add(s);
        SelectedSale = null;
        Retail.Refresh();
        Dashboard.Refresh();
        AuditLog.Clear(); if (IsManager) foreach (var item in store.GetAudit()) AuditLog.Add(item);
        Notify(nameof(IsManager)); Notify(nameof(IsCashier)); Notify(nameof(WorkspaceTitle)); Notify(nameof(HistoryTitle)); Notify(nameof(ManagementTitle)); Notify(nameof(AccountLabel));
        Filter(); Notify(nameof(DailyTotal)); Notify(nameof(DailyCount)); Notify(nameof(StockAlert)); Notify(nameof(IsCatalogEmpty));
    }
    private void Filter()
    {
        Products.Clear();
        var term = Search.Trim();
        var matches = Catalog.Where(p => (Category == "Tous les produits" || p.Category == Category) &&
            (p.Name.Contains(term, StringComparison.OrdinalIgnoreCase) || p.Barcode.Contains(term, StringComparison.OrdinalIgnoreCase))).ToList();
        matchingProducts = matches.Count;
        productPage = Math.Clamp(productPage, 0, Math.Max(0, (matchingProducts - 1) / PageSize));
        foreach (var p in matches.Skip(productPage * PageSize).Take(PageSize)) Products.Add(p);
        Notify(nameof(ProductCount));
        CommandManager.InvalidateRequerySuggested();
    }
    private static bool TryMoney(string text, out decimal value) => decimal.TryParse(text.Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
}
