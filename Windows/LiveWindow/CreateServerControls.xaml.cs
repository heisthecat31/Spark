using System;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;

namespace Spark
{
    public partial class CreateServerControls : UserControl
    {
        private string echoApiIp = "127.0.0.1";
        private int echoApiPort = 6721;
        private List<ServerData> servers = new List<ServerData>();
        private ServerData selectedServer = null;
        private bool isRunning = true;
        private DateTime? lastUpdate = null;
        
        public CreateServerControls()
        {
            InitializeComponent();
            InitializeServerBrowser();
            StartBackgroundThreads();
        }

        private static string IndexToRegion(int index)
        {
            return index switch
            {
                0 => "uscn",
                1 => "us-central-2",
                2 => "us-central-3",
                3 => "use",
                4 => "usw",
                5 => "euw",
                6 => "jp",
                7 => "aus",
                8 => "sin",
                _ => "",
            };
        }

        private static string IndexToMap(int index)
        {
            return index switch
            {
                0 => "mpl_arena_a",
                1 => "mpl_lobby_b2",
                2 => "mpl_combat_dyson",
                3 => "mpl_combat_combustion",
                4 => "mpl_combat_fission",
                5 => "mpl_combat_gauss",
                6 => "mpl_tutorial_lobby",
                7 => "mpl_tutorial_arena",
                _ => "",
            };
        }

        static readonly string[] gameTypes =
        {
            "",
            "Social_2.0_Private",
            "Social_2.0_NPE",
            "Social_2.0",
            "Echo_Arena",
            "Echo_Arena_Tournament",
            "Echo_Arena_Public_AI",
            "Echo_Arena_Practice_AI",
            "Echo_Arena_Private_AI",
            "Echo_Arena_First_Match",
            "Echo_Demo",
            "Echo_Demo_Public",
            "Echo_Arena_NPE",
            "Echo_Arena_Private",
            "Echo_Combat",
            "Echo_Combat_Tournament",
            "Echo_Combat_Private",
            "Echo_Combat_Public_AI",
            "Echo_Combat_Practice_AI",
            "Echo_Combat_Private_AI",
            "Echo_Combat_First_Match",
        };

        private static string IndexToGameType(int index)
        {
            return index < gameTypes.Length ? gameTypes[index] : "";
        }

        private void Create(object sender, RoutedEventArgs e)
        {
            string echoPath = SparkSettings.instance.echoVRPath;
            if (!string.IsNullOrEmpty(echoPath))
            {
                try
                {
                    Program.StartEchoVR(
                        SparkSettings.instance.chooseRegionSpectator ? Program.JoinType.Spectator : Program.JoinType.Player,
                        noovr: SparkSettings.instance.chooseRegionSpectator && SparkSettings.instance.chooseRegionNoOVR,
                        level: IndexToMap(SparkSettings.instance.chooseMapIndex),
                        region: IndexToRegion(SparkSettings.instance.chooseRegionIndex),
                        gameType: IndexToGameType(SparkSettings.instance.chooseGameTypeIndex),
                        port: 6721
                    );
                }
                catch (Exception ex)
                {
                    Logger.LogRow(Logger.LogType.Error, $"Error opening EchoVR Process for region selection\n{ex}");
                }
            }
            else
            {
                new MessageBox(Properties.Resources.echovr_path_not_set, Properties.Resources.Error, Program.Quit).Show();
            }
        }

        private void InstallOfflineEcho(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(SparkSettings.instance.echoVRPath)) return;
            if (!File.Exists(SparkSettings.instance.echoVRPath)) return;

            if (File.Exists(Path.Combine(Path.GetTempPath(), "dbgcore.dll")))
            {
                File.Delete(Path.Combine(Path.GetTempPath(), "dbgcore.dll"));
            }

            try
            {
                WebClient webClient = new WebClient();
                webClient.DownloadFileCompleted += OfflineEchoDownloadCompleted;
                webClient.DownloadFileAsync(new Uri("https://echo-foundation.pages.dev/files/offline_echo/dbgcore.dll"), Path.Combine(Path.GetTempPath(), "dbgcore.dll"));
            }
            catch (Exception)
            {
                new MessageBox("Something broke while trying to download update", "Error").Show();
            }
        }

