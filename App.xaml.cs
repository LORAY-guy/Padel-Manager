using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Padel.Manager.Services;
using Padel.Manager.Views;

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
            if (registry.DarkMode)
                RequestedThemeVariant = ThemeVariant.Dark;

            var serverSession = new ServerSession(registry);

            var startup = new StartupDialog(registry, serverSession);
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

                var mainWindow = new MainWindow(registry, serverSession, startup.ChosenEntry);
                mainWindow.Closed += (_, _) => desktop.Shutdown(0);
                desktop.MainWindow = mainWindow;
                mainWindow.Show();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
