using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using EchoVRAPI;

namespace Spark
{
	/// <summary>
	/// The idea is this will contain the entire set of data for the match, and will be used to review and then commit the data all at once 
	/// rather than little by little like before.
	/// 
	/// PlayerData represents the stats for a player only for a particular match
	/// </summary>
	public class MatchPlayer : DataContainer
	{
		public MatchPlayer()
		{
		}
		
		//
		// public MatchPlayer(MatchData match, Player player)
		// {
		// 	matchData = match;
		// 	Id = player.userid;
		// 	Name = player.name;
		// 	playspaceLocation = player.head.Position;
		// 	PlayspaceAbuses = 0;
		// }
		
		/// <summary>
		/// Based on a previous round.
		/// </summary>
		public MatchPlayer(AccumulatedFrame round, Player player)
		{
			matchData = round;
			Id = player.userid;
			Name = player.name;
			Level = player.level;
			Number = player.number;
			currentStats = player.stats;
			cachedStats = new Stats();
			oldRoundStats = new Stats();
			currentCombatStats = new CombatStats();
			cachedCombatStats = new CombatStats();
			oldRoundCombatStats = new CombatStats();
			PlayTime = 0;
			InvertedTime = 0;
			GoalsNum = 0;
			TwoPointers = 0;
			ThreePointers = 0;
			Passes = 0;
			Catches = 0;
			Won = 0;
			Turnovers = 0;
			playspaceLocation = player.head.Position;
		}

		/// <summary>
		/// Copy constructor
		/// </summary>
		public MatchPlayer(MatchPlayer matchPlayer)
		{
			matchData = matchPlayer.matchData;
			Id = matchPlayer.Id;
			Name = matchPlayer.Name;
			Level = matchPlayer.Level;
			Number = matchPlayer.Number;
			currentStats = matchPlayer.currentStats;
			cachedStats = matchPlayer.cachedStats;
			oldRoundStats = matchPlayer.oldRoundStats;
			currentCombatStats = matchPlayer.currentCombatStats;
			cachedCombatStats = matchPlayer.cachedCombatStats;
			oldRoundCombatStats = matchPlayer.oldRoundCombatStats;
			PlayTime = matchPlayer.PlayTime;
			InvertedTime = matchPlayer.InvertedTime;
			GoalsNum = matchPlayer.GoalsNum;
			TwoPointers = matchPlayer.TwoPointers;
			ThreePointers = matchPlayer.ThreePointers;
			Passes = matchPlayer.Passes;
			Catches = matchPlayer.Catches;
			Won = matchPlayer.Won;
			Turnovers = matchPlayer.Turnovers;
			playspaceLocation = matchPlayer.playspaceLocation;
		}

		/// <summary>
		/// Function to transform player data into the desired format for firestore.
		/// </summary>
		/// <returns></returns>
		public Dictionary<string, object> ToDict()
		{
			Dictionary<string, object> values = new Dictionary<string, object>
			{
				{ "session_id", matchData.frame.sessionid },
				{ "match_time", matchData.MatchTimeSQL },
				{ "player_id", Id },
				{ "player_name", Name },
				{ "level", Level },
				{ "player_number", Number },
				{ "team_color", TeamColor.ToString() },
				{ "possession_time", PossessionTime },
				{ "play_time", PlayTime },
				{ "inverted_time", InvertedTime },
				{ "points", Points },
				{ "2_pointers", TwoPointers },
				{ "3_pointers", ThreePointers },
				{ "shots_taken", ShotsTaken },
				{ "saves", Saves },
				{ "goals", GoalsNum },
				{ "stuns", Stuns },
				{ "passes", Passes },
				{ "catches", Catches },
				{ "steals", Steals },
				{ "blocks", Blocks },
				{ "interceptions", Interceptions },
				{ "assists", Assists },
				{ "combat_kills", CombatKills },
				{ "combat_assists", CombatAssists },
				{ "combat_deaths", CombatDeaths },
				{ "combat_damage", CombatDamage },
				{ "combat_damage_taken", CombatDamageTaken },
				{ "combat_damage_taken_raw", CombatDamageTakenRaw },
				{ "combat_eliminations", CombatEliminations },
				{ "combat_objective_eliminations", CombatObjectiveEliminations },
				{ "combat_objective_time", CombatObjectiveTime },
				{ "combat_objective_damage", CombatObjectiveDamage },
				{ "combat_hill_captures", CombatHillCaptures },
				{ "combat_hill_defends", CombatHillDefends },
				// { "turnovers", Turnovers }, // TODO enable once the db supports it
				{ "average_speed", averageSpeed[0] },
				{ "average_speed_lhand", averageSpeed[1] },
				{ "average_speed_rhand", averageSpeed[2] },
				{ "wingspan", DistanceBetweenHands },
				{ "playspace_abuses", PlayspaceAbuses },
				{ "wins", Won }
			};

			return values;
		}

