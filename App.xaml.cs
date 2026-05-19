using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using MessageBox.Avalonia.Enums;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using Padel.Manager.Services;
using Padel.Manager.Views;
using Velopack;

namespace Padel.Manager;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        var culture = CultureInfo.GetCultureInfo("fr-FR");
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var registry = DatasetRegistry.Load();
            var startup = new StartupDialog(registry);
            desktop.MainWindow = startup;

            startup.Closed += (_, _) =>
            {
                if (startup.ChosenEntry is null)
                {
                    desktop.Shutdown(0);
                    return;
                }

                registry.SetLastOpened(startup.ChosenEntry.Id);
                registry.Save();

                var mainWindow = new MainWindow(registry, startup.ChosenEntry);
                mainWindow.Closed += (_, _) => desktop.Shutdown(0);
                desktop.MainWindow = mainWindow;
                mainWindow.Show();
                _ = CheckForUpdatesAsync();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task CheckForUpdatesAsync()
    {
        try
        {
            var mgr = new UpdateManager("https://github.com/LORAY-guy/Padel-Manager");
            if (!mgr.IsInstalled) return;

            var newVersion = await mgr.CheckForUpdatesAsync();
            if (newVersion == null) return;

            await mgr.DownloadUpdatesAsync(newVersion);

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var result = await MessageBoxManager.GetMessageBoxStandard(
                    "Mise à jour disponible",
                    $"La version {newVersion.TargetFullRelease.Version} est disponible.\nVoulez-vous redémarrer maintenant pour mettre à jour ?",
                    ButtonEnum.YesNo
                ).ShowAsync();

                if (result == ButtonResult.Yes)
                    mgr.ApplyUpdatesAndRestart(newVersion);
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await MessageBoxManager.GetMessageBoxStandard(
                    "Erreur de mise à jour (debug)",
                    ex.ToString(),
                    ButtonEnum.Ok
                ).ShowAsync();
            });
        }
    }
}
