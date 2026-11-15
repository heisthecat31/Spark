// Ported from the Replay Analyser (EchoAnalyser.App/ViewModels/MainViewModel.cs). The analysis,
// indexing, coaching, career and trend logic is unchanged. What differs for Spark:
//   - startup work (loading the index, scanning the folder) happens when the tab is first opened,
//     and off the UI thread, rather than while Spark itself is starting;
//   - the replay folder defaults to Spark's own save folder, and a folder picked here is remembered;
//   - charts carry theme tones instead of fixed brushes.
#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Spark.ReplayAnalyser.Analysis;
using Spark.ReplayAnalyser.Controls;
using Spark.ReplayAnalyser.Model;
using Spark.ReplayAnalyser.Parsing;

namespace Spark.ReplayAnalyser.ViewModels;

public sealed class ReplayAnalyserViewModel : ObservableObject
{
    private readonly LibraryIndex _index = new LibraryIndex();
    private CancellationTokenSource? _indexCts;
    private CancellationTokenSource? _loadCts;
    private bool _initialised;
    private int _scanGeneration;

    public ReplayAnalyserViewModel()
    {
        _files = new ObservableCollection<MatchEntryVm>();
        _filesView = MakeView(_files);

        BrowseCommand = new RelayCommand(Browse, () => IsReady);
        RefreshCommand = new RelayCommand(() => { RememberFolder(FolderPath); _ = ScanFolderAsync(FolderPath); }, () => IsReady);
        IndexCommand = new RelayCommand(() => _ = IndexFolderAsync(), () => IsReady && !IsIndexing);
        CancelIndexCommand = new RelayCommand(() => _indexCts?.Cancel(), () => IsIndexing);
        OpenCommand = new RelayCommand(o => { if (o is MatchEntryVm e) _ = LoadMatchAsync(e); });

        FolderPath = DefaultFolder();
        LibraryStatus = "Loading the replay library…";
    }

    /// <summary>
    /// Load the cached index and read the folder. Kept out of the constructor so none of it runs
    /// until someone actually opens the tab.
    /// </summary>
    public async Task InitialiseAsync()
    {
        if (_initialised) return;
        _initialised = true;

        var loaded = await Task.Run(() => LibraryIndex.Load());
        lock (_index) _index.Entries = loaded.Entries;
        IsReady = true;

        await ScanFolderAsync(FolderPath);
        RefreshCareerPlayers();
        RefreshCoachRoster();
    }

    private bool _isReady;
    public bool IsReady
    {
        get => _isReady;
        private set { if (Set(ref _isReady, value)) CommandManager_Refresh(); }
    }

    // ------------------------------------------------------------------ library

    private ObservableCollection<MatchEntryVm> _files;
    public ObservableCollection<MatchEntryVm> Files { get => _files; private set => Set(ref _files, value); }

    private ICollectionView _filesView;
    public ICollectionView FilesView { get => _filesView; private set => Set(ref _filesView, value); }

    private ICollectionView MakeView(ObservableCollection<MatchEntryVm> files)
    {
        var view = new ListCollectionView(files);
        view.Filter = o => o is MatchEntryVm e && e.Matches(SearchText);
        return view;
    }

    private string _folderPath = "";
    public string FolderPath { get => _folderPath; set => Set(ref _folderPath, value); }

