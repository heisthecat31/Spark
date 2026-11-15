// Ported from the Replay Analyser (EchoAnalyser.Core/Model/MatchSummary.cs). Logic is unchanged; only the
// namespace, explicit usings and nullable context differ, so fixes can be diffed against
// the original.
#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spark.ReplayAnalyser.Model;

/// <summary>One player's line in a <see cref="MatchSummary"/>.</summary>
public sealed class PlayerSummary
{
    public string Name { get; set; } = "";
    public long UserId { get; set; }
    public int Side { get; set; }
    /// <summary>The game's own personal-score counter, which private matches inflate.</summary>
    public int Points { get; set; }
    /// <summary>Points actually put on the board — this reconciles with the team score.</summary>
    public int GoalPoints { get; set; }
    public int Goals { get; set; }
    public int Assists { get; set; }
    public int Saves { get; set; }
    public int Stuns { get; set; }
    public int Steals { get; set; }
    public int Interceptions { get; set; }
    public int Passes { get; set; }
    /// <summary>Handovers counted from the disc changing hands, for matches reporting no passes.</summary>
    public int Handovers { get; set; }
    public int Turnovers { get; set; }
    public int ShotsTaken { get; set; }
    public double PossessionTime { get; set; }
    public double SecondsPlayed { get; set; }
    public double DistanceTravelled { get; set; }
    public double AverageSpeed { get; set; }
    public double DefensiveThirdShare { get; set; }
    public double OffensiveThirdShare { get; set; }
    public bool HasThrowTelemetry { get; set; }
    public double AverageThrowSpeed { get; set; }

    // ---- errors we could identify frame by frame ----
    public int GrabChances { get; set; }
    public int MissedGrabs { get; set; }
    public int LostContests { get; set; }
    public int CarriedIntoStun { get; set; }
    public int TurnoversAtHome { get; set; }
    public double AveragePossessionSeconds { get; set; }
    public double SecondsInGoalieBox { get; set; }
    public double SecondsAsLastDefender { get; set; }
    public double AverageFieldProgress { get; set; }

    public double GrabSuccessRate => GrabChances == 0 ? 0 : 1 - (double)MissedGrabs / GrabChances;
    public double MissedGrabsPerMinute => SecondsPlayed <= 0 ? 0 : MissedGrabs / (SecondsPlayed / 60.0);

    public double PointsPerMinute => SecondsPlayed <= 0 ? 0 : GoalPoints / (SecondsPlayed / 60.0);
    public double StunsPerMinute => SecondsPlayed <= 0 ? 0 : Stuns / (SecondsPlayed / 60.0);
    public double TurnoversPerMinute => SecondsPlayed <= 0 ? 0 : Turnovers / (SecondsPlayed / 60.0);
}

/// <summary>
/// The condensed result of analysing one replay — enough to browse, filter and chart a
/// whole library without re-parsing gigabytes of frames every time the app starts.
/// </summary>
public sealed class MatchSummary
{
    public string FilePath { get; set; } = "";
    public long FileSize { get; set; }
    public long FileStamp { get; set; }
    public string Format { get; set; } = "";
    public string MapName { get; set; } = "";
    public string MatchType { get; set; } = "";
    public bool PrivateMatch { get; set; }
    public DateTime Recorded { get; set; }
    public double DurationSeconds { get; set; }
    public double LiveSeconds { get; set; }
    public int FrameCount { get; set; }
    public int BlueScore { get; set; }
    public int OrangeScore { get; set; }
    public int GoalCount { get; set; }
    public bool IsPartial { get; set; }
    public string? RecordingPlayer { get; set; }
    public string? Error { get; set; }
    public List<PlayerSummary> Players { get; set; } = new();

    [JsonIgnore] public string FileName => Path.GetFileName(FilePath);
    [JsonIgnore] public TimeSpan Duration => TimeSpan.FromSeconds(DurationSeconds);
    [JsonIgnore] public bool IsArena => MapName == "mpl_arena_a";
    [JsonIgnore] public bool Ok => Error == null && FrameCount > 0;
    [JsonIgnore] public string ScoreLine => $"{BlueScore} – {OrangeScore}";

