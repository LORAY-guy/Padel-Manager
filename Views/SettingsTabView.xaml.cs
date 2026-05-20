using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Padel.Manager.Views;

public partial class SettingsTabView : UserControl
{
    private bool _loading;

    public SettingsTabView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private MainWindow? GetMainWindow() => TopLevel.GetTopLevel(this) as MainWindow;

    internal void LoadSettings()
    {
        var mainWindow = GetMainWindow();
        if (mainWindow is null) return;

        _loading = true;
        var americano = mainWindow.AmericanoMode;
        AmericanoToggle.IsChecked = americano;
        AmericanoExampleBadge.IsVisible = americano;
        _loading = false;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        LoadSettings();

        if (!string.IsNullOrEmpty(AppText.Version))
        {
            VersionText.Text = AppText.Version;
            VersionSection.IsVisible = true;
        }
    }

    private void AmericanoToggle_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var mainWindow = GetMainWindow();
        if (mainWindow is null) return;

        var enabled = AmericanoToggle.IsChecked == true;
        mainWindow.SetAmericanoMode(enabled);
        AmericanoExampleBadge.IsVisible = enabled;
        mainWindow.SetStatus(enabled ? "Mode Americano activé." : "Mode Americano désactivé.");
    }
}
