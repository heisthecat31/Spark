using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Spark
{
	public partial class UpdateSparkControl : UserControl
	{
		private string _latestVersion = "";
		private string _currentVersion = "";
		private string _releaseUrl = "";
		private string _tempFolder = "";
		private string _appFolder = "";
		
		public UpdateSparkControl()
		{
			InitializeComponent();
			Loaded += OnLoaded;
		}
		
		private void OnLoaded(object sender, RoutedEventArgs e)
		{
			_currentVersion = GetCurrentVersion();
			CurrentVersionText.Text = _currentVersion;
			
			_tempFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Spark", "Temp");
			_appFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
			
			// Create temp directory if it doesn't exist
			if (!Directory.Exists(_tempFolder))
			{
				Directory.CreateDirectory(_tempFolder);
			}
		}
		
		private string GetCurrentVersion()
		{
			var version = Assembly.GetExecutingAssembly().GetName().Version;
			return $"{version.Major}.{version.Minor}.{version.Build}";
		}
		
		private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
		{
			CheckUpdateButton.IsEnabled = false;
			StatusText.Text = "Checking for updates...";
			UpdateDetailsText.Text += $"[{DateTime.Now}] Checking for updates...\n";
			
			try
			{
				using (var client = new WebClient())
				{
					client.Headers.Add("User-Agent", "Spark-Updater");
					string latestReleaseUrl = "https://api.github.com/repos/heisthecat31/Spark/releases/latest";
					string json = await client.DownloadStringTaskAsync(latestReleaseUrl);
					
					var release = JObject.Parse(json);
					_latestVersion = release["tag_name"]?.ToString().TrimStart('v') ?? "Unknown";
					_releaseUrl = release["html_url"]?.ToString();
					
					LatestVersionText.Text = _latestVersion;
					StatusText.Text = $"Latest version: {_latestVersion}";
					DownloadUpdateButton.IsEnabled = true;

					string releaseNotes = release["body"]?.ToString();
					UpdateDetailsText.Text += $"[{DateTime.Now}] Latest version found: {_latestVersion}\n";
					if (!string.IsNullOrEmpty(releaseNotes))
					{
						UpdateDetailsText.Text += $"Release Notes:\n{releaseNotes}\n";
					}
				}
			}
			catch (Exception ex)
			{
				StatusText.Text = $"Error checking updates: {ex.Message}";
				UpdateDetailsText.Text += $"[{DateTime.Now}] Error: {ex.Message}\n";
				DownloadUpdateButton.IsEnabled = false;
			}
			finally
			{
				CheckUpdateButton.IsEnabled = true;
			}
		}
		
		private async void DownloadUpdateButton_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				DownloadUpdateButton.IsEnabled = false;
				CheckUpdateButton.IsEnabled = false;
				UpdateProgressBar.Visibility = Visibility.Visible;
				UpdateProgressBar.Value = 0;
				
				StatusText.Text = "Downloading update...";
				UpdateDetailsText.Text += $"[{DateTime.Now}] Starting download...\n";
				
				// Get download URL for Spark.zip
				string apiUrl = "https://api.github.com/repos/heisthecat31/Spark/releases/latest";
				using (var client = new WebClient())
				{
					client.Headers.Add("User-Agent", "Spark-Updater");
					string json = await client.DownloadStringTaskAsync(apiUrl);
					var release = JObject.Parse(json);
					
					// Find Spark.zip asset
					var assets = release["assets"] as JArray;
					string sparkZipUrl = "";
					
					if (assets != null)
					{
						foreach (var asset in assets)
						{
							string name = asset["name"]?.ToString();
							if (name != null && name.Equals("Spark.zip", StringComparison.OrdinalIgnoreCase))
							{
								sparkZipUrl = asset["browser_download_url"]?.ToString();
								break;
							}
						}
					}
					
					if (string.IsNullOrEmpty(sparkZipUrl))
					{
						throw new Exception("Spark.zip not found in release assets");
					}
					
					string tempFilePath = Path.Combine(_tempFolder, "Spark_Update.zip");
					
					// Download with progress
					client.DownloadProgressChanged += (s, args) =>
					{
						Dispatcher.Invoke(() =>
						{
							UpdateProgressBar.Value = args.ProgressPercentage;
							StatusText.Text = $"Downloading: {args.ProgressPercentage}%";
						});
					};
					
					await client.DownloadFileTaskAsync(new Uri(sparkZipUrl), tempFilePath);
					
					Dispatcher.Invoke(() =>
					{
						StatusText.Text = "Download complete. Extracting and installing...";
						UpdateDetailsText.Text += $"[{DateTime.Now}] Download complete. Extracting...\n";
					});
					
					// Extract and install
					await Task.Run(() => InstallUpdate(tempFilePath));
				}
			}
			catch (Exception ex)
			{
				Dispatcher.Invoke(() =>
				{
					StatusText.Text = $"Error: {ex.Message}";
					UpdateDetailsText.Text += $"[{DateTime.Now}] Error: {ex.Message}\n";
					ResetButtons();
				});
			}
		}
		
		private void InstallUpdate(string zipFilePath)
		{
			try
			{
				string extractPath = Path.Combine(_tempFolder, "Spark_Extracted");
				
				// Clean up old extraction if exists
				if (Directory.Exists(extractPath))
					Directory.Delete(extractPath, true);
				
				// Extract zip
				ZipFile.ExtractToDirectory(zipFilePath, extractPath);
				
				Dispatcher.Invoke(() =>
				{
					UpdateDetailsText.Text += $"[{DateTime.Now}] Extracted to: {extractPath}\n";
					StatusText.Text = "Finding actual files...";
				});
				
				string currentExe = Process.GetCurrentProcess().MainModule.FileName;
				string targetFolder = Path.GetDirectoryName(currentExe);
				
				Dispatcher.Invoke(() =>
				{
					UpdateDetailsText.Text += $"[{DateTime.Now}] === DEBUG INFO ===\n";
					UpdateDetailsText.Text += $"[{DateTime.Now}] Spark.exe location: {currentExe}\n";
					UpdateDetailsText.Text += $"[{DateTime.Now}] Target folder: {targetFolder}\n";
					UpdateDetailsText.Text += $"[{DateTime.Now}] Contents of extractPath:\n";
				});
				
				var dirs = Directory.GetDirectories(extractPath);
				foreach (var dir in dirs)
				{
					string dirName = Path.GetFileName(dir);
					Dispatcher.Invoke(() =>
					{
						UpdateDetailsText.Text += $"[{DateTime.Now}]   Directory: {dirName}\n";
					});
				}
				
				var files = Directory.GetFiles(extractPath);
				foreach (var file in files)
				{
					string fileName = Path.GetFileName(file);
					Dispatcher.Invoke(() =>
					{
						UpdateDetailsText.Text += $"[{DateTime.Now}]   File: {fileName}\n";
					});
				}
				
				string actualSourceFolder = extractPath;
				
				// Check if there's a nested net6.0-windows10.0.17763.0 folder
				string nestedNetFolder = Path.Combine(extractPath, "net6.0-windows10.0.17763.0");
				if (Directory.Exists(nestedNetFolder))
				{
					actualSourceFolder = nestedNetFolder;
					Dispatcher.Invoke(() =>
					{
						UpdateDetailsText.Text += $"[{DateTime.Now}] Found nested .NET folder: {actualSourceFolder}\n";
					});
				}
				else
				{
					foreach (var dir in dirs)
					{
						if (Directory.Exists(Path.Combine(dir, "net6.0-windows10.0.17763.0")))
						{
							actualSourceFolder = Path.Combine(dir, "net6.0-windows10.0.17763.0");
							Dispatcher.Invoke(() =>
							{
								UpdateDetailsText.Text += $"[{DateTime.Now}] Found deep nested .NET folder: {actualSourceFolder}\n";
							});
							break;
						}
					}
				}
				
				Dispatcher.Invoke(() =>
				{
					UpdateDetailsText.Text += $"[{DateTime.Now}] Using source folder: {actualSourceFolder}\n";
					UpdateDetailsText.Text += $"[{DateTime.Now}] === END DEBUG ===\n";
					StatusText.Text = "Creating update script...";
				});
				
				string batchFile = Path.Combine(_tempFolder, "update_spark.bat");
				
				string batchContent = $@"
@echo off
echo ========================================
echo         SPARK UPDATE - FIXED
echo ========================================
echo.
echo Current Spark folder: {targetFolder}
echo Source update files: {actualSourceFolder}
echo.
echo Step 1: Killing Spark...
taskkill /f /im Spark.exe >nul 2>&1
timeout /t 2 /nobreak >nul

echo Step 2: Copying ALL files...
echo FROM: {actualSourceFolder}
echo TO: {targetFolder}
echo.

REM Copy EVERYTHING from source to target
xcopy ""{actualSourceFolder}"" ""{targetFolder}"" /E /Y /I /H

echo Step 3: Cleaning up...
if exist ""{extractPath}"" rmdir /s /q ""{extractPath}""
if exist ""{zipFilePath}"" del ""{zipFilePath}"" >nul 2>&1

echo Step 4: Starting Spark...
cd /d ""{targetFolder}""
start """" Spark.exe

echo Step 5: Deleting this script...
timeout /t 1 /nobreak >nul
del ""{batchFile}"" >nul 2>&1

echo.
echo UPDATE COMPLETE!
timeout /t 2 /nobreak >nul
exit
";
				
				File.WriteAllText(batchFile, batchContent);
				
				Dispatcher.Invoke(() =>
				{
					UpdateDetailsText.Text += $"[{DateTime.Now}] Batch file created\n";
					StatusText.Text = "Starting update...";
				});
				
				ProcessStartInfo psi = new ProcessStartInfo
				{
					FileName = batchFile,
					WindowStyle = ProcessWindowStyle.Normal,
					UseShellExecute = true,
					WorkingDirectory = _tempFolder
				};
				
				Dispatcher.Invoke(() =>
				{
					UpdateDetailsText.Text += $"[{DateTime.Now}] Launching update...\n";
					
					Process.Start(psi);
					
					Task.Delay(500).ContinueWith(t =>
					{
						Dispatcher.Invoke(() =>
						{
							Process.GetCurrentProcess().Kill();
						});
					});
				});
			}
			catch (Exception ex)
			{
				Dispatcher.Invoke(() =>
				{
					StatusText.Text = $"Installation failed: {ex.Message}";
					UpdateDetailsText.Text += $"[{DateTime.Now}] Installation failed: {ex.Message}\n";
					UpdateDetailsText.Text += $"[{DateTime.Now}] Stack: {ex.StackTrace}\n";
					ResetButtons();
				});
			}
		}
		
		private async void DownloadTTSCacheButton_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				DownloadTTSCacheButton.IsEnabled = false;
				TTSCacheStatus.Text = "Downloading TTS Cache...";
				
				// Get download URL for SparkTTSCache.zip
				string apiUrl = "https://api.github.com/repos/heisthecat31/Spark/releases/latest";
				using (var client = new WebClient())
				{
					client.Headers.Add("User-Agent", "Spark-Updater");
					string json = await client.DownloadStringTaskAsync(apiUrl);
					var release = JObject.Parse(json);
					
					var assets = release["assets"] as JArray;
					string ttsCacheUrl = "";
					
					if (assets != null)
					{
						foreach (var asset in assets)
						{
							string name = asset["name"]?.ToString();
							if (name != null && name.Equals("SparkTTSCache.zip", StringComparison.OrdinalIgnoreCase))
							{
								ttsCacheUrl = asset["browser_download_url"]?.ToString();
								break;
							}
						}
					}
					
					if (string.IsNullOrEmpty(ttsCacheUrl))
					{
						throw new Exception("SparkTTSCache.zip not found in release assets");
					}
					
					// Target folder: AppData\Local\Temp\SparkTTSCache
					string ttsCacheFolder = Path.Combine(
						Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
						"Temp",
						"SparkTTSCache");
					
					if (Directory.Exists(ttsCacheFolder))
					{
						Directory.Delete(ttsCacheFolder, true);
					}
					
					Directory.CreateDirectory(ttsCacheFolder);
					
					string tempTtsZip = Path.Combine(ttsCacheFolder, "SparkTTSCache.zip");
					
					var progressDialog = new Window
					{
						Title = "Downloading TTS Cache",
						Width = 300,
						Height = 100,
						WindowStartupLocation = WindowStartupLocation.CenterOwner,
						Owner = Window.GetWindow(this)
					};
					
					var progressBar = new ProgressBar { Height = 20, Margin = new Thickness(10) };
					var progressText = new TextBlock { Margin = new Thickness(10, 0, 10, 10), TextAlignment = TextAlignment.Center };
					var stackPanel = new StackPanel();
					stackPanel.Children.Add(progressBar);
					stackPanel.Children.Add(progressText);
					progressDialog.Content = stackPanel;
					progressDialog.Show();
					
					client.DownloadProgressChanged += (s, args) =>
					{
						Dispatcher.Invoke(() =>
						{
							progressBar.Value = args.ProgressPercentage;
							progressText.Text = $"Downloading: {args.ProgressPercentage}%";
						});
					};
					
					await client.DownloadFileTaskAsync(new Uri(ttsCacheUrl), tempTtsZip);
					progressDialog.Close();
					
					string tempExtractFolder = Path.Combine(ttsCacheFolder, "_temp_extract");
					if (Directory.Exists(tempExtractFolder))
						Directory.Delete(tempExtractFolder, true);
					
					Directory.CreateDirectory(tempExtractFolder);
					
					ZipFile.ExtractToDirectory(tempTtsZip, tempExtractFolder);
					
					var extractedItems = Directory.GetFileSystemEntries(tempExtractFolder);
					
					if (extractedItems.Length == 1 && Directory.Exists(extractedItems[0]))
					{
						string nestedFolder = extractedItems[0];
						var nestedItems = Directory.GetFileSystemEntries(nestedFolder);
						
						foreach (string item in nestedItems)
						{
							string destPath = Path.Combine(ttsCacheFolder, Path.GetFileName(item));
							if (File.Exists(item))
							{
								File.Move(item, destPath);
							}
							else if (Directory.Exists(item))
							{
								Directory.Move(item, destPath);
							}
						}
						
						Directory.Delete(nestedFolder);
					}
					else
					{
						foreach (string item in extractedItems)
						{
							string destPath = Path.Combine(ttsCacheFolder, Path.GetFileName(item));
							if (File.Exists(item))
							{
								File.Move(item, destPath);
							}
							else if (Directory.Exists(item))
							{
								Directory.Move(item, destPath);
							}
						}
					}
					
					Directory.Delete(tempExtractFolder, true);
					File.Delete(tempTtsZip);
					
					TTSCacheStatus.Text = "TTS Cache downloaded successfully!";
					UpdateDetailsText.Text += $"[{DateTime.Now}] TTS Cache downloaded to: {ttsCacheFolder}\n";
					
					new MessageBox($"TTS Cache downloaded and extracted to:\n{ttsCacheFolder}", "Success").Show();
				}
			}
			catch (Exception ex)
			{
				TTSCacheStatus.Text = $"Error: {ex.Message}";
				UpdateDetailsText.Text += $"[{DateTime.Now}] TTS Cache error: {ex.Message}\n";
				new MessageBox($"Error downloading TTS Cache: {ex.Message}", "Error").Show();
			}
			finally
			{
				DownloadTTSCacheButton.IsEnabled = true;
			}
		}
		
		private void OpenTempFolderButton_Click(object sender, RoutedEventArgs e)
		{
			if (Directory.Exists(_tempFolder))
			{
				Process.Start("explorer.exe", _tempFolder);
			}
		}
		
		private void ResetButtons()
		{
			DownloadUpdateButton.IsEnabled = true;
			CheckUpdateButton.IsEnabled = true;
			UpdateProgressBar.Visibility = Visibility.Collapsed;
		}
		
		private void CopyDirectory(string sourceDir, string destDir)
		{
			// don't copy files here anymore - the batch file handles it
			// This method is kept for compatibility but does nothing
			UpdateDetailsText.Text += $"[{DateTime.Now}] Note: File copying will be handled by update script\n";
		}
	}
}