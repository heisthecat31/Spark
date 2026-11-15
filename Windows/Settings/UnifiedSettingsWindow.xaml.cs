using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Data;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net;
using EchoVRAPI;
using System.Threading;



namespace Spark
{
	/// <summary>
	/// Interaction logic for UnifiedSettingsWindow.xaml
	/// </summary>
	public partial class UnifiedSettingsWindow
	{
		// set to false initially so that loading the settings from disk doesn't activate the events
		private bool initialized;



		/// <summary>
		/// Throttles theme updates during slider dragging to prevent UI lag.
		/// </summary>
		private readonly System.Windows.Threading.DispatcherTimer _themeDebounceTimer;


		/// <summary>
		/// Set before opening this window (see <see cref="Program.ToggleWindow"/>, which only supports
		/// a parameterless constructor) to land on the Change Theme tab instead of the default. Read
		/// once in the constructor and reset immediately after.
		/// </summary>
		public static bool OpenToChangeTheme;

		public UnifiedSettingsWindow()
		{
			InitializeComponent();

			if (OpenToChangeTheme)
			{
				OpenToChangeTheme = false;
				tabControl.SelectedItem = ChangeThemeTab;
			}

			_themeDebounceTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
			_themeDebounceTimer.Tick += (s, ev) =>
			{
				_themeDebounceTimer.Stop();
				ThemesController.ApplyCustomTheme(_themeDark, _themeMid, _themeLight);
			};
		}

		/// <summary>Selects the Change Theme tab on an already-open instance of this window.</summary>
		public void JumpToChangeThemeTab()
		{
			tabControl.SelectedItem = ChangeThemeTab;
		}

		private void WindowLoad(object sender, RoutedEventArgs e)
		{
			try
			{
				//Initialize();

				echodemoStatusLabel.Text = EchoReplayManager.StatusText();




#if WINDOWS_STORE_RELEASE
				enableBetasCheckbox.Visibility = Visibility.Collapsed;
#endif


				ThisPCLocalIP.Text = $"This PC's Local IP: {QuestIPFetching.GetLocalIP()} (for PC-PC Spectate Me)";

				CameraModeDropdownChanged(SparkSettings.instance.spectatorCamera);

				if (SparkSettings.instance.mutePlayerComms)
				{
					MutePlayerCommsDropdown.SelectedIndex = 2;
				}
				else if (SparkSettings.instance.muteEnemyTeam)
				{
					MutePlayerCommsDropdown.SelectedIndex = 1;
				}
				else
				{
					MutePlayerCommsDropdown.SelectedIndex = 0;
				}

				initialized = true;
			}
			catch (Exception ex)
			{
				Logger.LogRow(Logger.LogType.Error, $"Error during settings window load: {ex}");
			}
		}


		#region General

		public static bool StartWithWindows
		{
			get => SparkSettings.instance.startOnBoot;
			set
			{
				SparkSettings.instance.startOnBoot = value;

				RegistryKey rk =
					Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);

				if (value)
				{
					string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Spark.exe");
					rk?.SetValue(Properties.Resources.AppName, path);
				}
				else
					rk?.DeleteValue(Properties.Resources.AppName, false);
			}
		}

		private void CloseButtonEvent(object sender, RoutedEventArgs e)
		{
			SparkSettings.instance.Save();
			Close();
		}

		private void EchoVRIPChanged(object sender, TextChangedEventArgs e)
		{
			if (!initialized) return;
			Program.echoVRIP = ((TextBox)sender).Text;
			SparkSettings.instance.echoVRIP = Program.echoVRIP;
			SaveQuestIPButton.Visibility = Visibility.Visible;
		}

		private void EchoVRPortChanged(object sender, TextChangedEventArgs e)
		{
			if (!initialized) return;
			if (Program.overrideEchoVRPort)
			{
				EchoVRPortTextBox.Text = SparkSettings.instance.echoVRPort.ToString();
			}
			else
			{
				if (int.TryParse(((TextBox)sender).Text, out Program.echoVRPort))
				{
					SparkSettings.instance.echoVRPort = Program.echoVRPort;
				}
			}
		}

		private void ResetIP_Click(object sender, RoutedEventArgs e)
		{
			if (!initialized) return;
			Program.echoVRIP = "127.0.0.1";
			if (!Program.overrideEchoVRPort) Program.echoVRPort = 6721;
			EchoVRIPTextBox.Text = Program.echoVRIP;
			EchoVRPortTextBox.Text = Program.echoVRPort.ToString();
			SparkSettings.instance.echoVRIP = Program.echoVRIP;
			if (!Program.overrideEchoVRPort) SparkSettings.instance.echoVRPort = Program.echoVRPort;
			SaveQuestIPButton.Visibility = Visibility.Collapsed;
		}

		private void ExecutableLocationChanged(object sender, TextChangedEventArgs e)
		{
			if (!initialized) return;
			string path = ((TextBox)sender).Text.Trim().Trim('"');

			if (path.EndsWith("ready-at-dawn-echo-arena", StringComparison.OrdinalIgnoreCase) || 
				path.EndsWith("ready-at-dawn-echo-arena\\", StringComparison.OrdinalIgnoreCase) ||
				path.EndsWith("ready-at-dawn-echo-arena/", StringComparison.OrdinalIgnoreCase))
			{
				string exePath = Path.Combine(path, "bin", "win10", "echovr.exe");
				if (File.Exists(exePath))
				{
					path = exePath;
				}
				else
				{
					ExeLocationLabel.Content = "EchoVR Executable Location:   (echovr.exe not found in bin\\win10)";
					return;
				}
			}

			if (File.Exists(path))
			{
				ExeLocationLabel.Content = "EchoVR Executable Location:";
				SparkSettings.instance.echoVRPath = path;

				if (((TextBox)sender).Text != path)
				{
					((TextBox)sender).Text = path;
				}
			}
			else
			{
				ExeLocationLabel.Content = "EchoVR Executable Location:   (not valid)";
			}
		}

		private void OpenQuestSpectatorPopup(object sender, RoutedEventArgs e)
		{
			if (!string.IsNullOrEmpty(SparkSettings.instance.followPlayerName))
			{
				TargetNameInput.Text = SparkSettings.instance.followPlayerName;
			}

			AutoJoinCheckbox.IsChecked = SparkSettings.instance.questSpectatorAutoJoin;
			AnonymousModeCheckbox.IsChecked = SparkSettings.instance.questSpectatorAnonymous;
			FollowSelfRadio.IsChecked = SparkSettings.instance.questSpectatorFollowSelf;
			FollowFriendRadio.IsChecked = !SparkSettings.instance.questSpectatorFollowSelf;

			UpdateQuestSpectatorPanels();
			UpdateQuestSpectatorRunningUI();

			QuestSpectatorPopup.Visibility = Visibility.Visible;
		}