    private string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set { if (Set(ref _searchText, value)) FilesView.Refresh(); }
    }

    private MatchEntryVm? _selectedEntry;
    public MatchEntryVm? SelectedEntry
    {
        get => _selectedEntry;
        set { if (Set(ref _selectedEntry, value) && value != null) _ = LoadMatchAsync(value); }
    }

    private string _libraryStatus = "";
    public string LibraryStatus { get => _libraryStatus; set => Set(ref _libraryStatus, value); }

    private bool _isIndexing;
    public bool IsIndexing
    {
        get => _isIndexing;
        set { if (Set(ref _isIndexing, value)) { Raise(nameof(NotIndexing)); CommandManager_Refresh(); } }
    }
    public bool NotIndexing => !_isIndexing;

    private double _indexProgress;
    public double IndexProgress { get => _indexProgress; set => Set(ref _indexProgress, value); }

    public RelayCommand BrowseCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand IndexCommand { get; }
    public RelayCommand CancelIndexCommand { get; }
    public RelayCommand OpenCommand { get; }

    private static void CommandManager_Refresh() =>
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();

    /// <summary>
    /// The folder picked here last time, else wherever Spark saves its recordings — which is
    /// where the replays will be for almost everyone.
    /// </summary>
    public static string DefaultFolder()
    {
        string picked = SparkSettings.instance?.replayAnalyserFolder ?? "";
        if (!string.IsNullOrWhiteSpace(picked) && Directory.Exists(picked)) return picked;

        string saves = SparkSettings.instance?.saveFolder ?? "";
        if (!string.IsNullOrWhiteSpace(saves) && saves != "none" && Directory.Exists(saves)) return saves;

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Spark", "replays");
    }

    /// <summary>
    /// Remember a folder chosen on purpose. Spark's own save folder is stored as "no choice", so
    /// the library keeps following it if the save folder is moved later.
    /// </summary>
    private static void RememberFolder(string folder)
    {
        if (SparkSettings.instance == null || !Directory.Exists(folder)) return;
        string value = string.Equals(Path.GetFullPath(folder).TrimEnd('\\'),
                                     SafeFullPath(SparkSettings.instance.saveFolder), StringComparison.OrdinalIgnoreCase)
            ? ""
            : folder;
        if (SparkSettings.instance.replayAnalyserFolder == value) return;
        SparkSettings.instance.replayAnalyserFolder = value;
        SparkSettings.instance.Save();
    }

    private static string SafeFullPath(string path)
    {
        try { return string.IsNullOrWhiteSpace(path) || path == "none" ? "" : Path.GetFullPath(path).TrimEnd('\\'); }
        catch { return ""; }
    }

    private void Browse()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose the folder holding your replays",
            InitialDirectory = Directory.Exists(FolderPath) ? FolderPath : "",
        };
        if (dlg.ShowDialog() == true)
        {
            FolderPath = dlg.FolderName;
            RememberFolder(FolderPath);
            _ = ScanFolderAsync(FolderPath);
        }
    }

    private async Task ScanFolderAsync(string folder)
    {
        int generation = ++_scanGeneration;
        string? selectedPath = _selectedEntry?.FullPath;

        if (!Directory.Exists(folder))
        {
            ReplaceFiles(new List<MatchEntryVm>(), null);
            LibraryStatus = "That folder does not exist.";
            return;
        }

        List<MatchEntryVm> entries;
        long bytes;
        try
        {
            // Listing a big folder and statting every file is slow enough to hitch the window.
            (entries, bytes) = await Task.Run(() =>
            {
                var found = new[] { "*.echodemo", "*.butter", "*.echoreplay", "*.tape" }
                    .SelectMany(p => Directory.EnumerateFiles(folder, p))
                    .Select(f => new FileInfo(f))
                    .Where(f => f.Length > 2000)          // clip pointers and empty captures
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .ToList();

                List<MatchEntryVm> list;
                lock (_index)
                    list = found.Select(f => new MatchEntryVm(f, _index.IsCurrent(f) ? _index.Get(f.FullName) : null)).ToList();
                return (list, found.Sum(f => f.Length));
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (generation == _scanGeneration) LibraryStatus = $"Could not read that folder: {ex.Message}";
            return;
        }

        // A newer scan started while this one was reading; its result wins.
        if (generation != _scanGeneration) return;

        ReplaceFiles(entries, selectedPath);
        int indexed = entries.Count(f => f.IsIndexed);
        LibraryStatus = entries.Count == 0
            ? "No replay files in this folder."
            : $"{entries.Count} replays · {indexed} indexed · {bytes / 1073741824.0:F1} GB";
    }

    /// <summary>
    /// Swap in a whole new list at once — adding thousands of rows one at a time through a
    /// filtered view is what makes a big library slow to show. The open match stays selected.
    /// </summary>
    private void ReplaceFiles(List<MatchEntryVm> entries, string? selectedPath)
    {
        Files = new ObservableCollection<MatchEntryVm>(entries);
        FilesView = MakeView(Files);

        _selectedEntry = selectedPath == null
            ? null
            : entries.FirstOrDefault(e => string.Equals(e.FullPath, selectedPath, StringComparison.OrdinalIgnoreCase));
        Raise(nameof(SelectedEntry));
    }

    /// <summary>
    /// Parse and analyse every un-indexed file in the background so the library list can show
    /// scores and rosters. Parsing is the slow part, so this runs on all but one core.
    /// </summary>
    private async Task IndexFolderAsync()
    {
        if (IsIndexing) return;
        _indexCts = new CancellationTokenSource();
        var ct = _indexCts.Token;
        IsIndexing = true;
        IndexProgress = 0;

        var todo = Files.Where(f => !f.IsIndexed).ToList();
        int total = todo.Count, done = 0;
        if (total == 0)
        {
            LibraryStatus = "Everything in this folder is already indexed.";
            IsIndexing = false;
            return;
        }

        try
        {
            await Task.Run(() =>
            {
                var opts = new ParallelOptions
                {
                    CancellationToken = ct,
                    MaxDegreeOfParallelism = IndexingLimits.MaxParallelism,
                };
                Parallel.ForEach(todo, opts, entry =>
                {
                    MatchSummary summary;
                    try
                    {
                        var read = ReplayLoader.Load(entry.FullPath);
                        var m = MatchAnalyser.Analyse(read, ct);
                        summary = MatchSummary.From(m, entry.File);
                        if (!m.AllPlayers.Any() && m.FrameCount == 0)
                            summary.Error = read.Diagnostics.FirstOrDefault() ?? "no frames decoded";
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        summary = new MatchSummary
                        {
                            FilePath = entry.FullPath,
                            FileSize = entry.File.Length,
                            FileStamp = entry.File.LastWriteTimeUtc.Ticks,
                            Error = ex.Message,
                        };
                    }

                    lock (_index) _index.Put(summary);
                    int n = Interlocked.Increment(ref done);
                    Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        entry.Summary = summary;
                        IndexProgress = n * 100.0 / total;
                        LibraryStatus = $"Indexing… {n}/{total}";
                    });
                });
            }, ct);

            LibraryStatus = $"Indexed {done} replays.";
        }
        catch (OperationCanceledException)
        {
            LibraryStatus = $"Indexing stopped after {done} of {total}.";
        }
        finally
        {
            lock (_index) { try { _index.Save(); } catch { /* cache is best-effort */ } }
            IsIndexing = false;
            IndexProgress = 0;
            RebuildTrends();
            RefreshCareerPlayers();
            RefreshCoachRoster();
        }
    }

    // ------------------------------------------------------------------ current match

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; set => Set(ref _isLoading, value); }

    private string _loadStatus = "";
    public string LoadStatus { get => _loadStatus; set => Set(ref _loadStatus, value); }

    private MatchAnalysis? _match;
    public MatchAnalysis? Match
    {
        get => _match;
        private set
        {
            Set(ref _match, value);
            Raise(nameof(HasMatch));
            Raise(nameof(CoachingReady));
            Raise(nameof(MatchTitle));
            Raise(nameof(MatchSubtitle));
            Raise(nameof(BlueScoreText));
            Raise(nameof(OrangeScoreText));
            Raise(nameof(MatchTracks));
            Raise(nameof(ScoreSeries));
            Raise(nameof(PossessionSeries));
            Raise(nameof(TerritorySeries));
            Raise(nameof(GoalMarkers));
        }
    }

    public bool HasMatch => _match != null;

    public string MatchTitle => _match == null ? "" : _match.FileName;
    public string MatchSubtitle
    {
        get
        {
            if (_match is not { } m) return "";
            string map = m.IsArena ? "Echo Arena" : m.MapName;
            return $"{map} · {m.StartTime:ddd d MMM yyyy, HH:mm} · {m.Duration:m\\:ss} recorded · "
                 + $"{m.LiveTime:m\\:ss} live play · {m.FrameCount:N0} frames @ {m.SampleRateHz:F0} Hz";
        }
    }

    public string BlueScoreText => _match?.BlueScore.ToString() ?? "–";
    public string OrangeScoreText => _match?.OrangeScore.ToString() ?? "–";

    public ObservableCollection<PlayerVm> Players { get; } = new ObservableCollection<PlayerVm>();
    public ObservableCollection<PlayerVm> BluePlayers { get; } = new ObservableCollection<PlayerVm>();
    public ObservableCollection<PlayerVm> OrangePlayers { get; } = new ObservableCollection<PlayerVm>();
    public ObservableCollection<GoalVm> Goals { get; } = new ObservableCollection<GoalVm>();

    private PlayerVm? _selectedPlayer;
    public PlayerVm? SelectedPlayer
    {
        get => _selectedPlayer;
        set
        {
            var previous = _selectedPlayer;
            if (!Set(ref _selectedPlayer, value)) return;
            if (previous != null) previous.IsSelected = false;
            if (value != null) value.IsSelected = true;
            Raise(nameof(HasSelectedPlayer));
            RebuildSelfReport();
            RebuildTrends();
        }
    }
    public bool HasSelectedPlayer => _selectedPlayer != null;

    private PlayerVm? _selectedOpponent;
    public PlayerVm? SelectedOpponent
    {
        get => _selectedOpponent;
        set
        {
            if (!Set(ref _selectedOpponent, value)) return;
            Raise(nameof(HasSelectedOpponent));
            RebuildScoutReport();
        }
    }
    public bool HasSelectedOpponent => _selectedOpponent != null;

    public async Task LoadMatchAsync(MatchEntryVm entry)
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        IsLoading = true;
        LoadStatus = $"Reading {entry.FileName}…";
        try
        {
            var analysis = await Task.Run(() =>
            {
                var read = ReplayLoader.Load(entry.FullPath);
                ct.ThrowIfCancellationRequested();
                return MatchAnalyser.Analyse(read, ct);
            }, ct);

            if (ct.IsCancellationRequested) return;
            ApplyMatch(analysis, entry);
            LoadStatus = analysis.Diagnostics.Count > 0
                ? string.Join(" · ", analysis.Diagnostics.Take(2))
                : "";
        }
        catch (OperationCanceledException) { /* superseded by a newer selection */ }
        catch (Exception ex)
        {
            Match = null;
            LoadStatus = $"Could not read this replay: {ex.Message}";
        }
        finally
        {
            if (!ct.IsCancellationRequested) IsLoading = false;
        }
    }

    private void ApplyMatch(MatchAnalysis m, MatchEntryVm entry)
    {
        Match = m;

        Players.Clear(); BluePlayers.Clear(); OrangePlayers.Clear(); Goals.Clear();

        foreach (var p in m.Blue.Players.OrderByDescending(p => p.Points).ThenByDescending(p => p.SecondsPlayed))
        { var vm = new PlayerVm(p, m); Players.Add(vm); BluePlayers.Add(vm); }
        foreach (var p in m.Orange.Players.OrderByDescending(p => p.Points).ThenByDescending(p => p.SecondsPlayed))
        { var vm = new PlayerVm(p, m); Players.Add(vm); OrangePlayers.Add(vm); }

        foreach (var g in m.Goals) Goals.Add(new GoalVm(g));

        // Default to the player whose client recorded the replay — that is almost always the
        // person sitting in front of the app.
        var self = (m.RecordingPlayer != null
                       ? Players.FirstOrDefault(p => p.Name == m.RecordingPlayer)
                       : null)
                   ?? Players.OrderByDescending(p => p.Player.SecondsPlayed).FirstOrDefault();
        SelectedPlayer = self;
        SelectedOpponent = Players
            .Where(p => self == null || p.Side != self.Side)
            .OrderByDescending(p => p.Points)
            .ThenByDescending(p => p.Player.SecondsPlayed)
            .FirstOrDefault();

        RebuildTeamCompare();
        RebuildTeamInsights();
        RebuildLobbyCards();

        if (entry.Summary == null || !entry.Summary.Ok)
        {
            var summary = MatchSummary.From(m, entry.File);
            entry.Summary = summary;
            lock (_index) { _index.Put(summary); try { _index.Save(); } catch { } }
        }
    }

    // ------------------------------------------------------------------ charts

    /// <summary>
    /// Both teams plus the disc on one map, which is the view that shows how the game was
    /// actually played: where each side lived, and the lanes the disc travelled between them.
    /// </summary>
    public IReadOnlyList<TrackLayer> MatchTracks
    {
        get
        {
            if (_match is not { } m) return Array.Empty<TrackLayer>();
            var layers = new List<TrackLayer>
            {
                new TrackLayer
                {
                    Paths = m.Blue.Players.SelectMany(p => p.Tracks).ToList(),
                    Tone = Tone.Blue,
                    Weight = 0.85,
                },
                new TrackLayer
                {
                    Paths = m.Orange.Players.SelectMany(p => p.Tracks).ToList(),
                    Tone = Tone.Orange,
                    Weight = 0.85,
                },
                new TrackLayer
                {
                    Paths = m.DiscTracks,
                    Color = Color.FromRgb(0xE8, 0xF6, 0xFF),
                    Weight = 1.5,
                },
            };
            return layers;
        }
    }

    public IReadOnlyList<ChartSeries> ScoreSeries => _match == null
        ? Array.Empty<ChartSeries>()
        : new[]
        {
            new ChartSeries
            {
                Name = "Blue", Tone = Tone.Blue, Stepped = true, Thickness = 2, FillOpacity = 0.12,
                Points = _match.BlueScoreSeries.Select(p => (p.Seconds, p.Value)).ToList(),
            },
            new ChartSeries
            {
                Name = "Orange", Tone = Tone.Orange, Stepped = true, Thickness = 2, FillOpacity = 0.12,
                Points = _match.OrangeScoreSeries.Select(p => (p.Seconds, p.Value)).ToList(),
            },
        };

    public IReadOnlyList<ChartSeries> PossessionSeries => _match == null
        ? Array.Empty<ChartSeries>()
        : new[]
        {
            new ChartSeries
            {
                Name = "Blue share of possession", Tone = Tone.Blue, Thickness = 2, FillOpacity = 0.14,
                Points = _match.PossessionSeries.Select(p => (p.Seconds, p.Value * 100)).ToList(),
            },
        };

    public IReadOnlyList<ChartSeries> TerritorySeries => _match == null
        ? Array.Empty<ChartSeries>()
        : new[]
        {
            new ChartSeries
            {
                Name = "Disc position along the arena", Tone = Tone.Accent, Thickness = 1.4,
                Points = _match.DiscFieldPosition.Select(p => (p.Seconds, p.Value)).ToList(),
            },
        };

    public IReadOnlyList<ChartMarker> GoalMarkers => _match == null
        ? Array.Empty<ChartMarker>()
        : _match.Goals.Select(g => new ChartMarker
        {
            X = g.Time.TotalSeconds,
            Tone = g.Side == TeamSide.Blue ? Tone.Blue : Tone.Orange,
            Label = g.Scorer,
            Tooltip = $"{g.Scorer} {g.Points}pt",
        }).ToList();

    // ------------------------------------------------------------------ team comparison

    public ObservableCollection<CompareRow> TeamCompare { get; } = new ObservableCollection<CompareRow>();

    public sealed record CompareRow(string Label, double Blue, double Orange, string Format);

    private void RebuildTeamCompare()
    {
        TeamCompare.Clear();
        if (_match is not { } m) return;
        var b = m.Blue; var o = m.Orange;

        void Row(string label, double blue, double orange, string fmt = "0.#") =>
            TeamCompare.Add(new CompareRow(label, blue, orange, fmt));

        Row("Score", m.BlueScore, m.OrangeScore, "0");
        Row("Goals", b.Goals, o.Goals, "0");
        Row("Shots taken", b.ShotsTaken, o.ShotsTaken, "0");
        Row("Shot accuracy %", b.ShotAccuracy * 100, o.ShotAccuracy * 100, "0");
        Row("Possession %", b.PossessionShare * 100, o.PossessionShare * 100, "0");
        Row("Saves", b.Saves, o.Saves, "0");
        Row("Stuns", b.Stuns, o.Stuns, "0");
        Row("Turnovers", b.Turnovers, o.Turnovers, "0");
        Row("Team spread (m)", b.AverageSpread, o.AverageSpread, "0.0");
        Row("Time with nobody back %", b.DefendersBackShare[0] * 100, o.DefendersBackShare[0] * 100, "0");
        Row("Distance skated (m)", b.Players.Sum(p => p.DistanceTravelled), o.Players.Sum(p => p.DistanceTravelled), "0");
    }

    // ------------------------------------------------------------------ coaching

    public ObservableCollection<InsightVm> SelfInsights { get; } = new ObservableCollection<InsightVm>();
    public ObservableCollection<InsightVm> ScoutInsights { get; } = new ObservableCollection<InsightVm>();

    /// <summary>Shape notes for each side, so a full team can read both halves of the game.</summary>
    public ObservableCollection<InsightVm> BlueTeamInsights { get; } = new ObservableCollection<InsightVm>();
    public ObservableCollection<InsightVm> OrangeTeamInsights { get; } = new ObservableCollection<InsightVm>();

    /// <summary>Every player in the lobby, reviewed — no clicking through the roster.</summary>
    public ObservableCollection<PlayerCardVm> BlueCards { get; } = new ObservableCollection<PlayerCardVm>();
    public ObservableCollection<PlayerCardVm> OrangeCards { get; } = new ObservableCollection<PlayerCardVm>();

    private string _selfSummary = "";
    public string SelfSummary { get => _selfSummary; set => Set(ref _selfSummary, value); }

    private string _scoutSummary = "";
    public string ScoutSummary { get => _scoutSummary; set => Set(ref _scoutSummary, value); }

    private IReadOnlyList<RadarAxis> _selfRadar = Array.Empty<RadarAxis>();
    public IReadOnlyList<RadarAxis> SelfRadar { get => _selfRadar; set => Set(ref _selfRadar, value); }

    private IReadOnlyList<RadarAxis> _scoutRadar = Array.Empty<RadarAxis>();
    public IReadOnlyList<RadarAxis> ScoutRadar { get => _scoutRadar; set => Set(ref _scoutRadar, value); }

    private PlayerReport? _selfReport, _scoutReport;

    // ---- scope: this one match, or everything indexed ----

    /// <summary>
    /// When true the coaching report is built from the whole indexed library instead of the
    /// loaded replay, so a plan can be made against somebody before you ever load a game
    /// they are in.
    /// </summary>
    private bool _coachAllTime;
    public bool CoachAllTime
    {
        get => _coachAllTime;
        set
        {
            if (!Set(ref _coachAllTime, value)) return;
            Raise(nameof(CoachMatchScope));
            Raise(nameof(CoachingReady));
            RebuildSelfReport();
            RebuildScoutReport();
        }
    }
    public bool CoachMatchScope => !_coachAllTime;

    /// <summary>Anyone who appears in the index, most-played first.</summary>
    public ObservableCollection<string> CoachRoster { get; } = new ObservableCollection<string>();

    private string? _coachSelfName;
    public string? CoachSelfName
    {
        get => _coachSelfName;
        set
        {
            if (!Set(ref _coachSelfName, value)) return;
            RebuildSelfReport();
            // The head-to-head record depends on who is asking, so the scout follows.
            if (_coachAllTime) RebuildScoutReport();
        }
    }

    private string? _coachOpponentName;
    public string? CoachOpponentName
    {
        get => _coachOpponentName;
        set { if (Set(ref _coachOpponentName, value)) RebuildScoutReport(); }
    }

    /// <summary>All-time coaching needs an index; match coaching needs a loaded replay.</summary>
    public bool CoachingReady => _coachAllTime ? CoachRoster.Count > 0 : HasMatch;

    private string _coachScopeNote = "";
    public string CoachScopeNote { get => _coachScopeNote; set => Set(ref _coachScopeNote, value); }

    private CareerProfile? _careerSelf;
    private CareerScout? _careerScout;

    /// <summary>
    /// Everyone in the index, not just those with enough footage for a career verdict — you
    /// often want a read on somebody you have only met once, and the report says when the
    /// evidence is thin rather than hiding them.
    /// </summary>
    private void RefreshCoachRoster()
    {
        string? prevSelf = _coachSelfName, prevOpp = _coachOpponentName;
        CoachRoster.Clear();

        List<string> names;
        string? recorder;
        lock (_index)
        {
            names = CareerAnalyser.KnownPlayers(_index, minMatches: 1).ToList();
            recorder = _index.Entries.Values
                .Where(e => e.Ok && e.RecordingPlayer != null)
                .GroupBy(e => e.RecordingPlayer!)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault();
        }
        foreach (var n in names) CoachRoster.Add(n);

        _coachSelfName = prevSelf != null && names.Contains(prevSelf) ? prevSelf
            : recorder != null && names.Contains(recorder) ? recorder
            : names.FirstOrDefault();
        _coachOpponentName = prevOpp != null && names.Contains(prevOpp) ? prevOpp
            : names.FirstOrDefault(n => n != _coachSelfName);

        Raise(nameof(CoachSelfName));
        Raise(nameof(CoachOpponentName));
        Raise(nameof(CoachingReady));

        if (_coachAllTime) { RebuildSelfReport(); RebuildScoutReport(); }
    }

    private void RebuildSelfReport()
    {
        SelfInsights.Clear();
        SelfSummary = "";
        _selfReport = null;
        _careerSelf = null;

        if (_coachAllTime)
        {
            if (!string.IsNullOrEmpty(_coachSelfName))
            {
                lock (_index) _careerSelf = CareerAnalyser.Build(_index, _coachSelfName);
                if (_careerSelf != null)
                {
                    foreach (var i in _careerSelf.Insights) SelfInsights.Add(new InsightVm(i));
                    SelfSummary = _careerSelf.Summary;
                }
                else
                {
                    SelfSummary = $"No indexed match has {_coachSelfName} playing long enough to judge.";
                }
            }
            UpdateScopeNote();
        }
        else if (_match is { } m && _selectedPlayer is { } p)
        {
            _selfReport = InsightEngine.BuildReport(m, p.Player, asOpponent: false);
            foreach (var i in _selfReport.Insights) SelfInsights.Add(new InsightVm(i));
            SelfSummary = _selfReport.Summary;
        }
        RebuildRadars();
    }

    private void RebuildScoutReport()
    {
        ScoutInsights.Clear();
        ScoutSummary = "";
        _scoutReport = null;
        _careerScout = null;

        if (_coachAllTime)
        {
            if (!string.IsNullOrEmpty(_coachOpponentName))
            {
                lock (_index)
                    _careerScout = ScoutingAnalyser.Build(_index, _coachOpponentName, _coachSelfName);
                if (_careerScout != null)
                {
                    foreach (var i in _careerScout.Insights) ScoutInsights.Add(new InsightVm(i));
                    ScoutSummary = _careerScout.Summary;
                }
                else
                {
                    ScoutSummary = $"No indexed match has {_coachOpponentName} playing long enough to scout.";
                }
            }
            UpdateScopeNote();
        }
        else if (_match is { } m && _selectedOpponent is { } p)
        {
            _scoutReport = InsightEngine.BuildReport(m, p.Player, asOpponent: true);
            foreach (var i in _scoutReport.Insights) ScoutInsights.Add(new InsightVm(i));
            ScoutSummary = _scoutReport.Summary;
        }
        RebuildRadars();
    }

    private void UpdateScopeNote()
    {
        int matches = _careerScout?.Matches ?? _careerSelf?.Matches ?? 0;
        CoachScopeNote = matches == 0
            ? "Index your replay folder on the Library page to build all-time reports."
            : $"Built from every indexed replay — {_careerSelf?.Matches ?? 0} with "
              + $"{_coachSelfName}, {_careerScout?.Matches ?? 0} with {_coachOpponentName}.";
    }

    private void RebuildTeamInsights()
    {
        BlueTeamInsights.Clear();
        OrangeTeamInsights.Clear();
        BlueMatchup.Clear();
        OrangeMatchup.Clear();
        BlueTeam = null;
        OrangeTeam = null;
        if (_match is not { } m) return;

        foreach (var i in InsightEngine.BuildTeamInsights(m, TeamSide.Blue))
            BlueTeamInsights.Add(new InsightVm(i));
        foreach (var i in InsightEngine.BuildTeamInsights(m, TeamSide.Orange))
            OrangeTeamInsights.Add(new InsightVm(i));

        var (blue, orange) = TeamAnalyser.BuildBoth(m);
        BlueTeam = new TeamVm(blue);
        OrangeTeam = new TeamVm(orange);

        // Each list answers "what did the other side do to us", which is the half a team
        // cannot see from its own stat line.
        foreach (var i in TeamAnalyser.BuildMatchup(m, blue, orange)) BlueMatchup.Add(new InsightVm(i));
        foreach (var i in TeamAnalyser.BuildMatchup(m, orange, blue)) OrangeMatchup.Add(new InsightVm(i));
    }

    private TeamVm? _blueTeam;
    public TeamVm? BlueTeam
    {
        get => _blueTeam;
        private set { Set(ref _blueTeam, value); Raise(nameof(HasTeams)); }
    }

    private TeamVm? _orangeTeam;
    public TeamVm? OrangeTeam { get => _orangeTeam; private set => Set(ref _orangeTeam, value); }

    public bool HasTeams => _blueTeam != null;

    /// <summary>What orange did to blue, and vice versa.</summary>
    public ObservableCollection<InsightVm> BlueMatchup { get; } = new ObservableCollection<InsightVm>();
    public ObservableCollection<InsightVm> OrangeMatchup { get; } = new ObservableCollection<InsightVm>();

    /// <summary>
    /// Review every player once, up front. The rules are cheap — they run off numbers the
    /// frame walk already produced — so there is no reason to make a team click each name
    /// in turn to find out what the analysis says about them.
    /// </summary>
    private void RebuildLobbyCards()
    {
        BlueCards.Clear();
        OrangeCards.Clear();
        if (_match is not { } m) return;

        foreach (var vm in Players)
        {
            if (vm.Player.SecondsPlayed < 20) continue;   // spectators and brief visitors
            var report = InsightEngine.BuildReport(m, vm.Player, asOpponent: false);
            var card = new PlayerCardVm(vm, report);
            (vm.IsBlue ? BlueCards : OrangeCards).Add(card);
        }
    }

    private static readonly InsightArea[] RadarOrder =
    {
        InsightArea.Finishing, InsightArea.Possession, InsightArea.Passing,
        InsightArea.Defence, InsightArea.Physicality, InsightArea.Positioning,
        InsightArea.Movement, InsightArea.Discipline, InsightArea.Mechanics,
    };

    /// <summary>
    /// Build both radars off the same axes. Overlaying two polygons only means anything if
    /// their spokes line up, so when the two players do not share an area — throw mechanics
    /// exist only for the recording client — that spoke is dropped from both.
    /// </summary>
    /// <summary>
    /// Career reports rate a player by how far they sit from the room rather than by a
    /// percentile within one lobby, so the radar plots that edge: 50 is level with the people
    /// they play against, 100 is comfortably ahead.
    /// </summary>
    private static readonly string[] CareerRadarOrder =
    {
        "Scoring", "Finishing", "Playmaking", "Distribution",
        "Saves", "Physicality", "Hands", "Disc security", "Work rate",
    };

    private static IReadOnlyList<RadarAxis> CareerRadar(
        IReadOnlyList<CareerMetric>? metrics, IReadOnlyList<string> axes)
    {
        if (metrics == null) return Array.Empty<RadarAxis>();
        var list = new List<RadarAxis>(axes.Count);
        foreach (var name in axes)
        {
            var m = metrics.FirstOrDefault(x => x.Name == name);
            double value = m == null ? 50 : Math.Clamp(50 + m.Edge * 40, 0, 100);
            list.Add(new RadarAxis(name, value));
        }
        return list;
    }

    private void RebuildRadars()
    {
        if (_coachAllTime)
        {
            var axes = CareerRadarOrder.Where(n =>
                (_careerSelf?.Metrics.Any(m => m.Name == n) ?? false) ||
                (_careerScout?.Metrics.Any(m => m.Name == n) ?? false)).ToList();

            SelfRadar = CareerRadar(_careerSelf?.Metrics, axes);
            ScoutRadar = CareerRadar(_careerScout?.Metrics, axes);
            return;
        }

        if (_selfReport == null && _scoutReport == null)
        {
            SelfRadar = Array.Empty<RadarAxis>();
            ScoutRadar = Array.Empty<RadarAxis>();
            return;
        }

        var areas = RadarOrder.Where(a =>
            (_selfReport?.AreaScores.ContainsKey(a) ?? true) &&
            (_scoutReport?.AreaScores.ContainsKey(a) ?? true)).ToList();

        SelfRadar = _selfReport == null
            ? Array.Empty<RadarAxis>()
            : areas.Select(a => new RadarAxis(Pretty(a), _selfReport.AreaScores[a])).ToList();
        ScoutRadar = _scoutReport == null
            ? Array.Empty<RadarAxis>()
            : areas.Select(a => new RadarAxis(Pretty(a), _scoutReport.AreaScores[a])).ToList();
    }

    private static string Pretty(InsightArea a) => a switch
    {
        InsightArea.TeamShape => "Team shape",
        _ => a.ToString(),
    };

    // ------------------------------------------------------------------ all-time career

    public ObservableCollection<string> CareerPlayers { get; } = new ObservableCollection<string>();

    private string? _selectedCareerPlayer;
    public string? SelectedCareerPlayer
    {
        get => _selectedCareerPlayer;
        set { if (Set(ref _selectedCareerPlayer, value)) RebuildCareer(); }
    }

    private CareerVm? _career;
    public CareerVm? Career
    {
        get => _career;
        private set { Set(ref _career, value); Raise(nameof(HasCareer)); }
    }
    public bool HasCareer => _career != null;

    private string _careerStatus = "";
    public string CareerStatus { get => _careerStatus; set => Set(ref _careerStatus, value); }

    /// <summary>
    /// Refresh the list of players we have enough footage on. Whoever recorded the most
    /// replays is almost certainly the person using the app, so they are pre-selected.
    /// </summary>
    private void RefreshCareerPlayers()
    {
        string? previous = _selectedCareerPlayer;
        CareerPlayers.Clear();

        List<string> names;
        string? recorder;
        lock (_index)
        {
            names = CareerAnalyser.KnownPlayers(_index).ToList();
            recorder = _index.Entries.Values
                .Where(e => e.Ok && e.RecordingPlayer != null)
                .GroupBy(e => e.RecordingPlayer!)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault();
        }

        foreach (var n in names) CareerPlayers.Add(n);

        CareerStatus = names.Count == 0
            ? "Index the replay folder to build an all-time picture."
            : $"{names.Count} players with enough footage to judge.";

        string? pick = previous != null && names.Contains(previous) ? previous
            : recorder != null && names.Contains(recorder) ? recorder
            : names.FirstOrDefault();

        // Assign through the field so the setter does not fire before the list is populated.
        _selectedCareerPlayer = pick;
        Raise(nameof(SelectedCareerPlayer));
        RebuildCareer();
    }

    private void RebuildCareer()
    {
        if (string.IsNullOrEmpty(_selectedCareerPlayer)) { Career = null; return; }
        CareerProfile? profile;
        lock (_index) profile = CareerAnalyser.Build(_index, _selectedCareerPlayer);
        Career = profile == null ? null : new CareerVm(profile);
    }

    // ------------------------------------------------------------------ trends across the library

    public ObservableCollection<TrendRow> Trends { get; } = new ObservableCollection<TrendRow>();

    public sealed record TrendRow(
        string FileName, DateTime When, string Score, double Points, double Goals,
        double Stuns, double Saves, double Turnovers, double Minutes, double PointsPerMinute);

    private IReadOnlyList<ChartSeries> _trendSeries = Array.Empty<ChartSeries>();
    public IReadOnlyList<ChartSeries> TrendSeries { get => _trendSeries; set => Set(ref _trendSeries, value); }

    private string _trendSummary = "Index the folder to build a career view across every replay.";
    public string TrendSummary { get => _trendSummary; set => Set(ref _trendSummary, value); }

    /// <summary>
    /// Pull every indexed match the selected player appears in, so form can be read over
    /// time rather than from a single game.
    /// </summary>
    private void RebuildTrends()
    {
        Trends.Clear();
        TrendSeries = Array.Empty<ChartSeries>();
        string? name = _selectedPlayer?.Name;
        if (name == null)
        {
            TrendSummary = "Open a match and pick a player to see their history.";
            return;
        }

        List<(MatchSummary m, PlayerSummary p)> rows;
        lock (_index)
        {
            rows = _index.Entries.Values
                .Where(s => s.Ok)
                .Select(s => (m: s, p: s.Players.FirstOrDefault(x =>
                    string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))!))
                .Where(t => t.p != null && t.p.SecondsPlayed > 30)
                .OrderBy(t => t.m.Recorded)
                .ToList();
        }

        if (rows.Count == 0)
        {
            TrendSummary = $"No indexed matches found for {name}. Index the folder to build their history.";
            return;
        }

        foreach (var (m, p) in rows)
            Trends.Add(new TrendRow(
                Path.GetFileName(m.FilePath), m.Recorded, $"{m.BlueScore}–{m.OrangeScore}",
                p.GoalPoints, p.Goals, p.Stuns, p.Saves, p.Turnovers,
                p.SecondsPlayed / 60.0, p.PointsPerMinute));

        double totalMin = rows.Sum(t => t.p.SecondsPlayed) / 60.0;
        TrendSummary = $"{name} — {rows.Count} indexed matches, {totalMin:F0} minutes of live play. "
                     + $"{rows.Sum(t => t.p.GoalPoints)} points from {rows.Sum(t => t.p.Goals)} goals, "
                     + $"{rows.Sum(t => t.p.Assists)} assists, {rows.Sum(t => t.p.Saves)} saves and "
                     + $"{rows.Sum(t => t.p.Stuns)} stuns.";

        // Chart against match number rather than date, so a long break does not squash the line.
        var pts = rows.Select((t, i) => ((double)(i + 1), t.p.PointsPerMinute)).ToList();
        var stuns = rows.Select((t, i) => ((double)(i + 1), t.p.StunsPerMinute)).ToList();
        var tos = rows.Select((t, i) => ((double)(i + 1), t.p.TurnoversPerMinute)).ToList();
        TrendSeries = new[]
        {
            new ChartSeries { Name = "Points / min", Tone = Tone.Accent, Points = pts, Thickness = 2 },
            // Blue rather than the original orange: in Spark's palette orange and the "bad" colour
            // are close enough to confuse stuns with turnovers.
            new ChartSeries { Name = "Stuns / min", Tone = Tone.Blue, Points = stuns, Thickness = 1.6 },
            new ChartSeries { Name = "Turnovers / min", Tone = Tone.Bad, Points = tos, Thickness = 1.6 },
        };
    }
}