		private const int MINCLAMP = 0;

		#region Get/Set Methods

		public long Id { get; private set; }

		public string Name { get; private set; }
		public int Level { get; private set; }
		public int Number { get; private set; }
		public Team.TeamColor TeamColor { get; private set; }

		public Stats currentStats = new Stats();
		public Stats cachedStats = new Stats();
		public Stats oldRoundStats = new Stats();

		public CombatStats currentCombatStats = new CombatStats();
		public CombatStats cachedCombatStats = new CombatStats();
		public CombatStats oldRoundCombatStats = new CombatStats();

		public float PossessionTime
		{
			get => Math.Clamp(cachedStats.possession_time + currentStats.possession_time - oldRoundStats.possession_time, MINCLAMP, int.MaxValue);
			set => currentStats.possession_time = value;
		}

		public float PlayTime { get; set; }
		public float InvertedTime { get; set; }

		public int Points
		{
			get => Math.Clamp(cachedStats.points + currentStats.points - oldRoundStats.points, MINCLAMP, int.MaxValue);
			set => currentStats.points = value;
		}

		public int ShotsTaken
		{
			get => Math.Clamp(cachedStats.shots_taken + currentStats.shots_taken - oldRoundStats.shots_taken, MINCLAMP, int.MaxValue);
			set => currentStats.shots_taken = value;
		}

		public int Saves
		{
			get => Math.Clamp(cachedStats.saves + currentStats.saves - oldRoundStats.saves, MINCLAMP, int.MaxValue);
			set => currentStats.saves = value;
		}

		public int GoalsNum { get; set; }
		public int TwoPointers { get; set; }
		public int ThreePointers { get; set; }

		public int Passes { get; set; }

		public int Catches { get; set; }

		public int Steals
		{
			get => Math.Clamp(cachedStats.steals + currentStats.steals - oldRoundStats.steals, MINCLAMP, int.MaxValue);
			set => currentStats.steals = value;
		}

		public int Stuns
		{
			get => Math.Clamp(cachedStats.stuns + currentStats.stuns - oldRoundStats.stuns, MINCLAMP, int.MaxValue);
			set => currentStats.stuns = value;
		}

		public int Blocks
		{
			get => Math.Clamp(cachedStats.blocks + currentStats.blocks - oldRoundStats.blocks, MINCLAMP, int.MaxValue);
			set => currentStats.blocks = value;
		}

		public int Interceptions
		{
			get => Math.Clamp(cachedStats.interceptions + currentStats.interceptions - oldRoundStats.interceptions, MINCLAMP, int.MaxValue);
			set => currentStats.interceptions = value;
		}

		public int Assists
		{
			get => Math.Clamp(cachedStats.assists + currentStats.assists - oldRoundStats.assists, MINCLAMP, int.MaxValue);
			set => currentStats.assists = value;
		}

		public int CombatKills
		{
			get => Math.Clamp(cachedCombatStats.kills + currentCombatStats.kills - oldRoundCombatStats.kills, MINCLAMP, int.MaxValue);
			set => currentCombatStats.kills = value;
		}

		public int CombatAssists
		{
			get => Math.Clamp(cachedCombatStats.assists + currentCombatStats.assists - oldRoundCombatStats.assists, MINCLAMP, int.MaxValue);
			set => currentCombatStats.assists = value;
		}

		public int CombatDeaths
		{
			get => Math.Clamp(cachedCombatStats.deaths + currentCombatStats.deaths - oldRoundCombatStats.deaths, MINCLAMP, int.MaxValue);
			set => currentCombatStats.deaths = value;
		}

		public float CombatDamage
		{
			get => Math.Max(MINCLAMP, cachedCombatStats.damage + currentCombatStats.damage - oldRoundCombatStats.damage);
			set => currentCombatStats.damage = value;
		}

