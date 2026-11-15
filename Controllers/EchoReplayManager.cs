using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Spark
{
	/// <summary>
	/// EchoReplay: the Echo VR plugin that records <c>.echodemo</c> files, and EchoReplayViewer, which plays
	/// them without the game. Both ship in one <c>.zip</c> on the EchoReplay repo's GitHub releases
	/// (tags <c>vX.Y.Z</c>).
	///
	/// How the plugin gets into the game:
	/// <list type="bullet">
	/// <item>If <c>bin\win10\plugins\EchoReplay.dll</c> already exists (EchoLoader / nevr users), the game
	/// loads it itself. Spark only keeps that file up to date and never injects.</item>
	/// <item>Otherwise Spark injects its own copy into <c>echovr.exe</c> (LoadLibraryW in a remote thread)
	/// as soon as the game starts, so no plugin loader is needed. The copy lives in
	/// <c>%LOCALAPPDATA%\IgniteVR\Spark\EchoReplay\plugins\</c>; the plugin keeps its log and
	/// <c>echoreplay\config.json</c> beside that <c>plugins</c> folder, so the game folder is not touched.</item>
	/// </list>
	/// Recordings go to <c>Documents\EchoReplay</c>, where EchoReplayViewer looks for them.
	/// </summary>
	public static class EchoReplayManager
	{
		public const string RepoOwner = "heisthecat31";
		public const string RepoName = "EchoReplay";
		public const string DllName = "EchoReplay.dll";
		public const string ViewerExeName = "EchoReplayViewer.exe";
		private const string PendingSuffix = ".new";

		public static string BaseDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IgniteVR", "Spark", "EchoReplay");
		public static string LocalDllPath => Path.Combine(BaseDir, "plugins", DllName);
		public static string ViewerDir => Path.Combine(BaseDir, "viewer");
		public static string ViewerExePath => Path.Combine(ViewerDir, ViewerExeName);
		private static string VersionFile => Path.Combine(BaseDir, "version.txt");
		private static string ViewerVersionFile => Path.Combine(ViewerDir, "spark_version.txt");
		/// <summary>
		/// Where the injected copy records: Spark's replay folder (Settings > Replays), like Spark's other formats.
		/// EchoReplay Viewer reads the same folder from Spark's settings.json. A copy that a plugin loader loads from
		/// the game folder keeps its own setting (Documents\EchoReplay by default).
		/// </summary>
		public static string RecordingsDir
		{
			get
			{
				string folder = SparkSettings.instance.saveFolder;
				return string.IsNullOrEmpty(folder) || folder == "none"
					? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Spark", "replays")
					: folder;
			}
		}

		private static string ReadVersion(string file)
		{
			try
			{
				return File.Exists(file) ? File.ReadAllText(file).Trim() : "";
			}
			catch
			{
				return "";
			}
		}

		/// <summary>The plugin's installed release tag ("" when it isn't downloaded).</summary>
		public static string InstalledVersion => ReadVersion(VersionFile);

		/// <summary>The viewer's installed release tag ("" when it isn't downloaded).</summary>
		public static string ViewerVersion => ReadVersion(ViewerVersionFile);

		/// <summary>The newest release tag seen on GitHub ("" until checked).</summary>
		public static string LatestVersion { get; private set; } = "";

		/// <summary>The plugin is downloaded and GitHub has a newer release (it updates itself in the background).</summary>
		public static bool UpdateAvailable => InstalledVersion.Length > 0 && IsNewer(LatestVersion, InstalledVersion);

		/// <summary>The viewer is downloaded and GitHub has a newer release; it updates when it's next opened from Spark.</summary>
		public static bool ViewerUpdateAvailable => ViewerVersion.Length > 0 && IsNewer(LatestVersion, ViewerVersion);

		/// <summary>Raised (on a worker thread) when LatestVersion or the installed version changes.</summary>
		public static event Action StatusChanged;

		private static VersionJson latestRelease;
		private static DateTime latestFetched = DateTime.MinValue;
		private static readonly SemaphoreSlim installLock = new SemaphoreSlim(1, 1);
		private static Timer watcher;
		private static Timer updateTimer;

		/// <summary>Processes already handled: injected, or skipped because the game loads its own copy.</summary>
		private static readonly ConcurrentDictionary<int, string> handled = new ConcurrentDictionary<int, string>();

		/// <summary>Starts the update check (now and every 6 hours) and the echovr.exe watcher.</summary>
		public static void Start()
		{
			updateTimer ??= new Timer(_ => _ = CheckForUpdateAsync(), null, TimeSpan.FromSeconds(5), TimeSpan.FromHours(6));
			watcher ??= new Timer(_ => WatchTick(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1));
		}

		#region Releases

		/// <summary>"v1.2.3" -> 1.2.3. Null when it isn't a version number.</summary>
		public static Version ParseTag(string tag)
		{
			if (string.IsNullOrWhiteSpace(tag)) return null;
			string t = tag.Trim().TrimStart('v', 'V');
			int dash = t.IndexOfAny(new[] { '-', '+' });
			if (dash >= 0) t = t[..dash];
			return Version.TryParse(t.Contains('.') ? t : t + ".0", out Version v) ? v : null;
		}

		/// <summary>True when <paramref name="remote"/> is a newer release than <paramref name="local"/>.</summary>
		public static bool IsNewer(string remote, string local)
		{
			if (string.IsNullOrEmpty(remote)) return false;
			if (string.IsNullOrEmpty(local)) return true;
			Version r = ParseTag(remote);
			Version l = ParseTag(local);
			if (r != null && l != null) return r > l;
			return !string.Equals(remote.Trim(), local.Trim(), StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>The latest release, cached for 10 minutes. Null when GitHub can't be reached or there is no release.</summary>
		public static async Task<VersionJson> GetLatestReleaseAsync(bool force = false)
		{
			if (!force && latestRelease != null && DateTime.UtcNow - latestFetched < TimeSpan.FromMinutes(10)) return latestRelease;
			try
			{
				using var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest");
				req.Headers.Add("User-Agent", "Spark/" + Program.AppVersionString());
				req.Headers.Add("Accept", "application/vnd.github+json");
				using var resp = await FetchUtils.client.SendAsync(req);
				if (!resp.IsSuccessStatusCode)
				{
					Logger.LogRow(Logger.LogType.Error, $"[EchoReplay] no release found on {RepoOwner}/{RepoName} ({(int)resp.StatusCode})");
					return null;
				}

				VersionJson release = JsonConvert.DeserializeObject<VersionJson>(await resp.Content.ReadAsStringAsync());
				if (release?.tag_name == null) return null;
				latestRelease = release;
				latestFetched = DateTime.UtcNow;
				if (LatestVersion != release.tag_name)
				{
					LatestVersion = release.tag_name;
					StatusChanged?.Invoke();
				}

				return release;
			}
			catch (Exception e)
			{
				Logger.LogRow(Logger.LogType.Error, $"[EchoReplay] release check failed: {e.Message}");
				return null;
			}
		}

		public static async Task CheckForUpdateAsync()
		{
			await GetLatestReleaseAsync(true);
			ApplyPending();
			// keep game copies (plugins\EchoReplay.dll) on the same release as ours
			if (UpdateAvailable && SparkSettings.instance.saveEchodemoFiles && SparkSettings.instance.enableFullLogging)
			{
				await InstallOrUpdateAsync(false);
			}
		}

		/// <summary>
		/// Downloads the latest release and installs the plugin (and the viewer, with <paramref name="needViewer"/>)
		/// when it's newer than what's installed, or nothing is. Also updates every plugins\EchoReplay.dll in the known game folders.
		/// A file in use (the game or viewer is running) is staged as <c>.new</c> and swapped in later.
		/// Returns an error message, or null when it worked.
		/// </summary>
		public static async Task<string> InstallOrUpdateAsync(bool needViewer)
		{
			await installLock.WaitAsync();
			try
			{
				VersionJson release = await GetLatestReleaseAsync();
				bool installed = File.Exists(LocalDllPath) || File.Exists(LocalDllPath + PendingSuffix);
				bool viewerInstalled = File.Exists(ViewerExePath);
				if (release == null)
				{
					return installed && (!needViewer || viewerInstalled) ? null : $"Couldn't find an EchoReplay release on GitHub ({RepoOwner}/{RepoName}).";
				}

				bool pluginNeeded = !installed || IsNewer(release.tag_name, InstalledVersion);
				bool viewerNeeded = needViewer && (!viewerInstalled || IsNewer(release.tag_name, ViewerVersion));
				if (!pluginNeeded && !viewerNeeded) return null;

				Asset zip = release.assets?.FirstOrDefault(a => (a.name ?? a.browser_download_url ?? "").EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
				if (zip == null) return $"EchoReplay {release.tag_name} has no .zip download.";

				string temp = Path.Combine(Path.GetTempPath(), "spark_echoreplay_" + Guid.NewGuid().ToString("N"));
				Directory.CreateDirectory(temp);
				try
				{
					string zipPath = Path.Combine(temp, "release.zip");
					using (var resp = await FetchUtils.client.GetAsync(zip.browser_download_url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead))
					{
						resp.EnsureSuccessStatusCode();
						await using FileStream fs = new FileStream(zipPath, FileMode.Create);
						await resp.Content.CopyToAsync(fs);
					}

					string extracted = Path.Combine(temp, "x");
					await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, extracted));

					string dll = Directory.EnumerateFiles(extracted, DllName, SearchOption.AllDirectories).FirstOrDefault();
					string viewerExe = Directory.EnumerateFiles(extracted, ViewerExeName, SearchOption.AllDirectories).FirstOrDefault();
					// the plugin: Spark's own copy, and any game folder that loads it with a plugin loader
					if (pluginNeeded)
					{
						if (dll == null) return $"EchoReplay {release.tag_name}'s zip has no {DllName}.";
						Directory.CreateDirectory(Path.GetDirectoryName(LocalDllPath)!);
						ReplaceOrStage(dll, LocalDllPath);
						foreach (string gameCopy in GamePluginCopies())
						{
							ReplaceOrStage(dll, gameCopy);
						}

						await File.WriteAllTextAsync(VersionFile, release.tag_name);
						Logger.LogRow(Logger.LogType.Info, $"[EchoReplay] plugin {release.tag_name} installed");
					}

					// the viewer: the folder EchoReplayViewer.exe sits in. Only when it's being opened, so a
					// newer release shows as an update on the button until then.
					string viewerError = null;
					if (!viewerNeeded)
					{
						// nothing to do
					}
					else if (viewerExe != null)
					{
						if (Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ViewerExeName)).Length > 0)
						{
							viewerError = "Close EchoReplay Viewer so it can be updated.";
						}
						else
						{
							await Task.Run(() =>
							{
								if (Directory.Exists(ViewerDir)) Directory.Delete(ViewerDir, true);
								CopyDirectory(Path.GetDirectoryName(viewerExe)!, ViewerDir);
								File.WriteAllText(ViewerVersionFile, release.tag_name);
							});
							Logger.LogRow(Logger.LogType.Info, $"[EchoReplay] viewer {release.tag_name} installed");
						}
					}
					else
					{
						viewerError = $"EchoReplay {release.tag_name}'s zip has no {ViewerExeName}.";
					}

					StatusChanged?.Invoke();
					return viewerError;
				}
				finally
				{
					try
					{
						Directory.Delete(temp, true);
					}
					catch
					{
						// leftovers in %TEMP% are harmless
					}
				}
			}
			catch (Exception e)
			{
				Logger.LogRow(Logger.LogType.Error, $"[EchoReplay] install failed\n{e}");
				return "Couldn't install EchoReplay: " + e.Message;
			}
			finally
			{
				installLock.Release();
			}
		}

		/// <summary>Copies over <paramref name="dest"/>, or next to it as .new when it's loaded in a running game.</summary>
		private static void ReplaceOrStage(string source, string dest)
		{
			try
			{
				File.Copy(source, dest, true);
				if (File.Exists(dest + PendingSuffix)) File.Delete(dest + PendingSuffix);
			}
			catch (IOException)
			{
				File.Copy(source, dest + PendingSuffix, true);
				Logger.LogRow(Logger.LogType.Info, $"[EchoReplay] {dest} is in use; the update is applied when the game closes");
			}
			catch (UnauthorizedAccessException e)
			{
				Logger.LogRow(Logger.LogType.Error, $"[EchoReplay] can't write {dest}: {e.Message}");
			}
		}

		/// <summary>Swaps staged .new files in once nothing has the old one loaded.</summary>
		public static void ApplyPending()
		{
			foreach (string dest in GamePluginCopies().Append(LocalDllPath))
			{
				string pending = dest + PendingSuffix;
				if (!File.Exists(pending)) continue;
				try
				{
					File.Move(pending, dest, true);
					Logger.LogRow(Logger.LogType.Info, $"[EchoReplay] applied the staged update to {dest}");
				}
				catch (IOException)
				{
					// still loaded; try again later
				}
				catch (UnauthorizedAccessException)
				{
					// same
				}
			}
		}

		private static void CopyDirectory(string from, string to)
		{
			Directory.CreateDirectory(to);
			foreach (string dir in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
			{
				Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
			}

			foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
			{
				File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)), true);
			}
		}

		#endregion

		#region Game folders

		private static readonly ConcurrentDictionary<string, byte> knownGameDirs = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

		/// <summary>bin\win10 folders we know about: Spark's EchoVR path setting and every echovr.exe seen running.</summary>
		private static IEnumerable<string> GameDirs()
		{
			string fromSettings = Program.EchoGameDir;
			if (!string.IsNullOrEmpty(fromSettings)) knownGameDirs.TryAdd(fromSettings, 0);
			return knownGameDirs.Keys.Where(Directory.Exists);
		}

		/// <summary>Every plugins\EchoReplay.dll in a known game folder (a plugin loader loads these itself).</summary>
		public static IEnumerable<string> GamePluginCopies()
		{
			return GameDirs().Select(d => Path.Combine(d, "plugins", DllName)).Where(File.Exists).ToList();
		}

		#endregion

		#region Injection

		/// <summary>Should Spark be getting EchoReplay into the game at all?</summary>
		private static bool Wanted => SparkSettings.instance.enableFullLogging && SparkSettings.instance.saveEchodemoFiles;

		private static int ticking;

		private static void WatchTick()
		{
			if (Interlocked.Exchange(ref ticking, 1) == 1) return;
			Process[] games = Array.Empty<Process>();
			try
			{
				games = Process.GetProcessesByName("echovr");
				HashSet<int> alive = games.Select(p => p.Id).ToHashSet();
				foreach (int gone in handled.Keys.Where(id => !alive.Contains(id)).ToList())
				{
					handled.TryRemove(gone, out _);
				}

				if (games.Length == 0) ApplyPending();
				if (!Wanted) return;

				foreach (Process game in games)
				{
					if (handled.ContainsKey(game.Id)) continue;
					try
					{
						HandleGame(game);
					}
					catch (Exception e)
					{
						handled[game.Id] = "error";
						Logger.LogRow(Logger.LogType.Error, $"[EchoReplay] couldn't load into echovr.exe ({game.Id}): {e.Message}");
					}
				}
			}
			catch (Exception e)
			{
				Logger.LogRow(Logger.LogType.Error, $"[EchoReplay] watcher: {e}");
			}
			finally
			{
				foreach (Process p in games) p.Dispose();
				Interlocked.Exchange(ref ticking, 0);
			}
		}

		private static void HandleGame(Process game)
		{
			// let the process finish starting up first: a thread created before the Windows loader has
			// initialised the process can't call LoadLibraryW
			if (DateTime.Now - game.StartTime < TimeSpan.FromSeconds(1)) return;

			string exe = game.MainModule?.FileName;
			string gameDir = exe == null ? null : Path.GetDirectoryName(exe);
			if (gameDir != null) knownGameDirs.TryAdd(gameDir, 0);

			// dedicated servers aren't matches to record
			string cmd = CommandLineOf(game.Id);
			if (cmd.Contains("-server", StringComparison.OrdinalIgnoreCase) || cmd.Contains("-headless", StringComparison.OrdinalIgnoreCase))
			{
				handled[game.Id] = "server";
				return;
			}

			// a plugin loader already loads plugins\EchoReplay.dll: keep it updated, don't inject
			if (gameDir != null && File.Exists(Path.Combine(gameDir, "plugins", DllName)))
			{
				handled[game.Id] = "loader";
				Logger.LogRow(Logger.LogType.Info, "[EchoReplay] plugins\\EchoReplay.dll is in the game folder; leaving it to the plugin loader");
				_ = InstallOrUpdateAsync(false);
				return;
			}

			if (IsModuleLoaded(game, DllName))
			{
				handled[game.Id] = "loaded";
				return;
			}

			if (!File.Exists(LocalDllPath))
			{
				// download it first; the next tick injects
				_ = InstallOrUpdateAsync(false);
				return;
			}

			SetCaptureDir(RecordingsDir);
			Inject(game.Id, LocalDllPath);
			handled[game.Id] = "injected";
			Logger.LogRow(Logger.LogType.Info, $"[EchoReplay] injected {InstalledVersion} into echovr.exe ({game.Id}); recording to {RecordingsDir}");
		}

		/// <summary>The config file of Spark's injected copy (beside its plugins folder).</summary>
		private static string ConfigPath => Path.Combine(BaseDir, "echoreplay", "config.json");

		/// <summary>
		/// Points the injected copy's recordings at <paramref name="dir"/> (capture.dir in its config.json). The plugin
		/// writes a fully commented config.json only when there is none, so an existing file keeps everything else
		/// and only that one key is set or added.
		/// </summary>
		public static void SetCaptureDir(string dir)
		{
			try
			{
				Directory.CreateDirectory(dir);
				string value = JsonConvert.ToString(dir);   // quoted and escaped
				string path = ConfigPath;
				Directory.CreateDirectory(Path.GetDirectoryName(path)!);
				if (!File.Exists(path))
				{
					File.WriteAllText(path, "{\n  // set by Spark: recordings go to Spark's replay folder\n  \"capture.dir\": " + value + "\n}\n");
					return;
				}

				string text = File.ReadAllText(path);
				Regex key = new Regex("\"capture\\.dir\"\\s*:\\s*\"(?:[^\"\\\\]|\\\\.)*\"");
				string updated = key.IsMatch(text)
					? key.Replace(text, "\"capture.dir\": " + value.Replace("$", "$$"), 1)
					: new Regex("\\{").Replace(text, "{\n  \"capture.dir\": " + value.Replace("$", "$$") + ",", 1);
				if (updated != text) File.WriteAllText(path, updated);
			}
			catch (Exception e)
			{
				Logger.LogRow(Logger.LogType.Error, $"[EchoReplay] couldn't set the recording folder: {e.Message}");
			}
		}

		private static string CommandLineOf(int pid)
		{
			try
			{
				using ManagementObjectSearcher s = new ManagementObjectSearcher($"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}");
				foreach (ManagementBaseObject o in s.Get())
				{
					return o["CommandLine"]?.ToString() ?? "";
				}
			}
			catch
			{
				// not knowing the command line just means not skipping servers
			}

			return "";
		}

		private static bool IsModuleLoaded(Process p, string name)
		{
			try
			{
				p.Refresh();
				return p.Modules.Cast<ProcessModule>().Any(m => string.Equals(m.ModuleName, name, StringComparison.OrdinalIgnoreCase));
			}
			catch
			{
				return false;
			}
		}

		[Flags]
		private enum ProcessAccess : uint
		{
			CreateThread = 0x0002,
			VmOperation = 0x0008,
			VmRead = 0x0010,
			VmWrite = 0x0020,
			QueryInformation = 0x0400,
		}

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern IntPtr OpenProcess(ProcessAccess access, bool inherit, int pid);

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, UIntPtr size, uint allocationType, uint protect);

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool VirtualFreeEx(IntPtr process, IntPtr address, UIntPtr size, uint freeType);

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] buffer, UIntPtr size, out UIntPtr written);

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern IntPtr CreateRemoteThread(IntPtr process, IntPtr attributes, UIntPtr stackSize, IntPtr startAddress, IntPtr parameter, uint flags, out uint threadId);

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern IntPtr GetModuleHandleW(string name);

		[DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
		private static extern IntPtr GetProcAddress(IntPtr module, string name);

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern uint WaitForSingleObject(IntPtr handle, uint ms);

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool GetExitCodeThread(IntPtr thread, out uint exitCode);

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool CloseHandle(IntPtr handle);

		private const uint MemCommitReserve = 0x3000;
		private const uint MemRelease = 0x8000;
		private const uint PageReadWrite = 0x04;

		/// <summary>
		/// Loads <paramref name="dllPath"/> into process <paramref name="pid"/> by running LoadLibraryW on a
		/// remote thread. kernel32 is mapped at the same address in every process of a boot, so our own
		/// LoadLibraryW address is valid in the game. Both are 64-bit.
		/// </summary>
		public static void Inject(int pid, string dllPath)
		{
			if (!Environment.Is64BitProcess) throw new InvalidOperationException("Spark has to run as a 64-bit process to load EchoReplay into echovr.exe");

			IntPtr process = OpenProcess(ProcessAccess.CreateThread | ProcessAccess.VmOperation | ProcessAccess.VmRead | ProcessAccess.VmWrite | ProcessAccess.QueryInformation, false, pid);
			if (process == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcess failed (is the game running as administrator?)");
			IntPtr remote = IntPtr.Zero;
			try
			{
				byte[] path = Encoding.Unicode.GetBytes(Path.GetFullPath(dllPath) + "\0");
				remote = VirtualAllocEx(process, IntPtr.Zero, (UIntPtr)path.Length, MemCommitReserve, PageReadWrite);
				if (remote == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "VirtualAllocEx failed");
				if (!WriteProcessMemory(process, remote, path, (UIntPtr)path.Length, out _)) throw new Win32Exception(Marshal.GetLastWin32Error(), "WriteProcessMemory failed");

				IntPtr loadLibrary = GetProcAddress(GetModuleHandleW("kernel32.dll"), "LoadLibraryW");
				if (loadLibrary == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "LoadLibraryW not found");

				IntPtr thread = CreateRemoteThread(process, IntPtr.Zero, UIntPtr.Zero, loadLibrary, remote, 0, out _);
				if (thread == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateRemoteThread failed");
				try
				{
					// the plugin's DllMain only starts a worker thread, so this returns quickly
					if (WaitForSingleObject(thread, 15000) != 0) throw new TimeoutException("LoadLibraryW didn't return within 15 s");
					// the exit code is only the low 32 bits of the HMODULE, so 0 is checked against the module list
					GetExitCodeThread(thread, out uint module);
					if (module == 0)
					{
						using Process p = Process.GetProcessById(pid);
						if (!IsModuleLoaded(p, Path.GetFileName(dllPath)))
						{
							throw new InvalidOperationException("LoadLibraryW failed in the game (see echoreplay.log beside the plugins folder)");
						}
					}
				}
				finally
				{
					CloseHandle(thread);
				}
			}
			finally
			{
				if (remote != IntPtr.Zero) VirtualFreeEx(process, remote, UIntPtr.Zero, MemRelease);
				CloseHandle(process);
			}
		}

		#endregion

		/// <summary>One line for the Replays settings page.</summary>
		public static string StatusText()
		{
			string version = InstalledVersion;
			List<string> gameCopies = GamePluginCopies().ToList();
			string mode = gameCopies.Count > 0
				? "Your game folder has plugins\\EchoReplay.dll, so your plugin loader loads it and Spark keeps it updated (no injection)."
				: "Spark injects EchoReplay.dll into echovr.exe when the game starts.";
			string installed = version.Length > 0 ? $"Installed: {version}." : "Not downloaded yet; Spark downloads it the first time the game starts.";
			string update = UpdateAvailable ? $" Update available: {LatestVersion}." : "";
			return $"{installed}{update} {mode}";
		}
	}
}