        private void OfflineEchoDownloadCompleted(object sender, AsyncCompletedEventArgs e)
        {
            try
            {
                string dir = Path.GetDirectoryName(SparkSettings.instance.echoVRPath);
                if (dir != null)
                {
                    File.Copy(Path.Combine(Path.GetTempPath(), "dbgcore.dll"), Path.Combine(dir, "dbgcore.dll"), true);
                }
            }
            catch (Exception)
            {
                new MessageBox("Something broke while trying to install OfflineEcho. Report this to NtsFranz", "Error").Show();
            }
        }

        private void RemoveOfflineEcho(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(SparkSettings.instance.echoVRPath)) return;
            if (!File.Exists(SparkSettings.instance.echoVRPath)) return;
            string dir = Path.GetDirectoryName(SparkSettings.instance.echoVRPath);
            if (dir == null) return;

            try
            {
                File.Delete(Path.Combine(dir, "dbgcore.dll"));
            }
            catch (UnauthorizedAccessException)
            {
                new MessageBox("Can't uninstall OfflineEcho. Try closing EchoVR and trying again.", "Error").Show();
            }
        }

        private void RefreshServers_Click(object sender, RoutedEventArgs e)
        {
            RefreshServers();
        }

        private void ShowSettings_Click(object sender, RoutedEventArgs e)
        {
            ShowSettings();
        }

        private void ServerCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is ServerData server)
            {
                SelectServer(server);
            }
        }

        private void QuickJoinButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is ServerData server)
            {
                JoinServer(server);
            }
        }

        public class ServerData
        {
            public string id { get; set; }
            public string mode { get; set; }
            public bool open { get; set; }
            public List<PlayerData> players { get; set; }
            public GameStateData game_state { get; set; }
            
            public string DisplayName 
            { 
                get 
                { 
                    var modeDisplay = mode?.Replace("_", " ") ?? "";
                    if (!string.IsNullOrEmpty(modeDisplay))
                    {
                        modeDisplay = char.ToUpper(modeDisplay[0]) + modeDisplay.Substring(1).ToLower();
                    }
                    
                    var serverId = id?.Split('.')[0]?.ToUpper() ?? "N/A";
                    return $"{modeDisplay} - {serverId}";
                }
            }
            
            public string StatusText => open ? "OPEN" : "LOCKED";
            public string PlayerCountText => $"👥 {players?.Count ?? 0} players";
            public int BlueScore => game_state?.blue_score ?? 0;
            public int OrangeScore => game_state?.orange_score ?? 0;
        }

        public class PlayerData
        {
            public string display_name { get; set; }
            public string team { get; set; }
        }

        public class GameStateData
        {
            public int blue_score { get; set; }
            public int orange_score { get; set; }
        }

        public class ApiResponse
        {
            public List<ServerData> labels { get; set; }
        }

        private void InitializeServerBrowser()
        {
            ServerListControl.ItemsSource = servers;
        }

        private void RefreshServers()
        {
            Task.Run(() => FetchServers());
        }

        private async Task FetchServers()
        {
            try
            {
                using (WebClient client = new WebClient())
                {
                    string json = await client.DownloadStringTaskAsync("https://g.echovrce.com/status/matches");
                    var data = JsonConvert.DeserializeObject<ApiResponse>(json);
                    
                    if (data != null && data.labels != null)
                    {
                        List<ServerData> filteredServers = new List<ServerData>();
                        
                        foreach (var server in data.labels)
                        {
                            if (server.mode == "echo_arena" || server.mode == "echo_combat")
                            {
                                filteredServers.Add(server);
                            }
                        }
                        
                        servers = filteredServers;
                        lastUpdate = DateTime.Now;
                        
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            UpdateServerDisplay();
                            UpdateStatus(true, $"Found {servers.Count} servers");
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    UpdateStatus(false, $"Error: {ex.Message}");
                });
            }
        }

        private void UpdateServerDisplay()
        {
            ServerListControl.ItemsSource = null;
            ServerListControl.ItemsSource = servers;
            ServerCountLabel.Text = $"{servers.Count} servers";
            
            if (lastUpdate.HasValue)
            {
                UpdateLabel.Text = $"Last update: {lastUpdate.Value.ToString("HH:mm:ss")}";
            }
        }

        private void SelectServer(ServerData server)
        {
            selectedServer = server;
            UpdateServerDetails(server);
        }

        private void UpdateServerDetails(ServerData server)
        {
            ServerDetailsPanel.Children.Clear();
            
            var mode = server.mode?.Replace("_", " ") ?? "";
            if (!string.IsNullOrEmpty(mode))
            {
                mode = char.ToUpper(mode[0]) + mode.Substring(1).ToLower();
            }
            
            var serverId = server.id?.Split('.')[0]?.ToUpper() ?? "N/A";
            
            var titleLabel = new TextBlock
            {
                Text = mode,
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White,
                Margin = new Thickness(0, 0, 0, 5)
            };
            
            var idLabel = new TextBlock
            {
                Text = $"ID: {serverId}",
                FontSize = 11,
                Foreground = System.Windows.Media.Brushes.LightGray,
                Margin = new Thickness(0, 0, 0, 20)
            };
            
            ServerDetailsPanel.Children.Add(titleLabel);
            ServerDetailsPanel.Children.Add(idLabel);
            
            var statusFrame = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            var statusTitle = new TextBlock
            {
                Text = "Status:",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.LightGray,
                VerticalAlignment = VerticalAlignment.Center
            };
            var statusValue = new TextBlock
            {
                Text = server.open ? "OPEN" : "LOCKED",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = server.open ? System.Windows.Media.Brushes.Green : System.Windows.Media.Brushes.Red,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5, 0, 0, 0)
            };
            statusFrame.Children.Add(statusTitle);
            statusFrame.Children.Add(statusValue);
            ServerDetailsPanel.Children.Add(statusFrame);
            
            var playerCount = server.players?.Count ?? 0;
            var playersFrame = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            var playersTitle = new TextBlock
            {
                Text = "Players:",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.LightGray,
                VerticalAlignment = VerticalAlignment.Center
            };
            var playersValue = new TextBlock
            {
                Text = $"{playerCount}/14",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5, 0, 0, 0)
            };
            playersFrame.Children.Add(playersTitle);
            playersFrame.Children.Add(playersValue);
            ServerDetailsPanel.Children.Add(playersFrame);
            
            if (server.game_state != null)
            {
                var blueScore = server.game_state.blue_score;
                var orangeScore = server.game_state.orange_score;
                
                var scoreFrame = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 15) };
                var scoreTitle = new TextBlock
                {
                    Text = "Score:",
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Foreground = System.Windows.Media.Brushes.LightGray,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var scoreValue = new TextBlock
                {
                    Text = $"🔵 {blueScore} - {orangeScore} 🟠",
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Foreground = System.Windows.Media.Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(5, 0, 0, 0)
                };
                scoreFrame.Children.Add(scoreTitle);
                scoreFrame.Children.Add(scoreValue);
                ServerDetailsPanel.Children.Add(scoreFrame);
            }
            
            var buttonFrame = new StackPanel { Margin = new Thickness(0, 0, 0, 25) };
            
            var spectateButton = new Button
            {
                Content = "👁 SPECTATE",
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(35, 35, 35)),
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Height = 40,
                Cursor = Cursors.Hand,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 0, 0, 10)
            };
            spectateButton.Click += (s, e) => JoinServer(server);
            
            buttonFrame.Children.Add(spectateButton);
            ServerDetailsPanel.Children.Add(buttonFrame);
            
            var playerHeader = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            var playerTitle = new TextBlock
            {
                Text = $"Players ({playerCount})",
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };
            playerHeader.Children.Add(playerTitle);
            ServerDetailsPanel.Children.Add(playerHeader);
            
            var playerScrollViewer = new ScrollViewer
            {
                Height = 200,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            
            var playerContainer = new StackPanel();
            
            if (server.players != null && server.players.Count > 0)
            {
                var bluePlayers = server.players.Where(p => p.team == "blue").ToList();
                var orangePlayers = server.players.Where(p => p.team == "orange").ToList();
                var noTeamPlayers = server.players.Where(p => string.IsNullOrEmpty(p.team) || (p.team != "blue" && p.team != "orange")).ToList();
                
                if (bluePlayers.Count > 0)
                {
                    var blueHeader = new TextBlock
                    {
                        Text = "🔵 BLUE TEAM",
                        FontSize = 10,
                        FontWeight = FontWeights.Bold,
                        Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(96, 165, 250)),
                        Margin = new Thickness(0, 0, 0, 5)
                    };
                    playerContainer.Children.Add(blueHeader);
                    
                    foreach (var player in bluePlayers)
                    {
                        var playerLabel = new TextBlock
                        {
                            Text = $"• {player.display_name ?? "Unknown"}",
                            FontSize = 9,
                            Foreground = System.Windows.Media.Brushes.White,
                            Margin = new Thickness(10, 0, 0, 3)
                        };
                        playerContainer.Children.Add(playerLabel);
                    }
                    
                    playerContainer.Children.Add(new Border { Height = 10 });
                }
                
                if (orangePlayers.Count > 0)
                {
                    var orangeHeader = new TextBlock
                    {
                        Text = "🟠 ORANGE TEAM",
                        FontSize = 10,
                        FontWeight = FontWeights.Bold,
                        Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(251, 146, 60)),
                        Margin = new Thickness(0, 0, 0, 5)
                    };
                    playerContainer.Children.Add(orangeHeader);
                    
                    foreach (var player in orangePlayers)
                    {
                        var playerLabel = new TextBlock
                        {
                            Text = $"• {player.display_name ?? "Unknown"}",
                            FontSize = 9,
                            Foreground = System.Windows.Media.Brushes.White,
                            Margin = new Thickness(10, 0, 0, 3)
                        };
                        playerContainer.Children.Add(playerLabel);
                    }
                    
                    if (noTeamPlayers.Count > 0)
                    {
                        playerContainer.Children.Add(new Border { Height = 10 });
                    }
                }
                
                if (noTeamPlayers.Count > 0)
                {
                    var noTeamHeader = new TextBlock
                    {
                        Text = "⚪ NO TEAM",
                        FontSize = 10,
                        FontWeight = FontWeights.Bold,
                        Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 116, 139)),
                        Margin = new Thickness(0, 0, 0, 5)
                    };
                    playerContainer.Children.Add(noTeamHeader);
                    
                    foreach (var player in noTeamPlayers)
                    {
                        var playerLabel = new TextBlock
                        {
                            Text = $"• {player.display_name ?? "Unknown"}",
                            FontSize = 9,
                            Foreground = System.Windows.Media.Brushes.White,
                            Margin = new Thickness(10, 0, 0, 3)
                        };
                        playerContainer.Children.Add(playerLabel);
                    }
                }
            }
            else
            {
                var emptyLabel = new TextBlock
                {
                    Text = "No players in server",
                    FontSize = 10,
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 116, 139)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 20, 0, 0)
                };
                playerContainer.Children.Add(emptyLabel);
            }
            
            playerScrollViewer.Content = playerContainer;
            ServerDetailsPanel.Children.Add(playerScrollViewer);
            
            spectateButton.MouseEnter += (s, e) => spectateButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(50, 50, 50));
            spectateButton.MouseLeave += (s, e) => spectateButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(35, 35, 35));
        }

        private void JoinServer(ServerData server)
        {
            Task.Run(() => JoinServerThread(server));
        }

        private async Task JoinServerThread(ServerData server)
        {
            string serverId = server.id?.Split('.')[0]?.ToUpper() ?? "UNKNOWN";
            
            if (!await CheckApiConnection())
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    new MessageBox("Cannot connect to EchoVR API. Make sure EchoVR is running.", "API Error").Show();
                });
                return;
            }
            
            var joinData = new
            {
                session_id = serverId,
                password = ""
            };
            
            try
            {
                using (WebClient client = new WebClient())
                {
                    client.Headers[HttpRequestHeader.ContentType] = "application/json";
                    string response = await client.UploadStringTaskAsync($"{GetApiBaseUrl()}/join_session", 
                                                                       JsonConvert.SerializeObject(joinData));
                    
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        new MessageBox($"Spectating server: {serverId}", "Success").Show();
                    });
                }
            }
            catch (WebException ex)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    new MessageBox($"Failed to spectate server: {ex.Message}", "Error").Show();
                });
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    new MessageBox($"Failed to connect to EchoVR API:\n{ex.Message}", "Connection Error").Show();
                });
            }
        }

        private async Task<bool> CheckApiConnection()
        {
            try
            {
                using (WebClient client = new WebClient())
                {
                    string response = await client.DownloadStringTaskAsync($"{GetApiBaseUrl()}/session");
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        UpdateApiStatus(true);
                        UpdateStatus(true, "Connected");
                    });
                    return true;
                }
            }
            catch
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    UpdateApiStatus(false);
                    UpdateStatus(false, "Disconnected");
                });
                return false;
            }
        }

        private void UpdateApiStatus(bool connected)
        {
            if (connected)
            {
                ApiStatusLabel.Text = "API: Connected";
                ApiStatusLabel.Foreground = System.Windows.Media.Brushes.Green;
                StatusIndicator.Fill = System.Windows.Media.Brushes.Green;
            }
            else
            {
                ApiStatusLabel.Text = "API: Disconnected";
                ApiStatusLabel.Foreground = System.Windows.Media.Brushes.Red;
                StatusIndicator.Fill = System.Windows.Media.Brushes.Red;
            }
        }

        private void UpdateStatus(bool success, string message)
        {
            StatusTextBlock.Text = message;
            StatusIndicator.Fill = success ? System.Windows.Media.Brushes.Green : System.Windows.Media.Brushes.Red;
        }

        private string GetApiBaseUrl()
        {
            return $"http://{echoApiIp}:{echoApiPort}";
        }

        private void ShowSettings()
        {
            var settingsWindow = new Window
            {
                Title = "Settings",
                Width = 400,
                Height = 300,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this)
            };
            
            var stackPanel = new StackPanel { Margin = new Thickness(20) };
            
            var ipLabel = new Label { Content = "API IP Address:" };
            var ipTextBox = new TextBox { Text = echoApiIp, Margin = new Thickness(0, 0, 0, 10) };
            
            var portLabel = new Label { Content = "API Port:" };
            var portTextBox = new TextBox { Text = echoApiPort.ToString(), Margin = new Thickness(0, 0, 0, 20) };
            
            var testButton = new Button 
            { 
                Content = "Test Connection",
                Margin = new Thickness(0, 0, 0, 10)
            };
            testButton.Click += (s, e) => TestConnection(ipTextBox.Text, portTextBox.Text);
            
            var saveButton = new Button 
            { 
                Content = "Save Settings"
            };
            saveButton.Click += (s, e) => 
            {
                SaveSettings(ipTextBox.Text, portTextBox.Text);
                settingsWindow.Close();
            };
            
            stackPanel.Children.Add(ipLabel);
            stackPanel.Children.Add(ipTextBox);
            stackPanel.Children.Add(portLabel);
            stackPanel.Children.Add(portTextBox);
            stackPanel.Children.Add(testButton);
            stackPanel.Children.Add(saveButton);
            
            settingsWindow.Content = stackPanel;
            settingsWindow.ShowDialog();
        }

        private void TestConnection(string ip, string port)
        {
            Task.Run(async () =>
            {
                try
                {
                    using (WebClient client = new WebClient())
                    {
                        string response = await client.DownloadStringTaskAsync($"http://{ip}:{port}/session");
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            new MessageBox("Connection successful!", "Success").Show();
                        });
                    }
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        new MessageBox($"Connection failed: {ex.Message}", "Error").Show();
                    });
                }
            });
        }

        private void SaveSettings(string ip, string port)
        {
            try
            {
                echoApiIp = ip;
                echoApiPort = int.Parse(port);
                new MessageBox("Settings saved successfully!", "Success").Show();
            }
            catch (FormatException)
            {
                new MessageBox("Port must be a valid number!", "Error").Show();
            }
        }

        private void StartBackgroundThreads()
        {
            Task.Run(async () =>
            {
                while (isRunning)
                {
                    await FetchServers();
                    await Task.Delay(5000);
                }
            });

            Task.Run(async () =>
            {
                while (isRunning)
                {
                    await CheckApiConnection();
                    await Task.Delay(5000);
                }
            });
        }

        private void OnClosing()
        {
            isRunning = false;
        }
    }
}
