using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Input;
using Caisse.Application;
using Caisse.Domain;

namespace Caisse.Desktop;

public sealed record ReportRow(DateTime Date, string Type, string Reference, string Payment, decimal Amount);
public sealed record StockRow(DateTime Date, string Product, int Quantity, string Reason);
public sealed class RefundSelection(SaleLine line, int remaining, Action changed) : Observable
{
    private int quantity;
    public SaleLine Line { get; } = line;
    public int Remaining { get; } = remaining;
    public int Quantity { get => quantity; set { Set(ref quantity, Math.Clamp(value, 0, Remaining)); changed(); } }
    public ICommand Plus => new RelayCommand(_ => Quantity++, _ => Quantity < Remaining);
    public ICommand Minus => new RelayCommand(_ => Quantity--, _ => Quantity > 0);
}

public sealed class RetailViewModel : Observable
{
    private readonly IStore store;
    private readonly ShopSettings settings;
    private readonly Action changed;
    private string opening = "0", counted = "", movementAmount = "", movementReason = "", refundReason = "", saleSearch = "", status = "Démarrez votre shift pour commencer.";
    private bool restock = true;
    private Sale? selectedSale;
    private DateTime? from = DateTime.Today, to = DateTime.Today;
    private List<Sale> allSales = [];
    private List<Refund> allRefunds = [];
    private CashSession? current;
    public Func<string, bool> Confirm { get; set; } = _ => false;
    public Func<bool> BeforeClose { get; set; } = () => true;
    public Func<(int Id, string Pin)?> RequestApproval { get; set; } = () => null;
    public ObservableCollection<RefundSelection> RefundItems { get; } = [];
    public string[] Reasons => RefundReasons.All;
    private string reasonCode = RefundReasons.All[0];
    public string ReasonCode { get => reasonCode; set { Set(ref reasonCode, value); Notify(nameof(IsOtherReason)); } }
    public bool IsOtherReason => ReasonCode == "Autre motif";
    public decimal RefundAmount => SelectedSale is null || !RefundItems.Any(i => i.Quantity > 0) ? 0 : RefundCalculation.Calculate(SelectedSale, allRefunds, Requests()).Sum(l => l.Amount);
    public string RefundAmountText => Amount(RefundAmount);
    public string RefundPolicy => $"PIN manager au-delà de {Amount(store.GetRefundLimit())} cumulés par ticket.";
    private List<RefundRequest> Requests() => RefundItems.Where(i => i.Quantity > 0).Select(i => new RefundRequest(i.Line.Id, i.Quantity)).ToList();
    public event Action<Refund>? RefundReceiptRequested;
    public event Action? ShiftOpened;
    public ObservableCollection<CashSession> Sessions { get; } = [];
    public ObservableCollection<CashMovement> Movements { get; } = [];
    public ObservableCollection<Sale> Sales { get; } = [];
    public ObservableCollection<Refund> Refunds { get; } = [];
    public ObservableCollection<ReportRow> Report { get; } = [];
    public ObservableCollection<StockRow> StockJournal { get; } = [];
    public ObservableCollection<Product> LowStock { get; } = [];
    public string OperatorName => store.CurrentUser?.Name ?? "Non connecté";
    public bool IsManager => store.CurrentUser?.IsManager == true;
    public bool IsCashier => store.CurrentUser is { IsManager: false };
    public string PanelTitle => IsManager ? "Suivi des shifts et des retours" : "Mon shift & retours";
    public bool CanClose => current is not null && (IsOpen || store.CurrentUser?.IsManager == true);
    public string Opening { get => opening; set => Set(ref opening, value); }
    public string Counted { get => counted; set { Set(ref counted, value); Notify(nameof(ClosingDifference)); } }
    public string MovementAmount { get => movementAmount; set => Set(ref movementAmount, value); }
    public string MovementReason { get => movementReason; set => Set(ref movementReason, value); }
    public string RefundReason { get => refundReason; set => Set(ref refundReason, value); }
    public bool Restock { get => restock; set => Set(ref restock, value); }
    public string SaleSearch { get => saleSearch; set { Set(ref saleSearch, value); FilterSales(); } }
    public Sale? SelectedSale { get => selectedSale; set { Set(ref selectedSale, value); BuildRefundItems(); Notify(nameof(SelectedSaleSummary)); CommandManager.InvalidateRequerySuggested(); } }
    private Refund? selectedRefund;
    public Refund? SelectedRefund { get => selectedRefund; set => Set(ref selectedRefund, value); }
    public string SelectedSaleSummary => SelectedSale is null ? "Sélectionnez le ticket à rembourser." : $"{SelectedSale.Number}\n{Amount(SelectedSale.Total)} · {SelectedSale.PaymentMethod}";
    public DateTime? From { get => from; set { Set(ref from, value); BuildReport(); } }
    public DateTime? To { get => to; set { Set(ref to, value); BuildReport(); } }
    public string Status { get => status; set => Set(ref status, value); }
    public bool IsOpen => current is not null && current.UserId == store.CurrentUser?.Id && current.UserId.HasValue;
    public bool IsClosed => current is null;
    public bool CanStartShift => IsCashier && IsClosed;
    public decimal Expected { get; private set; }
    public string ExpectedText => Amount(Expected);
    public string SessionSummary => current is null ? (IsManager ? "Aucun shift ouvert" : "Caisse fermée · démarrez votre shift") : !IsManager && !IsOpen ? "Cette caisse a un autre shift ouvert. Contactez l’administrateur pour le clôturer." : $"Session #{current.Id} · {current.Operator} · fond {Amount(current.OpeningAmount)}";
    public string ClosingDifference => TryAmount(Counted, out var amount) ? "Écart : " + Amount(amount - Expected) : "Saisissez le total des espèces comptées.";
    public string Gross => Amount(Report.Where(r => r.Type == "Vente").Sum(r => r.Amount));
    public string Returned => Amount(-Report.Where(r => r.Type == "Retour").Sum(r => r.Amount));
    public string Net => Amount(Report.Sum(r => r.Amount));
    public string CashNet => Amount(Report.Where(r => r.Payment == "Espèces").Sum(r => r.Amount));
    public string CardNet => Amount(Report.Where(r => r.Payment != "Espèces").Sum(r => r.Amount));
    public string ReportCount => $"{Report.Count(r => r.Type == "Vente")} ventes · {Report.Count(r => r.Type == "Retour")} retours";
    public ICommand OpenCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand CashInCommand { get; }
    public ICommand CashOutCommand { get; }
    public ICommand RefundCommand { get; }
    public ICommand RefundReceiptCommand { get; }
    public ICommand RefreshCommand { get; }