		public float CombatDamageTaken
		{
			get => Math.Max(MINCLAMP, cachedCombatStats.damage_taken + currentCombatStats.damage_taken - oldRoundCombatStats.damage_taken);
			set => currentCombatStats.damage_taken = value;
		}

		public float CombatDamageTakenRaw
		{
			get => Math.Max(MINCLAMP, cachedCombatStats.damage_taken_raw + currentCombatStats.damage_taken_raw - oldRoundCombatStats.damage_taken_raw);
			set => currentCombatStats.damage_taken_raw = value;
		}

		public int CombatEliminations
		{
			get => Math.Clamp(cachedCombatStats.eliminations + currentCombatStats.eliminations - oldRoundCombatStats.eliminations, MINCLAMP, int.MaxValue);
			set => currentCombatStats.eliminations = value;
		}

		public int CombatObjectiveEliminations
		{
			get => Math.Clamp(cachedCombatStats.objective_eliminations + currentCombatStats.objective_eliminations - oldRoundCombatStats.objective_eliminations, MINCLAMP, int.MaxValue);
			set => currentCombatStats.objective_eliminations = value;
		}

		public float CombatObjectiveTime
		{
			get => Math.Max(MINCLAMP, cachedCombatStats.objective_time + currentCombatStats.objective_time - oldRoundCombatStats.objective_time);
			set => currentCombatStats.objective_time = value;
		}

		public float CombatObjectiveDamage
		{
			get => Math.Max(MINCLAMP, cachedCombatStats.objective_damage + currentCombatStats.objective_damage - oldRoundCombatStats.objective_damage);
			set => currentCombatStats.objective_damage = value;
		}

		public int CombatHillCaptures
		{
			get => Math.Clamp(cachedCombatStats.hill_captures + currentCombatStats.hill_captures - oldRoundCombatStats.hill_captures, MINCLAMP, int.MaxValue);
			set => currentCombatStats.hill_captures = value;
		}

		public int CombatHillDefends
		{
			get => Math.Clamp(cachedCombatStats.hill_defends + currentCombatStats.hill_defends - oldRoundCombatStats.hill_defends, MINCLAMP, int.MaxValue);
			set => currentCombatStats.hill_defends = value;
		}

		public float DistanceBetweenHands
		{
			get
			{
				distanceBetweenHands.Sort();
				if (distanceBetweenHands.Count > 100)
				{
					return distanceBetweenHands[(int)((distanceBetweenHands.Count - 1) * (99f / 100))];
				}

				if (distanceBetweenHands.Count > 0)
				{
					return distanceBetweenHands.Last();
				}

				return 0;
			}
		}

		public int Won { get; set; }
		public int Turnovers { get; set; }
		public AccumulatedFrame matchData;

		/// <summary>
		/// The location of the playspace within the arena. This is not the position of the player within the playspace
		/// </summary>
		public Vector3 playspaceLocation;

		public DateTime lastAbuse = DateTime.UtcNow;
		public TimeSpan playspaceInvincibility = TimeSpan.Zero;
		public int PlayspaceAbuses { get; set; }

		// head, lhand, rhand
		public float[] averageSpeed = { 0, 0, 0 };

		public List<float> distanceBetweenHands = new List<float>();

		// three values for head, lhand, rhand
		float[] avgSpeedTotal = { 0, 0, 0 };
		int[] avgSpeedCount = { 0, 0, 0 };

		public void UpdateAverageSpeed(float newSpeed)
		{
			avgSpeedTotal[0] += newSpeed;
			averageSpeed[0] = (avgSpeedTotal[0]) / ++(avgSpeedCount[0]);
		}

		public void UpdateAverageSpeedLHand(float newSpeed)
		{
			avgSpeedTotal[1] += newSpeed;
			averageSpeed[1] = (avgSpeedTotal[1]) / ++(avgSpeedCount[1]);
		}

		public void UpdateAverageSpeedRHand(float newSpeed)
		{
			avgSpeedTotal[2] += newSpeed;
			averageSpeed[2] = (avgSpeedTotal[2]) / ++(avgSpeedCount[2]);
		}

		public bool exitedTube;

		public static float boostVelCutoff = 20;
		public static float boostVelStopCutoff = 10;
		public List<float> recentVelocities = new List<float>();

