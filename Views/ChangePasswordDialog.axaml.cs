using Avalonia.Controls;
using Avalonia.Interactivity;
using Padel.Manager.Services;

namespace Padel.Manager.Views;

public partial class ChangePasswordDialog : Window
{
    private readonly ServerSession _session;

    public bool Changed { get; private set; }

    public ChangePasswordDialog()
    {
        InitializeComponent();
        _session = null!;
    }

    public ChangePasswordDialog(ServerSession session)
    {
        InitializeComponent();
        _session = session;
    }

    private async void SaveButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var current = CurrentBox.Text ?? string.Empty;
        var next = NewBox.Text ?? string.Empty;
        var confirm = ConfirmBox.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(current) || string.IsNullOrWhiteSpace(next))
        {
            ShowError("Renseignez le mot de passe actuel et le nouveau mot de passe.");
            return;
        }

        if (next != confirm)
        {
            ShowError("Le nouveau mot de passe et sa confirmation ne correspondent pas.");
            return;
        }

        SaveButton.IsEnabled = false;
        try
        {
            await _session.ChangePasswordAsync(current, next);
            Changed = true;
            Close();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            SaveButton.IsEnabled = true;
        }
    }

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e) => Close();

    private void ShowError(string message)
    {
        StatusText.Text = message;
        StatusText.IsVisible = true;
    }
}
