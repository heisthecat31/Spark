// Nullable is enabled here to match ButterV3Reader, which is annotated.
#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using EchoVRAPI;
using Newtonsoft.Json;

namespace Spark
{
	/// <summary>
	/// What <see cref="ButterV3Reader"/> recovered from a file, and how much of it survived.
	/// Mirrors the type the reader was written against in the Replay Analyser.
	/// </summary>
	public sealed class ReplayReadResult
	{
		public string? SourcePath { get; set; }
		public ReplayFormat Format { get; set; }
		public int FormatVersion { get; set; }
		public string? ClientName { get; set; }
		public string? SessionId { get; set; }
		public List<Frame> Frames { get; set; } = new List<Frame>();
		public List<string> Diagnostics { get; } = new List<string>();
		public int FailedChunks { get; set; }
		public bool TruncatedTail { get; set; }
		public int RecoveredPlayerSlots { get; set; }

		public bool HasFrames => Frames.Count > 0;
		public bool IsPartial => FailedChunks > 0 || TruncatedTail;
	}

	public enum ReplayFormat { Unknown, EchoReplay, Butter, Tape, EchoDemo }

	/// <summary>
	/// Rescues a .butter file the replay viewer refuses to open, by re-reading it with the
	/// fault-tolerant decoder and writing what survives back out as .echoreplay.
	///
	/// The stock decoder is all-or-nothing: one malformed frame and the whole file is discarded.
	/// The usual culprit is a player who joined after recording started, whose index runs past the
	/// fixed-size player table in the header. <see cref="ButterV3Reader"/> decodes each chunk
	/// independently, grows that table on demand, and keeps everything before a truncated tail, so
	/// files that fail to load elsewhere still give up most or all of their frames here.
	/// </summary>
	public static class ButterRepair
	{
		/// <summary>Timestamp layout every .echoreplay line starts with.</summary>
		private const string EchoreplayDateFormat = "yyyy/MM/dd HH:mm:ss.fff";

		public sealed class RepairResult
		{
			public bool Success { get; set; }
			public string? OutputPath { get; set; }
			public int FrameCount { get; set; }
			public int FailedChunks { get; set; }
			public bool TruncatedTail { get; set; }
			public int RecoveredPlayerSlots { get; set; }
			public List<string> Diagnostics { get; } = new List<string>();

			/// <summary>One-line summary suitable for a message box.</summary>
			public string Summary
			{
				get
				{
					if (!Success)
					{
						string why = Diagnostics.Count > 0 ? string.Join("\n", Diagnostics) : "No frames could be recovered.";
						return "Couldn't repair this file.\n\n" + why;
					}

					List<string> notes = new List<string>();
					if (FailedChunks > 0) notes.Add($"{FailedChunks} damaged chunk(s) skipped");
					if (TruncatedTail) notes.Add("file ended mid-frame, tail dropped");
					if (RecoveredPlayerSlots > 0) notes.Add($"{RecoveredPlayerSlots} late-joining player slot(s) recovered");

					string detail = notes.Count > 0 ? "\n\n" + string.Join("\n", notes) : "";
					return $"Recovered {FrameCount:N0} frames.\n\nSaved to:\n{OutputPath}{detail}";
				}
			}
		}

