using System.Globalization;
using System.IO;
using System.Windows;
using Caisse.Infrastructure;

namespace Caisse.Desktop;

public partial class App : System.Windows.Application
{
    private readonly bool initializeStore;
    public App() : this(true) { }
    public App(bool initializeStore) => this.initializeStore = initializeStore;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        TouchInput.Register();
        if (!initializeStore) return;
        if (e.Args.Length == 2 && e.Args[0] == "--verify-installation")
        {
            try { DeploymentCheck.Run(e.Args[1]); Shutdown(0); }
            catch (Exception ex) { Directory.CreateDirectory(e.Args[1]); File.WriteAllText(Path.Combine(e.Args[1], "verification-error.txt"), ex.ToString()); Shutdown(1); }
            return;
        }
        if (System.Diagnostics.Process.GetProcessesByName("Caisse.Desktop").Any(p => p.Id != Environment.ProcessId))
        {
            AppDialog.Inform(null, "Caisse déjà ouverte", "Une autre fenêtre de Caisse est déjà ouverte. Fermez-la avant de lancer cette version.");
            Shutdown(); return;
        }
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("fr-MA");
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
        try
        {
            var directory = Environment.GetEnvironmentVariable("CAISSE_DATA_DIR")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CaisseMagasin");
            var settings = ShopSettings.Load(directory);
            var store = new SqliteStore(Path.Combine(directory, "caisse.db"));
            store.Initialize();
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            if (!AccountDialogs.Login(null, store)) { Shutdown(); return; }
            var model = new MainViewModel(store, settings, directory);
            try { AutomaticBackup.CreateDaily(store, directory); }
            catch (Exception ex) { model.Status = "La caisse est disponible, mais la sauvegarde automatique a échoué : " + ex.Message; }
            var window = new MainWindow(model);
            MainWindow = window;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            window.Show();
        }
        catch (Exception ex)
        {
            AppDialog.Inform(null, "Démarrage", "Impossible d’ouvrir la caisse.\n" + ex.Message);
            Shutdown(1);
        }
    }
}
