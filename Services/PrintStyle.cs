using Avalonia.Media;

namespace Padel.Manager.Services;

/// <summary>
/// Defines a style for printing/exporting match sheets.
/// New styles can be easily added by implementing this interface.
/// </summary>
public interface IPrintStyle
{
    /// <summary>
    /// Unique identifier for this style.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Display name for UI menus.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Gets the background color for the sheet.
    /// </summary>
    Color SheetBackground { get; }

    /// <summary>
    /// Gets the color for the title text.
    /// </summary>
    Color TitleForeground { get; }

    /// <summary>
    /// Gets the background color for rotation headers.
    /// </summary>
    Color RotationHeaderBackground { get; }

    /// <summary>
    /// Gets the text color for rotation headers.
    /// </summary>
    Color RotationHeaderForeground { get; }

    /// <summary>
    /// Gets the background color for column headers.
    /// </summary>
    Color ColumnHeaderBackground { get; }

    /// <summary>
    /// Gets the text color for column headers.
    /// </summary>
    Color ColumnHeaderForeground { get; }

    /// <summary>
    /// Gets the border color for all cells.
    /// </summary>
    Color CellBorderColor { get; }

    /// <summary>
    /// Gets the background color for data rows (alternating).
    /// </summary>
    Color DataRowBackground { get; }

    /// <summary>
    /// Gets the text color for data rows.
    /// </summary>
    Color DataRowForeground { get; }

    /// <summary>
    /// Gets the border color for the outer panel.
    /// </summary>
    Color PanelBorderColor { get; }
}

/// <summary>
/// Colored style with blue headers and red title.
/// </summary>
public sealed class ColoredPrintStyle : IPrintStyle
{
    public string Name => "colored";
    public string DisplayName => "Couleur";
    public Color SheetBackground => Colors.White;
    public Color TitleForeground => Color.FromRgb(0xFF, 0x00, 0x00); // Red
    public Color RotationHeaderBackground => Color.FromRgb(0x0F, 0x2A, 0x52); // Dark blue
    public Color RotationHeaderForeground => Colors.White;
    public Color ColumnHeaderBackground => Color.FromRgb(0xEA, 0xF0, 0xF9); // Light blue
    public Color ColumnHeaderForeground => Colors.Black;
    public Color CellBorderColor => Color.FromRgb(0xBA, 0xC8, 0xDC); // Light gray-blue
    public Color DataRowBackground => Colors.White;
    public Color DataRowForeground => Colors.Black;
    public Color PanelBorderColor => Color.FromRgb(0xCF, 0xD8, 0xE6); // Light border
}

/// <summary>
/// Black and white style for economical printing.
/// </summary>
public sealed class BlackAndWhitePrintStyle : IPrintStyle
{
    public string Name => "blackandwhite";
    public string DisplayName => "Noir et blanc";
    public Color SheetBackground => Colors.White;
    public Color TitleForeground => Colors.Black;
    public Color RotationHeaderBackground => Colors.Black;
    public Color RotationHeaderForeground => Colors.White;
    public Color ColumnHeaderBackground => Color.FromRgb(0xE0, 0xE0, 0xE0); // Light gray
    public Color ColumnHeaderForeground => Colors.Black;
    public Color CellBorderColor => Colors.Black;
    public Color DataRowBackground => Colors.White;
    public Color DataRowForeground => Colors.Black;
    public Color PanelBorderColor => Colors.Black;
}