		/// <summary>
		/// Reads <paramref name="butterPath"/> tolerantly and writes the recovered frames as an
		/// .echoreplay beside it. Honours the Use Compression setting, so the output is zipped the
		/// same way Spark's own recordings are.
		/// </summary>
		/// <param name="outputPath">
		/// Where to write. Defaults to the source path with "_fixed.echoreplay" in place of the
		/// extension, without overwriting anything already there.
		/// </param>
		/// <param name="alreadyRead">
		/// A decode of this same file that the caller has already done, reused instead of reading
		/// and decoding it a second time. The scan passes the one its broken-check produced.
		/// </param>
		public static RepairResult RepairToEchoreplay(string butterPath, string? outputPath = null, ReplayReadResult? alreadyRead = null)
		{
			RepairResult repair = new RepairResult();

			try
			{
				if (!File.Exists(butterPath))
				{
					repair.Diagnostics.Add("File not found: " + butterPath);
					return repair;
				}

				ReplayReadResult read = alreadyRead ?? ButterV3Reader.Read(File.ReadAllBytes(butterPath), butterPath);

				repair.Diagnostics.AddRange(read.Diagnostics);
				repair.FailedChunks = read.FailedChunks;
				repair.TruncatedTail = read.TruncatedTail;
				repair.RecoveredPlayerSlots = read.RecoveredPlayerSlots;
				repair.FrameCount = read.Frames.Count;

				if (!read.HasFrames)
				{
					// Nothing usable came back, so there is nothing to write. The diagnostics
					// collected above say why, and are shown to the user rather than swallowed.
					return repair;
				}

				string target = outputPath ?? NextFreePath(
					Path.Combine(
						Path.GetDirectoryName(butterPath) ?? "",
						Path.GetFileNameWithoutExtension(butterPath) + "_fixed.echoreplay"));

				WriteEchoreplay(read.Frames, target, SparkSettings.instance.useCompression);

				repair.OutputPath = target;
				repair.Success = true;

				Logger.LogRow(Logger.LogType.File, "butter_repair",
					$"Repaired {butterPath}: {read.Frames.Count} frames, {read.FailedChunks} bad chunk(s), truncated={read.TruncatedTail} -> {target}");
			}
			catch (Exception e)
			{
				repair.Diagnostics.Add(e.GetType().Name + ": " + e.Message);
				Logger.LogRow(Logger.LogType.Error, $"Butter repair failed for {butterPath}\n{e}");
			}

			return repair;
		}