		private void CloseSpectatorPopup(object sender, RoutedEventArgs e)
		{
			QuestSpectatorPopup.Visibility = Visibility.Collapsed;
		}

		private void QuestSpectatorModeChanged(object sender, RoutedEventArgs e)
		{
			UpdateQuestSpectatorPanels();
		}

		private void UpdateQuestSpectatorPanels()
		{
			if (QuestSpectatorNamePanel == null || QuestSpectatorSelfPanel == null) return;

			bool self = FollowSelfRadio.IsChecked == true;
			QuestSpectatorNamePanel.Visibility = self ? Visibility.Collapsed : Visibility.Visible;
			QuestSpectatorSelfPanel.Visibility = self ? Visibility.Visible : Visibility.Collapsed;
		}

		private void UpdateQuestSpectatorRunningUI()
		{
			bool running = Program.simpleSpectateController?.Running == true;
			StopQuestSpectatorButton.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
			LaunchQuestSpectatorButton.Content = running ? "Relaunch Spectator" : "Launch Spectator";
		}

		private void SetQuestSpectatorStatus(string status)
		{
			Dispatcher.Invoke(() =>
			{
				QuestSpectatorStatusText.Text = status ?? "";
				QuestSpectatorStatusText.Visibility = string.IsNullOrEmpty(status) ? Visibility.Collapsed : Visibility.Visible;
			});
		}

		private void StopQuestSpectator(object sender, RoutedEventArgs e)
		{
			Program.simpleSpectateController.Stop();
			SetQuestSpectatorStatus("Stopped.");
			UpdateQuestSpectatorRunningUI();
		}

		private void LaunchQuestSpectator(object sender, RoutedEventArgs e)
		{
			bool followSelf = FollowSelfRadio.IsChecked == true;
			string targetName = TargetNameInput.Text.Trim();

			if (!followSelf && string.IsNullOrEmpty(targetName))
			{
				new Spark.MessageBox("Please enter the name of the player you want to spectate.", Properties.Resources.Error).Show();
				return;
			}

			// Following your own account means reading your own presence row off the friends bot,
			// which needs a Discord login on this PC as well as the playing one.
			if (followSelf && !FriendsPresence.Configured)
			{
				new Spark.MessageBox(
					"Log into Discord in Spark on both PCs to spectate yourself — that's how this PC finds the session the other one is in.",
					Properties.Resources.Error).Show();
				return;
			}

			SparkSettings.instance.questSpectatorFollowSelf = followSelf;
			if (!followSelf) SparkSettings.instance.followPlayerName = targetName;
			SparkSettings.instance.questSpectatorAutoJoin = AutoJoinCheckbox.IsChecked == true;
			SparkSettings.instance.questSpectatorAnonymous = AnonymousModeCheckbox.IsChecked == true;
			SparkSettings.instance.Save();

			// Point the spectator camera at whoever we're following.
			if (!followSelf)
			{
				SparkSettings.instance.spectatorCamera = 3;
				CameraModeDropdownChanged(3);
			}

			SimpleSpectateController controller = Program.simpleSpectateController;
			controller.StatusChanged -= SetQuestSpectatorStatus;
			controller.StatusChanged += SetQuestSpectatorStatus;

			controller.Start(
				followSelf ? SimpleSpectateController.FollowMode.Self : SimpleSpectateController.FollowMode.Named,
				targetName,
				SparkSettings.instance.questSpectatorAnonymous,
				SparkSettings.instance.questSpectatorAutoJoin);

			UpdateQuestSpectatorRunningUI();
		}
		
		private async void FindQuestClick(object sender, RoutedEventArgs e)
		{
			if (!initialized) return;
			FindQuestStatusLabel.Content = Properties.Resources.Searching_for_Quest_on_network;
			FindQuestStatusLabel.Visibility = Visibility.Visible;
			EchoVRIPTextBox.IsEnabled = false;
			EchoVRPortTextBox.IsEnabled = false;
			FindQuest.IsEnabled = false;
			ResetIP.IsEnabled = false;
			SaveQuestIPButton.Visibility = Visibility.Collapsed;

			Progress<string> progress = new Progress<string>(s => FindQuestStatusLabel.Content = s);
			await Task.Factory.StartNew(() => Program.echoVRIP = QuestIPFetching.FindQuestIP(progress), TaskCreationOptions.None);

			// if we failed with this method, scan the network with API requests instead
			if ((string)FindQuestStatusLabel.Content == Properties.Resources.Failed_to_find_Quest_on_network_)
			{
				FindQuestStatusLabel.Content = Properties.Resources.Failed__Scanning_for_Echo_VR_API_instead__This_may_take_a_while_;
				await Task.Run(async () =>
				{
					Progress<float> searchProgress = new Progress<float>();
					searchProgress.ProgressChanged += (o, val) => { Dispatcher.Invoke(() => { FindQuestStatusLabel.Content = $"{Properties.Resources.Failed__Scanning_for_Echo_VR_API_instead__This_may_take_a_while_}\t{val:P0}"; }); };
					List<IPAddress> ips = QuestIPFetching.GetPossibleLocalIPs();
					List<(IPAddress, string)> responses = await QuestIPFetching.PingEchoVRAPIAsync(ips, 20, searchProgress);
					IPAddress found = responses.FirstOrDefault(r => r.Item2 != null).Item1;
					Dispatcher.Invoke(() =>
					{
						if (found != null)
						{
							Program.echoVRIP = found.ToString();

							FindQuestStatusLabel.Content = Properties.Resources.Found_Quest_on_network_;
						}
						else
						{
							FindQuestStatusLabel.Content = Properties.Resources.Failed_to_find_Quest_on_network__Make_sure_you_are_in_a_private_public_match_and_API_Access_is_enabled_;
						}
					});
				});
			}

			EchoVRIPTextBox.IsEnabled = true;
			EchoVRPortTextBox.IsEnabled = true;
			FindQuest.IsEnabled = true;
			ResetIP.IsEnabled = true;
			if (!Program.overrideEchoVRPort) Program.echoVRPort = 6721;
			EchoVRIPTextBox.Text = Program.echoVRIP;
			EchoVRPortTextBox.Text = Program.echoVRPort.ToString();
			SparkSettings.instance.echoVRIP = Program.echoVRIP;
			if (!Program.overrideEchoVRPort) SparkSettings.instance.echoVRPort = Program.echoVRPort;
			SaveQuestIPButton.Visibility = Visibility.Collapsed;
		}

