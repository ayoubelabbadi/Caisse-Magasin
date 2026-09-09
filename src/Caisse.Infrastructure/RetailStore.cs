using Caisse.Domain;
using Microsoft.EntityFrameworkCore;

namespace Caisse.Infrastructure;

public sealed partial class SqliteStore
{
    private CashSession RequireOpenSession(StoreDb db, bool closing = false)
    {
        var user = RequireUser(db);
        var session = db.CashSessions.SingleOrDefault(s => s.ClosedAt == null) ?? throw new InvalidOperationException("Démarrez votre shift dans Gestion avant cette opération.");
        if (session.UserId != user.Id && !(closing && user.IsManager)) throw new InvalidOperationException("Ce shift appartient à un autre compte ou à l’ancienne version. Faites-le clôturer par un manager.");
        return session;
    }

    public List<CashSession> GetSessions()
    {
        using var db = new StoreDb(path);
        return db.CashSessions.AsNoTracking().OrderByDescending(s => s.OpenedAt).ToList();
    }

    public CashSession OpenSession(string operatorName, decimal openingAmount)
    {
        Money.Validate(openingAmount, "Fond de caisse");
        if (string.IsNullOrWhiteSpace(operatorName)) throw new InvalidOperationException("Saisissez le nom du caissier.");
        using var db = new StoreDb(path);
        using var transaction = db.Database.BeginTransaction();
        var user = RequireUser(db);
        if (db.CashSessions.Any(s => s.ClosedAt == null)) throw new InvalidOperationException("Une session est déjà ouverte.");
        var session = new CashSession { UserId = user.Id, Operator = user.Name, Register = operatorName.Trim(), OpeningAmount = openingAmount, OpenedAt = DateTime.UtcNow };
        db.CashSessions.Add(session); db.SaveChanges(); Audit(db, "Ouverture shift", $"Fond : {openingAmount}", session); db.SaveChanges(); transaction.Commit();
        return session;
    }

    private static decimal ExpectedCash(StoreDb db, CashSession session) => session.OpeningAmount
        + db.Sales.Where(s => s.CashSessionId == session.Id && s.PaymentMethod == "Espèces").Select(s => s.Total).AsEnumerable().Sum()
        + db.CashMovements.Where(m => m.CashSessionId == session.Id).Select(m => m.Amount).AsEnumerable().Sum()
        - db.Refunds.Where(r => r.CashSessionId == session.Id && r.PaymentMethod == "Espèces").Select(r => r.Amount).AsEnumerable().Sum();

    public decimal GetExpectedCash(int sessionId)
    {
        using var db = new StoreDb(path);
        var session = db.CashSessions.Single(s => s.Id == sessionId);
        return session.ExpectedAtClose ?? ExpectedCash(db, session);
    }

    public CashSession CloseSession(int sessionId, decimal countedAmount)
    {
        Money.Validate(countedAmount, "Montant compté");
        using var db = new StoreDb(path);
        using var transaction = db.Database.BeginTransaction();
        var session = RequireOpenSession(db, true);
        if (session.Id != sessionId) throw new InvalidOperationException("Cette session n’est plus active. Actualisez l’écran.");
        session.ExpectedAtClose = ExpectedCash(db, session);
        session.CountedAmount = countedAmount; session.ClosedAt = DateTime.UtcNow;
        Audit(db, "Clôture shift", $"Attendu {session.ExpectedAtClose} · compté {countedAmount} · écart {session.Difference}", session);
        db.SaveChanges(); transaction.Commit(); return session;
    }

