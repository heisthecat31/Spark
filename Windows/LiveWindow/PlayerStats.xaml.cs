using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Windows.Controls.Primitives;

namespace Spark
{
    public partial class PlayerStats : UserControl
    {
        private const string LeaderboardApiBase = "https://g.echovrce.com/leaderboard/records?guild_id=779349159852769310&game_mode=echo_arena&stat_name=ArenaWins&reset_schedule=alltime";
        private const string PlayerStatsApiBase = "https://g.echovrce.com/player/statistics?guild_id=779349159852769310&discord_id=";
        
        private ObservableCollection<LeaderboardEntry> _leaderboardData = new ObservableCollection<LeaderboardEntry>();
        private ObservableCollection<LeaderboardEntry> _filteredLeaderboardData = new ObservableCollection<LeaderboardEntry>();
        private HttpClient _httpClient = new HttpClient();
        private string _nextCursor = "";
        private bool _isLoading = false;
        private ScrollViewer _leaderboardScrollViewer;
        
        public PlayerStats()
        {
            InitializeComponent();
            Loaded += async (s, e) => 
            {
                await LoadInitialLeaderboard();
                AttachScrollViewer();
            };
            LeaderboardList.ItemsSource = _filteredLeaderboardData;
        }
        
        public class LeaderboardEntry
        {
            public int Rank { get; set; }
            public string Name { get; set; }
            public int WinCount { get; set; }
            public string OwnerId { get; set; }
        }
        
        public class PlayerStatistic
        {
            public string StatName { get; set; }
            public string StatValue { get; set; }
        }
        
        private async Task LoadInitialLeaderboard()
        {
            try
            {
                LeaderboardStatusText.Text = "Loading leaderboard...";
                LeaderboardStatusText.Foreground = Brushes.Yellow;
                
                _leaderboardData.Clear();
                _filteredLeaderboardData.Clear();
                _nextCursor = "";
                
                // Load first 100 entries
                string url = $"{LeaderboardApiBase}&limit=100&from_rank=1";
                await LoadLeaderboardPage(url);
                
                LeaderboardStatusText.Text = $"{_leaderboardData.Count} players loaded";
                LeaderboardStatusText.Foreground = Brushes.LightGreen;
            }
            catch (Exception ex)
            {
                LeaderboardStatusText.Text = $"Error: {ex.Message}";
                LeaderboardStatusText.Foreground = Brushes.Red;
            }
        }
        
        private async Task LoadLeaderboardPage(string url)
        {
            if (_isLoading) return;
            
            try
            {
                _isLoading = true;
                LeaderboardStatusText.Text = "Loading more...";
                LeaderboardStatusText.Foreground = Brushes.Yellow;
                
                string json = await _httpClient.GetStringAsync(url);
                var leaderboard = JsonConvert.DeserializeObject<JObject>(json);
                
                if (leaderboard?["records"] is JArray records)
                {
                    foreach (var record in records)
                    {
                        var entry = new LeaderboardEntry
                        {
                            Rank = record["rank"]?.Value<int>() ?? 0,
                            Name = record["username"]?["value"]?.Value<string>() ?? "Unknown Player",
                            WinCount = record["num_score"]?.Value<int>() ?? 0,
                            OwnerId = record["owner_id"]?.Value<string>() ?? string.Empty
                        };
                        
                        // Check for duplicates before adding
                        if (!_leaderboardData.Any(e => e.Rank == entry.Rank && e.Name == entry.Name))
                        {
                            _leaderboardData.Add(entry);
                            _filteredLeaderboardData.Add(entry);
                        }
                    }
                    
                    // Update cursor for next page
                    _nextCursor = leaderboard["next_cursor"]?.Value<string>() ?? "";
                }
                
                LeaderboardStatusText.Text = $"{_leaderboardData.Count} players loaded";
                LeaderboardStatusText.Foreground = Brushes.LightGreen;
            }
            catch (Exception ex)
            {
                LeaderboardStatusText.Text = $"Error: {ex.Message}";
                LeaderboardStatusText.Foreground = Brushes.Red;
            }
            finally
            {
                _isLoading = false;
            }
        }
        
        private void AttachScrollViewer()
        {
            // Find the ScrollViewer in the ItemsControl
            if (VisualTreeHelper.GetChildrenCount(LeaderboardList) > 0)
            {
                var border = VisualTreeHelper.GetChild(LeaderboardList, 0) as Border;
                if (border != null)
                {
                    _leaderboardScrollViewer = border.Child as ScrollViewer;
                    if (_leaderboardScrollViewer != null)
                    {
                        _leaderboardScrollViewer.ScrollChanged += LeaderboardScrollViewer_ScrollChanged;
                    }
                }
            }
        }
        
