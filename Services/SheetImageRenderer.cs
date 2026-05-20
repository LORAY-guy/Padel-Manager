using Avalonia.Media;
using Padel.Manager.Models;
using SkiaSharp;

namespace Padel.Manager.Services;

public static class SheetImageRenderer
{
    // Column widths in DIPs — must match the AXAML preview panel
    private const float W0 = 90f;
    private const float W1 = 250f;
    private const float W2 = 250f;
    private const float W3 = 80f;
    private const float W4 = 80f;
    private const float TableWidth = W0 + W1 + W2 + W3 + W4; // 750

    private const float PadH = 22f;
    private const float PadV = 20f;
    private const float TitleFontSize = 24f;
    private const float TitleRowH = 38f;
    private const float TitleGap = 16f;
    private const float RotHeaderH = 36f;
    private const float RotHeaderFontSize = 13f;
    private const float ColHeaderH = 34f;
    private const float RowH = 34f;
    private const float CellFontSize = 11.5f;
    private const float GroupGap = 14f;
    private const float CornerRadius = 6f;

    /// <summary>
    /// Renders a match sheet directly via SkiaSharp — no Avalonia visual tree, no ScrollViewer clipping.
    /// </summary>
    public static SKBitmap Render(
        string title,
        IReadOnlyList<PrintableRotationGroup> groups,
        IPrintStyle style,
        float dpi)
    {
        float scale = dpi / 96f;
        // Ensure total width is at least A4 width in DIPs
        float totalW = MathF.Max(TableWidth + 2 * PadH, 8.27f * 96f);

        float h = PadV + TitleRowH + TitleGap;
        foreach (var g in groups)
            h += RotHeaderH + ColHeaderH + RowH * g.Matches.Count + GroupGap;
        h += PadV;

        int bmpW = (int)MathF.Ceiling(totalW * scale);
        int bmpH = (int)MathF.Ceiling(h * scale);

        var bmp = new SKBitmap(bmpW, bmpH, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(ToSk(style.SheetBackground));
        canvas.Scale(scale, scale); // all subsequent coordinates are DIPs

        // Outer border
        using (var p = MakeStroke(style.PanelBorderColor, 1f))
            canvas.DrawRoundRect(0.5f, 0.5f, totalW - 1f, h - 1f, CornerRadius, CornerRadius, p);

        // Title
        float y = PadV;
        DrawHCentered(canvas, title, 0f, y, totalW, TitleRowH, TitleFontSize, bold: true, style.TitleForeground);
        y += TitleRowH + TitleGap;

        float tableX = (totalW - TableWidth) / 2f;

        foreach (var group in groups)
        {
            // Rotation header: rounded on top, flat on bottom
            using (var bg = MakeFill(style.RotationHeaderBackground))
            {
                canvas.DrawRoundRect(tableX, y, TableWidth, RotHeaderH, 4f, 4f, bg);
                canvas.DrawRect(tableX, y + RotHeaderH / 2f, TableWidth, RotHeaderH / 2f + 1f, bg);
            }
            DrawHCentered(canvas, $"ROTATION {group.RoundNumber}",
                tableX, y, TableWidth, RotHeaderH, RotHeaderFontSize, bold: true, style.RotationHeaderForeground);
            y += RotHeaderH;

            DrawTableRow(canvas, tableX, y, ColHeaderH,
                ["Terrain", "Équipe A", "Équipe B", "A", "B"],
                style.ColumnHeaderBackground, style.ColumnHeaderForeground, style.CellBorderColor, bold: true);
            y += ColHeaderH;

            foreach (var m in group.Matches)
            {
                DrawTableRow(canvas, tableX, y, RowH,
                    [m.TerrainNumberText, m.TeamA, m.TeamB, m.ScoreA, m.ScoreB],
                    style.DataRowBackground, style.DataRowForeground, style.CellBorderColor, bold: false);
                y += RowH;
            }

            y += GroupGap;
        }

        return bmp;
    }

    private static void DrawTableRow(SKCanvas canvas, float x, float y, float rowH, string[] cells,
        Color bg, Color fg, Color border, bool bold)
    {
        float[] ws = [W0, W1, W2, W3, W4];
        using var bgP = MakeFill(bg);
        using var borP = MakeStroke(border, 1f);

        float cx = x;
        for (int i = 0; i < ws.Length; i++)
        {
            var rect = new SKRect(cx, y, cx + ws[i], y + rowH);
            canvas.DrawRect(rect, bgP);
            canvas.DrawRect(rect, borP);
            DrawClippedCentered(canvas, cells[i], cx, y, ws[i], rowH, CellFontSize, bold, fg);
            cx += ws[i];
        }
    }

    private static void DrawHCentered(SKCanvas canvas, string text,
        float x, float y, float w, float h, float fontSize, bool bold, Color color)
    {
        DrawClippedCentered(canvas, text, x, y, w, h, fontSize, bold, color);
    }

    private static void DrawClippedCentered(SKCanvas canvas, string text,
        float cx, float cy, float cw, float ch, float fontSize, bool bold, Color color)
    {
        using var tf = SKTypeface.FromFamilyName(null, bold ? SKFontStyle.Bold : SKFontStyle.Normal);
        using var paint = new SKPaint
        {
            Typeface = tf,
            TextSize = fontSize,
            Color = ToSk(color),
            IsAntialias = true
        };

        float tw = paint.MeasureText(text);
        float tx = cx + (cw - tw) / 2f;
        float ty = Baseline(cy, ch, paint);

        canvas.Save();
        canvas.ClipRect(new SKRect(cx + 2f, cy + 1f, cx + cw - 2f, cy + ch - 1f));
        canvas.DrawText(text, tx, ty, paint);
        canvas.Restore();
    }

    /// <summary>
    /// Returns the Y baseline so the text block is vertically centered within the cell.
    /// SKFontMetrics.Ascent is negative (above baseline); Descent is positive (below).
    /// </summary>
    private static float Baseline(float cellY, float cellH, SKPaint paint)
    {
        var m = paint.FontMetrics;
        float textH = m.Descent - m.Ascent;
        return cellY + (cellH - textH) / 2f - m.Ascent;
    }

    private static SKPaint MakeFill(Color c) =>
        new() { Color = ToSk(c), Style = SKPaintStyle.Fill };

    private static SKPaint MakeStroke(Color c, float strokeWidth) =>
        new() { Color = ToSk(c), Style = SKPaintStyle.Stroke, StrokeWidth = strokeWidth };

    private static SKColor ToSk(Color c) => new(c.R, c.G, c.B, c.A);
}