    public void AddCashMovement(int sessionId, decimal signedAmount, string reason)
    {
        Money.Validate(Math.Abs(signedAmount), "Montant");
        if (signedAmount == 0 || string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("Un montant non nul et un motif sont obligatoires.");
        using var db = new StoreDb(path);
        using var transaction = db.Database.BeginTransaction();
        var session = RequireOpenSession(db);
        if (session.Id != sessionId) throw new InvalidOperationException("Cette session n’est plus active.");
        if (ExpectedCash(db, session) + signedAmount < 0) throw new InvalidOperationException("La sortie dépasse les espèces attendues en caisse.");
        db.CashMovements.Add(new CashMovement { CashSessionId = session.Id, Amount = signedAmount, Reason = reason.Trim(), CreatedAt = DateTime.UtcNow });
        Audit(db, "Mouvement espèces", $"{signedAmount} · {reason}", session);
        db.SaveChanges(); transaction.Commit();
    }

    public List<CashMovement> GetCashMovements()
    {
        using var db = new StoreDb(path);
        return db.CashMovements.AsNoTracking().OrderByDescending(m => m.CreatedAt).ToList();
    }

    public Refund RefundSale(int saleId, string reason, bool restock)
    {
        var sale = GetSales().Single(s => s.Id == saleId);
        var previous = GetRefunds().Where(r => r.SaleId == saleId).ToList();
        if (previous.Any(r => r.Lines.Count == 0)) throw new InvalidOperationException("Ticket déjà remboursé.");
        var items = sale.Lines.Select(l => new RefundRequest(l.Id, l.Quantity - previous.SelectMany(r => r.Lines).Where(r => r.SaleLineId == l.Id).Sum(r => r.Quantity))).Where(r => r.Quantity > 0).ToList();
        return RefundPartial(saleId, items, "Autre motif", reason, restock);
    }

    public Refund RefundPartial(int saleId, IReadOnlyList<RefundRequest> items, string reasonCode, string explanation, bool restock, int? managerId = null, string? managerPin = null)
    {
        if (!RefundReasons.All.Contains(reasonCode) || (reasonCode == "Autre motif" && string.IsNullOrWhiteSpace(explanation)))
            throw new InvalidOperationException("Choisissez un motif ; une explication est obligatoire pour « Autre motif ».");
        var approval = managerId.HasValue ? VerifyPin(managerId.Value, managerPin ?? "", true) : null;
        using var db = new StoreDb(path); using var transaction = db.Database.BeginTransaction();
        var session = RequireOpenSession(db);
        var user = RequireUser(db);
        var sale = db.Sales.Include(s => s.Lines).SingleOrDefault(s => s.Id == saleId) ?? throw new InvalidOperationException("Vente introuvable.");
        var previous = db.Refunds.Include(r => r.Lines).Where(r => r.SaleId == saleId).ToList();
        var lines = RefundCalculation.Calculate(sale, previous, items);
        var amount = lines.Sum(l => l.Amount);
        var limit = db.Policies.Single(p => p.Id == 1).RefundLimit;
        if (previous.Sum(r => r.Amount) + amount > limit && approval is null)
            throw new InvalidOperationException("PIN d’un manager requis : le cumul remboursé de ce ticket dépasse le seuil.");
        if (sale.PaymentMethod == "Espèces" && ExpectedCash(db, session) < amount)
            throw new InvalidOperationException("Espèces insuffisantes dans la caisse pour ce remboursement.");
        var refund = new Refund {
            SaleId = saleId, SaleNumber = sale.Number, CashSessionId = session.Id, Register = session.Register,
            CreatedAt = DateTime.UtcNow, Amount = amount, PaymentMethod = sale.PaymentMethod,
            ReasonCode = reasonCode, Reason = reasonCode == "Autre motif" ? explanation.Trim() : reasonCode, Restock = restock,
            RequestedById = user.Id, RequestedBy = user.Name, ApprovedById = approval?.Id, ApprovedBy = approval?.Name ?? "", Lines = lines
        };
        db.Refunds.Add(refund);
        if (restock) foreach (var line in lines) {
            var product = db.Products.Single(p => p.Id == line.ProductId); product.Stock = checked(product.Stock + line.Quantity);
            db.StockMovements.Add(new StockMovement { ProductId = product.Id, Quantity = line.Quantity, CreatedAt = refund.CreatedAt, Reason = "Retour " + sale.Number });
        }
        db.SaveChanges();
        Audit(db, "Remboursement", System.Text.Json.JsonSerializer.Serialize(new { refund.Id, refund.SaleNumber, refund.Amount, refund.Reason, refund.ApprovedBy, refund.Restock, Products = lines.Select(l => new { l.ProductName, l.Quantity, l.Amount }) }), session);
        db.SaveChanges(); transaction.Commit(); return refund;
    }
    public List<Refund> GetRefunds()
    {
        using var db = new StoreDb(path);
        return db.Refunds.Include(r => r.Lines).AsNoTracking().OrderByDescending(r => r.CreatedAt).ToList();
    }

    public List<StockMovement> GetStockMovements()
    {
        using var db = new StoreDb(path);
        return db.StockMovements.AsNoTracking().OrderByDescending(r => r.CreatedAt).ToList();
    }
}
