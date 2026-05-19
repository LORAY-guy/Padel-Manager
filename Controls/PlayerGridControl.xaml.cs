using Avalonia;
using Avalonia.Controls;

namespace Padel.Manager.Controls;

public partial class PlayerGridControl : UserControl
{
    public static readonly StyledProperty<bool> IsGridReadOnlyProperty =
        AvaloniaProperty.Register<PlayerGridControl, bool>(nameof(IsGridReadOnly), defaultValue: false);

    public bool IsGridReadOnly
    {
        get => GetValue(IsGridReadOnlyProperty);
        set => SetValue(IsGridReadOnlyProperty, value);
    }

    public PlayerGridControl()
    {
        InitializeComponent();
        PlayersGrid.CellEditEnding += PlayersGrid_OnCellEditEnding;
    }

    private void PlayersGrid_OnCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit)
        {
            return;
        }

        if (e.Column.Header?.ToString() != "Niveau")
        {
            return;
        }

        if (e.EditingElement is not TextBox tb)
        {
            return;
        }

        if (!int.TryParse(tb.Text, out var level) || level < 1 || level > 5)
        {
            e.Cancel = true;
        }
    }
}