		private void ShowFirstTimeSetupWindowClicked(object sender, RoutedEventArgs e)
		{
			if (!initialized) return;

			Program.ToggleWindow(typeof(FirstTimeSetupWindow));
		}

		public static Visibility FirestoreVisible => !DiscordOAuth.Personal ? Visibility.Visible : Visibility.Collapsed;

		public static string ReplayFilename => string.IsNullOrEmpty(Program.replayFilesManager.fileName) ? "---" : Program.replayFilesManager.fileName;

		private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
		{
			try
			{
				Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
				e.Handled = true;
			}
			catch (Exception ex)
			{
				Logger.LogRow(Logger.LogType.Error, ex.ToString());
			}
		}

		public int SetTheme
		{
			get => SparkSettings.instance.theme;
			set
			{
				SparkSettings.instance.theme = value;

				ThemesController.SetTheme((ThemesController.ThemeTypes)value);
			}
		}



		#endregion

		#region Replays

		private void OpenReplayFolder(object sender, RoutedEventArgs e)
		{
			if (!initialized) return;
			Process.Start(new ProcessStartInfo
			{
				FileName = SparkSettings.instance.saveFolder,
				UseShellExecute = true
			});
		}

		private void ResetReplayFolder(object sender, RoutedEventArgs e)
		{
			if (!initialized) return;
			SparkSettings.instance.saveFolder =
				Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Spark", "replays");
			Directory.CreateDirectory(SparkSettings.instance.saveFolder);
			storageLocationTextBox.Text = SparkSettings.instance.saveFolder;
		}

		private void SetStorageLocation(object sender, RoutedEventArgs e)
		{
			if (!initialized) return;
			string selectedPath = "";
			OpenFolderDialog folderBrowserDialog = new OpenFolderDialog
			{
				InitialDirectory = SparkSettings.instance.saveFolder
			};
			if (folderBrowserDialog.ShowDialog() == true)
			{
				selectedPath = folderBrowserDialog.FolderName;
			}

			if (selectedPath != "")
			{
				SetStorageLocation(selectedPath);
				Console.WriteLine(selectedPath);
			}
		}

		private void SetStorageLocation(string path)
		{
			SparkSettings.instance.saveFolder = path;
			storageLocationTextBox.Text = SparkSettings.instance.saveFolder;
		}

		private void SplitFileButtonClicked(object sender, RoutedEventArgs e)
		{
			if (!initialized) return;
			Program.replayFilesManager.Split();
		}

		/// <summary>
		/// Repairs a .butter file the replay viewer won't open, writing what survives out as a
		/// .echoreplay next to it. The decode runs off the UI thread: a long recording is tens of
		/// thousands of frames and would otherwise freeze the settings window.
		/// </summary>
		private async void RepairButterFileClicked(object sender, RoutedEventArgs e)
		{
			if (!initialized) return;

			Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog
			{
				FileName = "",
				DefaultExt = ".butter",
				Filter = "Butter replays (.butter)|*.butter|All files|*.*",
				InitialDirectory = Directory.Exists(SparkSettings.instance.saveFolder) ? SparkSettings.instance.saveFolder : "",
			};

			if (dlg.ShowDialog() != true) return;

			string path = dlg.FileName;

			repairButterButton.IsEnabled = false;
			repairButterStatusLabel.Content = "Repairing " + Path.GetFileName(path) + "...";

			try
			{
				ButterRepair.RepairResult result = await Task.Run(() => ButterRepair.RepairToEchoreplay(path));

				repairButterStatusLabel.Content = result.Success
					? $"Recovered {result.FrameCount:N0} frames"
					: "Couldn't recover any frames";

				new MessageBox(result.Summary, result.Success ? "Replay Repaired" : "Repair Failed").Show();

				// Drop the user at the result so they don't have to go hunting for it.
				if (result.Success && !string.IsNullOrEmpty(result.OutputPath))
				{
					try { Process.Start("explorer.exe", "/select,\"" + result.OutputPath + "\""); }
					catch (Exception ex) { Logger.LogRow(Logger.LogType.Error, "Couldn't reveal the repaired replay\n" + ex); }
				}
			}
			catch (Exception ex)
			{
				repairButterStatusLabel.Content = "Repair failed";
				Logger.LogRow(Logger.LogType.Error, $"Butter repair threw for {path}\n{ex}");
				new MessageBox("Repair failed:\n\n" + ex.Message, "Repair Failed").Show();
			}
			finally
			{
				repairButterButton.IsEnabled = true;
			}
		}

		/// <summary>
		/// Scans the save folder for .butter files the replay viewer won't open, repairs them, and
		/// deletes the originals.
		///
		/// Deliberately a button rather than something that runs on its own: it removes recordings,
		/// and that shouldn't happen while nobody is watching. The confirmation below is the last
		/// chance to back out, and a repair whose replacement doesn't verify keeps both files.
		/// </summary>
		/// <summary>Live while a scan is running; the same button cancels it.</summary>
		private CancellationTokenSource butterScanCancel;

