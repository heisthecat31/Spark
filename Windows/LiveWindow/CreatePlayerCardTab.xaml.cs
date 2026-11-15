using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;

namespace Spark
{
    /// <summary>
    /// Code-behind for the "Create PlayerCard" tab.
    /// Features: configurable stat slots, 10 background styles × 10 colour themes, Discord PFP, PNG export.
    /// </summary>
    public partial class CreatePlayerCardTab : UserControl
    {
        // ─────────────────────────────────────────────────────────────
        //  Stat option descriptor
        // ─────────────────────────────────────────────────────────────
        private class StatOption
        {
            public string DisplayName { get; }
            /// <summary>Key in the _loadedStats dictionary. Prefix "_game_" for game-level stats.</summary>
            public string JsonKey      { get; }
            public string Format       { get; }
            public string Suffix       { get; }
            public double Divisor      { get; }

            public StatOption(string displayName, string jsonKey,
                              string format = "N0", string suffix = "", double divisor = 1.0)
            {
                DisplayName = displayName;
                JsonKey     = jsonKey;
                Format      = format;
                Suffix      = suffix;
                Divisor     = divisor;
            }

            public string LabelText => DisplayName.ToUpperInvariant();

            public string FormattedValue(Dictionary<string, double> stats)
            {
                if (!stats.TryGetValue(JsonKey, out double raw)) return "N/A";
                double val = raw / Divisor;
                return val.ToString(Format) + Suffix;
            }

            public override string ToString() => DisplayName;
        }

        // ─────────────────────────────────────────────────────────────
        //  Static data
        // ─────────────────────────────────────────────────────────────
        private static readonly StatOption[] StatOptions = new[]
        {
            new StatOption("Win Streak",       "CurrentArenaWinStreak"),
            new StatOption("Arena MVP %",      "ArenaMVPPercentage",   "F1", "%"),
            new StatOption("Goals",            "Goals"),
            new StatOption("Games Played",     "GamesPlayed"),
            new StatOption("Points",           "Points"),
            new StatOption("Assists",          "Assists"),
            new StatOption("Stuns",            "Stuns"),
            new StatOption("Saves",            "Saves"),
            new StatOption("Steals",           "Steals"),
            new StatOption("Arena MVPs",       "ArenaMVPs"),
            new StatOption("Hat Tricks",       "HatTricks"),
            new StatOption("Possession (min)", "PossessionTime",       "F0", "m", 60.0),
            new StatOption("Passes",           "Passes"),
            new StatOption("Interceptions",    "Interceptions"),
            new StatOption("Shots on Goal",    "ShotsOnGoal"),
            new StatOption("Avg Points/Game",  "AveragePointsPerGame", "F2"),
            new StatOption("Goals/Game",       "GoalsPerGame",         "F2"),
            new StatOption("Wins",             "ArenaWins"),
            new StatOption("Win %",            "ArenaWinPercentage",   "F1", "%"),
        };

        private static readonly string[] StyleNames = new[]
        {
            "Solid", "Sparkle", "Glitter", "Shiny", "Stars",
            "Nebula", "Aurora", "Diamonds", "Holographic", "Neon Glow"
        };

        private static readonly string[] GuildNames = new[]
        {
            "Echo VR Lounge",
            "Echo Masters League",
            "EchoVR Ranked",
            "EchoVRCE",
            "Ladies of VR",
            "Echo Competitive Finals"
        };

        private static readonly (string Name, Color Primary, Color Secondary)[] ColorThemes = new[]
        {
            ("Gold",    Color.FromRgb(0xFF, 0xD7, 0x00), Color.FromRgb(0x8B, 0x69, 0x14)),
            ("Silver",  Color.FromRgb(0xC0, 0xC0, 0xC0), Color.FromRgb(0x60, 0x60, 0x60)),
            ("Pink",    Color.FromRgb(0xFF, 0x69, 0xB4), Color.FromRgb(0xC7, 0x15, 0x85)),
            ("Cyan",    Color.FromRgb(0x00, 0xE5, 0xFF), Color.FromRgb(0x00, 0x97, 0xA7)),
            ("Purple",  Color.FromRgb(0xCE, 0x93, 0xD8), Color.FromRgb(0x6A, 0x1B, 0x9A)),
            ("Red",     Color.FromRgb(0xFF, 0x55, 0x55), Color.FromRgb(0xB7, 0x1C, 0x1C)),
            ("Green",   Color.FromRgb(0x69, 0xFF, 0x47), Color.FromRgb(0x1B, 0x5E, 0x20)),
            ("Blue",    Color.FromRgb(0x64, 0xB5, 0xF6), Color.FromRgb(0x0D, 0x47, 0xA1)),
            ("Orange",  Color.FromRgb(0xFF, 0xA8, 0x26), Color.FromRgb(0xE6, 0x51, 0x00)),
            ("Pearl",   Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0xBD, 0xBD, 0xBD)),
        };

        // Bar chart normalisation ceilings
        private const double MaxGames      = 1000;
        private const double MaxPoints     = 3000;
        private const double MaxAssists    = 1000;
        private const double MaxStuns      = 10000;
        private const double MaxPossession = 80000;
        private const double MaxSaves      = 600;
        private const double MaxSteals     = 800;

        // ─────────────────────────────────────────────────────────────
        //  Instance state
        // ─────────────────────────────────────────────────────────────
        private int                        _currentStyle    = 0;
        private int                        _currentColor    = 0;
        private Color                      _primaryColor    = Color.FromRgb(0xFF, 0xD7, 0x00);
        private Color                      _secondaryColor  = Color.FromRgb(0x8B, 0x69, 0x14);
        private Dictionary<string, double> _loadedStats     = new Dictionary<string, double>();
        private bool                       _suppressEvents  = false;

