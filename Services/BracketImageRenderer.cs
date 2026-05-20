using Avalonia.Media;
using Padel.Manager.Models;
using SkiaSharp;

namespace Padel.Manager.Services;

public static class BracketImageRenderer
{
    // Layout constants in DIPs — must match RenderBracketCanvas
    private const float BoxW = 240f;
    private const float BoxH = 90f;
    private const float RoundGap = 280f;
    private const float VertGap = 30f;
    private const float HeaderH = 48f;
    private const float Left = 12f;
    private const float CornerRadius = 6f;

    private const float TitleH = 50f;
    private const float PadV = 14f;

    // Font sizes in DIPs
    private const float TitleFontSize = 22f;
    private const float RoundLabelFontSize = 11f;
    private const float RoundDateFontSize = 10f;
    private const float TeamFontSize = 10f;
    private const float ScoreFontSize = 9.5f;

    /// <summary>
    /// Renders a tournament bracket directly via SkiaSharp — no Avalonia visual tree, no ScrollViewer clipping.
    /// </summary>
    public static SKBitmap Render(
        string title,
        IReadOnlyList<TournamentMatch> matches,
        IPrintStyle style,
        float dpi)
    {
        float scale = dpi / 96f;

        var rounds = matches
            .GroupBy(m => m.RoundNumber)
            .OrderBy(g => g.Key)
            .Select(g => (RoundNumber: g.Key, Matches: g.OrderBy(m => m.MatchNumber).ToList()))
            .ToList();

        // Compute Y positions per round (mirrors ComputeRoundPositions)
        var posByRound = new Dictionary<int, List<float>>();
        float bracketBottom = HeaderH + 16f;

        for (int ri = 0; ri < rounds.Count; ri++)
        {
            var yPositions = ComputePositions(ri, rounds[ri].Matches.Count, rounds, posByRound);
            posByRound[rounds[ri].RoundNumber] = yPositions;
            foreach (float yp in yPositions)
                bracketBottom = MathF.Max(bracketBottom, yp + BoxH + 10f);
        }

        float bracketW = Left + MathF.Max(0, rounds.Count - 1) * RoundGap + BoxW + 20f;
        float bracketH = bracketBottom + 20f;

        // Total canvas size: title area + bracket, minimum A4 landscape
        float totalW = MathF.Max(bracketW, 11.69f * 96f);
        float totalH = TitleH + bracketH;

        int bmpW = (int)MathF.Ceiling(totalW * scale);
        int bmpH = (int)MathF.Ceiling(totalH * scale);

        var bmp = new SKBitmap(bmpW, bmpH, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(ToSk(style.SheetBackground));
        canvas.Scale(scale, scale); // all subsequent coordinates are DIPs

        // Outer border
        using (var p = MakeStroke(style.PanelBorderColor, 1f))
            canvas.DrawRoundRect(0.5f, 0.5f, totalW - 1f, totalH - 1f, CornerRadius, CornerRadius, p);

        // Title
        DrawHCentered(canvas, title, 0f, PadV, totalW, TitleH - PadV, TitleFontSize, bold: true, style.TitleForeground);

        // Bracket content starts below title, horizontally centered
        float bracketOffsetX = (totalW - bracketW) / 2f;
        canvas.Save();
        canvas.Translate(bracketOffsetX, TitleH);

        int totalRounds = rounds.Count;
        for (int ri = 0; ri < rounds.Count; ri++)
        {
            var round = rounds[ri];
            float x = Left + ri * RoundGap;
            var yPositions = posByRound[round.RoundNumber];
            var roundDate = round.Matches.Select(m => m.MatchDate.Date).FirstOrDefault();
            string label = GetRoundLabel(round.RoundNumber, totalRounds);

            // Round header: rounded on top, flat on bottom
            using (var bg = MakeFill(style.RotationHeaderBackground))
            {
                canvas.DrawRoundRect(x, 0f, BoxW, HeaderH, 4f, 4f, bg);
                canvas.DrawRect(x, HeaderH / 2f, BoxW, HeaderH / 2f + 1f, bg);
            }
            DrawHCentered(canvas, label, x, 2f, BoxW, HeaderH / 2f, RoundLabelFontSize, bold: true, style.RotationHeaderForeground);
            DrawHCentered(canvas, roundDate.ToString("dd/MM/yyyy"), x, HeaderH / 2f + 3f, BoxW, HeaderH / 2f - 6f, RoundDateFontSize, bold: false, style.RotationHeaderForeground);

            // Match cards
            foreach ((var match, float cardY) in round.Matches.Zip(yPositions))
            {
                DrawCard(canvas, match, x, cardY, style);
            }
        }

        // Connector lines between rounds (skip round 1→2, same as the canvas renderer)
        for (int ri = 0; ri < rounds.Count - 1; ri++)
        {
            var current = rounds[ri];
            var next = rounds[ri + 1];

            if (current.RoundNumber <= 1)
                continue;

            var currYs = posByRound[current.RoundNumber];
            var nextYs = posByRound[next.RoundNumber];

            float xRight = Left + ri * RoundGap + BoxW;
            float xLeft = Left + (ri + 1) * RoundGap;
            float mergeX = xLeft - 28f;

            using var lp = MakeStroke(style.CellBorderColor, 1.5f);
            lp.IsAntialias = true;

            if (currYs.Count == nextYs.Count)
            {
                for (int i = 0; i < currYs.Count; i++)
                {
                    float ly = currYs[i] + BoxH / 2f;
                    canvas.DrawLine(xRight, ly, xLeft, ly, lp);
                }
            }
            else if (currYs.Count == nextYs.Count * 2)
            {
                for (int i = 0; i < nextYs.Count; i++)
                {
                    float y1 = currYs[i * 2] + BoxH / 2f;
                    float y2 = currYs[i * 2 + 1] + BoxH / 2f;
                    float yT = nextYs[i] + BoxH / 2f;
                    canvas.DrawLine(xRight, y1, mergeX, y1, lp);
                    canvas.DrawLine(xRight, y2, mergeX, y2, lp);
                    canvas.DrawLine(mergeX, y1, mergeX, y2, lp);
                    canvas.DrawLine(mergeX, yT, xLeft, yT, lp);
                }
            }
            else
            {
                for (int i = 0; i < Math.Min(currYs.Count, nextYs.Count); i++)
                {
                    float y1 = currYs[i] + BoxH / 2f;
                    float y2 = nextYs[i] + BoxH / 2f;
                    canvas.DrawLine(xRight, y1, xLeft, y2, lp);
                }
            }
        }

        canvas.Restore();
        return bmp;
    }

    private static void DrawCard(SKCanvas canvas, TournamentMatch match, float x, float y, IPrintStyle style)
    {
        using (var bg = MakeFill(style.DataRowBackground))
            canvas.DrawRect(x, y, BoxW, BoxH, bg);
        using (var border = MakeStroke(style.CellBorderColor, 1f))
            canvas.DrawRect(x, y, BoxW, BoxH, border);

        float innerX = x + 7f;
        float innerW = BoxW - 14f;

        string teamA = string.IsNullOrWhiteSpace(match.TeamA) ? " " : match.TeamA;
        string teamB = string.IsNullOrWhiteSpace(match.TeamB) ? " " : match.TeamB;
        string score = string.IsNullOrWhiteSpace(match.ScoreA) && string.IsNullOrWhiteSpace(match.ScoreB)
            ? "Score :   -   "
            : $"Score : {match.ScoreA} - {match.ScoreB}";

        DrawTruncatedCentered(canvas, teamA, innerX, y + 9f, innerW, 18f, TeamFontSize, bold: false, style.DataRowForeground);
        DrawTruncatedCentered(canvas, teamB, innerX, y + 29f, innerW, 18f, TeamFontSize, bold: false, style.DataRowForeground);
        DrawHCentered(canvas, score, x, y + 56f, BoxW, 18f, ScoreFontSize, bold: false, style.DataRowForeground);
    }

    // Mirrors ComputeRoundPositions in TournamentTabView
    private static List<float> ComputePositions(
        int roundIndex,
        int matchCount,
        IReadOnlyList<(int RoundNumber, List<TournamentMatch> Matches)> rounds,
        IReadOnlyDictionary<int, List<float>> posByRound)
    {
        float top = HeaderH + 16f;

        if (roundIndex == 0)
            return Enumerable.Range(0, matchCount).Select(i => top + i * (BoxH + VertGap)).ToList();

        var prev = posByRound[rounds[roundIndex - 1].RoundNumber];

        if (prev.Count == matchCount)
            return [.. prev];

        if (prev.Count == matchCount * 2)
        {
            var result = new List<float>(matchCount);
            for (int i = 0; i < matchCount; i++)
            {
                float c1 = prev[i * 2] + BoxH / 2f;
                float c2 = prev[i * 2 + 1] + BoxH / 2f;
                result.Add((c1 + c2) / 2f - BoxH / 2f);
            }
            return result;
        }

        return Enumerable.Range(0, matchCount).Select(i => top + i * (BoxH + VertGap)).ToList();
    }

    // Mirrors GetRoundLabel in TournamentTabView
    private static string GetRoundLabel(int roundNumber, int totalRounds)
    {
        if (roundNumber <= 2)
            return $"QUALIFICATION {roundNumber}";

        int knockoutIndex = roundNumber - 2;
        int knockoutCount = Math.Max(1, totalRounds - 2);
        int fromFinal = knockoutCount - knockoutIndex;

        return fromFinal switch
        {
            0 => "FINALE",
            1 => "DEMI-FINALE",
            2 => "QUART DE FINALE",
            3 => "8ÈMES DE FINALE",
            4 => "16ÈMES DE FINALE",
            5 => "32ÈMES DE FINALE",
            _ => $"TOUR ELIMINATOIRE {knockoutIndex}"
        };
    }

    private static void DrawHCentered(SKCanvas canvas, string text,
        float x, float y, float w, float h, float fontSize, bool bold, Color color)
    {
        DrawTruncatedCentered(canvas, text, x, y, w, h, fontSize, bold, color);
    }

    private static void DrawTruncatedCentered(SKCanvas canvas, string text,
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

        // Truncate with ellipsis if needed
        string display = TruncateWithEllipsis(text, cw - 4f, paint);
        float tw = paint.MeasureText(display);
        float tx = cx + (cw - tw) / 2f;
        float ty = Baseline(cy, ch, paint);

        canvas.Save();
        canvas.ClipRect(new SKRect(cx + 2f, cy, cx + cw - 2f, cy + ch));
        canvas.DrawText(display, tx, ty, paint);
        canvas.Restore();
    }

    private static string TruncateWithEllipsis(string text, float maxWidth, SKPaint paint)
    {
        if (paint.MeasureText(text) <= maxWidth)
            return text;

        const string ellipsis = "…";
        float ellipsisW = paint.MeasureText(ellipsis);

        for (int len = text.Length - 1; len > 0; len--)
        {
            string candidate = text[..len] + ellipsis;
            if (paint.MeasureText(candidate) <= maxWidth || paint.MeasureText(text[..len]) + ellipsisW <= maxWidth)
                return candidate;
        }

        return ellipsis;
    }

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
