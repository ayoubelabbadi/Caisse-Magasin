using Caisse.Domain;

namespace Caisse.Application;

public interface IStore
{
    UserIdentity? CurrentUser { get; }
    bool HasAccounts();
    List<UserIdentity> GetUsers();
    void CreateInitialManager(string name, string pin);
    UserIdentity Login(int userId, string pin);
    void Logout();
    void SaveUser(string name, string pin, bool manager);
    void ResetPin(int userId, string pin);
    decimal GetRefundLimit();
    void SetRefundLimit(decimal amount);
    void RecordActivity(string action, string details);
    List<AuditEntry> GetAudit();
    Refund RefundPartial(int saleId, IReadOnlyList<RefundRequest> items, string reasonCode, string explanation, bool restock, int? managerId = null, string? managerPin = null);
    void Initialize();
    List<Product> GetProducts();
    Product SaveProduct(Product product);
    Sale Checkout(IReadOnlyList<CartRequest> cart, string paymentMethod, decimal tendered, decimal discount = 0);
    List<Sale> GetSales();
    void Backup(string destination);
    void LoadDemoProducts();
    List<CashSession> GetSessions();
    CashSession OpenSession(string operatorName, decimal openingAmount);
    CashSession CloseSession(int sessionId, decimal countedAmount);
    decimal GetExpectedCash(int sessionId);
    void AddCashMovement(int sessionId, decimal signedAmount, string reason);
    List<CashMovement> GetCashMovements();
    Refund RefundSale(int saleId, string reason, bool restock);
    List<Refund> GetRefunds();
    List<StockMovement> GetStockMovements();
}