    public static MatchSummary From(MatchAnalysis m, FileInfo fi) => new()
    {
        FilePath = m.FilePath,
        FileSize = fi.Length,
        FileStamp = fi.LastWriteTimeUtc.Ticks,
        Format = m.Format,
        MapName = m.MapName,
        MatchType = m.MatchType,
        PrivateMatch = m.PrivateMatch,
        Recorded = m.StartTime,
        DurationSeconds = m.Duration.TotalSeconds,
        LiveSeconds = m.LiveSeconds,
        FrameCount = m.FrameCount,
        BlueScore = m.BlueScore,
        OrangeScore = m.OrangeScore,
        GoalCount = m.Goals.Count,
        IsPartial = m.IsPartial,
        RecordingPlayer = m.RecordingPlayer,
        Players = m.AllPlayers.Select(p => new PlayerSummary
        {
            Name = p.Name,
            UserId = p.UserId,
            Side = (int)p.Side,
            Points = p.Points,
            GoalPoints = p.GoalPoints,
            Goals = p.Goals,
            Assists = p.Assists,
            Saves = p.Saves,
            Stuns = p.Stuns,
            Steals = p.Steals,
            Interceptions = p.Interceptions,
            Passes = p.Passes,
            Handovers = p.Handovers,
            Turnovers = p.Turnovers,
            ShotsTaken = p.ShotsTaken,
            PossessionTime = p.PossessionTime,
            SecondsPlayed = p.SecondsPlayed,
            DistanceTravelled = p.DistanceTravelled,
            AverageSpeed = p.AverageSpeed,
            DefensiveThirdShare = p.DefensiveThirdShare,
            OffensiveThirdShare = p.OffensiveThirdShare,
            HasThrowTelemetry = p.HasThrowTelemetry,
            AverageThrowSpeed = p.AverageThrowSpeed,
            GrabChances = p.GrabChances,
            MissedGrabs = p.MissedGrabs,
            LostContests = p.LostContests,
            CarriedIntoStun = p.CarriedIntoStun,
            TurnoversAtHome = p.TurnoversAtHome,
            AveragePossessionSeconds = p.AveragePossessionSeconds,
            SecondsInGoalieBox = p.SecondsInGoalieBox,
            SecondsAsLastDefender = p.SecondsAsLastDefender,
            AverageFieldProgress = p.AverageFieldProgress,
        }).ToList(),
    };
}

/// <summary>
/// A JSON-backed cache of match summaries, so indexing a replay folder is a one-off cost.
/// Entries are invalidated when a file's size or timestamp changes.
/// </summary>
public sealed class LibraryIndex
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Bumped whenever the analysis or the summary shape changes. A cache written by an older
    /// build is thrown away rather than shown: stale numbers that look plausible are worse
    /// than an empty library, because nothing about them says they are out of date.
    /// </summary>
    public const int CurrentSchema = 4;

    public int Schema { get; set; } = CurrentSchema;

    public Dictionary<string, MatchSummary> Entries { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Spark's own copy, alongside the rest of its data.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "IgniteVR", "Spark", "ReplayAnalyser", "library-index.json");

    /// <summary>
    /// Where the standalone Replay Analyser keeps its index. The analysis is the same code, so a
    /// library indexed there is valid here: on first run it's copied over rather than rebuilt,
    /// which would otherwise mean re-parsing every replay. Copied, not shared — two apps writing
    /// one file would keep overwriting each other.
    /// </summary>
    public static string StandalonePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EchoAnalyser", "library-index.json");

    private sealed class Payload
    {
        public int Schema { get; set; }
        public Dictionary<string, MatchSummary> Entries { get; set; } = new();
    }

    public static LibraryIndex Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (!File.Exists(path) && path == DefaultPath && File.Exists(StandalonePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.Copy(StandalonePath, path);
            }

            if (!File.Exists(path)) return new LibraryIndex();
            var loaded = JsonSerializer.Deserialize<Payload>(File.ReadAllText(path), Json);
            if (loaded == null || loaded.Schema != CurrentSchema) return new LibraryIndex();
            return new LibraryIndex
            {
                Entries = new Dictionary<string, MatchSummary>(
                    loaded.Entries, StringComparer.OrdinalIgnoreCase),
            };
        }
        catch
        {
            // A corrupt or older-format cache is not worth failing over — rebuild it.
            return new LibraryIndex();
        }
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(
            new Payload { Schema = CurrentSchema, Entries = Entries }, Json));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>True when we already hold an up-to-date summary for this file.</summary>
    public bool IsCurrent(FileInfo fi) =>
        Entries.TryGetValue(fi.FullName, out var e)
        && e.FileSize == fi.Length
        && e.FileStamp == fi.LastWriteTimeUtc.Ticks;

    public MatchSummary? Get(string path) => Entries.GetValueOrDefault(path);

    public void Put(MatchSummary s) => Entries[s.FilePath] = s;
}