		/// <summary>
		/// Writes frames in the .echoreplay layout: one line per frame, tab separated, as
		/// <c>timestamp \t session-json</c>. Optionally wrapped in a zip, which is what the
		/// viewer expects from a compressed recording.
		/// </summary>
		private static void WriteEchoreplay(List<Frame> frames, string path, bool compress)
		{
			string directory = Path.GetDirectoryName(path) ?? "";
			if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

			if (!compress)
			{
				using StreamWriter writer = new StreamWriter(path, false);
				foreach (Frame frame in frames) writer.WriteLine(FormatLine(frame));
				return;
			}

			// Zip via a temp directory, matching how ReplayFilesManager compresses a finished
			// recording: a single entry named after the file the viewer expects to find inside.
			string tempDir = Path.Combine(Path.GetTempPath(), "spark_butter_repair_" + Guid.NewGuid());
			try
			{
				Directory.CreateDirectory(tempDir);

				string inner = Path.Combine(tempDir, Path.GetFileNameWithoutExtension(path) + ".echoreplay");
				using (StreamWriter writer = new StreamWriter(inner, false))
				{
					foreach (Frame frame in frames) writer.WriteLine(FormatLine(frame));
				}

				if (File.Exists(path)) File.Delete(path);
				ZipFile.CreateFromDirectory(tempDir, path);
			}
			finally
			{
				try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); }
				catch (Exception e) { Logger.LogRow(Logger.LogType.Error, "Couldn't clear the repair temp folder\n" + e); }
			}
		}

		private static string FormatLine(Frame frame)
		{
			DateTime recorded = frame.recorded_time == default ? DateTime.Now : frame.recorded_time;
			return recorded.ToString(EchoreplayDateFormat, CultureInfo.InvariantCulture)
			       + "\t"
			       + JsonConvert.SerializeObject(frame);
		}

		/// <summary>Why a file was judged broken, and what the tolerant reader got out of it.</summary>
		public sealed class BrokenCheck
		{
			public bool IsBroken { get; set; }
			public string Reason { get; set; } = "";
			public int RecoverableFrames { get; set; }

			/// <summary>
			/// The decode the check already paid for, passed to the repair so a broken file is
			/// read once rather than once to diagnose and again to fix.
			/// </summary>
			internal ReplayReadResult? Read { get; set; }
		}

		public sealed class ScanResult
		{
			public int Scanned { get; set; }
			public int Broken { get; set; }
			public int Repaired { get; set; }
			public int Deleted { get; set; }
			public bool Cancelled { get; set; }
			public List<string> Details { get; } = new List<string>();

			public string Summary
			{
				get
				{
					string stopped = Cancelled ? "Stopped. " : "";
					if (Scanned == 0) return Cancelled ? "Stopped before checking anything." : "No .butter files found to check.";
					if (Broken == 0) return $"{stopped}Checked {Scanned:N0} file(s). None were broken.";

					string body = $"{stopped}Checked {Scanned:N0} file(s).\n\n"
					              + $"Broken: {Broken:N0}\n"
					              + $"Repaired: {Repaired:N0}\n"
					              + $"Originals deleted: {Deleted:N0}";

					if (Repaired < Broken)
						body += $"\n\n{Broken - Repaired:N0} could not be repaired and were left alone.";

					return body;
				}
			}
		}

		/// <summary>
		/// Decides whether a .butter file is broken, meaning the viewer will not open it but
		/// something is still recoverable.
		///
		/// Two ways to qualify. The stock decoder — the same one the viewer uses — either throwing
		/// or returning nothing is the direct signal. The tolerant reader reporting damaged chunks
		/// or a truncated tail is the other: those files sometimes still limp through the stock
		/// decoder, but they are missing data and are worth rewriting while the frames are still
		/// readable.
		///
		/// A file only counts as broken if the tolerant reader can actually recover frames from it.
		/// Without that there is nothing to repair, and deleting the original would destroy the
		/// only copy of whatever is in there.
		/// </summary>
		public static BrokenCheck CheckBroken(string butterPath)
		{
			BrokenCheck check = new BrokenCheck();

			byte[] bytes;
			try { bytes = File.ReadAllBytes(butterPath); }
			catch (Exception e)
			{
				check.Reason = "could not be read: " + e.Message;
				return check;
			}

			// The tolerant read goes first because it is the cheaper of the two passes and settles
			// the damage question outright. A file it reports as damaged is broken whatever the
			// stock decoder makes of it, so that case skips the stock decode entirely — and the
			// decode it produces is handed to the repair, which would otherwise redo it.
			ReplayReadResult read;
			try { read = ButterV3Reader.Read(bytes, butterPath); }
			catch (Exception e)
			{
				check.Reason = "tolerant read threw: " + e.Message;
				return check;
			}

			check.Read = read;
			check.RecoverableFrames = read.Frames.Count;

			// Nothing to salvage, so leave it exactly where it is.
			if (!read.HasFrames)
			{
				check.Reason = "no frames recoverable";
				return check;
			}

			// Damaged or cut short. These sometimes still limp through the stock decoder, but they
			// are missing data, and rewriting them while the frames are still readable is the whole
			// point of the exercise.
			if (read.FailedChunks > 0 || read.TruncatedTail)
			{
				check.IsBroken = true;

				List<string> bits = new List<string>();
				if (read.FailedChunks > 0) bits.Add($"{read.FailedChunks} damaged chunk(s)");
				if (read.TruncatedTail) bits.Add("truncated tail");
				check.Reason = string.Join(", ", bits);
				return check;
			}

			// Intact as far as the tolerant reader can tell, so the only question left is whether
			// the decoder the viewer actually uses will open it.
			try
			{
				List<Frame> stock = ButterReplays.ButterFile.FromBytes(bytes);
				if (stock != null && stock.Count > 0)
				{
					check.Reason = "decodes cleanly";
					return check;
				}

				check.IsBroken = true;
				check.Reason = "the standard decoder returned no frames";
			}
			catch (Exception e)
			{
				check.IsBroken = true;
				check.Reason = "the standard decoder failed with " + e.GetType().Name;
			}

			return check;
		}

		/// <summary>
		/// Checks every .butter file in <paramref name="folder"/>, repairs the broken ones, and —
		/// when <paramref name="deleteOriginals"/> is set — removes the original afterwards.
		///
		/// A original is only deleted once its replacement has been written AND read back with the
		/// expected frame count. Deleting a recording is not undoable, so a repair that cannot be
		/// verified leaves both files in place.
		/// </summary>
		public static ScanResult ScanAndRepair(string folder, bool deleteOriginals, Action<string>? progress = null, CancellationToken token = default)
		{
			ScanResult scan = new ScanResult();

			if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
			{
				scan.Details.Add("Save folder not found: " + folder);
				return scan;
			}

			string[] files;
			try { files = Directory.GetFiles(folder, "*.butter", SearchOption.TopDirectoryOnly); }
			catch (Exception e)
			{
				scan.Details.Add("Could not list the save folder: " + e.Message);
				return scan;
			}

			foreach (string file in files)
			{
				// Checked between files rather than mid-decode: stopping partway through a repair
				// could leave a half-written replacement beside an original about to be judged.
				if (token.IsCancellationRequested)
				{
					scan.Cancelled = true;
					scan.Details.Add($"Stopped early — {files.Length - scan.Scanned:N0} file(s) not checked.");
					break;
				}

				scan.Scanned++;
				string name = Path.GetFileName(file);
				progress?.Invoke($"Checking {name} ({scan.Scanned}/{files.Length})...");

				BrokenCheck check = CheckBroken(file);
				if (!check.IsBroken)
				{
					scan.Details.Add($"OK      {name} — {check.Reason}");
					continue;
				}

				scan.Broken++;
				progress?.Invoke($"Repairing {name} ({scan.Scanned}/{files.Length})...");

				// Reuses the decode the check already did, so a broken file is read once.
				RepairResult repair = RepairToEchoreplay(file, null, check.Read);
				if (!repair.Success || string.IsNullOrEmpty(repair.OutputPath))
				{
					scan.Details.Add($"FAILED  {name} — {check.Reason}; repair did not produce a file");
					continue;
				}

				scan.Repaired++;

				int verified = CountEchoreplayFrames(repair.OutputPath);
				bool trustworthy = verified > 0 && verified == repair.FrameCount;

				if (!deleteOriginals)
				{
					scan.Details.Add($"FIXED   {name} — {check.Reason}; {repair.FrameCount:N0} frames -> {Path.GetFileName(repair.OutputPath)}");
					continue;
				}

				if (!trustworthy)
				{
					scan.Details.Add($"KEPT    {name} — repaired but the copy read back {verified:N0} of {repair.FrameCount:N0} frames, so the original was left in place");
					continue;
				}

				try
				{
					File.Delete(file);
					scan.Deleted++;
					scan.Details.Add($"FIXED   {name} — {check.Reason}; {repair.FrameCount:N0} frames -> {Path.GetFileName(repair.OutputPath)}, original deleted");
				}
				catch (Exception e)
				{
					scan.Details.Add($"KEPT    {name} — repaired, but the original could not be deleted: {e.Message}");
				}
			}

			Logger.LogRow(Logger.LogType.File, "butter_repair",
				$"Scan of {folder}: scanned={scan.Scanned} broken={scan.Broken} repaired={scan.Repaired} deleted={scan.Deleted}");

			return scan;
		}

		/// <summary>
		/// Re-reads a written .echoreplay and counts the frames that parse, so a repair can be
		/// proven good before the original is thrown away. Handles the zipped form, since that is
		/// what gets written when compression is on. Returns -1 if it cannot be read at all.
		/// </summary>
		private static int CountEchoreplayFrames(string path)
		{
			try
			{
				bool zipped;
				using (FileStream probe = File.OpenRead(path))
				{
					zipped = probe.ReadByte() == 0x50 && probe.ReadByte() == 0x4B;
				}

				ZipArchive archive = null;
				TextReader reader;

				if (zipped)
				{
					archive = ZipFile.OpenRead(path);
					ZipArchiveEntry entry = archive.Entries.FirstOrDefault(e => e.Length > 0) ?? archive.Entries.FirstOrDefault();
					if (entry == null) { archive.Dispose(); return -1; }
					reader = new StreamReader(entry.Open());
				}
				else
				{
					reader = new StreamReader(File.OpenRead(path));
				}

				int good = 0;
				try
				{
					string line;
					while ((line = reader.ReadLine()) != null)
					{
						if (line.Length == 0) continue;

						int tab = line.IndexOf('\t');
						if (tab <= 0) continue;

						string json = line.Substring(tab + 1);
						int tab2 = json.IndexOf('\t');
						if (tab2 > 0) json = json.Substring(0, tab2);

						if (json.Length >= 2 && json[0] == '{') good++;
					}
				}
				finally
				{
					reader.Dispose();
					archive?.Dispose();
				}

				return good;
			}
			catch (Exception e)
			{
				Logger.LogRow(Logger.LogType.Error, $"Couldn't verify the repaired replay {path}\n{e}");
				return -1;
			}
		}

		/// <summary>Appends a counter rather than overwriting a previous repair of the same file.</summary>
		private static string NextFreePath(string preferred)
		{
			if (!File.Exists(preferred)) return preferred;

			string dir = Path.GetDirectoryName(preferred) ?? "";
			string name = Path.GetFileNameWithoutExtension(preferred);
			string ext = Path.GetExtension(preferred);

			for (int i = 2; i < 1000; i++)
			{
				string candidate = Path.Combine(dir, $"{name}_{i}{ext}");
				if (!File.Exists(candidate)) return candidate;
			}

			return Path.Combine(dir, $"{name}_{Guid.NewGuid():N}{ext}");
		}
	}
}