        private async void LeaderboardScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_leaderboardScrollViewer == null || _isLoading || string.IsNullOrEmpty(_nextCursor))
                return;
            
            // Check if user has scrolled to the bottom
            if (_leaderboardScrollViewer.VerticalOffset >= _leaderboardScrollViewer.ScrollableHeight - 50)
            {
                // Load next page
                string url = $"{LeaderboardApiBase}&limit=100&cursor={Uri.EscapeDataString(_nextCursor)}";
                await LoadLeaderboardPage(url);
            }
        }
        
        private void LeaderboardSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string searchText = LeaderboardSearchBox.Text.Trim();
            
            if (string.IsNullOrWhiteSpace(searchText))
            {
                // Show all loaded data
                _filteredLeaderboardData.Clear();
                foreach (var entry in _leaderboardData)
                {
                    _filteredLeaderboardData.Add(entry);
                }
                LeaderboardStatusText.Text = $"{_leaderboardData.Count} players loaded";
            }
            else
            {
                // Search API for specific player
                _ = SearchLeaderboardPlayer(searchText);
            }
        }
        
        private async Task SearchLeaderboardPlayer(string playerName)
        {
            try
            {
                _isLoading = true;
                LeaderboardStatusText.Text = $"Searching for '{playerName}'...";
                LeaderboardStatusText.Foreground = Brushes.Yellow;
                
                // Clear current filtered results
                _filteredLeaderboardData.Clear();
                
                // Search from rank 1
                string searchUrl = $"{LeaderboardApiBase}&limit=100&from_rank=1";
                bool found = false;
                string cursor = "";
                
                while (!found && !string.IsNullOrEmpty(cursor) || cursor == "")
                {
                    string url = cursor == "" ? searchUrl : $"{LeaderboardApiBase}&limit=100&cursor={Uri.EscapeDataString(cursor)}";
                    string json = await _httpClient.GetStringAsync(url);
                    var leaderboard = JsonConvert.DeserializeObject<JObject>(json);
                    
                    if (leaderboard?["records"] is JArray records)
                    {
                        foreach (var record in records)
                        {
                            var entryName = record["username"]?["value"]?.Value<string>() ?? "";
                            
                            if (entryName.IndexOf(playerName, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                var entry = new LeaderboardEntry
                                {
                                    Rank = record["rank"]?.Value<int>() ?? 0,
                                    Name = entryName,
                                    WinCount = record["num_score"]?.Value<int>() ?? 0,
                                    OwnerId = record["owner_id"]?.Value<string>() ?? string.Empty
                                };
                                
                                _filteredLeaderboardData.Add(entry);
                                found = true;
                            }
                        }
                        
                        cursor = leaderboard["next_cursor"]?.Value<string>() ?? "";
                        
                        // If we found results and want to stop after first match
                        if (found && _filteredLeaderboardData.Count >= 20)
                            break;
                    }
                    else
                    {
                        break;
                    }
                    
                    // Small delay to avoid rate limiting
                    await Task.Delay(50);
                }
                
                if (_filteredLeaderboardData.Count > 0)
                {
                    LeaderboardStatusText.Text = $"Found {_filteredLeaderboardData.Count} match(es)";
                    LeaderboardStatusText.Foreground = Brushes.LightGreen;
                }
                else
                {
                    LeaderboardStatusText.Text = $"No players found matching '{playerName}'";
                    LeaderboardStatusText.Foreground = Brushes.Orange;
                    
                    // Show all loaded data if no search results
                    foreach (var entry in _leaderboardData)
                    {
                        _filteredLeaderboardData.Add(entry);
                    }
                }
            }
            catch (Exception ex)
            {
                LeaderboardStatusText.Text = $"Search error: {ex.Message}";
                LeaderboardStatusText.Foreground = Brushes.Red;
                
                // Fallback to local search on error
                _filteredLeaderboardData.Clear();
                foreach (var entry in _leaderboardData.Where(e => 
                    e.Name.IndexOf(playerName, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    _filteredLeaderboardData.Add(entry);
                }
            }
            finally
            {
                _isLoading = false;
            }
        }
        
        private async void SearchPlayerButton_Click(object sender, RoutedEventArgs e)
        {
            string discordId = DiscordIdInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(discordId) || discordId == "Enter Discord ID")
            {
                PlayerStatusText.Text = "Please enter a valid Discord ID";
                PlayerStatusText.Foreground = Brushes.Red;
                return;
            }
            
            await SearchPlayerStats(discordId);
        }
        
        private async Task SearchPlayerStats(string discordId)
        {
            try
            {
                PlayerStatusText.Text = "Searching...";
                PlayerStatusText.Foreground = Brushes.Yellow;
                SearchPlayerButton.IsEnabled = false;
                
                string apiUrl = $"{PlayerStatsApiBase}{discordId}";
                string json = await _httpClient.GetStringAsync(apiUrl);
                var playerData = JsonConvert.DeserializeObject<JObject>(json);
                
                if (playerData == null)
                {
                    throw new Exception("No data returned from API");
                }
                
                PlayerStatsGrid.Visibility = Visibility.Visible;
                
                var arenaStats = playerData["stats"]?["arena"];
                if (arenaStats != null)
                {
                    int playerWins = arenaStats["ArenaWins"]?["val"]?.Value<int>() ?? 0;
                    
                    string matchedName = "Unknown Player";
                    int matchedRank = 0;
                    
                    var matchingEntries = _leaderboardData
                        .Where(entry => entry.WinCount == playerWins)
                        .OrderBy(entry => entry.Rank)
                        .ToList();
                    
                    if (matchingEntries.Count > 0)
                    {
                        matchedName = matchingEntries[0].Name;
                        matchedRank = matchingEntries[0].Rank;
                        
                        if (matchingEntries.Count > 1)
                        {
                            matchedName += $" (+{matchingEntries.Count - 1} others)";
                        }
                    }
                    
                    PlayerNameText.Text = matchedName;
                    PlayerRankText.Text = matchedRank > 0 
                        ? $"Global Rank: #{matchedRank}" 
                        : $"Wins: {playerWins} (Not on leaderboard)";
                    
                    TotalWinsText.Text = playerWins.ToString();
                    ArenaWinsText.Text = playerWins.ToString();
                    SavesText.Text = arenaStats["Saves"]?["val"]?.Value<string>() ?? "0";
                    GoalsText.Text = arenaStats["Goals"]?["val"]?.Value<string>() ?? "0";
                    StunsText.Text = arenaStats["Stuns"]?["val"]?.Value<string>() ?? "0";
                    
                    var allStats = new ObservableCollection<PlayerStatistic>();
                    
                    foreach (var property in ((JObject)arenaStats).Properties())
                    {
                        var statValue = property.Value["val"]?.ToString() ?? "0";
                        if (double.TryParse(statValue, out double numericValue))
                        {
                            statValue = numericValue.ToString("0.##");
                        }
                        
                        allStats.Add(new PlayerStatistic
                        {
                            StatName = FormatStatName(property.Name),
                            StatValue = statValue
                        });
                    }
                    
                    AllStatsList.ItemsSource = new ObservableCollection<PlayerStatistic>(
                        allStats.OrderBy(s => s.StatName));
                }
                
                PlayerStatusText.Text = "Player data loaded";
                PlayerStatusText.Foreground = Brushes.LightGreen;
            }
            catch (Exception ex)
            {
                PlayerStatsGrid.Visibility = Visibility.Hidden;
                PlayerStatusText.Text = $"Error: {ex.Message}";
                PlayerStatusText.Foreground = Brushes.Red;
            }
            finally
            {
                SearchPlayerButton.IsEnabled = true;
            }
        }
        
        private string FormatStatName(string propertyName)
        {
            return System.Text.RegularExpressions.Regex.Replace(propertyName, "([a-z])([A-Z])", "$1 $2");
        }
        
        private async void RefreshLeaderboard_Click(object sender, RoutedEventArgs e)
        {
            await LoadInitialLeaderboard();
        }
        
        private void DiscordIdInput_GotFocus(object sender, RoutedEventArgs e)
        {
            if (DiscordIdInput.Text == "Enter Discord ID")
            {
                DiscordIdInput.Text = "";
                DiscordIdInput.Foreground = Brushes.White;
            }
        }
        
        private void DiscordIdInput_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(DiscordIdInput.Text))
            {
                DiscordIdInput.Text = "Enter Discord ID";
                DiscordIdInput.Foreground = Brushes.Gray;
            }
        }
    }
}