		private async void ScanBrokenButterClicked(object sender, RoutedEventArgs e)
		{
			if (!initialized) return;

			// Second press while a scan is in flight means stop, not start again.
			if (butterScanCancel != null)
			{
				butterScanCancel.Cancel();
				scanBrokenButterButton.IsEnabled = false;
				scanBrokenButterButton.Content = "Stopping...";
				scanBrokenButterStatusLabel.Content = "Finishing the current file...";
				return;
			}

			string folder = SparkSettings.instance.saveFolder;
			if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
			{
				new MessageBox("Your replay save folder isn't set or doesn't exist, so there's nothing to scan.", "Nothing to Scan").Show();
				return;
			}

			int butterCount;
			try { butterCount = Directory.GetFiles(folder, "*.butter", SearchOption.TopDirectoryOnly).Length; }
			catch (Exception ex)
			{
				new MessageBox("Couldn't read the save folder:\n\n" + ex.Message, "Nothing to Scan").Show();
				return;
			}

			if (butterCount == 0)
			{
				new MessageBox("No .butter files found in:\n" + folder, "Nothing to Scan").Show();
				return;
			}

			if (System.Windows.MessageBox.Show(
					$"Check {butterCount:N0} .butter file(s) in:\n{folder}\n\n" +
					"Broken ones will be converted to .echoreplay and the broken originals deleted.\n\n" +
					"Files that decode fine are left alone, and a repair that can't be verified keeps the original.",
					"Search and Repair",
					MessageBoxButton.OKCancel,
					MessageBoxImage.Warning) != MessageBoxResult.OK)
			{
				return;
			}

			butterScanCancel = new CancellationTokenSource();
			CancellationToken token = butterScanCancel.Token;

			scanBrokenButterButton.Content = "Cancel";
			repairButterButton.IsEnabled = false;
			scanBrokenButterStatusLabel.Content = "Scanning...";

			try
			{
				// Progress arrives on a worker thread, so hop to the dispatcher to touch the label.
				// BeginInvoke rather than Invoke: a synchronous hop per file would stall the scan
				// behind the UI thread, which is part of why this felt slow.
				Action<string> progress = msg => Dispatcher.BeginInvoke(
					new Action(() => scanBrokenButterStatusLabel.Content = msg));

				ButterRepair.ScanResult result = await Task.Run(
					() => ButterRepair.ScanAndRepair(folder, deleteOriginals: true, progress: progress, token: token), token);

				scanBrokenButterStatusLabel.Content = result.Cancelled
					? $"Stopped after {result.Scanned:N0}"
					: result.Broken == 0
						? $"Checked {result.Scanned:N0}, none broken"
						: $"Repaired {result.Repaired:N0} of {result.Broken:N0}";

				string detail = result.Details.Count > 0
					? "\n\n" + string.Join("\n", result.Details.Take(20))
					  + (result.Details.Count > 20 ? $"\n...and {result.Details.Count - 20:N0} more (see the log)" : "")
					: "";

				new MessageBox(result.Summary + detail, "Search and Repair").Show();
			}
			catch (OperationCanceledException)
			{
				scanBrokenButterStatusLabel.Content = "Stopped";
			}
			catch (Exception ex)
			{
				scanBrokenButterStatusLabel.Content = "Scan failed";
				Logger.LogRow(Logger.LogType.Error, $"Butter scan threw for {folder}\n{ex}");
				new MessageBox("Scan failed:\n\n" + ex.Message, "Search and Repair").Show();
			}
			finally
			{
				butterScanCancel?.Dispose();
				butterScanCancel = null;

				scanBrokenButterButton.Content = "Search and repair...";
				scanBrokenButterButton.IsEnabled = true;
				repairButterButton.IsEnabled = true;
			}
		}

		#endregion

		#region TTS

		public static bool DiscordLoggedIn => DiscordOAuth.IsLoggedIn;

		public static Visibility DiscordNotLoggedInVisible =>
			DiscordOAuth.IsLoggedIn ? Visibility.Collapsed : Visibility.Visible;

		public static bool JoustTime
		{
			get => SparkSettings.instance.joustTimeTTS;
			set
			{
				SparkSettings.instance.joustTimeTTS = value;

				if (value) Program.synth.SpeakAsync($"{Team.TeamColor.orange.ToLocalizedString()} 1.8");
				Console.WriteLine($"{Team.TeamColor.orange.ToLocalizedString()} 1.8");
			}
		}

		public static bool JoustSpeed
		{
			get => SparkSettings.instance.joustSpeedTTS;
			set
			{
				SparkSettings.instance.joustSpeedTTS = value;

				if (value)
					Program.synth.SpeakAsync(
						$"{Team.TeamColor.orange.ToLocalizedString()} 32 {Properties.Resources.tts_meters_per_second}");
			}
		}

		public static bool ServerLocation
		{
			get => SparkSettings.instance.serverLocationTTS;
			set
			{
				SparkSettings.instance.serverLocationTTS = value;

				if (value) Program.synth.SpeakAsync("Chicago, Illinois");
			}
		}

		public static bool MaxBoostSpeed
		{
			get => SparkSettings.instance.maxBoostSpeedTTS;
			set
			{
				SparkSettings.instance.maxBoostSpeedTTS = value;

				if (value) Program.synth.SpeakAsync($"32 {Properties.Resources.tts_meters_per_second}");
			}
		}

		public static bool TubeExitSpeed
		{
			get => SparkSettings.instance.tubeExitSpeedTTS;
			set
			{
				SparkSettings.instance.tubeExitSpeedTTS = value;

				if (value) Program.synth.SpeakAsync($"32 {Properties.Resources.tts_meters_per_second}");
			}
		}

		/// <summary>Speech speed as a multiple of normal; the list's values come from TTSController.SpeedOptions.</summary>
		public static double SpeechSpeed
		{
			get => TTSController.NearestSpeed(SparkSettings.instance.ttsSpeedMultiplier);
			set
			{
				bool changed = Math.Abs(value - SparkSettings.instance.ttsSpeedMultiplier) > 0.001;
				Program.synth.SetRate(value);

				if (changed)
					Program.synth.SpeakAsync(Properties.Resources.This_is_the_new_speed);
			}
		}

		public static bool GamePaused
		{
			get => SparkSettings.instance.pausedTTS;
			set
			{
				SparkSettings.instance.pausedTTS = value;

				if (value)
					Program.synth.SpeakAsync(
						$"{Team.TeamColor.orange.ToLocalizedString()} {Properties.Resources.tts_paused}");
			}
		}

		public static bool PlayerJoin
		{
			get => SparkSettings.instance.playerJoinTTS;
			set
			{
				SparkSettings.instance.playerJoinTTS = value;

				if (value)
					Program.synth.SpeakAsync(
						$"NtsFranz {Properties.Resources.tts_join_1} {Team.TeamColor.orange.ToLocalizedString()} {Properties.Resources.tts_join_2}");
			}
		}

		public static bool PlayerLeave
		{
			get => SparkSettings.instance.playerLeaveTTS;
			set
			{
				SparkSettings.instance.playerLeaveTTS = value;

				if (value)
					Program.synth.SpeakAsync(
						$"NtsFranz {Properties.Resources.tts_leave_1} {Team.TeamColor.orange.ToLocalizedString()} {Properties.Resources.tts_leave_2}");
			}
		}

		public static bool PlayerSwitch
		{
			get => SparkSettings.instance.playerSwitchTeamTTS;
			set
			{
				SparkSettings.instance.playerSwitchTeamTTS = value;

				if (value)
					Program.synth.SpeakAsync(
						$"NtsFranz {Properties.Resources.tts_switch_1} {Team.TeamColor.blue.ToLocalizedString()} {Properties.Resources.tts_switch_2} {Team.TeamColor.orange.ToLocalizedString()} {Properties.Resources.tts_switch_3}");
			}
		}

