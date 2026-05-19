using System.IO;
using System.Text.Json;
using ClosedXML.Excel;
using Padel.Manager.Models;

namespace Padel.Manager.Services;

public sealed class WorkbookService
{
    private const string BaseSheetName = "Base";
    private const string ScoresSheetName = "Scores";
    private const string PlanningSheetName = "PlanningApp";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    private readonly object _sync = new();
    private PadelDataFile? _cachedData;
    private DateTime _cachedLastWriteUtc;

    public WorkbookService(string dataPath, string? legacyWorkbookPath = null)
    {
        WorkbookPath = dataPath;
        EnsureStorageExists(legacyWorkbookPath);
    }

    public string WorkbookPath { get; }

    public (List<PlayerProfile> Players, List<PlayerScoreSummary> Summaries) LoadPlayersAndSummaries()
    {
        lock (_sync)
        {
            var data = LoadData();
            var players = BuildPlayers(data);
            var summaries = BuildPlayerScoreSummaries(players, data.ScoreEntries, data.Players);
            return (players, summaries);
        }
    }

    public (List<MatchHistoryEntry> History, List<PlayerScoreSummary> Summaries) LoadScoreTabData()
    {
        lock (_sync)
        {
            var data = LoadData();
            var players = BuildPlayers(data);
            var history = BuildMatchHistory(data);
            var summaries = BuildPlayerScoreSummaries(players, data.ScoreEntries, data.Players);
            return (history, summaries);
        }
    }

    public List<PlayerProfile> LoadPlayers()
    {
        lock (_sync)
        {
            var data = LoadData();
            return BuildPlayers(data);
        }
    }

