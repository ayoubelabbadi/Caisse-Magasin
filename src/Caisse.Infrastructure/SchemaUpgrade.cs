using Microsoft.Data.Sqlite;

namespace Caisse.Infrastructure;

public sealed partial class SqliteStore
{
    private void UpgradeSchema()
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        connection.Open();
        using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "PRAGMA user_version";
        var version = Convert.ToInt32(versionCommand.ExecuteScalar());
        if (version > 3) throw new InvalidOperationException("Cette base provient d’une version plus récente de la caisse.");
        if (version == 3) return;
        using var columnsCommand = connection.CreateCommand();
        columnsCommand.CommandText = "PRAGMA table_info('Sales')";
        var columns = new HashSet<string>();
        using (var reader = columnsCommand.ExecuteReader()) while (reader.Read()) columns.Add(reader.GetString(1));
        // Back up the v1 database before any schema changes. SQLite's backup API includes WAL data.
        if (version is 1 or 2 || !columns.Contains("Discount"))
        {
            var directory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "backups");
            Directory.CreateDirectory(directory);
            Backup(Path.Combine(directory, $"avant-mise-a-jour-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.db"));
        }
        using var transaction = connection.BeginTransaction();
        void Execute(string sql)
        {
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = sql; command.ExecuteNonQuery();
        }
        if (!columns.Contains("Discount")) Execute("ALTER TABLE Sales ADD COLUMN Discount INTEGER NOT NULL DEFAULT 0");
        if (!columns.Contains("CashSessionId")) Execute("ALTER TABLE Sales ADD COLUMN CashSessionId INTEGER NULL");
        Execute("""
            CREATE TABLE IF NOT EXISTS CashSessions (
                Id INTEGER NOT NULL CONSTRAINT PK_CashSessions PRIMARY KEY AUTOINCREMENT,
                Operator TEXT NOT NULL, OpenedAt TEXT NOT NULL, ClosedAt TEXT NULL,
                OpeningAmount INTEGER NOT NULL, CountedAmount INTEGER NULL, ExpectedAtClose INTEGER NULL);
            CREATE UNIQUE INDEX IF NOT EXISTS IX_CashSessions_OneOpen ON CashSessions ((1)) WHERE ClosedAt IS NULL;
            CREATE TABLE IF NOT EXISTS CashMovements (
                Id INTEGER NOT NULL CONSTRAINT PK_CashMovements PRIMARY KEY AUTOINCREMENT,
                CashSessionId INTEGER NOT NULL, CreatedAt TEXT NOT NULL, Amount INTEGER NOT NULL, Reason TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS Refunds (
                Id INTEGER NOT NULL CONSTRAINT PK_Refunds PRIMARY KEY AUTOINCREMENT,
                SaleId INTEGER NOT NULL, CashSessionId INTEGER NOT NULL, SaleNumber TEXT NOT NULL,
                CreatedAt TEXT NOT NULL, Amount INTEGER NOT NULL, PaymentMethod TEXT NOT NULL,
                Reason TEXT NOT NULL, Restock INTEGER NOT NULL);
            CREATE UNIQUE INDEX IF NOT EXISTS IX_Refunds_SaleId ON Refunds (SaleId);
            PRAGMA user_version = 2;
            """);
        bool HasColumn(string table, string name) {
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = $"PRAGMA table_info('{table}')";
            using var reader = command.ExecuteReader(); while (reader.Read()) if (reader.GetString(1) == name) return true; return false;
        }
        if (!HasColumn("CashSessions", "UserId")) Execute("ALTER TABLE CashSessions ADD COLUMN UserId INTEGER NULL");
        if (!HasColumn("CashSessions", "Register")) Execute("ALTER TABLE CashSessions ADD COLUMN Register TEXT NOT NULL DEFAULT ''");
        foreach (var name in new[] { "RequestedById", "ApprovedById" })
            if (!HasColumn("Refunds", name)) Execute($"ALTER TABLE Refunds ADD COLUMN {name} INTEGER NULL");
        foreach (var name in new[] { "RequestedBy", "ApprovedBy", "Register", "ReasonCode" })
            if (!HasColumn("Refunds", name)) Execute($"ALTER TABLE Refunds ADD COLUMN {name} TEXT NOT NULL DEFAULT ''");
        Execute("""
            CREATE TABLE IF NOT EXISTS Users (
                Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, Role TEXT NOT NULL,
                Salt TEXT NOT NULL, PinHash TEXT NOT NULL, Active INTEGER NOT NULL,
                FailedAttempts INTEGER NOT NULL, LockedUntil TEXT NULL);
            CREATE UNIQUE INDEX IF NOT EXISTS IX_Users_Name ON Users(Name);
            CREATE TABLE IF NOT EXISTS Policies (Id INTEGER PRIMARY KEY, RefundLimit INTEGER NOT NULL);
            INSERT OR IGNORE INTO Policies VALUES(1,50000);
            CREATE TABLE IF NOT EXISTS Audit (
                Id INTEGER PRIMARY KEY AUTOINCREMENT, CreatedAt TEXT NOT NULL, UserId INTEGER NOT NULL,
                UserName TEXT NOT NULL, CashSessionId INTEGER NULL, Register TEXT NOT NULL,
                Action TEXT NOT NULL, Details TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS RefundLines (
                Id INTEGER PRIMARY KEY AUTOINCREMENT, RefundId INTEGER NOT NULL REFERENCES Refunds(Id),
                SaleLineId INTEGER NOT NULL, ProductId INTEGER NOT NULL, ProductName TEXT NOT NULL,
                Quantity INTEGER NOT NULL, Amount INTEGER NOT NULL);
            DROP INDEX IF EXISTS IX_Refunds_SaleId;
            CREATE INDEX IX_Refunds_SaleId ON Refunds(SaleId);
            CREATE INDEX IF NOT EXISTS IX_RefundLines_RefundId ON RefundLines(RefundId);
            CREATE TRIGGER IF NOT EXISTS Audit_NoDelete BEFORE DELETE ON Audit BEGIN SELECT RAISE(ABORT,'Journal non supprimable'); END;
            CREATE TRIGGER IF NOT EXISTS Audit_NoUpdate BEFORE UPDATE ON Audit BEGIN SELECT RAISE(ABORT,'Journal non modifiable'); END;
            CREATE TRIGGER IF NOT EXISTS Refunds_NoDelete BEFORE DELETE ON Refunds BEGIN SELECT RAISE(ABORT,'Retour non supprimable'); END;
            CREATE TRIGGER IF NOT EXISTS Refunds_NoUpdate BEFORE UPDATE ON Refunds BEGIN SELECT RAISE(ABORT,'Retour non modifiable'); END;
            CREATE TRIGGER IF NOT EXISTS RefundLines_NoDelete BEFORE DELETE ON RefundLines BEGIN SELECT RAISE(ABORT,'Retour non supprimable'); END;
            CREATE TRIGGER IF NOT EXISTS RefundLines_NoUpdate BEFORE UPDATE ON RefundLines BEGIN SELECT RAISE(ABORT,'Retour non modifiable'); END;
            PRAGMA user_version = 3;
            """);
        transaction.Commit();
    }
}
