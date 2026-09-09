using System.IO;
using Caisse.Application;

namespace Caisse.Desktop;

public static class AutomaticBackup
{
    public static string CreateDaily(IStore store, string directory)
    {
        var backups = Path.Combine(directory, "backups"); Directory.CreateDirectory(backups);
        var file = Path.Combine(backups, $"caisse-{DateTime.Now:yyyy-MM-dd}.db");
        if (File.Exists(file)) return "Sauvegarde quotidienne disponible : " + file;
        var temporary = file + ".tmp";
        store.Backup(temporary);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var settings = Path.Combine(directory, "settings.json");
        if (File.Exists(settings)) File.Copy(settings, Path.Combine(backups, $"settings-{DateTime.Now:yyyy-MM-dd}.json"), true);
        File.Move(temporary, file, true);
        return "Sauvegarde quotidienne créée : " + file;
    }
}
