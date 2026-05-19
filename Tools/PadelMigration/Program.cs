using System.Text.Json;
using ClosedXML.Excel;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: PadelMigration <input.xlsx> <output.padel>");
    return 1;
}

var inputPath = args[0];
var outputPath = args[1];

if (!File.Exists(inputPath))
{
    Console.Error.WriteLine($"Input XLSX not found: {inputPath}");
    return 2;
}

var outputDir = Path.GetDirectoryName(outputPath);
if (!string.IsNullOrWhiteSpace(outputDir))
{
    Directory.CreateDirectory(outputDir);
}

var data = ImportFromXlsx(inputPath);
var json = JsonSerializer.Serialize(data);
File.WriteAllText(outputPath, json);

Console.WriteLine($"Converted: {inputPath}");
Console.WriteLine($"Output: {outputPath}");
Console.WriteLine($"Players: {data.Players.Count}, Scores: {data.ScoreEntries.Count}, Planning: {data.PlannedEntries.Count}");

return 0;

static PadelDataFile ImportFromXlsx(string workbookPath)
{
    using var workbook = new XLWorkbook(workbookPath);
    var data = new PadelDataFile();

    var baseSheet = workbook.Worksheet("Base");
    var row = 2;
    while (true)
    {
        var name = baseSheet.Cell(row, 1).GetString().Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            break;
        }

        data.Players.Add(new PlayerEntry
        {
            Id = data.NextPlayerId++,
            Name = name,
            Level = ParseInt(baseSheet.Cell(row, 2).GetString(), 3)
        });

        row++;
    }

    var playerIds = data.Players.ToDictionary(p => p.Name, p => p.Id, StringComparer.OrdinalIgnoreCase);

    if (workbook.TryGetWorksheet("Scores", out var scoresSheet) && scoresSheet is not null)
    {
        var lastRow = scoresSheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var r = 2; r <= lastRow; r++)
        {
            var a1 = scoresSheet.Cell(r, 3).GetString().Trim();
            var a2 = scoresSheet.Cell(r, 4).GetString().Trim();
            var b1 = scoresSheet.Cell(r, 5).GetString().Trim();
            var b2 = scoresSheet.Cell(r, 6).GetString().Trim();
            if (string.IsNullOrWhiteSpace(a1) || string.IsNullOrWhiteSpace(a2) || string.IsNullOrWhiteSpace(b1) || string.IsNullOrWhiteSpace(b2))
            {
                continue;
            }

            if (!TryResolvePlayerId(playerIds, a1, out var a1Id)
                || !TryResolvePlayerId(playerIds, a2, out var a2Id)
                || !TryResolvePlayerId(playerIds, b1, out var b1Id)
                || !TryResolvePlayerId(playerIds, b2, out var b2Id))
            {
                continue;
            }

            var scoreA = ParseInt(scoresSheet.Cell(r, 7).GetString(), 0);
            var scoreB = ParseInt(scoresSheet.Cell(r, 8).GetString(), 0);
            var points = ComputePoints(scoreA, scoreB);

            data.ScoreEntries.Add(new ScoreEntry
            {
                Date = TryReadDate(scoresSheet.Cell(r, 1)) ?? DateTime.MinValue,
                MatchNumber = ParseInt(scoresSheet.Cell(r, 2).GetString(), 0),
                RoundNumber = 0,
                TerrainNumber = 0,
                TeamAPlayer1Id = a1Id,
                TeamAPlayer2Id = a2Id,
                TeamBPlayer1Id = b1Id,
                TeamBPlayer2Id = b2Id,
                ScoreA = scoreA,
                ScoreB = scoreB,
                TeamAPoints = ParseInt(scoresSheet.Cell(r, 10).GetString(), points.TeamAPoints),
                TeamBPoints = ParseInt(scoresSheet.Cell(r, 11).GetString(), points.TeamBPoints)
            });
        }
    }

    if (workbook.TryGetWorksheet("PlanningApp", out var planningSheet) && planningSheet is not null)
    {
        var lastRow = planningSheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var r = 2; r <= lastRow; r++)
        {
            var a1 = planningSheet.Cell(r, 4).GetString().Trim();
            var a2 = planningSheet.Cell(r, 5).GetString().Trim();
            var b1 = planningSheet.Cell(r, 6).GetString().Trim();
            var b2 = planningSheet.Cell(r, 7).GetString().Trim();
            if (string.IsNullOrWhiteSpace(a1) || string.IsNullOrWhiteSpace(a2) || string.IsNullOrWhiteSpace(b1) || string.IsNullOrWhiteSpace(b2))
            {
                continue;
            }

            if (!TryResolvePlayerId(playerIds, a1, out var a1Id)
                || !TryResolvePlayerId(playerIds, a2, out var a2Id)
                || !TryResolvePlayerId(playerIds, b1, out var b1Id)
                || !TryResolvePlayerId(playerIds, b2, out var b2Id))
            {
                continue;
            }

            data.PlannedEntries.Add(new PlannedEntry
            {
                Date = TryReadDate(planningSheet.Cell(r, 1)) ?? DateTime.MinValue,
                RoundNumber = ParseInt(planningSheet.Cell(r, 2).GetString(), 0),
                TerrainNumber = ParseInt(planningSheet.Cell(r, 3).GetString(), 0),
                TeamAPlayer1Id = a1Id,
                TeamAPlayer2Id = a2Id,
                TeamBPlayer1Id = b1Id,
                TeamBPlayer2Id = b2Id,
                ScoreA = TryParseNullableInt(planningSheet.Cell(r, 8).GetString()),
                ScoreB = TryParseNullableInt(planningSheet.Cell(r, 9).GetString())
            });
        }
    }

    return data;
}