    public RetailViewModel(IStore store, ShopSettings settings, Action changed)
    {
        this.store = store; this.settings = settings; this.changed = changed;
        OpenCommand = new RelayCommand(_ => Run(() => { store.OpenSession(settings.RegisterName, Parse(Opening)); changed(); Status = "Shift ouvert pour " + OperatorName; ShiftOpened?.Invoke(); }), _ => IsClosed && store.CurrentUser is not null);
        CloseCommand = new RelayCommand(_ => Run(() => {
            var amount = Parse(Counted);
            if (!Confirm($"Clôturer la session #{current!.Id} ?\nAttendu : {ExpectedText}\nCompté : {Amount(amount)}\nÉcart : {Amount(amount - Expected)}")) return;
            if (!BeforeClose()) return;
            var closed = store.CloseSession(current.Id, amount); changed(); Counted = "";
            Status = $"Session #{closed.Id} clôturée. Écart : {Amount(closed.Difference ?? 0)}.";
        }), _ => CanClose);
        CashInCommand = new RelayCommand(_ => RecordMovement(false), _ => IsOpen);
        CashOutCommand = new RelayCommand(_ => RecordMovement(true), _ => IsOpen);
        RefundCommand = new RelayCommand(_ => Run(() => {
            if (SelectedSale is null) return;
            if (IsOtherReason && string.IsNullOrWhiteSpace(RefundReason)) throw new InvalidOperationException("Indiquez une explication pour « Autre motif ».");
            if (!RefundItems.Any(i => i.Quantity > 0)) throw new InvalidOperationException("Sélectionnez les quantités à rembourser.");
            var external = SelectedSale.PaymentMethod == "Espèces" ? "Rendez cette somme au client en espèces." : "Le remboursement doit être effectué sur le terminal externe. Aucun débit bancaire n’est exécuté ici.";
            if (!Confirm($"Rembourser {RefundAmountText} sur {SelectedSale.Number} ?\n{string.Join("\n", RefundItems.Where(i => i.Quantity > 0).Select(i => $"{i.Quantity} × {i.Line.ProductName}"))}\n{external}\nRemise en stock : {(Restock ? "oui" : "non")}\nMotif : {(IsOtherReason ? RefundReason : ReasonCode)}")) return;
            (int Id, string Pin)? approval = null;
            if (allRefunds.Where(r => r.SaleId == SelectedSale.Id).Sum(r => r.Amount) + RefundAmount > store.GetRefundLimit()) { approval = RequestApproval(); if (approval is null) return; }
            var refund = store.RefundPartial(SelectedSale.Id, Requests(), ReasonCode, RefundReason, Restock, approval?.Id, approval?.Pin);
            changed(); RefundReason = ""; Status = "Remboursement enregistré sans supprimer la vente d’origine.";
            RefundReceiptRequested?.Invoke(refund);
        }), _ => IsOpen && SelectedSale is not null && RefundItems.Any(i => i.Quantity > 0));
        RefundReceiptCommand = new RelayCommand(_ => { if (SelectedRefund is not null) RefundReceiptRequested?.Invoke(SelectedRefund); }, _ => SelectedRefund is not null);
        RefreshCommand = new RelayCommand(_ => Run(() => { changed(); Status = "Données actualisées."; }));
    }
    private void BuildRefundItems()
    {
        RefundItems.Clear();
        if (SelectedSale is not null) {
            var history = allRefunds.Where(r => r.SaleId == SelectedSale.Id).ToList();
            foreach (var line in SelectedSale.Lines) {
                var remaining = history.Any(r => r.Lines.Count == 0) ? 0 : line.Quantity - history.SelectMany(r => r.Lines).Where(l => l.SaleLineId == line.Id).Sum(l => l.Quantity);
                RefundItems.Add(new RefundSelection(line, remaining, () => { Notify(nameof(RefundAmountText)); CommandManager.InvalidateRequerySuggested(); }));
            }
        }
        Notify(nameof(RefundAmountText));
    }

