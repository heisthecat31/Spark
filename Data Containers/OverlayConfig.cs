﻿using System;
using System.Collections.Generic;
using System.Linq;
using EchoVRAPI;

namespace Spark
{
	public class OverlayConfig
	{
		public static Dictionary<string, object> ToDict()
		{
			List<AccumulatedFrame> previousRounds = OverlayServer.GetPreviousRounds();
			return new Dictionary<string, object>()
			{
				{
					"visibility", new Dictionary<string, bool>
					{
						{ "minimap", SparkSettings.instance.configurableOverlaySettings.minimap },
						{ "compact_minimap", SparkSettings.instance.configurableOverlaySettings.compact_minimap },
						{ "player_rosters", SparkSettings.instance.configurableOverlaySettings.player_rosters },
						{ "main_banner", SparkSettings.instance.configurableOverlaySettings.main_banner },
						{ "neutral_jousts", SparkSettings.instance.configurableOverlaySettings.neutral_jousts },
						{ "defensive_jousts", SparkSettings.instance.configurableOverlaySettings.defensive_jousts },
						{ "event_log", SparkSettings.instance.configurableOverlaySettings.event_log },
						{ "playspace", SparkSettings.instance.configurableOverlaySettings.playspace },
						{ "player_speed", SparkSettings.instance.configurableOverlaySettings.player_speed },
						{ "disc_speed", SparkSettings.instance.configurableOverlaySettings.disc_speed },
						{ "show_team_logos", SparkSettings.instance.configurableOverlaySettings.show_team_logos },
						{ "show_team_names", SparkSettings.instance.configurableOverlaySettings.show_team_names },
					}
				},
				{ "caster_prefs", SparkSettings.instance.casterPrefs },
				{
					"round_scores", new Dictionary<string, object>
					{
						{ "manual_round_scores", SparkSettings.instance.overlaysRoundScoresManual },
						{ "round_count", SparkSettings.instance.overlaysRoundScoresManual ? SparkSettings.instance.overlaysManualRoundCount : Program.CurrentRound.frame.total_round_count },
						{ "round_scores_orange", SparkSettings.instance.overlaysRoundScoresManual ? SparkSettings.instance.overlaysManualRoundScoresOrange : previousRounds?.Select(m => m.frame.orange_points).ToArray() ?? Array.Empty<float>() },
						{ "round_scores_blue", SparkSettings.instance.overlaysRoundScoresManual ? SparkSettings.instance.overlaysManualRoundScoresBlue : previousRounds?.Select(m => m.frame.blue_points).ToArray() ?? Array.Empty<float>() },
					}
				},
				{ "team_names_source", SparkSettings.instance.overlaysTeamSource },
				{ "auto_eml_teams", SparkSettings.instance.overlaysAutoEmlTeams },
				{
					"teams", new[]
					{
						TeamDict(Program.CurrentRound, Team.TeamColor.blue),
						TeamDict(Program.CurrentRound, Team.TeamColor.orange),
					}
				}
			};
		}

		/// <summary>
		/// One side's names for the overlays: the league team found from the rosters (EML for Arena,
		/// ECGL for Combat) and the name/logo the overlays should show.
		/// </summary>
		public static Dictionary<string, object> TeamDict(AccumulatedFrame round, Team.TeamColor color)
		{
			LeagueTeam league = round?.teams[color]?.leagueTeam ?? LeagueTeam.None;
			(string name, string logo) = GetOverlayTeam(round, color);
			return new Dictionary<string, object>
			{
				{ "eml_team_name", league.name ?? "" },
				{ "eml_team_logo", league.logo ?? "" },
				{ "eml_team_url", league.url ?? "" },
				{ "team_name", name ?? "" },
				{ "team_logo", logo ?? "" },
			};
		}

		/// <summary>
		/// The name and logo the overlays show for one side: typed in (source 0) or found on the league
		/// rosters (source 1). With <see cref="SparkSettings.overlaysAutoEmlTeams"/>, a side made up
		/// entirely of one league team shows that team even when the names are typed in.
		/// </summary>
		public static (string name, string logo) GetOverlayTeam(AccumulatedFrame round, Team.TeamColor color)
		{
			if (color == Team.TeamColor.spectator)
			{
				Logger.LogRow(Logger.LogType.Error, "Can't get team name from spectator team");
				return ("", "");
			}

			LeagueTeam league = round?.teams[color]?.leagueTeam ?? LeagueTeam.None;
			if (SparkSettings.instance.overlaysAutoEmlTeams && league.full && !league.IsEmpty)
			{
				return (league.name, league.logo);
			}

			bool blue = color == Team.TeamColor.blue;
			return SparkSettings.instance.overlaysTeamSource switch
			{
				0 => blue
					? (SparkSettings.instance.overlaysManualTeamNameBlue, SparkSettings.instance.overlaysManualTeamLogoBlue)
					: (SparkSettings.instance.overlaysManualTeamNameOrange, SparkSettings.instance.overlaysManualTeamLogoOrange),
				1 => (league.name, league.logo),
				_ => ("", ""),
			};
		}

		public static string GetOverlayTeamName(Team.TeamColor team) => GetOverlayTeam(Program.CurrentRound, team).name;

		public static string GetOverlayTeamLogo(Team.TeamColor team) => GetOverlayTeam(Program.CurrentRound, team).logo;
	}
}
