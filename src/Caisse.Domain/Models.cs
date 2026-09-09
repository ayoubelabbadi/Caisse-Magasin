namespace Caisse.Domain;

public sealed class Product
{
    public int Id { get; set; }
    public string Barcode { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "Épicerie";
    public decimal Price { get; set; }
    public int Stock { get; set; }
}

public sealed class Sale
{
    public int Id { get; set; }
    public string Number { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string PaymentMethod { get; set; } = "Espèces";
    public decimal Total { get; set; }
    public decimal Tendered { get; set; }
    public decimal Change { get; set; }
    public decimal Discount { get; set; }
    public int? CashSessionId { get; set; }
    public List<SaleLine> Lines { get; set; } = [];
}

public sealed class SaleLine
{
    public int Id { get; set; }
    public int SaleId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public string Barcode { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal Total => UnitPrice * Quantity;
}

public sealed class StockMovement
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public string Reason { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public sealed record CartRequest(int ProductId, int Quantity, decimal ExpectedPrice);

public sealed class CashSession
{
    public int? UserId { get; set; }
    public string Register { get; set; } = "";
    public int Id { get; set; }
    public string Operator { get; set; } = "";
    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public decimal OpeningAmount { get; set; }
    public decimal? CountedAmount { get; set; }
    public decimal? ExpectedAtClose { get; set; }
    public decimal? Difference => CountedAmount - ExpectedAtClose;
    public string State => ClosedAt is null ? "Ouverte" : "Clôturée";
}

public sealed class CashMovement
{
    public int Id { get; set; }
    public int CashSessionId { get; set; }
    public DateTime CreatedAt { get; set; }
    public decimal Amount { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class Refund
{
    public int? RequestedById { get; set; }
    public string RequestedBy { get; set; } = "";
    public int? ApprovedById { get; set; }
    public string ApprovedBy { get; set; } = "";
    public string Register { get; set; } = "";
    public string ReasonCode { get; set; } = "";
    public List<RefundLine> Lines { get; set; } = [];
    public int Id { get; set; }
    public int SaleId { get; set; }
    public int CashSessionId { get; set; }
    public string SaleNumber { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = "";
    public string Reason { get; set; } = "";
    public bool Restock { get; set; }
}

public sealed class UserAccount
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Role { get; set; } = "Caissier";
    public string Salt { get; set; } = "";
    public string PinHash { get; set; } = "";
    public bool Active { get; set; } = true;
    public int FailedAttempts { get; set; }
    public DateTime? LockedUntil { get; set; }
}
public sealed record UserIdentity(int Id, string Name, string Role) { public bool IsManager => Role == "Manager"; }
public sealed class StorePolicy { public int Id { get; set; } = 1; public decimal RefundLimit { get; set; } = 500; }
public sealed class RefundLine
{
    public int Id { get; set; }
    public int RefundId { get; set; }
    public int SaleLineId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public int Quantity { get; set; }
    public decimal Amount { get; set; }
}
public sealed record RefundRequest(int SaleLineId, int Quantity);
public sealed class AuditEntry
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public int? CashSessionId { get; set; }
    public string Register { get; set; } = "";
    public string Action { get; set; } = "";
    public string Details { get; set; } = "";
}
public static class RefundReasons
{
    public static string[] All { get; } = ["Produit retourné", "Produit défectueux", "Erreur de prix", "Erreur du caissier", "Double paiement", "Commande annulée", "Insatisfaction du client", "Produit manquant", "Autre motif"];
}

public static class Money
{
    public static void Validate(decimal value, string label)
    {
        if (value < 0 || value > 999999999m || decimal.Round(value, 2) != value)
            throw new InvalidOperationException($"{label} : saisir un montant positif avec deux décimales au maximum.");
    }
}