    private void RecordMovement(bool outgoing) => Run(() => {
        var amount = Parse(MovementAmount);
        if (amount <= 0) throw new InvalidOperationException("Saisissez un montant strictement positif.");
        if (string.IsNullOrWhiteSpace(MovementReason)) throw new InvalidOperationException("Indiquez un motif.");
        if (!Confirm($"Enregistrer une {(outgoing ? "sortie" : "entrée")} de {Amount(amount)} ?\nMotif : {MovementReason}")) return;
        store.AddCashMovement(current!.Id, outgoing ? -amount : amount, MovementReason);
        changed(); MovementAmount = ""; MovementReason = ""; Status = "Mouvement d’espèces enregistré.";
    });

    public void Refresh()
    {
        var sessions = store.GetSessions(); current = sessions.SingleOrDefault(s => s.ClosedAt == null);
        Replace(Sessions, sessions.Where(s => IsManager || s.UserId == store.CurrentUser?.Id));
        Expected = current is null || (!IsManager && !IsOpen) ? 0 : store.GetExpectedCash(current.Id);
        var visibleSessions = Sessions.Select(s => s.Id).ToHashSet();
        Replace(Movements, store.GetCashMovements().Where(m => visibleSessions.Contains(m.CashSessionId)));
        allSales = store.GetSales(); allRefunds = store.GetRefunds();
        SelectedRefund = null;
        Replace(Refunds, allRefunds.Where(r => IsManager || r.RequestedById == store.CurrentUser?.Id)); FilterSales(); BuildReport();
        var products = store.GetProducts().ToDictionary(p => p.Id);
        Replace(StockJournal, IsManager ? store.GetStockMovements().Select(m => new StockRow(m.CreatedAt.ToLocalTime(), products.GetValueOrDefault(m.ProductId)?.Name ?? $"Produit #{m.ProductId}", m.Quantity, m.Reason)) : []);
        Replace(LowStock, products.Values.Where(p => IsManager && p.Stock <= 5).OrderBy(p => p.Stock));
        Notify(nameof(IsOpen)); Notify(nameof(IsClosed)); Notify(nameof(ExpectedText)); Notify(nameof(SessionSummary)); Notify(nameof(ClosingDifference));
        Notify(nameof(OperatorName)); Notify(nameof(CanClose)); Notify(nameof(RefundPolicy));
        Notify(nameof(IsManager)); Notify(nameof(IsCashier)); Notify(nameof(PanelTitle)); Notify(nameof(CanStartShift));
        CommandManager.InvalidateRequerySuggested();
    }

