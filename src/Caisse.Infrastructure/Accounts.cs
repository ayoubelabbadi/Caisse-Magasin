using System.Security.Cryptography;
using Caisse.Domain;
using Microsoft.EntityFrameworkCore;

namespace Caisse.Infrastructure;

public sealed partial class SqliteStore
{
    public UserIdentity? CurrentUser { get; private set; }
    public bool HasAccounts() { using var db = new StoreDb(path); return db.Users.Any(); }
    public List<UserIdentity> GetUsers() { using var db = new StoreDb(path); return db.Users.Where(u => u.Active).OrderBy(u => u.Name).Select(u => new UserIdentity(u.Id, u.Name, u.Role)).ToList(); }
    private static void ValidatePin(string pin) { if (pin.Length is < 6 or > 12 || !pin.All(char.IsAsciiDigit)) throw new InvalidOperationException("Le PIN doit contenir 6 à 12 chiffres."); }
    private static void SetPin(UserAccount user, string pin)
    {
        ValidatePin(pin); user.Salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        user.PinHash = Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(pin, Convert.FromBase64String(user.Salt), 600000, HashAlgorithmName.SHA256, 32));
        user.FailedAttempts = 0; user.LockedUntil = null;
    }
    private static UserIdentity Identity(UserAccount user) => new(user.Id, user.Name, user.Role);
    private UserIdentity RequireUser(StoreDb db, bool manager = false)
    {
        var user = CurrentUser is null ? null : db.Users.SingleOrDefault(u => u.Id == CurrentUser.Id && u.Active);
        if (user is null || (manager && user.Role != "Manager")) throw new InvalidOperationException(manager ? "Compte manager requis." : "Connectez-vous avec votre compte et votre PIN.");
        return Identity(user);
    }
    public void CreateInitialManager(string name, string pin)
    {
        using var db = new StoreDb(path); using var tx = db.Database.BeginTransaction();
        if (db.Users.Any()) throw new InvalidOperationException("Le compte initial existe déjà.");
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Nom du manager obligatoire.");
        var user = new UserAccount { Name = name.Trim(), Role = "Manager" }; SetPin(user, pin);
        db.Users.Add(user); db.SaveChanges(); CurrentUser = Identity(user);
        Audit(db, "Création du manager initial", user.Name); db.SaveChanges(); tx.Commit();
    }
    private UserIdentity VerifyPin(int userId, string pin, bool manager)
    {
        using var db = new StoreDb(path);
        var user = db.Users.SingleOrDefault(u => u.Id == userId && u.Active);
        if (user is null || (manager && user.Role != "Manager")) throw new InvalidOperationException("Compte ou PIN incorrect.");
        if (user.LockedUntil > DateTime.UtcNow) throw new InvalidOperationException("Compte temporairement verrouillé. Réessayez dans cinq minutes.");
        var hash = Rfc2898DeriveBytes.Pbkdf2(pin, Convert.FromBase64String(user.Salt), 600000, HashAlgorithmName.SHA256, 32);
        if (!CryptographicOperations.FixedTimeEquals(hash, Convert.FromBase64String(user.PinHash))) {
            user.FailedAttempts++; if (user.FailedAttempts >= 5) { user.LockedUntil = DateTime.UtcNow.AddMinutes(5); user.FailedAttempts = 0; }
            db.SaveChanges(); throw new InvalidOperationException("Compte ou PIN incorrect.");
        }
        user.FailedAttempts = 0; user.LockedUntil = null; db.SaveChanges(); return Identity(user);
    }
    public UserIdentity Login(int userId, string pin)
    {
        var identity = VerifyPin(userId, pin, false); CurrentUser = identity;
        using var db = new StoreDb(path); Audit(db, "Connexion", identity.Name); db.SaveChanges(); return identity;
    }
    public void Logout() { if (CurrentUser is not null) { using var db = new StoreDb(path); Audit(db, "Verrouillage", ""); db.SaveChanges(); } CurrentUser = null; }
    public void SaveUser(string name, string pin, bool manager)
    {
        using var db = new StoreDb(path); RequireUser(db, true);
        if (string.IsNullOrWhiteSpace(name) || db.Users.AsEnumerable().Any(u => u.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Nom obligatoire et unique.");
        var user = new UserAccount { Name = name.Trim(), Role = manager ? "Manager" : "Caissier" }; SetPin(user, pin); db.Users.Add(user); Audit(db, "Création de compte", user.Name + " · " + user.Role); db.SaveChanges();
    }
    public void ResetPin(int userId, string pin) { using var db = new StoreDb(path); RequireUser(db, true); var user = db.Users.Single(u => u.Id == userId); SetPin(user, pin); Audit(db, "Réinitialisation PIN", user.Name); db.SaveChanges(); }
    public decimal GetRefundLimit() { using var db = new StoreDb(path); return db.Policies.Single(p => p.Id == 1).RefundLimit; }
    public void SetRefundLimit(decimal amount) { Money.Validate(amount, "Seuil"); using var db = new StoreDb(path); RequireUser(db, true); db.Policies.Single(p => p.Id == 1).RefundLimit = amount; Audit(db, "Seuil remboursement", amount.ToString(System.Globalization.CultureInfo.InvariantCulture)); db.SaveChanges(); }
    private void Audit(StoreDb db, string action, string details, CashSession? session = null)
    {
        var user = RequireUser(db);
        session ??= db.CashSessions.SingleOrDefault(s => s.ClosedAt == null && s.UserId == user.Id);
        db.Audit.Add(new AuditEntry { CreatedAt = DateTime.UtcNow, UserId = user.Id, UserName = user.Name, CashSessionId = session?.Id, Register = session?.Register ?? "", Action = action, Details = details });
    }
    public void RecordActivity(string action, string details) { using var db = new StoreDb(path); var session = RequireOpenSession(db); Audit(db, action, details, session); db.SaveChanges(); }
    public List<AuditEntry> GetAudit() { using var db = new StoreDb(path); RequireUser(db, true); return db.Audit.AsNoTracking().OrderByDescending(a => a.CreatedAt).ToList(); }
}
