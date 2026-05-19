using System.Globalization;
using SkiaSharp;

namespace Padel.Manager.Services;

public sealed class LeaderboardSheetService
{
    private const float Dpi = 300f;
    private const float A4WidthInches = 11.69f;
    private const float A4HeightInches = 8.27f;
    private const float MarginPx = 40f * (Dpi / 96f);

    private const float PanelPadding = 16f * (Dpi / 96f);
    private const float HeaderBlockHeight = 84f * (Dpi / 96f);
    private const float PodiumBlockHeight = 76f * (Dpi / 96f);
    private const float HeaderToPodiumGap = 12f * (Dpi / 96f);
    private const float PodiumToTableGap = 12f * (Dpi / 96f);
    private const float TableHeaderHeight = 24f * (Dpi / 96f);

    public int PageWidthPx { get; } = (int)Math.Round(A4WidthInches * Dpi);
    public int PageHeightPx { get; } = (int)Math.Round(A4HeightInches * Dpi);

    public IReadOnlyList<byte[]> RenderPages(IReadOnlyList<LeaderboardSheetRow> allRows, int playersPerSheet, DateTime exportDate)
    {
        if (playersPerSheet <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(playersPerSheet), "Le nombre de joueurs par feuille doit être supérieur à 0.");
        }

        var safeRows = allRows.Count == 0
            ? new List<LeaderboardSheetRow>()
            : allRows.OrderBy(r => r.Rank).ToList();

        var totalPages = Math.Max(1, (int)Math.Ceiling(safeRows.Count / (double)playersPerSheet));
        var pages = new List<byte[]>(totalPages);

        for (var pageIndex = 0; pageIndex < totalPages; pageIndex++)
        {
            var pageRows = safeRows
                .Skip(pageIndex * playersPerSheet)
                .Take(playersPerSheet)
                .ToList();

            pages.Add(RenderSinglePage(safeRows, pageRows, playersPerSheet, exportDate, pageIndex + 1, totalPages));
        }