		public static bool ThrowSpeed
		{
			get => SparkSettings.instance.throwSpeedTTS;
			set
			{
				SparkSettings.instance.throwSpeedTTS = value;

				if (value) Program.synth.SpeakAsync("19");
			}
		}

		public static bool GoalSpeed
		{
			get => SparkSettings.instance.goalSpeedTTS;
			set
			{
				SparkSettings.instance.goalSpeedTTS = value;

				if (value) Program.synth.SpeakAsync($"19 {Properties.Resources.tts_meters_per_second}");
			}
		}

		public static bool GoalDistance
		{
			get => SparkSettings.instance.goalDistanceTTS;
			set
			{
				SparkSettings.instance.goalDistanceTTS = value;

				if (value) Program.synth.SpeakAsync($"23 {Properties.Resources.tts_meters}");
			}
		}


		public static bool RulesChanged
		{
			get => SparkSettings.instance.rulesChangedTTS;
			set
			{
				SparkSettings.instance.rulesChangedTTS = value;

				if (value)
				{
					Program.synth.SpeakAsync($"NtsFranz changed the rules");
				}
			}
		}

		public static bool PingSpike
		{
			get => SparkSettings.instance.pingSpikeTTS;
			set
			{
				SparkSettings.instance.pingSpikeTTS = value;

				if (value)
				{
					Program.synth.SpeakAsync($"NtsFranz's ping spiked to 150");
				}
			}
		}

		public static bool PingSpikePrivateOnly
		{
			get => SparkSettings.instance.pingSpikeTTSPrivateOnly;
			set => SparkSettings.instance.pingSpikeTTSPrivateOnly = value;
		}

		public static bool TTSSpecificNumbers
		{
			get => SparkSettings.instance.ttsSpecific;
			set
			{
				SparkSettings.instance.ttsSpecific = value;

				if (value)
				{
					Program.synth.SpeakAsync("18.67");
				}
			}
		}

		#endregion


		#region EchoVR Settings

		public Visibility EchoVRSettingsProgramOpenWarning =>
			Program.GetEchoVRProcess()?.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

		public Visibility EchoVRInstalled =>
			File.Exists(SparkSettings.instance.echoVRPath) ? Visibility.Visible : Visibility.Collapsed;

		public Visibility EchoVRNotInstalled => !File.Exists(SparkSettings.instance.echoVRPath)
			? Visibility.Visible
			: Visibility.Collapsed;

		public bool Fullscreen
		{
			get => EchoVRSettingsManager.Fullscreen;
			set => EchoVRSettingsManager.Fullscreen = value;
		}

		public bool MultiResShading
		{
			get => EchoVRSettingsManager.MultiResShading;
			set => EchoVRSettingsManager.MultiResShading = value;
		}

		public bool AutoRes
		{
			get => EchoVRSettingsManager.AutoRes;
			set => EchoVRSettingsManager.AutoRes = value;
		}

		public bool TemporalAA
		{
			get => EchoVRSettingsManager.TemporalAA;
			set => EchoVRSettingsManager.TemporalAA = value;
		}

		public bool Volumetrics
		{
			get => EchoVRSettingsManager.Volumetrics;
			set => EchoVRSettingsManager.Volumetrics = value;
		}

		public bool Bloom
		{
			get => EchoVRSettingsManager.Bloom;
			set => EchoVRSettingsManager.Bloom = value;
		}

		public string Monitor
		{
			get => EchoVRSettingsManager.Monitor.ToString();
			set => EchoVRSettingsManager.Monitor = int.Parse(value);
		}

		public string Resolution
		{
			get => EchoVRSettingsManager.Resolution.ToString();
			set => EchoVRSettingsManager.Resolution = float.Parse(value);
		}

		public string FoV
		{
			get => EchoVRSettingsManager.FoV.ToString();
			set => EchoVRSettingsManager.FoV = float.Parse(value);
		}

		public string Sharpening
		{
			get => EchoVRSettingsManager.Sharpening.ToString();
			set => EchoVRSettingsManager.Sharpening = float.Parse(value);
		}

		public int AA
		{
			get => EchoVRSettingsManager.AA;
			set => EchoVRSettingsManager.AA = value;
		}

		public int ShadowQuality
		{
			get => EchoVRSettingsManager.ShadowQuality;
			set => EchoVRSettingsManager.ShadowQuality = value;
		}

		public int MeshQuality
		{
			get => EchoVRSettingsManager.MeshQuality;
			set => EchoVRSettingsManager.MeshQuality = value;
		}

		public int FXQuality
		{
			get => EchoVRSettingsManager.FXQuality;
			set => EchoVRSettingsManager.FXQuality = value;
		}

		public int TextureQuality
		{
			get => EchoVRSettingsManager.TextureQuality;
			set => EchoVRSettingsManager.TextureQuality = value;
		}

		public int LightingQuality
		{
			get => EchoVRSettingsManager.LightingQuality;
			set => EchoVRSettingsManager.LightingQuality = value;
		}


		private void RefreshAllSettings(object sender, SelectionChangedEventArgs e)
		{
			if (initialized)
			{
				DataContext = null;
				DataContext = this;
			}
		}

		#endregion


		public static string AppVersionLabelText => $"v{Program.AppVersionString()}";


		private void CameraModeDropdownChanged(object sender, SelectionChangedEventArgs e)
		{
			if (!initialized) return;

			// setting already handled in binding
			ComboBox box = (ComboBox)sender;
			CameraModeDropdownChanged(box.SelectedIndex);
		}

