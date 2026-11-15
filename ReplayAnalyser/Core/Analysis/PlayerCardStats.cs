#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spark.ReplayAnalyser.Model;

namespace Spark.ReplayAnalyser.Analysis;

/// <summary>The PlayerCard's numbers, and how much of the replay folder they cover.</summary>
public sealed class PlayerCardResult
{
	/// <summary>Keyed like the game's own arena stats (Goals, GamesPlayed, ArenaMVPs, …).</summary>
	public Dictionary<string, double> Stats { get; } = new(StringComparer.OrdinalIgnoreCase);

	public string Folder { get; init; } = "";

	/// <summary>Replays in the folder, and how many of them the Replay Analyser has indexed.</summary>
	public int Total { get; init; }

	public int Indexed { get; init; }

	/// <summary>Public arena matches the player was found in.</summary>
	public int Matches { get; set; }

	/// <summary>Indexed public arena matches left out because the player wasn't in them.</summary>
	public int WithoutPlayer { get; set; }

	/// <summary>Private matches left out.</summary>
	public int PrivateSkipped { get; set; }

	public bool FullyIndexed => Indexed >= Total;
}

/// <summary>
/// Career stats for the PlayerCard, built from the Replay Analyser's index of the replay folder rather than from the
/// game's profile (which only some servers fill in). Only public Echo Arena matches count: private matches inflate
/// the scoreboard and are mostly scrims and practice.
/// </summary>
public static class PlayerCardStats
{
	/// <param name="folder">The replay folder (the Replay Analyser's).</param>
	/// <param name="ids">The player's platform ids (OVR-ORG-… numbers).</param>
	/// <param name="names">The player's display names, for replays that carry no ids.</param>
	/// <param name="index">The library index; the Replay Analyser's saved one when null.</param>
	public static PlayerCardResult Compute(string folder, ICollection<ulong> ids, ICollection<string> names, LibraryIndex? index = null)
	{
		List<FileInfo> files = new();
		if (Directory.Exists(folder))
		{
			// the same files the Replay Analyser's library lists
			files = new[] { "*.echodemo", "*.butter", "*.echoreplay", "*.tape" }
				.SelectMany(p => Directory.EnumerateFiles(folder, p))
				.Select(f => new FileInfo(f))
				.Where(f => f.Length > 2000)
				.ToList();
		}

		index ??= LibraryIndex.Load();
		List<MatchSummary> indexed = files.Where(index.IsCurrent).Select(f => index.Get(f.FullName)!).ToList();
		var result = new PlayerCardResult { Folder = folder, Total = files.Count, Indexed = indexed.Count };

		var nameSet = new HashSet<string>(names.Where(n => !string.IsNullOrWhiteSpace(n)), StringComparer.OrdinalIgnoreCase);
		bool IsMe(PlayerSummary p) =>
			(p.UserId != 0 && ids.Contains(unchecked((ulong)p.UserId))) || (p.Name.Length > 0 && nameSet.Contains(p.Name));

		var games = new List<(MatchSummary match, PlayerSummary me, bool won, bool mvp)>();
		foreach (MatchSummary m in indexed.Where(m => m.Ok && m.IsArena).OrderBy(m => m.Recorded))
		{
			if (m.PrivateMatch)
			{
				result.PrivateSkipped++;
				continue;
			}

			PlayerSummary? me = m.Players.FirstOrDefault(p => p.Side is 0 or 1 && IsMe(p));
			if (me == null)
			{
				result.WithoutPlayer++;
				continue;
			}

			int mine = me.Side == 0 ? m.BlueScore : m.OrangeScore;
			int theirs = me.Side == 0 ? m.OrangeScore : m.BlueScore;
			int best = m.Players.Where(p => p.Side is 0 or 1).Max(p => p.Points);
			games.Add((m, me, mine > theirs, me.Points > 0 && me.Points == best));
		}

		int n = games.Count;
		result.Matches = n;
		Dictionary<string, double> s = result.Stats;
		s["GamesPlayed"] = n;
		s["ArenaWins"] = games.Count(g => g.won);
		s["ArenaLosses"] = n - games.Count(g => g.won);
		s["Points"] = games.Sum(g => g.me.Points);
		s["Goals"] = games.Sum(g => g.me.Goals);
		s["Assists"] = games.Sum(g => g.me.Assists);
		s["Saves"] = games.Sum(g => g.me.Saves);
		s["Stuns"] = games.Sum(g => g.me.Stuns);
		s["Steals"] = games.Sum(g => g.me.Steals);
		s["Interceptions"] = games.Sum(g => g.me.Interceptions);
		s["Passes"] = games.Sum(g => g.me.Passes);
		s["ShotsOnGoal"] = games.Sum(g => g.me.ShotsTaken);
		s["PossessionTime"] = games.Sum(g => g.me.PossessionTime);
		s["ArenaMVPs"] = games.Count(g => g.mvp);
		s["HatTricks"] = games.Count(g => g.me.Goals >= 3);
		s["ArenaMVPPercentage"] = n == 0 ? 0 : 100.0 * games.Count(g => g.mvp) / n;
		s["ArenaWinPercentage"] = n == 0 ? 0 : 100.0 * games.Count(g => g.won) / n;
		s["AveragePointsPerGame"] = n == 0 ? 0 : s["Points"] / n;
		s["GoalsPerGame"] = n == 0 ? 0 : s["Goals"] / n;

		// wins in a row, counting back from the latest match
		int streak = 0;
		for (int i = n - 1; i >= 0 && games[i].won; i--) streak++;
		s["CurrentArenaWinStreak"] = streak;
		return result;
	}
}