static bool TryResolvePlayerId(IReadOnlyDictionary<string, int> playerIds, string playerName, out int id)
{
    return playerIds.TryGetValue(playerName, out id);
}

static DateTime? TryReadDate(IXLCell cell)
{
    if (cell.TryGetValue<DateTime>(out var dateValue))
    {
        return dateValue.Date;
    }

    if (double.TryParse(cell.GetString(), out var serialDate))
    {
        try
        {
            return DateTime.FromOADate(serialDate).Date;
        }
        catch
        {
            return null;
        }
    }

    var text = cell.GetString().Trim();
    return DateTime.TryParse(text, out var parsedDate) ? parsedDate.Date : null;
}

static int ParseInt(string? input, int defaultValue)
{
    return int.TryParse(input, out var value) ? value : defaultValue;
}

static int? TryParseNullableInt(string? input)
{
    return int.TryParse(input, out var value) ? value : null;
}

static (int TeamAPoints, int TeamBPoints) ComputePoints(int scoreA, int scoreB)
{
    if (scoreA > scoreB)
    {
        return (3, 1);
    }

    if (scoreA < scoreB)
    {
        return (1, 3);
    }

    return (2, 2);
}

public sealed class PadelDataFile
{
    public int Version { get; set; } = 1;
    public int NextPlayerId { get; set; } = 1;
    public List<PlayerEntry> Players { get; set; } = new();
    public List<ScoreEntry> ScoreEntries { get; set; } = new();
    public List<PlannedEntry> PlannedEntries { get; set; } = new();
}

public sealed class PlayerEntry
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Level { get; set; }
}

public sealed class ScoreEntry
{
    public DateTime Date { get; set; }
    public int MatchNumber { get; set; }
    public int RoundNumber { get; set; }
    public int TerrainNumber { get; set; }
    public int TeamAPlayer1Id { get; set; }
    public int TeamAPlayer2Id { get; set; }
    public int TeamBPlayer1Id { get; set; }
    public int TeamBPlayer2Id { get; set; }
    public int ScoreA { get; set; }
    public int ScoreB { get; set; }
    public int TeamAPoints { get; set; }
    public int TeamBPoints { get; set; }
}

public sealed class PlannedEntry
{
    public DateTime Date { get; set; }
    public int RoundNumber { get; set; }
    public int TerrainNumber { get; set; }
    public int TeamAPlayer1Id { get; set; }
    public int TeamAPlayer2Id { get; set; }
    public int TeamBPlayer1Id { get; set; }
    public int TeamBPlayer2Id { get; set; }
    public int? ScoreA { get; set; }
    public int? ScoreB { get; set; }
}