    public Dictionary<string, double> LoadAveragePointsByPlayer()
    {
        lock (_sync)
        {
            var data = LoadData();
            var playersById = data.Players.ToDictionary(p => p.Id, p => p.Name);
            var stats = new Dictionary<string, (double Total, int Matches)>(StringComparer.OrdinalIgnoreCase);

            foreach (var score in data.ScoreEntries)
            {
                AddAverage(stats, playersById, score.TeamAPlayer1Id, score.TeamAPoints);
                AddAverage(stats, playersById, score.TeamAPlayer2Id, score.TeamAPoints);
                AddAverage(stats, playersById, score.TeamBPlayer1Id, score.TeamBPoints);
                AddAverage(stats, playersById, score.TeamBPlayer2Id, score.TeamBPoints);
            }

            return stats.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.Matches == 0 ? 0d : kvp.Value.Total / kvp.Value.Matches,
                StringComparer.OrdinalIgnoreCase);
        }
    }

    public void AddPlayer(PlayerProfile player)
    {
        lock (_sync)
        {
            var data = LoadData();
            if (data.Players.Any(p => string.Equals(p.Name, player.Name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Le joueur '{player.Name}' existe déjà.");
            }

            data.Players.Add(new PlayerEntry
            {
                Id = data.NextPlayerId++,
                Name = player.Name.Trim(),
                Level = player.Level
            });

            SaveData(data);
        }
    }

    public void UpdatePlayerLevel(string playerName, int level)
    {
        lock (_sync)
        {
            var data = LoadData();
            var player = data.Players.FirstOrDefault(p => string.Equals(p.Name, playerName, StringComparison.OrdinalIgnoreCase));
            if (player is null)
            {
                throw new InvalidOperationException($"Le joueur '{playerName}' est introuvable dans la base.");
            }

            player.Level = level;
            SaveData(data);
        }
    }

    public void UpdatePlayerLevels(IReadOnlyCollection<PlayerProfile> players)
    {
        if (players.Count == 0)
        {
            return;
        }

        lock (_sync)
        {
            var data = LoadData();
            var byName = data.Players.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var player in players)
            {
                if (!byName.TryGetValue(player.Name, out var entry))
                {
                    throw new InvalidOperationException($"Le joueur '{player.Name}' est introuvable dans la base.");
                }

                entry.Level = player.Level;
            }

            SaveData(data);
        }
    }

    public int GetNextMatchNumberForDate(DateTime date)
    {
        lock (_sync)
        {
            var data = LoadData();
            var target = date.Date;
            var max = data.ScoreEntries
                .Where(s => s.Date.Date == target)
                .Select(s => s.MatchNumber)
                .DefaultIfEmpty(0)
                .Max();
            return max + 1;
        }
    }

    public void AppendMatches(IReadOnlyCollection<MatchWriteModel> matches)
    {
        if (matches.Count == 0)
        {
            return;
        }

        lock (_sync)
        {
            var data = LoadData();
            var playerIds = BuildPlayerIdMap(data);

            foreach (var match in matches)
            {
                var points = ComputePoints(match.ScoreA, match.ScoreB);
                data.ScoreEntries.Add(new ScoreEntry
                {
                    Date = match.Date.Date,
                    MatchNumber = match.MatchNumber,
                    RoundNumber = match.RoundNumber,
                    TerrainNumber = match.TerrainNumber,
                    TeamAPlayer1Id = ResolvePlayerId(playerIds, match.TeamAPlayer1),
                    TeamAPlayer2Id = ResolvePlayerId(playerIds, match.TeamAPlayer2),
                    TeamBPlayer1Id = ResolvePlayerId(playerIds, match.TeamBPlayer1),
                    TeamBPlayer2Id = ResolvePlayerId(playerIds, match.TeamBPlayer2),
                    ScoreA = match.ScoreA,
                    ScoreB = match.ScoreB,
                    TeamAPoints = points.TeamAPoints,
                    TeamBPoints = points.TeamBPoints
                });
            }

            SaveData(data);
        }
    }

    public void SavePlannedMatches(DateTime date, IReadOnlyCollection<UiMatch> matches)
    {
        lock (_sync)
        {
            var data = LoadData();
            var playerIds = BuildPlayerIdMap(data);
            var targetDate = date.Date;

            data.PlannedEntries.RemoveAll(p => p.Date.Date == targetDate);

            foreach (var match in matches.OrderBy(m => m.RoundNumber).ThenBy(m => m.TerrainNumber))
            {
                data.PlannedEntries.Add(new PlannedEntry
                {
                    Date = targetDate,
                    RoundNumber = match.RoundNumber,
                    TerrainNumber = match.TerrainNumber,
                    TeamAPlayer1Id = ResolvePlayerId(playerIds, match.TeamAPlayer1),
                    TeamAPlayer2Id = ResolvePlayerId(playerIds, match.TeamAPlayer2),
                    TeamBPlayer1Id = ResolvePlayerId(playerIds, match.TeamBPlayer1),
                    TeamBPlayer2Id = ResolvePlayerId(playerIds, match.TeamBPlayer2),
                    ScoreA = int.TryParse(match.ScoreA, out var scoreA) ? scoreA : null,
                    ScoreB = int.TryParse(match.ScoreB, out var scoreB) ? scoreB : null
                });
            }

            SaveData(data);
        }
    }

    public List<PlannedMatchRow> LoadPlannedMatches(DateTime date)
    {
        lock (_sync)
        {
            var data = LoadData();
            var playersById = data.Players.ToDictionary(p => p.Id);
            var targetDate = date.Date;

            return data.PlannedEntries
                .Where(p => p.Date.Date == targetDate)
                .OrderBy(p => p.RoundNumber)
                .ThenBy(p => p.TerrainNumber)
                .Select(p => TryBuildPlannedRow(p, playersById))
                .Where(row => row is not null)
                .Select(row => row!)
                .ToList();
        }
    }

    public void SaveTournament(DateTime tournamentDate, IReadOnlyCollection<TournamentMatch> matches)
    {
        lock (_sync)
        {
            var data = LoadData();
            var targetDate = tournamentDate.Date;

            data.TournamentEntries.RemoveAll(t => t.TournamentDate.Date == targetDate);

            foreach (var match in matches.OrderBy(m => m.RoundNumber).ThenBy(m => m.MatchNumber))
            {
                data.TournamentEntries.Add(new TournamentEntry
                {
                    TournamentDate = targetDate,
                    MatchDate = match.MatchDate.Date,
                    RoundNumber = match.RoundNumber,
                    MatchNumber = match.MatchNumber,
                    TeamA = match.TeamA.Trim(),
                    TeamB = match.TeamB.Trim(),
                    SourceTeamA = match.SourceTeamA?.Trim(),
                    SourceTeamB = match.SourceTeamB?.Trim(),
                    ScoreA = TryParseNullableInt(match.ScoreA),
                    ScoreB = TryParseNullableInt(match.ScoreB)
                });
            }

            SaveData(data);
        }
    }

    public List<TournamentMatch> LoadTournament(DateTime tournamentDate)
    {
        lock (_sync)
        {
            var data = LoadData();
            var targetDate = tournamentDate.Date;

            return data.TournamentEntries
                .Where(t => t.TournamentDate.Date == targetDate)
                .OrderBy(t => t.RoundNumber)
                .ThenBy(t => t.MatchNumber)
                .Select(t => new TournamentMatch
                {
                    TournamentDate = t.TournamentDate.Date,
                    MatchDate = t.MatchDate.Date,
                    RoundNumber = t.RoundNumber,
                    MatchNumber = t.MatchNumber,
                    TeamA = t.TeamA,
                    TeamB = t.TeamB,
                    SourceTeamA = t.SourceTeamA,
                    SourceTeamB = t.SourceTeamB,
                    ScoreA = t.ScoreA?.ToString() ?? string.Empty,
                    ScoreB = t.ScoreB?.ToString() ?? string.Empty
                })
                .ToList();
        }
    }

    public List<PlannedMatchRow> LoadMatchesFromScores(DateTime date)
    {
        lock (_sync)
        {
            var data = LoadData();
            var playersById = data.Players.ToDictionary(p => p.Id);
            var targetDate = date.Date;

            var rows = data.ScoreEntries
                .Where(s => s.Date.Date == targetDate)
                .OrderBy(s => s.MatchNumber)
                .Select(s => TryBuildPlannedRowFromScore(s, playersById))
                .Where(row => row is not null)
                .Select(row => row!)
                .ToList();

            // Ensure display-friendly values even for legacy score rows with missing round/terrain.
            var terrainByRound = new Dictionary<int, int>();
            for (var i = 0; i < rows.Count; i++)
            {
                var source = rows[i];
                var normalizedRound = source.RoundNumber > 0 ? source.RoundNumber : 1;
                var normalizedTerrain = source.TerrainNumber;

                if (normalizedTerrain <= 0)
                {
                    terrainByRound.TryGetValue(normalizedRound, out var nextTerrain);
                    normalizedTerrain = nextTerrain + 1;
                    terrainByRound[normalizedRound] = normalizedTerrain;
                }

                rows[i] = new PlannedMatchRow
                {
                    Date = source.Date,
                    RoundNumber = normalizedRound,
                    TerrainNumber = normalizedTerrain,
                    TeamAPlayer1 = source.TeamAPlayer1,
                    TeamAPlayer2 = source.TeamAPlayer2,
                    TeamBPlayer1 = source.TeamBPlayer1,
                    TeamBPlayer2 = source.TeamBPlayer2,
                    ScoreA = source.ScoreA,
                    ScoreB = source.ScoreB
                };
            }

            return rows;
        }
    }

    public void UpdatePlannedMatchScores(DateTime date, IReadOnlyCollection<MatchWriteModel> matches)
    {
        lock (_sync)
        {
            var data = LoadData();
            var targetDate = date.Date;
            var lookup = matches.ToDictionary(
                m => (m.TerrainNumber, m.RoundNumber),
                m => (m.ScoreA, m.ScoreB));

            foreach (var planned in data.PlannedEntries.Where(p => p.Date.Date == targetDate))
            {
                if (!lookup.TryGetValue((planned.TerrainNumber, planned.RoundNumber), out var scores))
                {
                    continue;
                }

                planned.ScoreA = scores.ScoreA;
                planned.ScoreB = scores.ScoreB;
            }

            SaveData(data);
        }
    }

    public (int RemovedPlanned, int RemovedScores) DeleteMatch(DateTime date, int roundNumber, int terrainNumber)
    {
        lock (_sync)
        {
            var data = LoadData();
            var targetDate = date.Date;

            var removedPlanned = data.PlannedEntries.RemoveAll(p =>
                p.Date.Date == targetDate
                && p.RoundNumber == roundNumber
                && p.TerrainNumber == terrainNumber);

            var removedScores = data.ScoreEntries.RemoveAll(s =>
                s.Date.Date == targetDate
                && (
                    (s.RoundNumber == roundNumber && s.TerrainNumber == terrainNumber)
                    || (s.RoundNumber <= 0 && s.MatchNumber == roundNumber && (s.TerrainNumber == terrainNumber || s.TerrainNumber <= 0))
                ));

            if (removedPlanned > 0 || removedScores > 0)
            {
                SaveData(data);
            }

            return (removedPlanned, removedScores);
        }
    }

    public int DeleteScoreEntry(DateTime date, int matchNumber)
    {
        lock (_sync)
        {
            var data = LoadData();
            var targetDate = date.Date;

            var removed = data.ScoreEntries.RemoveAll(s =>
                s.Date.Date == targetDate
                && s.MatchNumber == matchNumber);

            if (removed > 0)
            {
                SaveData(data);
            }

            return removed;
        }
    }

    public List<MatchHistoryEntry> LoadMatchHistory()
    {
        lock (_sync)
        {
            var data = LoadData();
            return BuildMatchHistory(data);
        }
    }

    public List<PlayerScoreSummary> LoadPlayerScoreSummaries()
    {
        lock (_sync)
        {
            var data = LoadData();
            var players = BuildPlayers(data);
            return BuildPlayerScoreSummaries(players, data.ScoreEntries, data.Players);
        }
    }

    public int LoadLeaderboardPlayersPerSheet()
    {
        lock (_sync)
        {
            var data = LoadData();
            return Math.Clamp(data.LeaderboardPlayersPerSheet, 5, 200);
        }
    }

    public void SaveLeaderboardPlayersPerSheet(int playersPerSheet)
    {
        lock (_sync)
        {
            var data = LoadData();
            data.LeaderboardPlayersPerSheet = Math.Clamp(playersPerSheet, 5, 200);
            SaveData(data);
        }
    }

    public void ExportToXlsx(string outputPath)
    {
        lock (_sync)
        {
            var data = LoadData();
            var playersById = data.Players.ToDictionary(p => p.Id);

            using var workbook = new XLWorkbook();

            var baseSheet = workbook.AddWorksheet(BaseSheetName);
            baseSheet.Cell(1, 1).Value = "Nom";
            baseSheet.Cell(1, 2).Value = "Niveau";
            baseSheet.Row(1).Style.Font.Bold = true;

            var baseRow = 2;
            foreach (var player in data.Players.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                baseSheet.Cell(baseRow, 1).Value = player.Name;
                baseSheet.Cell(baseRow, 2).Value = player.Level;
                baseRow++;
            }

            var scoresSheet = workbook.AddWorksheet(ScoresSheetName);
            scoresSheet.Cell(1, 1).Value = "Date";
            scoresSheet.Cell(1, 2).Value = "Match";
            scoresSheet.Cell(1, 3).Value = "EquipeA_J1";
            scoresSheet.Cell(1, 4).Value = "EquipeA_J2";
            scoresSheet.Cell(1, 5).Value = "EquipeB_J1";
            scoresSheet.Cell(1, 6).Value = "EquipeB_J2";
            scoresSheet.Cell(1, 7).Value = "ScoreA";
            scoresSheet.Cell(1, 8).Value = "ScoreB";
            scoresSheet.Cell(1, 10).Value = "PtsA";
            scoresSheet.Cell(1, 11).Value = "PtsB";
            scoresSheet.Row(1).Style.Font.Bold = true;

            var scoreRow = 2;
            foreach (var score in data.ScoreEntries.OrderBy(s => s.Date).ThenBy(s => s.MatchNumber))
            {
                if (!TryResolvePlayerNames(playersById, score.TeamAPlayer1Id, score.TeamAPlayer2Id, score.TeamBPlayer1Id, score.TeamBPlayer2Id,
                        out var a1, out var a2, out var b1, out var b2))
                {
                    continue;
                }

                scoresSheet.Cell(scoreRow, 1).Value = score.Date.Date;
                scoresSheet.Cell(scoreRow, 2).Value = score.MatchNumber;
                scoresSheet.Cell(scoreRow, 3).Value = a1;
                scoresSheet.Cell(scoreRow, 4).Value = a2;
                scoresSheet.Cell(scoreRow, 5).Value = b1;
                scoresSheet.Cell(scoreRow, 6).Value = b2;
                scoresSheet.Cell(scoreRow, 7).Value = score.ScoreA;
                scoresSheet.Cell(scoreRow, 8).Value = score.ScoreB;
                scoresSheet.Cell(scoreRow, 10).Value = score.TeamAPoints;
                scoresSheet.Cell(scoreRow, 11).Value = score.TeamBPoints;
                scoreRow++;
            }

            var planningSheet = workbook.AddWorksheet(PlanningSheetName);
            planningSheet.Cell(1, 1).Value = "Date";
            planningSheet.Cell(1, 2).Value = "Rotation";
            planningSheet.Cell(1, 3).Value = "Terrain";
            planningSheet.Cell(1, 4).Value = "EquipeA_J1";
            planningSheet.Cell(1, 5).Value = "EquipeA_J2";
            planningSheet.Cell(1, 6).Value = "EquipeB_J1";
            planningSheet.Cell(1, 7).Value = "EquipeB_J2";
            planningSheet.Cell(1, 8).Value = "ScoreA";
            planningSheet.Cell(1, 9).Value = "ScoreB";
            planningSheet.Row(1).Style.Font.Bold = true;

            var planningRow = 2;
            foreach (var planned in data.PlannedEntries.OrderBy(p => p.Date).ThenBy(p => p.RoundNumber).ThenBy(p => p.TerrainNumber))
            {
                if (!TryResolvePlayerNames(playersById, planned.TeamAPlayer1Id, planned.TeamAPlayer2Id, planned.TeamBPlayer1Id, planned.TeamBPlayer2Id,
                        out var a1, out var a2, out var b1, out var b2))
                {
                    continue;
                }

                planningSheet.Cell(planningRow, 1).Value = planned.Date.Date;
                planningSheet.Cell(planningRow, 2).Value = planned.RoundNumber;
                planningSheet.Cell(planningRow, 3).Value = planned.TerrainNumber;
                planningSheet.Cell(planningRow, 4).Value = a1;
                planningSheet.Cell(planningRow, 5).Value = a2;
                planningSheet.Cell(planningRow, 6).Value = b1;
                planningSheet.Cell(planningRow, 7).Value = b2;
                if (planned.ScoreA.HasValue)
                {
                    planningSheet.Cell(planningRow, 8).Value = planned.ScoreA.Value;
                }

                if (planned.ScoreB.HasValue)
                {
                    planningSheet.Cell(planningRow, 9).Value = planned.ScoreB.Value;
                }

                planningRow++;
            }

            baseSheet.Columns().AdjustToContents();
            scoresSheet.Columns().AdjustToContents();
            planningSheet.Columns().AdjustToContents();

            workbook.SaveAs(outputPath);
        }
    }

    private void EnsureStorageExists(string? legacyWorkbookPath)
    {
        var directory = Path.GetDirectoryName(WorkbookPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(WorkbookPath))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(legacyWorkbookPath) && File.Exists(legacyWorkbookPath))
        {
            var imported = ImportFromXlsx(legacyWorkbookPath);
            SaveData(imported);
            return;
        }

        SaveData(new PadelDataFile());
    }

    private PadelDataFile LoadData()
    {
        if (!File.Exists(WorkbookPath))
        {
            return new PadelDataFile();
        }

        var lastWriteUtc = File.GetLastWriteTimeUtc(WorkbookPath);
        if (_cachedData is not null && _cachedLastWriteUtc == lastWriteUtc)
        {
            return _cachedData;
        }

        using var stream = File.Open(WorkbookPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (stream.Length == 0)
        {
            _cachedData = new PadelDataFile();
            _cachedLastWriteUtc = lastWriteUtc;
            return _cachedData;
        }

        _cachedData = JsonSerializer.Deserialize<PadelDataFile>(stream, JsonOptions) ?? new PadelDataFile();
        _cachedLastWriteUtc = lastWriteUtc;
        return _cachedData;
    }

    private void SaveData(PadelDataFile data)
    {
        if (data.NextPlayerId <= 0)
        {
            data.NextPlayerId = data.Players.Count == 0 ? 1 : data.Players.Max(p => p.Id) + 1;
        }

        using (var stream = File.Open(WorkbookPath, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            JsonSerializer.Serialize(stream, data, JsonOptions);
        }

        _cachedData = data;
        _cachedLastWriteUtc = File.GetLastWriteTimeUtc(WorkbookPath);
    }

    private static Dictionary<string, int> BuildPlayerIdMap(PadelDataFile data)
    {
        return data.Players.ToDictionary(p => p.Name, p => p.Id, StringComparer.OrdinalIgnoreCase);
    }

    private static int ResolvePlayerId(IReadOnlyDictionary<string, int> playerIds, string playerName)
    {
        if (playerIds.TryGetValue(playerName, out var id))
        {
            return id;
        }

        throw new InvalidOperationException($"Le joueur '{playerName}' est introuvable dans la base.");
    }

    private static List<PlayerProfile> BuildPlayers(PadelDataFile data)
    {
        return data.Players
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => new PlayerProfile
            {
                Name = p.Name,
                Level = p.Level
            })
            .ToList();
    }

    private static List<MatchHistoryEntry> BuildMatchHistory(PadelDataFile data)
    {
        var playersById = data.Players.ToDictionary(p => p.Id);
        var entries = new List<MatchHistoryEntry>();

        foreach (var score in data.ScoreEntries)
        {
            if (!TryResolvePlayerNames(playersById, score.TeamAPlayer1Id, score.TeamAPlayer2Id, score.TeamBPlayer1Id, score.TeamBPlayer2Id,
                    out var a1, out var a2, out var b1, out var b2))
            {
                continue;
            }

            entries.Add(new MatchHistoryEntry
            {
                Date = score.Date,
                MatchNumber = score.MatchNumber,
                TeamAPlayer1 = a1,
                TeamAPlayer2 = a2,
                TeamBPlayer1 = b1,
                TeamBPlayer2 = b2,
                ScoreA = score.ScoreA,
                ScoreB = score.ScoreB,
                TeamAPoints = score.TeamAPoints,
                TeamBPoints = score.TeamBPoints
            });
        }

        return entries
            .OrderByDescending(e => e.Date)
            .ThenByDescending(e => e.MatchNumber)
            .ToList();
    }

    private static List<PlayerScoreSummary> BuildPlayerScoreSummaries(
        List<PlayerProfile> players,
        List<ScoreEntry> scores,
        List<PlayerEntry> playerEntries)
    {
        var namesById = playerEntries.ToDictionary(p => p.Id, p => p.Name);
        var mappedScores = scores
            .OrderBy(s => s.Date)
            .ThenBy(s => s.MatchNumber)
            .Select(s => new
            {
                TeamAPlayer1 = namesById.TryGetValue(s.TeamAPlayer1Id, out var a1) ? a1 : string.Empty,
                TeamAPlayer2 = namesById.TryGetValue(s.TeamAPlayer2Id, out var a2) ? a2 : string.Empty,
                TeamBPlayer1 = namesById.TryGetValue(s.TeamBPlayer1Id, out var b1) ? b1 : string.Empty,
                TeamBPlayer2 = namesById.TryGetValue(s.TeamBPlayer2Id, out var b2) ? b2 : string.Empty,
                s.TeamAPoints,
                s.TeamBPoints
            }).Where(s => !string.IsNullOrWhiteSpace(s.TeamAPlayer1)
                       && !string.IsNullOrWhiteSpace(s.TeamAPlayer2)
                       && !string.IsNullOrWhiteSpace(s.TeamBPlayer1)
                       && !string.IsNullOrWhiteSpace(s.TeamBPlayer2));

        var stats = new Dictionary<string, PlayerMatchStats>(StringComparer.OrdinalIgnoreCase);
        foreach (var player in players)
        {
            stats[player.Name] = new PlayerMatchStats();
        }

        foreach (var score in mappedScores)
        {
            AddPlayerMatchStats(stats, score.TeamAPlayer1, score.TeamAPoints);
            AddPlayerMatchStats(stats, score.TeamAPlayer2, score.TeamAPoints);
            AddPlayerMatchStats(stats, score.TeamBPlayer1, score.TeamBPoints);
            AddPlayerMatchStats(stats, score.TeamBPlayer2, score.TeamBPoints);
        }

        return players
            .Select(player =>
            {
                var stat = stats.TryGetValue(player.Name, out var value)
                    ? value
                    : new PlayerMatchStats();
                var overallAvg = stat.Played == 0 ? 0d : (double)stat.Points / stat.Played;
                var last3 = stat.MatchPoints.Count > 3
                    ? stat.MatchPoints.GetRange(stat.MatchPoints.Count - 3, 3)
                    : stat.MatchPoints;
                var recentAvg = last3.Count == 0 ? 0d : last3.Average();
                return new PlayerScoreSummary
                {
                    Name = player.Name,
                    Level = player.Level,
                    MatchesPlayed = stat.Played,
                    MatchesWon = stat.Won,
                    TotalPoints = stat.Points,
                    AveragePoints = overallAvg,
                    RecentMatchCount = last3.Count,
                    RecentAverage = recentAvg,
                    RecentDelta = last3.Count == 0 ? 0d : recentAvg - overallAvg
                };
            })
            .OrderByDescending(summary => summary.TotalPoints)
            .ThenByDescending(summary => summary.MatchesWon)
            .ThenBy(summary => summary.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static PlannedMatchRow? TryBuildPlannedRow(PlannedEntry planned, IReadOnlyDictionary<int, PlayerEntry> playersById)
    {
        if (!TryResolvePlayerNames(playersById, planned.TeamAPlayer1Id, planned.TeamAPlayer2Id, planned.TeamBPlayer1Id, planned.TeamBPlayer2Id,
                out var a1, out var a2, out var b1, out var b2))
        {
            return null;
        }

        return new PlannedMatchRow
        {
            Date = planned.Date,
            RoundNumber = planned.RoundNumber,
            TerrainNumber = planned.TerrainNumber,
            TeamAPlayer1 = a1,
            TeamAPlayer2 = a2,
            TeamBPlayer1 = b1,
            TeamBPlayer2 = b2,
            ScoreA = planned.ScoreA,
            ScoreB = planned.ScoreB
        };
    }

    private static PlannedMatchRow? TryBuildPlannedRowFromScore(ScoreEntry score, IReadOnlyDictionary<int, PlayerEntry> playersById)
    {
        if (!TryResolvePlayerNames(playersById, score.TeamAPlayer1Id, score.TeamAPlayer2Id, score.TeamBPlayer1Id, score.TeamBPlayer2Id,
                out var a1, out var a2, out var b1, out var b2))
        {
            return null;
        }

        return new PlannedMatchRow
        {
            Date = score.Date,
            // Older entries may not have explicit round numbers. In that case, MatchNumber is the best rotation key.
            RoundNumber = score.RoundNumber > 0 ? score.RoundNumber : score.MatchNumber,
            TerrainNumber = score.TerrainNumber,
            TeamAPlayer1 = a1,
            TeamAPlayer2 = a2,
            TeamBPlayer1 = b1,
            TeamBPlayer2 = b2,
            ScoreA = score.ScoreA,
            ScoreB = score.ScoreB
        };
    }

    private static bool TryResolvePlayerNames(
        IReadOnlyDictionary<int, PlayerEntry> playersById,
        int teamAPlayer1Id,
        int teamAPlayer2Id,
        int teamBPlayer1Id,
        int teamBPlayer2Id,
        out string teamAPlayer1,
        out string teamAPlayer2,
        out string teamBPlayer1,
        out string teamBPlayer2)
    {
        teamAPlayer1 = string.Empty;
        teamAPlayer2 = string.Empty;
        teamBPlayer1 = string.Empty;
        teamBPlayer2 = string.Empty;

        if (!playersById.TryGetValue(teamAPlayer1Id, out var a1)
            || !playersById.TryGetValue(teamAPlayer2Id, out var a2)
            || !playersById.TryGetValue(teamBPlayer1Id, out var b1)
            || !playersById.TryGetValue(teamBPlayer2Id, out var b2))
        {
            return false;
        }

        teamAPlayer1 = a1.Name;
        teamAPlayer2 = a2.Name;
        teamBPlayer1 = b1.Name;
        teamBPlayer2 = b2.Name;
        return true;
    }

    private static void AddAverage(
        IDictionary<string, (double Total, int Matches)> stats,
        IReadOnlyDictionary<int, string> playersById,
        int playerId,
        int points)
    {
        if (!playersById.TryGetValue(playerId, out var name))
        {
            return;
        }

        if (!stats.TryGetValue(name, out var stat))
        {
            stats[name] = (points, 1);
            return;
        }

        stats[name] = (stat.Total + points, stat.Matches + 1);
    }

    private static (int TeamAPoints, int TeamBPoints) ComputePoints(int scoreA, int scoreB)
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

    private static void AddPlayerMatchStats(IDictionary<string, PlayerMatchStats> stats, string playerName, int points)
    {
        if (!stats.TryGetValue(playerName, out var stat))
        {
            stat = new PlayerMatchStats();
            stats[playerName] = stat;
        }

        stat.Played++;
        if (points == 3) stat.Won++;
        stat.Points += points;
        stat.MatchPoints.Add(points);
    }

    private static PadelDataFile ImportFromXlsx(string workbookPath)
    {
        using var workbook = new XLWorkbook(workbookPath);
        var data = new PadelDataFile();

        var baseSheet = workbook.Worksheet(BaseSheetName);
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

        if (workbook.TryGetWorksheet(ScoresSheetName, out var scoresSheet) && scoresSheet is not null)
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

                if (!TryResolveLegacyPlayerId(playerIds, a1, out var a1Id)
                    || !TryResolveLegacyPlayerId(playerIds, a2, out var a2Id)
                    || !TryResolveLegacyPlayerId(playerIds, b1, out var b1Id)
                    || !TryResolveLegacyPlayerId(playerIds, b2, out var b2Id))
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

        if (workbook.TryGetWorksheet(PlanningSheetName, out var planningSheet) && planningSheet is not null)
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

                if (!TryResolveLegacyPlayerId(playerIds, a1, out var a1Id)
                    || !TryResolveLegacyPlayerId(playerIds, a2, out var a2Id)
                    || !TryResolveLegacyPlayerId(playerIds, b1, out var b1Id)
                    || !TryResolveLegacyPlayerId(playerIds, b2, out var b2Id))
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

    private static bool TryResolveLegacyPlayerId(
        IReadOnlyDictionary<string, int> playerIds,
        string playerName,
        out int id)
    {
        return playerIds.TryGetValue(playerName, out id);
    }

    private static DateTime? TryReadDate(IXLCell cell)
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

    private static int ParseInt(string? input, int defaultValue)
    {
        return int.TryParse(input, out var value) ? value : defaultValue;
    }

    private static int? TryParseNullableInt(string? input)
    {
        return int.TryParse(input, out var value) ? value : null;
    }

    private sealed class PlayerMatchStats
    {
        public int Played;
        public int Won;
        public int Points;
        public readonly List<int> MatchPoints = [];
    }

    private sealed class PadelDataFile
    {
        public int Version { get; set; } = 1;

        public int NextPlayerId { get; set; } = 1;

        public int LeaderboardPlayersPerSheet { get; set; } = 20;

        public List<PlayerEntry> Players { get; set; } = new();

        public List<ScoreEntry> ScoreEntries { get; set; } = new();

        public List<PlannedEntry> PlannedEntries { get; set; } = new();

        public List<TournamentEntry> TournamentEntries { get; set; } = new();
    }

    private sealed class PlayerEntry
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public int Level { get; set; }
    }

    private sealed class ScoreEntry
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

    private sealed class PlannedEntry
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

    private sealed class TournamentEntry
    {
        public DateTime TournamentDate { get; set; }

        public DateTime MatchDate { get; set; }

        public int RoundNumber { get; set; }

        public int MatchNumber { get; set; }

        public string TeamA { get; set; } = string.Empty;

        public string TeamB { get; set; } = string.Empty;

        public string? SourceTeamA { get; set; }

        public string? SourceTeamB { get; set; }

        public int? ScoreA { get; set; }

        public int? ScoreB { get; set; }
    }
}