        // ─────────────────────────────────────────────────────────────
        //  Init
        // ─────────────────────────────────────────────────────────────
        public CreatePlayerCardTab()
        {
            InitializeComponent();
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            _suppressEvents = true;

            // Populate slot combos
            foreach (var combo in new[] { Slot1Combo, Slot2Combo, Slot3Combo, Slot4Combo })
                combo.ItemsSource = StatOptions;

            Slot1Combo.SelectedIndex = 0;  // Win Streak
            Slot2Combo.SelectedIndex = 1;  // Arena MVP %
            Slot3Combo.SelectedIndex = 2;  // Goals
            Slot4Combo.SelectedIndex = 3;  // Games Played

            // Populate background and guild combos
            StyleCombo.ItemsSource  = StyleNames;
            StyleCombo.SelectedIndex = 0;

            ColorCombo.ItemsSource  = ColorThemes.Select(ct => ct.Name).ToList();
            ColorCombo.SelectedIndex = 0;

            GuildCombo.ItemsSource   = GuildNames;
            GuildCombo.SelectedIndex = 0;

            _suppressEvents = false;

            UpdateDiscordLabel();
            UpdateBackground();
            UpdateColorAccents();

            _ = LoadAndPopulateAsync();
        }

        // ─────────────────────────────────────────────────────────────
        //  Button handlers
        // ─────────────────────────────────────────────────────────────
        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshButton.IsEnabled = false;
            await LoadAndPopulateAsync();
            RefreshButton.IsEnabled = true;
        }

        private void SaveCardButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new SaveFileDialog
                {
                    Title      = "Save PlayerCard",
                    Filter     = "PNG Image|*.png",
                    FileName   = $"playercard_{PlayerNameText.Text}.png",
                    DefaultExt = ".png"
                };
                if (dlg.ShowDialog() != true) return;

                // Force layout before render
                CardRoot.Measure(new Size(CardRoot.Width, CardRoot.Height));
                CardRoot.Arrange(new Rect(new Size(CardRoot.Width, CardRoot.Height)));
                CardRoot.UpdateLayout();

                // Render at 2× for crisp sharing
                const double dpi   = 192;
                const double scale = dpi / 96.0;

                var rtb = new RenderTargetBitmap(
                    (int)(CardRoot.ActualWidth  * scale),
                    (int)(CardRoot.ActualHeight * scale),
                    dpi, dpi, PixelFormats.Pbgra32);

                // Hide outer glow so the PNG has a clean background
                var savedEffect   = CardRoot.Effect;
                CardRoot.Effect   = null;
                rtb.Render(CardRoot);
                CardRoot.Effect   = savedEffect;

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));

                using var fs = new FileStream(dlg.FileName, FileMode.Create, FileAccess.Write);
                encoder.Save(fs);

                SetStatus($"✅ Card saved to: {dlg.FileName}", good: true);
            }
            catch (Exception ex)
            {
                SetStatus($"❌ Save failed: {ex.Message}", good: false);
                Logger.LogRow(Logger.LogType.Error, $"[PlayerCard] Save error: {ex}");
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Combo handlers
        // ─────────────────────────────────────────────────────────────
        private void SlotCombo_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressEvents) return;
            UpdateAllCardSlots();
        }

        private void StyleCombo_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressEvents) return;
            _currentStyle = StyleCombo.SelectedIndex;
            UpdateBackground();
        }

        private void ColorCombo_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressEvents) return;
            _currentColor    = ColorCombo.SelectedIndex;
            var theme        = ColorThemes[_currentColor];
            _primaryColor    = theme.Primary;
            _secondaryColor  = theme.Secondary;
            UpdateBackground();
            UpdateColorAccents();
        }

        private void GuildCombo_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressEvents) return;
            if (GuildCombo.SelectedItem is string guild)
            {
                if (PlayerSubtitleText != null) PlayerSubtitleText.Text = guild;
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Core load
        // ─────────────────────────────────────────────────────────────
        private async Task LoadAndPopulateAsync()
        {
            SaveCardButton.IsEnabled = false;
            SetStatus("Loading stats…", good: true);

            try
            {
                string jsonPath = FindServerProfilePath();
                if (jsonPath == null)
                {
                    SetStatus("❌ Could not find serverprofile.json under %LOCALAPPDATA%\\rad\\echovr\\users\\ovr-org", good: false);
                    return;
                }

                string  raw  = File.ReadAllText(jsonPath);
                JObject root = JObject.Parse(raw);

                string displayName = root["displayname"]?.ToString() ?? "Unknown";
                PlayerNameText.Text = displayName;
                InfoUsername.Text   = displayName;

                // Stats come from the replay folder as the Replay Analyser indexed it (public arena matches only),
                // found by every account that has played on this PC.
                var profiles = EchoDemoReader.LocalProfileNames();
                var ids      = new HashSet<ulong>(profiles.Keys);
                if (EchoDemoReader.PlatformId(root["xplatformid"]?.ToString()) is ulong ownId) ids.Add(ownId);
                var names    = new HashSet<string>(profiles.Values, StringComparer.OrdinalIgnoreCase) { displayName };
                string folder = ReplayAnalyser.ViewModels.ReplayAnalyserViewModel.DefaultFolder();

                var card = await Task.Run(() => ReplayAnalyser.Analysis.PlayerCardStats.Compute(folder, ids, names));
                _loadedStats = card.Stats;
                ShowIndexNotice(card);

                // Update sidebar read-outs
                InfoGamesPlayed.Text = GetFmt("GamesPlayed");
                InfoPoints.Text      = GetFmt("Points");
                InfoAssists.Text     = GetFmt("Assists");
                InfoStuns.Text       = GetFmt("Stuns");
                InfoSaves.Text       = GetFmt("Saves");
                InfoSteals.Text      = GetFmt("Steals");
                InfoGoals.Text       = GetFmt("Goals");

                // Update configurable card slots
                UpdateAllCardSlots();

                // Load Discord PFP
                await LoadAvatarAsync();
                UpdateDiscordLabel();

                // Set bar widths after layout
                await Dispatcher.InvokeAsync(() =>
                {
                    SetBarWidth(GamesBar,      "GamesPlayed",    MaxGames);
                    SetBarWidth(PointsBar,     "Points",         MaxPoints);
                    SetBarWidth(AssistsBar,    "Assists",        MaxAssists);
                    SetBarWidth(StunsBar,      "Stuns",          MaxStuns);
                    SetBarWidth(PossessionBar, "PossessionTime", MaxPossession);
                    SetBarWidth(SavesBar,      "Saves",          MaxSaves);
                    SetBarWidth(StealsBar,     "Steals",         MaxSteals);
                }, System.Windows.Threading.DispatcherPriority.Loaded);

                SaveCardButton.IsEnabled = true;
                SetStatus($"✅ Loaded stats for {displayName}", good: true);
            }
            catch (Exception ex)
            {
                SetStatus($"❌ Error: {ex.Message}", good: false);
                Logger.LogRow(Logger.LogType.Error, $"[PlayerCard] Load error: {ex}");
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Card slot update
        // ─────────────────────────────────────────────────────────────
        private void UpdateAllCardSlots()
        {
            var slots = new[]
            {
                (Slot1Combo, Row1Label, Row1Value),
                (Slot2Combo, Row2Label, Row2Value),
                (Slot3Combo, Row3Label, Row3Value),
                (Slot4Combo, Row4Label, Row4Value),
            };
            foreach (var (combo, label, value) in slots)
            {
                if (combo.SelectedItem is StatOption opt)
                {
                    label.Text = opt.LabelText;
                    value.Text = _loadedStats.Count > 0 ? opt.FormattedValue(_loadedStats) : "—";
                }
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Background generation
        // ─────────────────────────────────────────────────────────────
        private void UpdateBackground()
        {
            CardBackground.Background = GenerateBackground(_currentStyle, _primaryColor, _secondaryColor);
        }

        private static Brush GenerateBackground(int style, Color primary, Color secondary)
        {
            switch (style)
            {
                case 0:  return CreateSolidBrush(primary, secondary);
                case 1:  return CreateSparkleBrush(primary, secondary);
                case 2:  return CreateGlitterBrush(primary, secondary);
                case 3:  return CreateShinyBrush(primary, secondary);
                case 4:  return CreateStarsBrush(primary, secondary);
                case 5:  return CreateNebulaBrush(primary, secondary);
                case 6:  return CreateAuroraBrush(primary, secondary);
                case 7:  return CreateDiamondsBrush(primary, secondary);
                case 8:  return CreateHolographicBrush(primary);
                case 9:  return CreateNeonGlowBrush(primary, secondary);
                default: return CreateSolidBrush(primary, secondary);
            }
        }

        // ── 0: Solid ──────────────────────────────────────────────────
        private static Brush CreateSolidBrush(Color primary, Color secondary)
        {
            var dg = new DrawingGroup();
            // Rich multi-stop gradient
            var base_ = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(1, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Darken(primary,   0.50), 0.00),
                    new GradientStop(Darken(primary,   0.20), 0.40),
                    new GradientStop(Darken(secondary, 0.10), 0.75),
                    new GradientStop(Darken(secondary, 0.40), 1.00),
                }
            };
            dg.Children.Add(new GeometryDrawing(base_, null, new RectangleGeometry(new Rect(0, 0, 420, 580))));
            // Subtle diagonal highlight
            AddDiagonalHighlight(dg, 420, 580, alpha: 28);
            // Vignette
            AddVignette(dg, 420, 580, alpha: 90);
            return new DrawingBrush(dg)
            {
                Stretch = Stretch.Fill,
                Viewbox = new Rect(0, 0, 420, 580),
                ViewboxUnits = BrushMappingMode.Absolute
            };
        }

        // ── 1: Sparkle (magical floating stars) ───────────────────────
        private static Brush CreateSparkleBrush(Color primary, Color secondary)
        {
            const double W = 420, H = 580;
            var dg = new DrawingGroup();

            var bg = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(1, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Darken(primary,   0.45), 0.00),
                    new GradientStop(Darken(primary,   0.15), 0.45),
                    new GradientStop(Darken(secondary, 0.30), 1.00),
                }
            };
            dg.Children.Add(new GeometryDrawing(bg, null, new RectangleGeometry(new Rect(0, 0, W, H))));

            var rng = new Random(4242);
            for (int i = 0; i < 90; i++)
            {
                double x    = rng.NextDouble() * W;
                double y    = rng.NextDouble() * H;
                double size = rng.NextDouble() * 2.8 + 0.8;
                byte   a    = (byte)(rng.NextDouble() * 170 + 70);
                Color  dot  = rng.NextDouble() > 0.55 ? Colors.White : Mix(Colors.White, primary, 0.28);
                dg.Children.Add(new GeometryDrawing(
                    new SolidColorBrush(Color.FromArgb(a, dot.R, dot.G, dot.B)),
                    null, new EllipseGeometry(new Point(x, y), size, size)));

                if (size > 2.0 && rng.NextDouble() > 0.4)
                {
                    byte ca   = (byte)(a * 0.55);
                    var  pen  = new Pen(new SolidColorBrush(Color.FromArgb(ca, dot.R, dot.G, dot.B)), 0.9);
                    double arm = size * 3.5;
                    dg.Children.Add(new GeometryDrawing(null, pen,
                        new LineGeometry(new Point(x - arm, y), new Point(x + arm, y))));
                    dg.Children.Add(new GeometryDrawing(null, pen,
                        new LineGeometry(new Point(x, y - arm), new Point(x, y + arm))));
                }
            }

            AddVignette(dg, W, H, alpha: 80);
            return new DrawingBrush(dg)
            {
                Stretch = Stretch.Fill,
                Viewbox = new Rect(0, 0, 420, 580),
                ViewboxUnits = BrushMappingMode.Absolute
            };
        }

        // ── 2: Glitter (dense textured physical glitter) ────────────────
        private static Brush CreateGlitterBrush(Color primary, Color secondary)
        {
            const double W = 420, H = 580;
            var dg = new DrawingGroup();

            var bg = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(1, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Mix(primary, Colors.Black, 0.4), 0.0),
                    new GradientStop(Mix(secondary, Colors.Black, 0.5), 1.0),
                }
            };
            dg.Children.Add(new GeometryDrawing(bg, null, new RectangleGeometry(new Rect(0, 0, W, H))));

            var rng = new Random(8888);
            var highlight = Brighten(primary, 0.5);
            var shadow = Darken(primary, 0.4);

            var geoGroup = new GeometryGroup();
            for (int i = 0; i < 2200; i++)
            {
                double x = rng.NextDouble() * W;
                double y = rng.NextDouble() * H;
                double size = rng.NextDouble() * 3.5 + 0.8;
                
                // Draw a small rotated square (diamond) to simulate a glitter fleck
                var fig = new PathFigure(new Point(x, y - size), new[] {
                    new LineSegment(new Point(x + size, y), true),
                    new LineSegment(new Point(x, y + size), true),
                    new LineSegment(new Point(x - size, y), true)
                }, true);
                geoGroup.Children.Add(new PathGeometry(new[] { fig }));
            }
            
            // Fill all the dense flecks with a semi-transparent mix
            dg.Children.Add(new GeometryDrawing(
                new SolidColorBrush(Color.FromArgb(160, primary.R, primary.G, primary.B)), 
                new Pen(new SolidColorBrush(Color.FromArgb(60, highlight.R, highlight.G, highlight.B)), 0.5), 
                geoGroup));

            // Over-layer with a few bright white hot-spots
            for (int i = 0; i < 300; i++)
            {
                double x = rng.NextDouble() * W;
                double y = rng.NextDouble() * H;
                byte a = (byte)(rng.NextDouble() * 150 + 50);
                double size = rng.NextDouble() * 1.5 + 0.5;
                dg.Children.Add(new GeometryDrawing(
                    new SolidColorBrush(Color.FromArgb(a, 255, 255, 255)),
                    null, new EllipseGeometry(new Point(x, y), size, size)));
            }

            AddVignette(dg, W, H, alpha: 90);
            return new DrawingBrush(dg)
            {
                Stretch = Stretch.Fill,
                Viewbox = new Rect(0, 0, 420, 580),
                ViewboxUnits = BrushMappingMode.Absolute
            };
        }

        // ── 3: Shiny (metallic specular) ──────────────────────────────
        private static Brush CreateShinyBrush(Color primary, Color secondary)
        {
            const double W = 420, H = 580;
            var dg = new DrawingGroup();

            // Metallic angled gradient — dark→light→bright→dim→dark
            var metal = new LinearGradientBrush
            {
                StartPoint = new Point(0.05, 0), EndPoint = new Point(0.95, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Darken(primary,   0.60), 0.00),
                    new GradientStop(Darken(primary,   0.20), 0.28),
                    new GradientStop(Brighten(primary, 0.55), 0.44),  // specular peak
                    new GradientStop(Darken(primary,   0.08), 0.62),
                    new GradientStop(Darken(secondary, 0.45), 1.00),
                }
            };
            dg.Children.Add(new GeometryDrawing(metal, null, new RectangleGeometry(new Rect(0, 0, W, H))));

            // Primary specular streak (narrow bright diagonal band)
            var spec1 = DiagonalBand(W, H, yStart: 120, yEnd: 148);
            dg.Children.Add(new GeometryDrawing(
                new LinearGradientBrush
                {
                    StartPoint = new Point(0.1, 0.5), EndPoint = new Point(0.9, 0.5),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(  0, 255, 255, 255), 0.00),
                        new GradientStop(Color.FromArgb(170, 255, 255, 255), 0.45),
                        new GradientStop(Color.FromArgb(220, 255, 255, 255), 0.50),
                        new GradientStop(Color.FromArgb(170, 255, 255, 255), 0.55),
                        new GradientStop(Color.FromArgb(  0, 255, 255, 255), 1.00),
                    }
                }, null, spec1));

            // Softer secondary shimmer band below
            var spec2 = DiagonalBand(W, H, yStart: 165, yEnd: 210);
            dg.Children.Add(new GeometryDrawing(
                new SolidColorBrush(Color.FromArgb(22, 255, 255, 255)), null, spec2));

            // Fine-grain glitter overlay
            var rng = new Random(9191);
            for (int i = 0; i < 60; i++)
            {
                double x = rng.NextDouble() * W;
                double y = rng.NextDouble() * H;
                double s = rng.NextDouble() * 1.4 + 0.3;
                byte   a = (byte)(rng.NextDouble() * 130 + 60);
                dg.Children.Add(new GeometryDrawing(
                    new SolidColorBrush(Color.FromArgb(a, 255, 255, 255)), null,
                    new EllipseGeometry(new Point(x, y), s, s)));
            }

            AddVignette(dg, W, H, alpha: 100);
            return new DrawingBrush(dg)
            {
                Stretch = Stretch.Fill,
                Viewbox = new Rect(0, 0, 420, 580),
                ViewboxUnits = BrushMappingMode.Absolute
            };
        }

        // ── 4: Stars (single full-card canvas) ───────────────────────
        private static Brush CreateStarsBrush(Color primary, Color secondary)
        {
            const double W = 420, H = 580;
            var dg = new DrawingGroup();

            // Deep-space background gradient
            var spaceBg = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(1, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Color.FromRgb(4,  5, 18), 0.0),
                    new GradientStop(Darken(primary, 0.82),    0.5),
                    new GradientStop(Color.FromRgb(6,  4, 22), 1.0),
                }
            };
            dg.Children.Add(new GeometryDrawing(spaceBg, null, new RectangleGeometry(new Rect(0, 0, W, H))));

            // Tiny distant star dots
            var rng = new Random(7777);
            for (int i = 0; i < 140; i++)
            {
                double x = rng.NextDouble() * W;
                double y = rng.NextDouble() * H;
                double s = rng.NextDouble() * 1.2 + 0.15;
                byte   a = (byte)(rng.NextDouble() * 190 + 55);
                dg.Children.Add(new GeometryDrawing(
                    new SolidColorBrush(Color.FromArgb(a, 255, 255, 255)), null,
                    new EllipseGeometry(new Point(x, y), s, s)));
            }

            // Medium glow dots (coloured, primary tint)
            for (int i = 0; i < 20; i++)
            {
                double x = rng.NextDouble() * W;
                double y = rng.NextDouble() * H;
                double s = rng.NextDouble() * 2.5 + 1.0;
                byte   a = (byte)(rng.NextDouble() * 140 + 80);
                Color  c = rng.NextDouble() > 0.5 ? primary : Colors.White;
                dg.Children.Add(new GeometryDrawing(
                    new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B)), null,
                    new EllipseGeometry(new Point(x, y), s, s)));
            }

            // Large coloured star polygons with cross spikes
            var starDefs = new[] {
                (75.0, 80.0, 13.0, 5.0), (300.0, 150.0, 11.0, 4.4),
                (160.0, 340.0, 9.0, 3.6), (350.0, 440.0, 8.0, 3.2),
                (55.0, 470.0, 7.0, 2.8), (390.0, 60.0, 6.5, 2.6),
            };
            foreach (var (cx, cy, outer, inner) in starDefs)
            {
                dg.Children.Add(new GeometryDrawing(
                    new SolidColorBrush(Color.FromArgb(220, primary.R, primary.G, primary.B)),
                    null, StarPath(cx, cy, outer, inner)));
                // Cross spike
                byte pa = 130;
                var pen = new Pen(new SolidColorBrush(Color.FromArgb(pa, primary.R, primary.G, primary.B)), 0.8);
                dg.Children.Add(new GeometryDrawing(null, pen,
                    new LineGeometry(new Point(cx - outer * 2, cy), new Point(cx + outer * 2, cy))));
                dg.Children.Add(new GeometryDrawing(null, pen,
                    new LineGeometry(new Point(cx, cy - outer * 2), new Point(cx, cy + outer * 2))));
            }

            AddVignette(dg, W, H, alpha: 70);
            return new DrawingBrush(dg)
            {
                Stretch = Stretch.Fill,
                Viewbox = new Rect(0, 0, 420, 580),
                ViewboxUnits = BrushMappingMode.Absolute
            };
        }

        // ── 5: Nebula ─────────────────────────────────────────────────
        private static Brush CreateNebulaBrush(Color primary, Color secondary)
        {
            const double W = 420, H = 580;
            var dg = new DrawingGroup();

            // Deep dark base
            dg.Children.Add(new GeometryDrawing(
                new SolidColorBrush(Color.FromRgb(4, 3, 18)), null,
                new RectangleGeometry(new Rect(0, 0, W, H))));

            // Primary nebula cloud (large, off-centre)
            var cloud1 = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.5, 0.5), Center = new Point(0.5, 0.5),
                RadiusX = 0.5, RadiusY = 0.5,
            };
            cloud1.GradientStops.Add(new GradientStop(Color.FromArgb(160, primary.R, primary.G, primary.B),   0.00));
            cloud1.GradientStops.Add(new GradientStop(Color.FromArgb(70,  primary.R, primary.G, primary.B),   0.55));
            cloud1.GradientStops.Add(new GradientStop(Color.FromArgb(0,   0, 0, 0), 1.00));
            dg.Children.Add(new GeometryDrawing(cloud1, null, new EllipseGeometry(new Point(140, 210), 230, 180)));

            // Secondary cloud (secondary colour, offset)
            var cloud2 = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.5, 0.5), Center = new Point(0.5, 0.5),
                RadiusX = 0.5, RadiusY = 0.5,
            };
            cloud2.GradientStops.Add(new GradientStop(Color.FromArgb(120, secondary.R, secondary.G, secondary.B), 0.00));
            cloud2.GradientStops.Add(new GradientStop(Color.FromArgb(50,  secondary.R, secondary.G, secondary.B), 0.55));
            cloud2.GradientStops.Add(new GradientStop(Color.FromArgb(0,   0, 0, 0), 1.00));
            dg.Children.Add(new GeometryDrawing(cloud2, null, new EllipseGeometry(new Point(310, 390), 190, 150)));

            // Blended core glow
            Color blend = Mix(primary, secondary, 0.5);
            var  core   = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.5, 0.5), Center = new Point(0.5, 0.5),
                RadiusX = 0.5, RadiusY = 0.5,
            };
            core.GradientStops.Add(new GradientStop(Color.FromArgb(80, blend.R, blend.G, blend.B), 0.00));
            core.GradientStops.Add(new GradientStop(Color.FromArgb(0,  0, 0, 0), 1.00));
            dg.Children.Add(new GeometryDrawing(core, null, new EllipseGeometry(new Point(210, 300), 150, 120)));

            // Star dust
            var rng = new Random(5566);
            for (int i = 0; i < 60; i++)
            {
                double x = rng.NextDouble() * W;
                double y = rng.NextDouble() * H;
                double s = rng.NextDouble() * 0.9 + 0.15;
                byte   a = (byte)(rng.NextDouble() * 160 + 50);
                dg.Children.Add(new GeometryDrawing(
                    new SolidColorBrush(Color.FromArgb(a, 255, 255, 255)), null,
                    new EllipseGeometry(new Point(x, y), s, s)));
            }

            AddVignette(dg, W, H, alpha: 60);
            return new DrawingBrush(dg)
            {
                Stretch = Stretch.Fill,
                Viewbox = new Rect(0, 0, 420, 580),
                ViewboxUnits = BrushMappingMode.Absolute
            };
        }

        // ── 6: Aurora (northern lights streaks) ───────────────────────
        private static Brush CreateAuroraBrush(Color primary, Color secondary)
        {
            const double W = 420, H = 580;
            var dg = new DrawingGroup();

            var spaceBg = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Color.FromRgb(3,  4, 16), 0.0),
                    new GradientStop(Color.FromRgb(6,  5, 24), 0.5),
                    new GradientStop(Color.FromRgb(4,  3, 14), 1.0),
                }
            };
            dg.Children.Add(new GeometryDrawing(spaceBg, null, new RectangleGeometry(new Rect(0, 0, W, H))));

            var rng = new Random(3388);
            for (int i = 0; i < 55; i++)
            {
                double x = rng.NextDouble() * W;
                double y = rng.NextDouble() * H;
                double s = rng.NextDouble() * 1.0 + 0.15;
                byte   a = (byte)(rng.NextDouble() * 160 + 55);
                dg.Children.Add(new GeometryDrawing(
                    new SolidColorBrush(Color.FromArgb(a, 255, 255, 255)), null,
                    new EllipseGeometry(new Point(x, y), s, s)));
            }

            // Draw angled vertical streaks with a fade top and bottom
            void AuroraVerticalBand(double xOffset, double width, Color c, double angle)
            {
                var lb = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                lb.GradientStops.Add(new GradientStop(Color.FromArgb(0,   c.R, c.G, c.B), 0.00));
                lb.GradientStops.Add(new GradientStop(Color.FromArgb(160, c.R, c.G, c.B), 0.50));
                lb.GradientStops.Add(new GradientStop(Color.FromArgb(0,   c.R, c.G, c.B), 1.00));
                
                var rect = new RectangleGeometry(new Rect(xOffset, -100, width, H + 200));
                var drawing = new GeometryDrawing(lb, null, rect);
                
                var trans = new TransformGroup();
                trans.Children.Add(new SkewTransform(angle, 0));
                drawing.Geometry.Transform = trans;
                
                dg.Children.Add(drawing);
            }

            AuroraVerticalBand(-10,   120, primary, 15);
            AuroraVerticalBand(120,    80, Brighten(primary, 0.4), -8);
            AuroraVerticalBand(230,   140, secondary, 25);
            AuroraVerticalBand(320,    90, Mix(primary, secondary, 0.6), -12);

            AddVignette(dg, W, H, alpha: 65);
            return new DrawingBrush(dg)
            {
                Stretch = Stretch.Fill,
                Viewbox = new Rect(0, 0, 420, 580),
                ViewboxUnits = BrushMappingMode.Absolute
            };
        }

        // ── 7: Diamonds (single full-card grid — no tiling) ───────────
        private static Brush CreateDiamondsBrush(Color primary, Color secondary)
        {
            const double W = 420, H = 580;
            var dg = new DrawingGroup();

            // Metallic gradient base
            var bg = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(1, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Darken(primary,   0.45), 0.00),
                    new GradientStop(Darken(primary,   0.15), 0.45),
                    new GradientStop(Darken(secondary, 0.35), 1.00),
                }
            };
            dg.Children.Add(new GeometryDrawing(bg, null, new RectangleGeometry(new Rect(0, 0, W, H))));

            // Draw every diamond manually on the full-card canvas
            const double sX = 48, sY = 48;   // grid spacing
            const double hw = 20, hh = 20;   // half-extents of each diamond
            var outlinePen  = new Pen(new SolidColorBrush(Color.FromArgb(65, 255, 255, 255)), 1.2);
            var fillBrush   = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255));

            for (double row = -sY / 2; row < H + sY; row += sY)
            {
                for (double col = -sX / 2; col < W + sX; col += sX)
                {
                    double cx = col + ((int)(row / sY) % 2) * sX * 0.5; // offset alternate rows
                    double cy = row;
                    var    fig = new PathFigure(new Point(cx, cy - hh), new PathSegment[]
                    {
                        new LineSegment(new Point(cx + hw, cy), true),
                        new LineSegment(new Point(cx,      cy + hh), true),
                        new LineSegment(new Point(cx - hw, cy), true),
                    }, true);
                    dg.Children.Add(new GeometryDrawing(fillBrush, outlinePen, new PathGeometry(new[] { fig })));
                }
            }

            // Specular highlight for gemstone look
            AddDiagonalHighlight(dg, W, H, alpha: 35);
            AddVignette(dg, W, H, alpha: 85);
            return new DrawingBrush(dg)
            {
                Stretch = Stretch.Fill,
                Viewbox = new Rect(0, 0, 420, 580),
                ViewboxUnits = BrushMappingMode.Absolute
            };
        }

        // ── 8: Holographic ────────────────────────────────────────────
        private static Brush CreateHolographicBrush(Color primary)
        {
            const double W = 420, H = 580;
            var dg = new DrawingGroup();

            // Multi-band rainbow gradient tinted toward chosen colour
            var spectral = new Color[]
            {
                Colors.Violet, Colors.Blue, Colors.Cyan,
                Colors.Green, Colors.Yellow, Colors.Orange, Colors.Red, Colors.Violet
            };
            var lgb = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            for (int i = 0; i < spectral.Length; i++)
                lgb.GradientStops.Add(new GradientStop(Mix(spectral[i], primary, 0.42), (double)i / (spectral.Length - 1)));
            dg.Children.Add(new GeometryDrawing(lgb, null, new RectangleGeometry(new Rect(0, 0, W, H))));

            // Sparkly micro-glitter overlay
            var rng = new Random(1234);
            for (int i = 0; i < 50; i++)
            {
                double x = rng.NextDouble() * W;
                double y = rng.NextDouble() * H;
                double s = rng.NextDouble() * 1.5 + 0.3;
                byte   a = (byte)(rng.NextDouble() * 130 + 70);
                dg.Children.Add(new GeometryDrawing(
                    new SolidColorBrush(Color.FromArgb(a, 255, 255, 255)), null,
                    new EllipseGeometry(new Point(x, y), s, s)));
            }

            // Bright specular streak
            AddDiagonalHighlight(dg, W, H, alpha: 50);
            AddVignette(dg, W, H, alpha: 55);
            return new DrawingBrush(dg)
            {
                Stretch = Stretch.Fill,
                Viewbox = new Rect(0, 0, 420, 580),
                ViewboxUnits = BrushMappingMode.Absolute
            };
        }

        // ── 9: Neon Glow ──────────────────────────────────────────────
        private static Brush CreateNeonGlowBrush(Color primary, Color secondary)
        {
            const double W = 420, H = 580;
            var dg = new DrawingGroup();

            // Dark base
            dg.Children.Add(new GeometryDrawing(
                new SolidColorBrush(Color.FromRgb(5, 5, 12)), null,
                new RectangleGeometry(new Rect(0, 0, W, H))));

            // Central radial glow
            Color bright = Brighten(primary, 0.40);
            var   glow   = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.5, 0.42), Center = new Point(0.5, 0.5),
                RadiusX = 0.72, RadiusY = 0.72,
            };
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(200, bright.R, bright.G, bright.B), 0.00));
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(100, primary.R, primary.G, primary.B), 0.40));
            glow.GradientStops.Add(new GradientStop(Color.FromArgb( 30, secondary.R, secondary.G, secondary.B), 0.72));
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(  0, 0, 0, 0), 1.00));
            dg.Children.Add(new GeometryDrawing(glow, null, new RectangleGeometry(new Rect(0, 0, W, H))));

            // Secondary off-centre accent glow
            var glow2 = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.5, 0.5), Center = new Point(0.5, 0.5),
                RadiusX = 0.5, RadiusY = 0.5,
            };
            glow2.GradientStops.Add(new GradientStop(Color.FromArgb(80, secondary.R, secondary.G, secondary.B), 0.00));
            glow2.GradientStops.Add(new GradientStop(Color.FromArgb( 0, 0, 0, 0), 1.00));
            dg.Children.Add(new GeometryDrawing(glow2, null, new EllipseGeometry(new Point(330, 450), 180, 150)));

            // Neon scan-line glitter
            var rng = new Random(5050);
            for (int i = 0; i < 35; i++)
            {
                double x = rng.NextDouble() * W;
                double y = rng.NextDouble() * H;
                double s = rng.NextDouble() * 2.0 + 0.4;
                byte   a = (byte)(rng.NextDouble() * 150 + 70);
                dg.Children.Add(new GeometryDrawing(
                    new SolidColorBrush(Color.FromArgb(a, bright.R, bright.G, bright.B)), null,
                    new EllipseGeometry(new Point(x, y), s, s)));
            }

            AddVignette(dg, W, H, alpha: 90);
            return new DrawingBrush(dg)
            {
                Stretch = Stretch.Fill,
                Viewbox = new Rect(0, 0, 420, 580),
                ViewboxUnits = BrushMappingMode.Absolute
            };
        }

        // ─────────────────────────────────────────────────────────────
        //  Colour accent update (border, avatar, bars, labels)
        // ─────────────────────────────────────────────────────────────
        private void UpdateColorAccents()
        {
            var primaryBrush = new SolidColorBrush(_primaryColor);

            // Card glow
            if (CardGlowEffect is DropShadowEffect cg) cg.Color = _primaryColor;

            // Border ring
            CardBorderRing.BorderBrush = primaryBrush;

            // Avatar border + glow
            AvatarBorder.BorderBrush = primaryBrush;
            if (AvatarGlowEffect is DropShadowEffect ag) ag.Color = _primaryColor;

            // Subtitle accent
            PlayerSubtitleText.Foreground = primaryBrush;

            // Refresh Button dynamic color
            RefreshButton.Background = primaryBrush;
            RefreshButton.BorderBrush = primaryBrush;
            RefreshButton.Foreground = new SolidColorBrush(Colors.Black); // High contrast text on bright color

            // Slot row labels
            foreach (var lbl in new[] { Row1Label, Row2Label, Row3Label, Row4Label })
                lbl.Foreground = primaryBrush;

            // Bar fills
            var barBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0.5),
                EndPoint   = new Point(1, 0.5),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(_primaryColor,   0),
                    new GradientStop(_secondaryColor, 1),
                }
            };
            barBrush.Freeze();

            foreach (var bar in new[] { GamesBar, PointsBar, AssistsBar, StunsBar, PossessionBar, SavesBar, StealsBar })
                bar.Background = barBrush;
        }

        // ─────────────────────────────────────────────────────────────
        //  Avatar / Discord
        // ─────────────────────────────────────────────────────────────
        private async Task LoadAvatarAsync()
        {
            try
            {
                if (DiscordOAuth.IsLoggedIn &&
                    DiscordOAuth.discordUserData != null &&
                    DiscordOAuth.discordUserData.TryGetValue("avatar", out string avatarHash) &&
                    !string.IsNullOrEmpty(avatarHash))
                {
                    using var http  = new HttpClient();
                    byte[]    bytes = await http.GetByteArrayAsync(DiscordOAuth.DiscordPFPURL);

                    var bmp = new BitmapImage();
                    using var ms = new MemoryStream(bytes);
                    bmp.BeginInit();
                    bmp.CacheOption  = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                    bmp.Freeze();
                    AvatarImage.Source = bmp;
                }
                else
                {
                    // No Discord PFP – use a blank image or keep whatever is there
                    AvatarImage.Source = null;
                }
            }
            catch (Exception ex)
            {
                Logger.LogRow(Logger.LogType.Error, $"[PlayerCard] Avatar error: {ex.Message}");
                AvatarImage.Source = null;
            }
        }

        private void UpdateDiscordLabel()
        {
            if (DiscordOAuth.IsLoggedIn && DiscordOAuth.discordUserData != null)
            {
                string name = DiscordOAuth.DiscordUsername ?? "Unknown";
                DiscordStatusLabel.Text       = $"Logged in as {name} — Discord avatar will be used.";
                DiscordStatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
            }
            else
            {
                DiscordStatusLabel.Text       = "Not logged in to Discord — no avatar will be shown.";
                DiscordStatusLabel.Foreground = (Brush)FindResource("ControlDisabledGlythColour");
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────────────────────
        private static string FindServerProfilePath()
        {
            try
            {
                string ovrOrg = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "rad", "echovr", "users", "ovr-org");

                if (!Directory.Exists(ovrOrg)) return null;

                return Directory.GetDirectories(ovrOrg)
                    .Select(d => Path.Combine(d, "serverprofile.json"))
                    .FirstOrDefault(File.Exists);
            }
            catch { return null; }
        }

        /// <summary>
        /// Says how much of the replay folder the stats cover. Anything not indexed yet isn't counted, so a
        /// partly indexed folder asks for a full index.
        /// </summary>
        private void ShowIndexNotice(ReplayAnalyser.Analysis.PlayerCardResult card)
        {
            string counted = $"{card.Matches:N0} public arena match{(card.Matches == 1 ? "" : "es")} counted";
            if (card.PrivateSkipped > 0) counted += $", {card.PrivateSkipped:N0} private left out";
            if (card.Total == 0)
            {
                IndexNoticeText.Text = $"No replays found in {card.Folder}. Record some matches, then index them in Replay Analyser.";
                IndexNoticeText.Foreground = (Brush)FindResource("ControlDisabledGlythColour");
            }
            else if (!card.FullyIndexed)
            {
                IndexNoticeText.Text = $"⚠ Full index required: {card.Indexed:N0} of {card.Total:N0} replays indexed. " +
                                       "Open Replay Analyser › Library and press Index, then Refresh Stats. " +
                                       $"({counted} so far.)";
                IndexNoticeText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xA8, 0x26));
            }
            else
            {
                IndexNoticeText.Text = $"All {card.Total:N0} replays indexed: {counted}.";
                IndexNoticeText.Foreground = (Brush)FindResource("ControlDisabledGlythColour");
            }
        }

        private string GetFmt(string key, string format = "N0")
            => _loadedStats.TryGetValue(key, out double v) ? v.ToString(format) : "—";

        private void SetBarWidth(Border bar, string statKey, double maxValue)
        {
            if (bar.Parent is not Border container) return;
            double parentWidth = container.ActualWidth;
            if (parentWidth <= 0) parentWidth = 230;

            double val   = _loadedStats.TryGetValue(statKey, out double v) ? v : 0;
            double ratio = Math.Min(val / maxValue, 1.0);
            bar.Width    = Math.Max(ratio * parentWidth, 0);
        }

        private void SetStatus(string message, bool good)
        {
            // Removed StatusLabel block per user request. 
            // We just log errors now so the UI remains clean.
            if (!good) 
            {
                Logger.LogRow(Logger.LogType.Error, $"[PlayerCard] Status: {message}");
            }
            
            // Revert message of refresh button
            if (RefreshButton != null)
            {
                RefreshButton.Content = message.StartsWith("Loading") ? "🔄 Loading..." : "🔄 Refresh Stats";
            }
        }

        // ── Colour math helpers ───────────────────────────────────────
        private static Color Darken(Color c, double factor)
            => Color.FromRgb(
                (byte)(c.R * (1 - factor)),
                (byte)(c.G * (1 - factor)),
                (byte)(c.B * (1 - factor)));

        private static Color Brighten(Color c, double factor)
            => Color.FromRgb(
                (byte)Math.Min(255, c.R + (255 - c.R) * factor),
                (byte)Math.Min(255, c.G + (255 - c.G) * factor),
                (byte)Math.Min(255, c.B + (255 - c.B) * factor));

        private static Color Mix(Color a, Color b, double t)
            => Color.FromRgb(
                (byte)(a.R * (1 - t) + b.R * t),
                (byte)(a.G * (1 - t) + b.G * t),
                (byte)(a.B * (1 - t) + b.B * t));

        // ── Shared DrawingGroup layer helpers ─────────────────────────
        /// <summary>Adds a dark edge vignette to an existing DrawingGroup.</summary>
        private static void AddVignette(DrawingGroup dg, double w, double h, byte alpha)
        {
            var vig = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.5, 0.5), Center = new Point(0.5, 0.5),
                RadiusX = 0.75, RadiusY = 0.75,
            };
            vig.GradientStops.Add(new GradientStop(Color.FromArgb(0,     0, 0, 0), 0.0));
            vig.GradientStops.Add(new GradientStop(Color.FromArgb(0,     0, 0, 0), 0.5));
            vig.GradientStops.Add(new GradientStop(Color.FromArgb(alpha, 0, 0, 0), 1.0));
            dg.Children.Add(new GeometryDrawing(vig, null, new RectangleGeometry(new Rect(0, 0, w, h))));
        }

        /// <summary>Adds a semi-transparent diagonal bright highlight streak.</summary>
        private static void AddDiagonalHighlight(DrawingGroup dg, double w, double h, byte alpha)
        {
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                // Narrow band from upper-right to lower-left
                ctx.BeginFigure(new Point(-30,      115), true, true);
                ctx.LineTo(new Point(w + 30, -30),       true, false);
                ctx.LineTo(new Point(w + 30,  -5),       true, false);
                ctx.LineTo(new Point(-30,      140), true, false);
            }
            geo.Freeze();
            dg.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromArgb(alpha, 255, 255, 255)), null, geo));
        }

        /// <summary>Returns a diagonal band polygon from upper-left to lower-right at given y extents.</summary>
        private static StreamGeometry DiagonalBand(double w, double h, double yStart, double yEnd)
        {
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(-30, yStart),      true, true);
                ctx.LineTo(new Point(w + 30, yStart - h),   true, false);
                ctx.LineTo(new Point(w + 30, yEnd   - h),   true, false);
                ctx.LineTo(new Point(-30,    yEnd),          true, false);
            }
            geo.Freeze();
            return geo;
        }

        // ── Star polygon geometry ─────────────────────────────────────
        private static PathGeometry StarPath(double cx, double cy, double outerR, double innerR, int points = 5)
        {
            var fig   = new PathFigure();
            bool first = true;
            for (int i = 0; i < points * 2; i++)
            {
                double angle = i * Math.PI / points - Math.PI / 2;
                double r     = (i % 2 == 0) ? outerR : innerR;
                var    pt    = new Point(cx + r * Math.Cos(angle), cy + r * Math.Sin(angle));
                if (first) { fig.StartPoint = pt; first = false; }
                else fig.Segments.Add(new LineSegment(pt, true));
            }
            fig.IsClosed = true;
            return new PathGeometry(new[] { fig });
        }
    }
}