		private void CameraModeDropdownChanged(int index)
		{
			switch (index)
			{
				case 0:
				case 1:
					followSpecificPlayerPanel.Visibility = Visibility.Collapsed;
					followCameraModeLabel.Visibility = Visibility.Collapsed;
					followCameraModeDropdown.Visibility = Visibility.Collapsed;
					discholderFollowTeamCheckbox.Visibility = Visibility.Collapsed;
					discholderFollowCameraModeLabel.Visibility = Visibility.Collapsed;
					discholderFollowCameraModeDropdown.Visibility = Visibility.Collapsed;
					goProCameraPanel.Visibility = Visibility.Collapsed;
					break;
				case 2:
					followSpecificPlayerPanel.Visibility = Visibility.Collapsed;
					followCameraModeLabel.Visibility = Visibility.Visible;
					followCameraModeDropdown.Visibility = Visibility.Visible;
					discholderFollowTeamCheckbox.Visibility = Visibility.Collapsed;
					discholderFollowCameraModeLabel.Visibility = Visibility.Collapsed;
					discholderFollowCameraModeDropdown.Visibility = Visibility.Collapsed;
					goProCameraPanel.Visibility = Visibility.Collapsed;
					break;
				case 3:
					followSpecificPlayerPanel.Visibility = Visibility.Visible;
					followCameraModeLabel.Visibility = Visibility.Visible;
					followCameraModeDropdown.Visibility = Visibility.Visible;
					discholderFollowTeamCheckbox.Visibility = Visibility.Collapsed;
					discholderFollowCameraModeLabel.Visibility = Visibility.Collapsed;
					discholderFollowCameraModeDropdown.Visibility = Visibility.Collapsed;
					goProCameraPanel.Visibility = Visibility.Collapsed;
					break;
				case 4:
					followSpecificPlayerPanel.Visibility = Visibility.Collapsed;
					followCameraModeLabel.Visibility = Visibility.Collapsed;
					followCameraModeDropdown.Visibility = Visibility.Collapsed;
					discholderFollowTeamCheckbox.Visibility = Visibility.Visible;
					discholderFollowCameraModeLabel.Visibility = Visibility.Visible;
					discholderFollowCameraModeDropdown.Visibility = Visibility.Visible;
					goProCameraPanel.Visibility = Visibility.Collapsed;
					break;
				case 5:
					followSpecificPlayerPanel.Visibility = Visibility.Collapsed;
					followCameraModeLabel.Visibility = Visibility.Collapsed;
					followCameraModeDropdown.Visibility = Visibility.Collapsed;
					discholderFollowTeamCheckbox.Visibility = Visibility.Collapsed;
					discholderFollowCameraModeLabel.Visibility = Visibility.Collapsed;
					discholderFollowCameraModeDropdown.Visibility = Visibility.Collapsed;
					goProCameraPanel.Visibility = Visibility.Visible;
					break;
			}
		}

		private void SpectatorCameraFindNow(object sender, RoutedEventArgs e)
		{
			CameraWriteController.UseCameraControlKeys();
		}

		private void HideEchoVRUINow(object sender, RoutedEventArgs e)
		{
			if (!Program.InGame) return;
			CameraWriteController.SetUIVisibility(HideUICheckbox.IsChecked != true);
		}

		private void HideMinimapNow(object sender, RoutedEventArgs e)
		{
			if (!Program.InGame) return;
			CameraWriteController.SetMinimapVisibility(HideMinimapCheckbox.IsChecked != true);
		}

		private void ToggleNameplatesNow(object sender, RoutedEventArgs e)
		{
			if (!Program.InGame) return;
			CameraWriteController.SetNameplatesVisibility(HideNameplatesCheckbox.IsChecked != true);
		}



		private void ResetAllSettings(object sender, RoutedEventArgs e)
		{
			EchoVRSettingsManager.ResetAllSettingsToDefault();

			RefreshAllSettings(sender, null);
		}

		private void MutePlayerCommsDropdownChanged(object sender, SelectionChangedEventArgs e)
		{
			if (!initialized) return;

			int index = ((ComboBox)sender).SelectedIndex;
			switch (index)
			{
				case 0: // leave default
					SparkSettings.instance.mutePlayerComms = false;
					SparkSettings.instance.muteEnemyTeam = false;
					CameraWriteController.SetTeamsMuted(false, false);
					break;
				case 1: // mute enemy team
					SparkSettings.instance.mutePlayerComms = false;
					SparkSettings.instance.muteEnemyTeam = true;
					break;
				case 2: // mute both teams
					SparkSettings.instance.mutePlayerComms = true;
					SparkSettings.instance.muteEnemyTeam = false;
					break;
			}

			CameraWriteController.SetPlayersMuted();
		}


		private void ClearTTSCacheButton(object sender, RoutedEventArgs e)
		{
			TTSController.ClearCacheFolder();
		}

		private void ChangeTTSCacheFolderButton_Click(object sender, RoutedEventArgs e)
		{
			if (!initialized) return;
			string selectedPath = "";
			OpenFolderDialog folderBrowserDialog = new OpenFolderDialog
			{
				InitialDirectory = TTSController.CacheFolder
			};
			if (folderBrowserDialog.ShowDialog() == true)
			{
				selectedPath = folderBrowserDialog.FolderName;
			}

			if (selectedPath != "")
			{
				SparkSettings.instance.ttsCacheFolder = selectedPath;
				RefreshAllSettings(sender, null);
			}
		}