        return pages;
    }

    private byte[] RenderSinglePage(
        IReadOnlyList<LeaderboardSheetRow> allRows,
        IReadOnlyList<LeaderboardSheetRow> pageRows,
        int playersPerSheet,
        DateTime exportDate,
        int pageNumber,
        int totalPages)
    {
        var info = new SKImageInfo(PageWidthPx, PageHeightPx, SKColorType.Bgra8888, SKAlphaType.Premul);

        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        var contentRect = new SKRect(
            MarginPx,
            MarginPx,
            PageWidthPx - MarginPx,
            PageHeightPx - MarginPx);

        using var panelPaint = new SKPaint { Color = ParseHex("#FBFCFF"), IsAntialias = true };
        using var panelStroke = new SKPaint { Color = ParseHex("#CDD7EA"), IsAntialias = true, IsStroke = true, StrokeWidth = 1f };
        canvas.DrawRoundRect(contentRect, 6f, 6f, panelPaint);
        canvas.DrawRoundRect(contentRect, 6f, 6f, panelStroke);

        var innerLeft = contentRect.Left + PanelPadding;
        var innerTop = contentRect.Top + PanelPadding;
        var innerRight = contentRect.Right - PanelPadding;
        var innerWidth = innerRight - innerLeft;

        DrawHeader(canvas, innerLeft, innerTop, innerWidth, allRows.Count, exportDate, pageNumber, totalPages);
        DrawPodium(canvas, innerLeft, innerTop, innerWidth, allRows);
        DrawTable(canvas, innerLeft, innerTop, innerWidth, contentRect.Bottom - PanelPadding, pageRows, playersPerSheet);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static void DrawHeader(SKCanvas canvas, float x, float y, float width, int totalRows, DateTime exportDate, int pageNumber, int totalPages)
    {
        var rect = new SKRect(x, y, x + width, y + HeaderBlockHeight);

        using var bg = new SKPaint { Color = ParseHex("#EAF1FF"), IsAntialias = true };
        using var stroke = new SKPaint { Color = ParseHex("#B9C9E8"), IsAntialias = true, IsStroke = true, StrokeWidth = 1f };
        canvas.DrawRoundRect(rect, 6f, 6f, bg);
        canvas.DrawRoundRect(rect, 6f, 6f, stroke);

        var title = $"CLASSEMENT GLOBAL - {exportDate:dd/MM/yyyy}";
        var subtitle = $"Top {Math.Max(3, totalRows)} joueurs - Page {pageNumber}/{totalPages}";

        DrawCenteredText(canvas, title, true, ScalePt(28f), ParseHex("#0F2A52"),
            new SKRect(rect.Left, rect.Top + ScalePx(8f), rect.Right, rect.Top + ScalePx(8f) + ScalePx(42f)));
        DrawCenteredText(canvas, subtitle, false, ScalePt(13f), ParseHex("#3A4C73"),
            new SKRect(rect.Left, rect.Top + ScalePx(48f), rect.Right, rect.Top + ScalePx(48f) + ScalePx(24f)));
    }

    private static void DrawPodium(SKCanvas canvas, float x, float y, float width, IReadOnlyList<LeaderboardSheetRow> allRows)
    {
        var top = y + HeaderBlockHeight + HeaderToPodiumGap;
        var cardGap = ScalePx(10f);
        var cardWidth = (width - (2 * cardGap)) / 3f;

        var second = allRows.FirstOrDefault(r => r.Rank == 2);
        var first = allRows.FirstOrDefault(r => r.Rank == 1);
        var third = allRows.FirstOrDefault(r => r.Rank == 3);

        DrawPodiumCard(canvas, new SKRect(x, top, x + cardWidth, top + PodiumBlockHeight), "2ème", second, "#F5F7FA", "#D6D9DF", "#515B70", false);
        DrawPodiumCard(canvas, new SKRect(x + cardWidth + cardGap, top, x + cardWidth + cardGap + cardWidth, top + PodiumBlockHeight), "1er", first, "#FFF4CF", "#E3C66A", "#7E5B00", true);
        DrawPodiumCard(canvas, new SKRect(x + 2 * (cardWidth + cardGap), top, x + 2 * (cardWidth + cardGap) + cardWidth, top + PodiumBlockHeight), "3ème", third, "#FBEFE7", "#D9B89B", "#6B4A2E", false);
    }

    private static void DrawPodiumCard(SKCanvas canvas, SKRect rect, string label, LeaderboardSheetRow? row, string bgHex, string borderHex, string textHex, bool highlight)
    {
        using var bg = new SKPaint { Color = ParseHex(bgHex), IsAntialias = true };
        using var stroke = new SKPaint { Color = ParseHex(borderHex), IsAntialias = true, IsStroke = true, StrokeWidth = 1f };
        using var textPaint = new SKPaint { Color = ParseHex(textHex) };

        canvas.DrawRoundRect(rect, 6f, 6f, bg);
        canvas.DrawRoundRect(rect, 6f, 6f, stroke);

        DrawCenteredText(canvas, label, true, ScalePt(16f), ParseHex(textHex),
            new SKRect(rect.Left, rect.Top + ScalePx(6f), rect.Right, rect.Top + ScalePx(6f) + ScalePx(22f)));
        DrawCenteredText(canvas, row?.Name ?? "-", highlight, ScalePt(highlight ? 16f : 14f), SKColors.Black,
            new SKRect(rect.Left + ScalePx(6f), rect.Top + ScalePx(28f), rect.Right - ScalePx(6f), rect.Top + ScalePx(28f) + ScalePx(24f)));
        DrawCenteredText(canvas, row is null ? "0 pts" : $"{row.TotalPoints} pts", false, ScalePt(14f), ParseHex(textHex),
            new SKRect(rect.Left, rect.Top + ScalePx(52f), rect.Right, rect.Top + ScalePx(52f) + ScalePx(20f)));
    }

    private static void DrawTable(SKCanvas canvas, float x, float y, float width, float bottom, IReadOnlyList<LeaderboardSheetRow> pageRows, int playersPerSheet)
    {
        var tableTop = y + HeaderBlockHeight + HeaderToPodiumGap + PodiumBlockHeight + PodiumToTableGap;
        var tableRect = new SKRect(x, tableTop, x + width, bottom);

        using var borderPaint = new SKPaint { Color = ParseHex("#CFD8E6"), IsAntialias = true, IsStroke = true, StrokeWidth = 1f };
        using var rowLinePaint = new SKPaint { Color = ParseHex("#E5EAF3"), IsAntialias = true, IsStroke = true, StrokeWidth = 1f };

        canvas.DrawRect(tableRect, new SKPaint { Color = SKColors.White });
        canvas.DrawRect(tableRect, borderPaint);

        var nameWidth = Math.Max(ScalePx(180f), width - ScalePx(55f + 90f + 95f + 95f + 90f + 100f + 120f));
        var colWidths = new[] { ScalePx(55f), nameWidth, ScalePx(90f), ScalePx(95f), ScalePx(95f), ScalePx(90f), ScalePx(100f), ScalePx(120f) };
        var colHeaders = new[] { "#", "Nom", "Niveau", "Joués", "Gagnés", "Points", "Moyenne", "Forme récente" };

        var headerRect = new SKRect(tableRect.Left, tableRect.Top, tableRect.Right, tableRect.Top + TableHeaderHeight);
        canvas.DrawRect(headerRect, new SKPaint { Color = ParseHex("#0F2A52") });

        var cx = tableRect.Left;
        for (var i = 0; i < colHeaders.Length; i++)
        {
            DrawCenteredText(canvas, colHeaders[i], true, ScalePt(13f), SKColors.White,
                new SKRect(cx, headerRect.Top, cx + colWidths[i], headerRect.Bottom));
            cx += colWidths[i];
        }

        var tableBodyHeight = Math.Max(0f, tableRect.Height - TableHeaderHeight);
        var rowHeight = playersPerSheet > 0 ? tableBodyHeight / playersPerSheet : tableBodyHeight;

        for (var rowIndex = 0; rowIndex < playersPerSheet; rowIndex++)
        {
            var rowY = tableRect.Top + TableHeaderHeight + (rowIndex * rowHeight);
            var rowRect = new SKRect(tableRect.Left, rowY, tableRect.Right, rowY + rowHeight);

            var rowColor = rowIndex % 2 == 0 ? SKColors.White : ParseHex("#F7FAFF");
            canvas.DrawRect(rowRect, new SKPaint { Color = rowColor });

            if (rowIndex > 0)
            {
                canvas.DrawLine(tableRect.Left, rowY, tableRect.Right, rowY, rowLinePaint);
            }

            var row = rowIndex < pageRows.Count ? pageRows[rowIndex] : null;
            var values = row is null
                ? new[] { string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty }
                : new[]
                {
                    row.Rank.ToString(CultureInfo.InvariantCulture),
                    row.Name,
                    row.Level.ToString(CultureInfo.InvariantCulture),
                    row.MatchesPlayed.ToString(CultureInfo.InvariantCulture),
                    row.MatchesWon.ToString(CultureInfo.InvariantCulture),
                    row.TotalPoints.ToString(CultureInfo.InvariantCulture),
                    row.AveragePoints.ToString("F2", CultureInfo.InvariantCulture),
                    row.RecentFormDisplay
                };

            var vx = tableRect.Left;
            for (var i = 0; i < values.Length; i++)
            {
                DrawCenteredText(canvas, values[i], false, ScalePt(13f), SKColors.Black,
                    new SKRect(vx, rowY, vx + colWidths[i], rowY + rowHeight));
                vx += colWidths[i];
            }
        }

        canvas.DrawLine(tableRect.Left, tableRect.Bottom, tableRect.Right, tableRect.Bottom, borderPaint);

        var vertX = tableRect.Left;
        for (var i = 0; i < colWidths.Length - 1; i++)
        {
            vertX += colWidths[i];
            canvas.DrawLine(vertX, tableRect.Top, vertX, tableRect.Bottom, rowLinePaint);
        }
    }

    private static void DrawCenteredText(SKCanvas canvas, string text, bool bold, float fontSize, SKColor color, SKRect bounds)
    {
        using var paint = new SKPaint
        {
            Color = color,
            IsAntialias = true,
            TextSize = fontSize,
            Typeface = bold ? SKTypeface.FromFamilyName("Arial", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
                            : SKTypeface.FromFamilyName("Arial"),
            TextAlign = SKTextAlign.Center
        };

        var textBounds = new SKRect();
        paint.MeasureText(text, ref textBounds);
        var textX = bounds.MidX;
        var textY = bounds.MidY - textBounds.MidY;

        canvas.Save();
        canvas.ClipRect(bounds);
        canvas.DrawText(text, textX, textY, paint);
        canvas.Restore();
    }

    private static SKColor ParseHex(string hex)
    {
        if (SKColor.TryParse(hex, out var color))
        {
            return color;
        }

        return SKColors.Black;
    }

    private static float ScalePx(float dipValue) => dipValue * (Dpi / 96f);

    private static float ScalePt(float ptValue) => ptValue * (Dpi / 72f);
}

public sealed class LeaderboardSheetRow
{
    public required int Rank { get; init; }
    public required string Name { get; init; }
    public required int Level { get; init; }
    public required int MatchesPlayed { get; init; }
    public required int MatchesWon { get; init; }
    public required int TotalPoints { get; init; }
    public required double AveragePoints { get; init; }
    public required string RecentFormDisplay { get; init; }
}
