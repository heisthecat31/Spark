﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EchoVRAPI;

namespace Spark
{
	/// <summary>
	/// Object containing a teams basic data and MatchPlayer for the corresponding team.
	/// </summary>
	public class TeamData
	{
		/// <summary>The league team this side belongs to: EML in Arena, ECGL in Combat.</summary>
		public LeagueTeam leagueTeam = LeagueTeam.None;

		public string leagueTeamName => leagueTeam.name;
		public string leagueTeamLogo => leagueTeam.logo;
		public string leagueTeamUrl => leagueTeam.url;

		/// <summary>Bumped on every lookup so a late answer for an older roster is dropped.</summary>
		private int lookupId;

		/// <summary>
		/// Looks this side's players up on the league rosters (EML for Arena, ECGL for Combat). At
		/// least two players from the same team make it that team; otherwise the name is cleared.
		/// </summary>
		public void FindTeamNamesFromPlayerList(Team team)
		{
			if (team == null) return;
			List<string> names = team.players.Select(p => p.name).Where(n => !string.IsNullOrEmpty(n)).ToList();
			bool combat = Program.lastFrame?.InCombat ?? false;
			int id = ++lookupId;

			if (names.Count < 2)
			{
				leagueTeam = LeagueTeam.None;
				return;
			}

			Task.Run(async () =>
			{
				try
				{
					LeagueTeam found = await Leagues.FindTeam(names, combat);
					if (id == lookupId) leagueTeam = found;
				}
				catch (Exception ex)
				{
					Logger.LogRow(Logger.LogType.Error, $"Can't look up league team: {ex}");
				}
			});
		}
	}
}