		public void AddRecentVelocity(float vel)
		{
			recentVelocities.Add(vel);
			// anything older than 10s
			while (recentVelocities.Count > (1000f / Program.MeasuredIntervalMs) * 10)
			{
				recentVelocities.RemoveAt(0);
			}
		}

		public (float, float) GetMaxRecentVelocity(bool reset = false)
		{
			int index = (int)(.99f * (recentVelocities.Count - 1));
			List<float> sortedVelocities = new List<float>(recentVelocities);
			sortedVelocities.Sort();
			float maxVel = sortedVelocities[index];
			int maxVelIndex = recentVelocities.IndexOf(maxVel);
			if (maxVel < boostVelCutoff)
			{
			}

			if (reset)
			{
				recentVelocities.Clear();
			}

			return (maxVel, (recentVelocities.Count - maxVelIndex) * (Program.MeasuredIntervalMs / 1000f));
		}

		public float GetSmoothedVelocity(float smoothTime = 1)
		{
			int N = (int)(smoothTime * (1000f / Program.MeasuredIntervalMs));
			if (N > recentVelocities.Count - 1)
			{
				return recentVelocities.Average();
			}

			return recentVelocities.Skip(recentVelocities.Count - N).Take(N - 1).Average();
		}

		public bool boosting = false;

		#endregion

		/// <summary>
		/// Store players current stats in case we lose them from a crash.
		/// </summary>
		/// <param name="newPlayerStats"></param>
		public void CacheStats(Stats newPlayerStats)
		{
			if (newPlayerStats.Sum() != 0)
			{
				Logger.LogRow(Logger.LogType.Error, $"Would have cached, but new stats weren't 0: {Name}\n{newPlayerStats}");
				return;
			}

			cachedStats += currentStats;
			cachedCombatStats += currentCombatStats;
		}

		/// <summary>
		/// Store players stats from last round for later use in determining stats on a per round basis.
		/// </summary>
		/// <param name="lastPlayer"></param>
		public void StoreLastRoundStats(MatchPlayer lastPlayer)
		{
			// TODO this looks wrong
			oldRoundStats = lastPlayer.oldRoundStats;
			oldRoundStats = cachedStats + currentStats;
			
			oldRoundCombatStats = lastPlayer.oldRoundCombatStats;
			oldRoundCombatStats = cachedCombatStats + currentCombatStats;
		}

		public static MatchPlayer operator +(MatchPlayer a, MatchPlayer b)
		{
			return new MatchPlayer
			{
				Id = a.Id,
				Name = a.Name,
				Level = b.Level,
				Number = a.Number,
				TeamColor = a.TeamColor,
				currentStats = a.currentStats + b.currentStats,
				cachedStats = a.cachedStats + b.cachedStats,
				oldRoundStats = a.oldRoundStats + b.oldRoundStats,
				currentCombatStats = a.currentCombatStats + b.currentCombatStats,
				cachedCombatStats = a.cachedCombatStats + b.cachedCombatStats,
				oldRoundCombatStats = a.oldRoundCombatStats + b.oldRoundCombatStats,
				PlayTime = a.PlayTime + b.PlayTime,
				InvertedTime = a.InvertedTime + b.InvertedTime,
				GoalsNum = a.GoalsNum + b.GoalsNum,
				TwoPointers = a.TwoPointers + b.TwoPointers,
				ThreePointers = a.ThreePointers + b.ThreePointers,
				Passes = a.Passes + b.Passes,
				Catches = a.Catches + b.Catches,
				Won = a.Won + b.Won,
				Turnovers = a.Turnovers + b.Turnovers,
				matchData = a.matchData,
				playspaceLocation = b.playspaceLocation,
			};
		}

		public void Accumulate(Frame frame, Player player, Frame lastFrame)
		{
			Team.TeamColor winningTeam = frame.blue_points > frame.orange_points ? Team.TeamColor.blue : Team.TeamColor.orange;
			Won = TeamColor == winningTeam ? 1 : 0;

			TeamColor = player.team_color;
			
			// TODO stuff like PlayTime
			// float deltaTime = (float)(frame.recorded_time - lastFrame.recorded_time).TotalSeconds;
			// if (frame.game_status == "playing")
			// {
			// 	PlayTime += deltaTime;
			// }

			currentStats = player.stats;
			
			currentCombatStats = CombatDataParser.GetCombatStats(player.userid);
		}
	}
}