using System.Collections.ObjectModel;
using System.Windows.Input;
using Caisse.Application;
using Caisse.Domain;

namespace Caisse.Desktop;

public sealed record CashierSummary(string Key, string Name, string State, int Sessions, int Tickets, int Units,
    decimal Sales, decimal Refunds, decimal Cash, decimal Card, decimal Difference)
{
    public decimal Net => Sales - Refunds;
    public decimal Average => Tickets == 0 ? 0 : Sales / Tickets;
}

public sealed record SessionDetail(int Id, string Operator, DateTime Opened, DateTime? Closed, string State,
    decimal Opening, decimal? Expected, decimal? Counted, decimal? Difference);
public sealed record SalesChartPoint(string Label, string Description, decimal Amount, double Height);
public sealed record CashierChartPoint(CashierSummary Cashier, double Width, string Amount);

public static class CashierStatistics
{
    public const string UnassignedKey = "unassigned:";
    public static string Key(CashSession session) => session.UserId.HasValue ? "user:" + session.UserId : Key(session.Operator);
    public static string Key(string name) => "operator:" + name.Trim().ToUpperInvariant();
    public static IReadOnlyList<CashierSummary> Build(IReadOnlyList<CashSession> sessions, IReadOnlyList<Sale> sales,
        IReadOnlyList<Refund> refunds, DateTime from, DateTime to)
    {
        if (from.Date > to.Date) return [];
        bool Included(DateTime date) => date.ToLocalTime().Date >= from.Date && date.ToLocalTime().Date <= to.Date;
        var sessionMap = sessions.ToDictionary(s => s.Id);
        string Owner(int? id) => id is not null && sessionMap.TryGetValue(id.Value, out var session) ? Key(session) : UnassignedKey;
        var periodSales = sales.Where(s => Included(s.CreatedAt)).ToLookup(s => Owner(s.CashSessionId));
        var periodRefunds = refunds.Where(r => Included(r.CreatedAt)).ToLookup(r => Owner(r.CashSessionId));
        var periodSessions = sessions.Where(s => s.OpenedAt.ToLocalTime().Date <= to.Date && (s.ClosedAt is null || s.ClosedAt.Value.ToLocalTime().Date >= from.Date)).ToLookup(s => Key(s));
        var names = sessions.GroupBy(s => Key(s)).ToDictionary(g => g.Key, g => g.First().Operator.Trim());
        return periodSessions.Select(g => g.Key).Union(periodSales.Select(g => g.Key)).Union(periodRefunds.Select(g => g.Key))
            .Select(key => new CashierSummary(key, names.GetValueOrDefault(key, "Non attribué"),
                sessions.Any(s => Key(s) == key && s.ClosedAt is null) ? "En caisse" : "Hors session",
                periodSessions[key].Count(), periodSales[key].Count(), periodSales[key].Sum(s => s.Lines.Sum(l => l.Quantity)),
                periodSales[key].Sum(s => s.Total), periodRefunds[key].Sum(r => r.Amount),
                periodSales[key].Where(s => s.PaymentMethod == "Espèces").Sum(s => s.Total) - periodRefunds[key].Where(r => r.PaymentMethod == "Espèces").Sum(r => r.Amount),
                periodSales[key].Where(s => s.PaymentMethod != "Espèces").Sum(s => s.Total) - periodRefunds[key].Where(r => r.PaymentMethod != "Espèces").Sum(r => r.Amount),
                periodSessions[key].Where(s => s.ClosedAt.HasValue && Included(s.ClosedAt.Value)).Sum(s => s.Difference ?? 0)))
            .OrderByDescending(r => r.Sales).ThenBy(r => r.Name).ToList();
    }
}