		private void OpenTTSCacheButton(object sender, RoutedEventArgs e)
		{
			string folder = TTSController.CacheFolder;
			if (!Directory.Exists(Path.GetDirectoryName(folder)))
			{
				Directory.CreateDirectory(folder);
			}

			if (Directory.Exists(folder))
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = folder,
					UseShellExecute = true
				});
			}
		}

		private void OpenSettingsFileFolder(object sender, RoutedEventArgs e)
		{
			string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IgniteVR", "Spark");
			if (!Directory.Exists(Path.GetDirectoryName(folder)))
			{
				Directory.CreateDirectory(folder);
			}

			if (Directory.Exists(folder))
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = folder,
					UseShellExecute = true
				});
			}
		}

		private void InstallReshade(object sender, RoutedEventArgs e)
		{
			if (string.IsNullOrEmpty(SparkSettings.instance.echoVRPath)) return;
			if (!File.Exists(SparkSettings.instance.echoVRPath)) return;

			ReshadeProgress.Visibility = Visibility.Visible;
			ReshadeProgress.Value = 0;

			// delete the old temp file
			if (File.Exists(Path.Combine(Path.GetTempPath(), "reshade.zip")))
			{
				File.Delete(Path.Combine(Path.GetTempPath(), "reshade.zip"));
			}

			// download reshade
			try
			{
				WebClient webClient = new WebClient();
				webClient.DownloadFileCompleted += ReshadeDownloadCompleted;
				webClient.DownloadProgressChanged += ReshadeDownloadProgressChanged;
				webClient.DownloadFileAsync(new Uri("https://github.com/NtsFranz/Spark/raw/main/resources/reshade.zip"), Path.Combine(Path.GetTempPath(), "reshade.zip"));
			}
			catch (Exception)
			{
				new MessageBox("Something broke while trying to download update", Properties.Resources.Error).Show();
			}
		}

		private void ReshadeDownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
		{
			ReshadeProgress.Visibility = Visibility.Visible;
			ReshadeProgress.Value = e.ProgressPercentage;
		}

		private void ReshadeDownloadCompleted(object sender, AsyncCompletedEventArgs e)
		{
			try
			{
				// install reshade from the zip
				string dir = Path.GetDirectoryName(SparkSettings.instance.echoVRPath);
				if (dir != null)
				{
					ZipFile.ExtractToDirectory(Path.Combine(Path.GetTempPath(), "reshade.zip"), dir, true);
				}
			}
			catch (Exception)
			{
				new MessageBox("Something broke while trying to install Reshade. Report this to NtsFranz", Properties.Resources.Error).Show();
			}

			ReshadeProgress.Visibility = Visibility.Collapsed;
		}

		private void RemoveReshade(object sender, RoutedEventArgs e)
		{
			if (string.IsNullOrEmpty(SparkSettings.instance.echoVRPath)) return;
			if (!File.Exists(SparkSettings.instance.echoVRPath)) return;
			string dir = Path.GetDirectoryName(SparkSettings.instance.echoVRPath);
			if (dir == null) return;

			try
			{
				File.Delete(Path.Combine(dir, "DefaultPreset.ini"));
				File.Delete(Path.Combine(dir, "dxgi.dll"));
				File.Delete(Path.Combine(dir, "dxgi.log"));
				File.Delete(Path.Combine(dir, "ReShade.ini"));
				File.Delete(Path.Combine(dir, "Reshade.log"));
				File.Delete(Path.Combine(dir, "ReshadePreset.ini"));
				if (Directory.Exists(Path.Combine(dir, "reshade-shaders")))
				{
					Directory.Delete(Path.Combine(dir, "reshade-shaders"), true);
				}
			}
			catch (UnauthorizedAccessException)
			{
				new MessageBox("Can't uninstall Reshade. Try closing EchoVR and trying again.", Properties.Resources.Error).Show();
			}
		}		
		

		private void SaveQuestIPClicked(object sender, RoutedEventArgs e)
		{
			SaveQuestIPButton.Visibility = Visibility.Collapsed;
		}

		private void PlayTestSound(object sender, RoutedEventArgs e)
		{
			Program.synth.SpeakAsync("THIS IS A TEST SOUND!");
		}

		#region Change Theme

		// Tracks whether we're currently programmatically updating sliders/hex boxes
		// (to avoid feedback loops between TextChanged and Slider.ValueChanged)
		private bool _themeUpdating;

		// The 3 working colours
		private Color _themeDark;
		private Color _themeMid;
		private Color _themeLight;

		private void ChangeThemeTab_Loaded(object sender, RoutedEventArgs e)
		{
			LoadThemeFromSettings();
		}

		private void LoadThemeFromSettings()
		{
			_themeDark  = ThemesController.ParseHex(SparkSettings.instance?.customThemeDark  ?? "#151515");
			_themeMid   = ThemesController.ParseHex(SparkSettings.instance?.customThemeMid   ?? "#363636");
			_themeLight = ThemesController.ParseHex(SparkSettings.instance?.customThemeLight ?? "#3E3E3E");
			SyncUIFromColors();
		}

		private void SyncUIFromColors()
		{
			// Crash Fix: Check if UI controls are ready before syncing.
			// This happens if the settings window is loaded but the theme tab hasn't been visited yet.
			if (DarkPreviewRect == null || DarkHexBox == null || DarkHueSlider == null) return;

			_themeUpdating = true;
			SyncColorToUI(DarkPreviewRect,  DarkHexBox,  DarkHueSlider,  DarkSatSlider,  DarkValSlider,  _themeDark);
			SyncColorToUI(MidPreviewRect,   MidHexBox,   MidHueSlider,   MidSatSlider,   MidValSlider,   _themeMid);
			SyncColorToUI(LightPreviewRect, LightHexBox, LightHueSlider, LightSatSlider, LightValSlider, _themeLight);
			_themeUpdating = false;
		}

		private static void SyncColorToUI(System.Windows.Shapes.Rectangle preview,
			                               TextBox hexBox,
			                               Slider hue, Slider sat, Slider val,
			                               Color color)
		{
			if (preview == null || hexBox == null || hue == null || sat == null || val == null) return;
			preview.Fill = new SolidColorBrush(color);
			hexBox.Text = ThemesController.ColorToHex(color);
			ColorToHsv(color, out double h, out double s, out double v);
			hue.Value = h;
			sat.Value = s;
			val.Value = v;
		}

		// ─── Dark sliders ───────────────────────────────────────────────────────
		private void DarkSlider_Changed(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
		{
			if (_themeUpdating || DarkHueSlider == null) return;
			_themeDark = HsvToColor(DarkHueSlider.Value, DarkSatSlider.Value, DarkValSlider.Value);
			_themeUpdating = true;
			DarkPreviewRect.Fill = new SolidColorBrush(_themeDark);
			DarkHexBox.Text = ThemesController.ColorToHex(_themeDark);
			_themeUpdating = false;
			_themeDebounceTimer.Stop();
			_themeDebounceTimer.Start();
		}

		private void DarkHexBox_TextChanged(object sender, TextChangedEventArgs e)
		{
			if (_themeUpdating || DarkHexBox == null) return;
			if (!TryParseHexBox(DarkHexBox.Text, out Color c)) return;
			_themeDark = c;
			_themeUpdating = true;
			DarkPreviewRect.Fill = new SolidColorBrush(c);
			ColorToHsv(c, out double h, out double s, out double v);
			DarkHueSlider.Value = h; DarkSatSlider.Value = s; DarkValSlider.Value = v;
			_themeUpdating = false;
			ThemesController.ApplyCustomTheme(_themeDark, _themeMid, _themeLight);
		}

		// ─── Mid sliders ────────────────────────────────────────────────────────
		private void MidSlider_Changed(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
		{
			if (_themeUpdating || MidHueSlider == null) return;
			_themeMid = HsvToColor(MidHueSlider.Value, MidSatSlider.Value, MidValSlider.Value);
			_themeUpdating = true;
			MidPreviewRect.Fill = new SolidColorBrush(_themeMid);
			MidHexBox.Text = ThemesController.ColorToHex(_themeMid);
			_themeUpdating = false;
			_themeDebounceTimer.Stop();
			_themeDebounceTimer.Start();
		}

		private void MidHexBox_TextChanged(object sender, TextChangedEventArgs e)
		{
			if (_themeUpdating || MidHexBox == null) return;
			if (!TryParseHexBox(MidHexBox.Text, out Color c)) return;
			_themeMid = c;
			_themeUpdating = true;
			MidPreviewRect.Fill = new SolidColorBrush(c);
			ColorToHsv(c, out double h, out double s, out double v);
			MidHueSlider.Value = h; MidSatSlider.Value = s; MidValSlider.Value = v;
			_themeUpdating = false;
			ThemesController.ApplyCustomTheme(_themeDark, _themeMid, _themeLight);
		}

		// ─── Light sliders ──────────────────────────────────────────────────────
		private void LightSlider_Changed(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
		{
			if (_themeUpdating || LightHueSlider == null) return;
			_themeLight = HsvToColor(LightHueSlider.Value, LightSatSlider.Value, LightValSlider.Value);
			_themeUpdating = true;
			LightPreviewRect.Fill = new SolidColorBrush(_themeLight);
			LightHexBox.Text = ThemesController.ColorToHex(_themeLight);
			_themeUpdating = false;
			_themeDebounceTimer.Stop();
			_themeDebounceTimer.Start();
		}

		private void LightHexBox_TextChanged(object sender, TextChangedEventArgs e)
		{
			if (_themeUpdating || LightHexBox == null) return;
			if (!TryParseHexBox(LightHexBox.Text, out Color c)) return;
			_themeLight = c;
			_themeUpdating = true;
			LightPreviewRect.Fill = new SolidColorBrush(c);
			ColorToHsv(c, out double h, out double s, out double v);
			LightHueSlider.Value = h; LightSatSlider.Value = s; LightValSlider.Value = v;
			_themeUpdating = false;
			ThemesController.ApplyCustomTheme(_themeDark, _themeMid, _themeLight);
		}

		// ─── Presets ────────────────────────────────────────────────────────────
		private void ApplyPreset(string dark, string mid, string light)
		{
			_themeDark  = ThemesController.ParseHex(dark);
			_themeMid   = ThemesController.ParseHex(mid);
			_themeLight = ThemesController.ParseHex(light);
			SyncUIFromColors();
			ThemesController.ApplyCustomTheme(_themeDark, _themeMid, _themeLight);
		}

		private void PresetBlack_Click     (object s, RoutedEventArgs e) => ApplyPreset("#000000", "#141414", "#363636");
		private void PresetBlue_Click      (object s, RoutedEventArgs e) => ApplyPreset("#4040ff", "#5b5bff", "#9d9dff");
		private void PresetCoolPurple_Click(object s, RoutedEventArgs e) => ApplyPreset("#c76fdd", "#ce82e1", "#e3b9ee");
		private void PresetDarkRed_Click   (object s, RoutedEventArgs e) => ApplyPreset("#4a0002", "#820003", "#ff0d13");
		private void PresetGreen_Click     (object s, RoutedEventArgs e) => ApplyPreset("#006400", "#008000", "#00e800");
		private void PresetHotPink_Click   (object s, RoutedEventArgs e) => ApplyPreset("#fe63d8", "#fe81df", "#ffbfef");
		private void PresetPurple_Click    (object s, RoutedEventArgs e) => ApplyPreset("#690f96", "#8a14c2", "#d213bb");
		private void PresetRed_Click       (object s, RoutedEventArgs e) => ApplyPreset("#b70004", "#df0005", "#ff7174");
		private void PresetYellow_Click    (object s, RoutedEventArgs e) => ApplyPreset("#fab011", "#fcc520", "#fcde64");
		private void PresetPink_Click      (object s, RoutedEventArgs e) => ApplyPreset("#c32b61", "#ea6192", "#ffaac9");
		private void PresetPVU_Click      (object s, RoutedEventArgs e) => ApplyPreset("#022432", "#011720", "#ADD8E6");

		// ─── Apply / Reset ──────────────────────────────────────────────────────
		private void ApplyTheme_Click(object sender, RoutedEventArgs e)
		{
			ThemesController.SaveAndApply(_themeDark, _themeMid, _themeLight);
		}

		private void ResetTheme_Click(object sender, RoutedEventArgs e)
		{
			// Reset to Spark Stealth Gray (Default)
			ApplyPreset("#151515", "#363636", "#3E3E3E");
			ThemesController.SaveAndApply(_themeDark, _themeMid, _themeLight);
		}

		// ─── Helpers ────────────────────────────────────────────────────────────
		private static bool TryParseHexBox(string text, out Color color)
		{
			color = default;
			string t = text?.TrimStart('#') ?? "";
			if (t.Length != 6) return false;
			try { color = ThemesController.ParseHex("#" + t); return true; }
			catch { return false; }
		}

		private static Color HsvToColor(double hue, double sat, double val)
		{
			hue = hue % 360.0;
			if (hue < 0) hue += 360.0;
			if (sat < 0) sat = 0; if (sat > 1) sat = 1;
			if (val < 0) val = 0; if (val > 1) val = 1;

			if (sat == 0)
			{
				byte c = (byte)(val * 255);
				return Color.FromRgb(c, c, c);
			}

			double h = hue / 60.0;
			int i = (int)Math.Floor(h);
			double f = h - i;
			double p = val * (1 - sat);
			double q = val * (1 - sat * f);
			double t2 = val * (1 - sat * (1 - f));

			double r, g, b;
			switch (i % 6)
			{
				case 0: r = val; g = t2;  b = p;   break;
				case 1: r = q;   g = val; b = p;   break;
				case 2: r = p;   g = val; b = t2;  break;
				case 3: r = p;   g = q;   b = val; break;
				case 4: r = t2;  g = p;   b = val; break;
				default: r = val; g = p;  b = q;   break;
			}

			return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
		}

		private static void ColorToHsv(Color color, out double hue, out double sat, out double val)
		{
			double r = color.R / 255.0;
			double g = color.G / 255.0;
			double b = color.B / 255.0;
			double max = Math.Max(r, Math.Max(g, b));
			double min = Math.Min(r, Math.Min(g, b));
			double delta = max - min;

			val = max;
			sat = max == 0 ? 0 : delta / max;

			if (delta == 0) { hue = 0; return; }

			if (max == r)      hue = 60 * (((g - b) / delta) % 6);
			else if (max == g) hue = 60 * (((b - r) / delta) + 2);
			else               hue = 60 * (((r - g) / delta) + 4);

			if (hue < 0) hue += 360;
		}

		#endregion
	}

	
	public class SettingBindingExtension : Binding
	{
		public SettingBindingExtension()
		{
			Initialize();
		}

		public SettingBindingExtension(string path) : base(path)
		{
			Initialize();
		}

		private void Initialize()
		{
			Source = SparkSettings.instance;
			Mode = BindingMode.TwoWay;
		}
	}

	public class SettingLoadExtension : Binding
	{
		public SettingLoadExtension()
		{
			Initialize();
		}

		public SettingLoadExtension(string path) : base(path)
		{
			Initialize();
		}

		private void Initialize()
		{
			Source = SparkSettings.instance;
			Mode = BindingMode.OneWay;
		}
	}
}