    private void FilterSales()
    {
        var selectedId = SelectedSale?.Id;
        Replace(Sales, allSales.Where(s => (IsManager || !string.IsNullOrWhiteSpace(SaleSearch)) && s.Number.Contains(SaleSearch.Trim(), StringComparison.OrdinalIgnoreCase)));
        SelectedSale = Sales.FirstOrDefault(s => s.Id == selectedId);
    }

    private void BuildReport()
    {
        var start = From?.Date; var end = To?.Date;
        bool Included(DateTime date) => IsManager && start is not null && end is not null && date.ToLocalTime().Date >= start && date.ToLocalTime().Date <= end;
        var rows = allSales.Where(s => Included(s.CreatedAt)).Select(s => new ReportRow(s.CreatedAt.ToLocalTime(), "Vente", s.Number, s.PaymentMethod, s.Total))
            .Concat(allRefunds.Where(r => Included(r.CreatedAt)).Select(r => new ReportRow(r.CreatedAt.ToLocalTime(), "Retour", r.SaleNumber, r.PaymentMethod, -r.Amount)))
            .OrderByDescending(r => r.Date);
        Replace(Report, rows);
        foreach (var property in new[] { nameof(Gross), nameof(Returned), nameof(Net), nameof(CashNet), nameof(CardNet), nameof(ReportCount) }) Notify(property);
    }

    public void ExportReport(string path) => Run(() => {
        if (!IsManager) throw new InvalidOperationException("Accès réservé à l’administrateur.");
        if (From is null || To is null || From.Value.Date > To.Value.Date) throw new InvalidOperationException("Choisissez une période valide.");
        // Quote every field and neutralize spreadsheet formula prefixes, including user-configurable currency.
        static string Cell(string value) => "\"" + ((value.Length > 0 && "=+-@\t\r".Contains(value[0])) ? "'" : "") + value.Replace("\"", "\"\"") + "\"";
        var lines = new List<string> { "Date;Type;Reference;Paiement;Montant;Devise" };
        lines.AddRange(Report.Select(r => string.Join(";", new[] { r.Date.ToString("yyyy-MM-dd HH:mm:ss"), r.Type, r.Reference, r.Payment, r.Amount.ToString("0.00", CultureInfo.GetCultureInfo("fr-FR")), settings.Currency }.Select(Cell))));
        File.WriteAllLines(path, lines, new UTF8Encoding(true)); Status = "Rapport exporté : " + path;
    });

    private string Amount(decimal value) => $"{value:N2} {settings.Currency}";
    private static bool TryAmount(string text, out decimal value) => decimal.TryParse(text.Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    private static decimal Parse(string text)
    {
        if (!TryAmount(text, out var value)) throw new InvalidOperationException("Montant invalide. Exemple : 150,50.");
        Money.Validate(value, "Montant"); return value;
    }
    private void Run(Action action)
    {
        try { action(); } catch (Exception ex) { Status = ex.GetBaseException().Message; }
    }
    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source) { target.Clear(); foreach (var item in source) target.Add(item); }
}
