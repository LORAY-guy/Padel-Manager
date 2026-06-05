using ClosedXML.Excel;
using Padel.Core.Model;

namespace Padel.Server;

/// <summary>Builds an .xlsx export of a dataset (Base + Scores sheets) like the desktop app.</summary>
public static class ExcelExport
{
    public static byte[] Build(PadelDataFile data)
    {
        var namesById = data.Players.ToDictionary(p => p.Id, p => p.Name);

        using var workbook = new XLWorkbook();

        var baseSheet = workbook.AddWorksheet("Base");
        baseSheet.Cell(1, 1).Value = "Nom";
        baseSheet.Cell(1, 2).Value = "Niveau";
        baseSheet.Row(1).Style.Font.Bold = true;
        var row = 2;
        foreach (var p in data.Players.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            baseSheet.Cell(row, 1).Value = p.Name;
            baseSheet.Cell(row, 2).Value = p.Level;
            row++;
        }

        var scores = workbook.AddWorksheet("Scores");
        string[] headers = { "Date", "Match", "EquipeA_J1", "EquipeA_J2", "EquipeB_J1", "EquipeB_J2", "ScoreA", "ScoreB", "PtsA", "PtsB" };
        for (var c = 0; c < headers.Length; c++)
        {
            scores.Cell(1, c + 1).Value = headers[c];
        }
        scores.Row(1).Style.Font.Bold = true;

        var sr = 2;
        foreach (var s in data.ScoreEntries.OrderBy(s => s.Date).ThenBy(s => s.MatchNumber))
        {
            if (!namesById.TryGetValue(s.TeamAPlayer1Id, out var a1) || !namesById.TryGetValue(s.TeamAPlayer2Id, out var a2)
                || !namesById.TryGetValue(s.TeamBPlayer1Id, out var b1) || !namesById.TryGetValue(s.TeamBPlayer2Id, out var b2))
            {
                continue;
            }

            scores.Cell(sr, 1).Value = s.Date.Date;
            scores.Cell(sr, 2).Value = s.MatchNumber;
            scores.Cell(sr, 3).Value = a1;
            scores.Cell(sr, 4).Value = a2;
            scores.Cell(sr, 5).Value = b1;
            scores.Cell(sr, 6).Value = b2;
            scores.Cell(sr, 7).Value = s.ScoreA;
            scores.Cell(sr, 8).Value = s.ScoreB;
            scores.Cell(sr, 9).Value = s.TeamAPoints;
            scores.Cell(sr, 10).Value = s.TeamBPoints;
            sr++;
        }

        baseSheet.Columns().AdjustToContents();
        scores.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