public sealed class DashboardViewModel : Observable
{
    private readonly IStore store;
    private readonly ShopSettings settings;
    private List<CashSession> sessions = [];
    private List<Sale> sales = [];
    private List<Refund> refunds = [];
    private DateTime? from = DateTime.Today.AddDays(-29), to = DateTime.Today;
    private CashierSummary? selected;
    public ObservableCollection<CashierSummary> Cashiers { get; } = [];
    public ObservableCollection<SessionDetail> SelectedSessions { get; } = [];
    public ObservableCollection<SalesChartPoint> SalesChart { get; } = [];
    public ObservableCollection<CashierChartPoint> CashierChart { get; } = [];
    public decimal CashSales { get; private set; }
    public decimal CardSales { get; private set; }
    public string CashSalesText => "Espèces · " + Amount(CashSales);
    public string CardSalesText => "Carte · " + Amount(CardSales);
    public ICommand SelectCashierCommand => new RelayCommand(p => Selected = (CashierSummary)p!);
    public DateTime? From { get => from; set { Set(ref from, value); Build(); } }
    public DateTime? To { get => to; set { Set(ref to, value); Build(); } }
    public CashierSummary? Selected { get => selected; set { Set(ref selected, value); BuildSessions(); } }
    public bool IsValid => From.HasValue && To.HasValue && From.Value.Date <= To.Value.Date;
    public string PeriodMessage => !IsValid ? "Choisissez une date de début antérieure ou égale à la date de fin." : Cashiers.Count == 0 ? "Aucune activité sur cette période." : $"{Cashiers.Count} caissier(s) · montants en {settings.Currency}";
    public string SalesTotal => Amount(Cashiers.Sum(c => c.Sales));
    public string RefundTotal => Amount(Cashiers.Sum(c => c.Refunds));
    public string NetTotal => Amount(Cashiers.Sum(c => c.Net));
    public string SelectedTitle => Selected is null ? "Sélectionnez un caissier pour voir ses sessions" : $"Sessions · {Selected.Name}";
    public string SelectedAmounts => Selected is null ? "" : $"Solde espèces : {Amount(Selected.Cash)}   ·   Solde carte : {Amount(Selected.Card)}   ·   {Selected.Units} articles vendus";
    public ICommand RefreshCommand { get; }
    public ICommand TodayCommand { get; }
    public ICommand MonthCommand { get; }
    public DashboardViewModel(IStore store, ShopSettings settings, Action refresh)
    {
        this.store = store; this.settings = settings;
        RefreshCommand = new RelayCommand(_ => refresh());
        TodayCommand = new RelayCommand(_ => { from = to = DateTime.Today; Notify(nameof(From)); Notify(nameof(To)); Build(); });
        MonthCommand = new RelayCommand(_ => { from = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1); to = DateTime.Today; Notify(nameof(From)); Notify(nameof(To)); Build(); });
    }
    public void Refresh()
    {
        var manager = store.CurrentUser?.IsManager == true;
        sessions = manager ? store.GetSessions() : []; sales = manager ? store.GetSales() : []; refunds = manager ? store.GetRefunds() : []; Build();
    }
    private void Build()
    {
        var previous = Selected?.Key;
        Cashiers.Clear();
        if (IsValid) foreach (var row in CashierStatistics.Build(sessions, sales, refunds, From!.Value, To!.Value)) Cashiers.Add(row);
        Selected = Cashiers.FirstOrDefault(c => c.Key == previous) ?? Cashiers.FirstOrDefault();
        BuildCharts();
        foreach (var name in new[] { nameof(IsValid), nameof(PeriodMessage), nameof(SalesTotal), nameof(RefundTotal), nameof(NetTotal) }) Notify(name);
    }
    private void BuildCharts()
    {
        SalesChart.Clear(); CashierChart.Clear(); CashSales = CardSales = 0;
        if (IsValid) {
            var start = From!.Value.Date; var end = To!.Value.Date;
            var period = sales.Where(s => s.CreatedAt.ToLocalTime().Date >= start && s.CreatedAt.ToLocalTime().Date <= end).ToList();
            CashSales = period.Where(s => s.PaymentMethod == "Espèces").Sum(s => s.Total); CardSales = period.Where(s => s.PaymentMethod != "Espèces").Sum(s => s.Total);
            var days = (end - start).Days + 1; var bucket = Math.Max(1, (int)Math.Ceiling(days / 14d));
            var groups = period.GroupBy(s => (s.CreatedAt.ToLocalTime().Date - start).Days / bucket).ToDictionary(g => g.Key, g => g.Sum(s => s.Total));
            var max = Math.Max(1, groups.Values.DefaultIfEmpty(0).Max());
            for (var i = 0; i * bucket < days; i++) {
                var date = start.AddDays(i * bucket); var until = date.AddDays(Math.Min(bucket - 1, (end - date).Days)); var amount = groups.GetValueOrDefault(i);
                SalesChart.Add(new(date.ToString("dd/MM"), $"{date:dd/MM/yyyy} → {until:dd/MM/yyyy} : {Amount(amount)}", amount, (double)(amount / max) * 150));
            }
            var best = Math.Max(1, Cashiers.Select(c => c.Sales).DefaultIfEmpty(0).Max());
            foreach (var c in Cashiers) CashierChart.Add(new(c, (double)(c.Sales / best) * 340, Amount(c.Sales)));
        }
        Notify(nameof(CashSales)); Notify(nameof(CardSales)); Notify(nameof(CashSalesText)); Notify(nameof(CardSalesText));
    }
    private void BuildSessions()
    {
        SelectedSessions.Clear();
        if (Selected is not null && IsValid)
            foreach (var s in sessions.Where(s => CashierStatistics.Key(s) == Selected.Key && s.OpenedAt.ToLocalTime().Date <= To!.Value.Date && (s.ClosedAt is null || s.ClosedAt.Value.ToLocalTime().Date >= From!.Value.Date)).OrderByDescending(s => s.OpenedAt))
                SelectedSessions.Add(new(s.Id, s.Operator, s.OpenedAt.ToLocalTime(), s.ClosedAt?.ToLocalTime(), s.State, s.OpeningAmount, s.ExpectedAtClose, s.CountedAmount, s.Difference));
        Notify(nameof(SelectedTitle)); Notify(nameof(SelectedAmounts));
    }
    private string Amount(decimal value) => $"{value:N2} {settings.Currency}";
}
