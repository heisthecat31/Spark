using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Spark
{
	/// <summary>
	/// The league team a side of the arena belongs to, found from its player list: EML for Arena,
	/// ECGL for Combat. Empty when no team has two players there.
	/// </summary>
	public class LeagueTeam
	{
		public string name = "";
		public string logo = "";

		/// <summary>The team's page on the league site.</summary>
		public string url = "";

		/// <summary>Every player on that side is on this team's roster.</summary>
		public bool full;

		public static readonly LeagueTeam None = new LeagueTeam();

		public bool IsEmpty => string.IsNullOrEmpty(name);
	}

	/// <summary>
	/// Echo Master League teams, which replace the old team lookup (that league has shut down).
	///
	/// Rosters come from the EML site (https://coleecvr.github.io/eml-site-preview/mvp.html), which
	/// publishes a copy of the league sheets as <c>data/rosters.csv</c> and the registered players as
	/// <c>data/players.csv</c>. Logos are the ones the site lists in <c>assets/js/data.js</c>
	/// (<c>TEAM_LOGOS</c>). A side counts as an EML team when at least two of its players are on that
	/// team's roster.
	/// </summary>
	public class EmlDirectory
	{
		public const string Hub = "https://coleecvr.github.io/eml-site-preview";
		private static readonly TimeSpan Refresh = TimeSpan.FromMinutes(30);

		public class Roster
		{
			public string team;
			public List<string> players;
		}

		private List<Roster> rosters = new List<Roster>();

		/// <summary>lowercase team name -> logo URL</summary>
		private Dictionary<string, string> logos = new Dictionary<string, string>();

		/// <summary>lowercase registered player name -> name as registered</summary>
		private Dictionary<string, string> registered = new Dictionary<string, string>();

		private DateTime? fetched;

		public IReadOnlyList<Roster> Rosters => rosters;

		/// <summary>A team's page on the EML site (empty for no team).</summary>
		public static string TeamUrl(string team) => string.IsNullOrEmpty(team) ? "" : $"{Hub}/team.html?team={Uri.EscapeDataString(team)}";

		/// <summary>A player's page on the EML site.</summary>
		public static string PlayerUrl(string name) => string.IsNullOrEmpty(name) ? "" : $"{Hub}/player.html?name={Uri.EscapeDataString(name)}";

		public bool IsStale => fetched == null || DateTime.UtcNow - fetched.Value > Refresh;

		/// <summary>Splits one CSV line (quoted fields may hold commas and doubled quotes).</summary>
		private static List<string> CsvLine(string line)
		{
			List<string> output = new List<string>();
			StringBuilder cur = new StringBuilder();
			bool quoted = false;
			for (int i = 0; i < line.Length; i++)
			{
				char c = line[i];
				if (c == '"' && quoted && i + 1 < line.Length && line[i + 1] == '"')
				{
					cur.Append('"');
					i++;
				}
				else if (c == '"') quoted = !quoted;
				else if (c == ',' && !quoted)
				{
					output.Add(cur.ToString());
					cur.Clear();
				}
				else cur.Append(c);
			}

			output.Add(cur.ToString());
			return output;
		}

		private static IEnumerable<string> Lines(string text) => text.Replace("\r\n", "\n").Split('\n').Where(l => l.Length > 0);

		/// <summary><c>rosters.csv</c>: Team, Captain, Co-Captain, Player x4, Status. Inactive teams are skipped.</summary>
		public static List<Roster> ParseRosters(string csv)
		{
			List<string> lines = Lines(csv).ToList();
			if (lines.Count == 0) return new List<Roster>();
			List<string> head = CsvLine(lines[0]);
			int status = head.FindIndex(h => h.Trim().Equals("status", StringComparison.OrdinalIgnoreCase));

			return lines.Skip(1)
				.Select(CsvLine)
				.Where(row => row.Count > 0 && !string.IsNullOrWhiteSpace(row[0]))
				.Where(row => status < 0 || status >= row.Count || !row[status].Trim().Equals("inactive", StringComparison.OrdinalIgnoreCase))
				.Select(row =>
				{
					int end = Math.Min(status < 0 ? row.Count : status, row.Count);
					return new Roster
					{
						team = row[0].Trim(),
						players = row.Skip(1).Take(Math.Max(0, end - 1))
							.Select(p =>
							{
								p = p.Trim();
								if (p.StartsWith("(CC)")) p = p[4..];
								return p.Trim();
							})
							.Where(p => p.Length > 0)
							.ToList()
					};
				})
				.ToList();
		}

		/// <summary><c>players.csv</c>: Player Name, Region, Cooldown.</summary>
		public static Dictionary<string, string> ParsePlayers(string csv)
		{
			Dictionary<string, string> output = new Dictionary<string, string>();
			foreach (string line in Lines(csv).Skip(1))
			{
				string n = CsvLine(line)[0].Trim();
				if (n.Length > 0) output[n.ToLowerInvariant()] = n;
			}

			return output;
		}

		/// <summary><c>TEAM_LOGOS</c> entries in the site's <c>data.js</c>: <c>'team name': ['file.webp', ...]</c>.</summary>
		public static Dictionary<string, string> ParseLogos(string js)
		{
			Dictionary<string, string> output = new Dictionary<string, string>();
			// the assignment, not a mention of the name in a comment
			System.Text.RegularExpressions.Match decl = System.Text.RegularExpressions.Regex.Match(js, @"TEAM_LOGOS\s*=\s*\{");
			if (!decl.Success) return output;
			string body = js[decl.Index..];
			int end = body.IndexOf("};", StringComparison.Ordinal);
			if (end >= 0) body = body[..end];
			foreach (string line in body.Split('\n'))
			{
				// ["    ", "name", ": [", "file.webp", ...]
				string[] parts = line.Split('\'');
				if (parts.Length >= 4 && parts[2].TrimStart().StartsWith(":") && parts[3].EndsWith(".webp"))
				{
					output[parts[1].ToLowerInvariant()] = $"{Hub}/assets/logos/{parts[3]}";
				}
			}

			return output;
		}

		/// <summary>For tests and previews.</summary>
		public static EmlDirectory FromParts(List<Roster> rosters, Dictionary<string, string> logos, Dictionary<string, string> registered = null)
		{
			return new EmlDirectory { rosters = rosters, logos = logos, registered = registered ?? new Dictionary<string, string>(), fetched = DateTime.UtcNow };
		}

		/// <summary>The player's page, when they're registered with EML (or on a roster).</summary>
		public string PlayerPage(string name)
		{
			if (string.IsNullOrEmpty(name)) return null;
			string key = name.ToLowerInvariant();
			if (!registered.TryGetValue(key, out string found))
			{
				found = rosters.SelectMany(r => r.players).FirstOrDefault(p => p.ToLowerInvariant() == key);
			}

			return found == null ? null : PlayerUrl(found);
		}

		/// <summary>Download the rosters and logos again. Keeps the old copy if a download fails.</summary>
		public async Task RefreshAsync(HttpClient http)
		{
			// try again in 30 minutes even if this fails
			fetched = DateTime.UtcNow;
			string csv = await http.GetStringAsync($"{Hub}/data/rosters.csv");
			List<Roster> newRosters = ParseRosters(csv);
			if (newRosters.Count == 0) throw new Exception("no teams in rosters.csv");
			rosters = newRosters;

			try
			{
				Dictionary<string, string> names = ParsePlayers(await http.GetStringAsync($"{Hub}/data/players.csv"));
				if (names.Count > 0) registered = names;
			}
			catch (Exception e)
			{
				Logger.LogRow(Logger.LogType.Error, $"EML players: {e.Message}");
			}

			try
			{
				Dictionary<string, string> newLogos = ParseLogos(await http.GetStringAsync($"{Hub}/assets/js/data.js"));
				if (newLogos.Count > 0) logos = newLogos;
			}
			catch (Exception e)
			{
				Logger.LogRow(Logger.LogType.Error, $"EML logos: {e.Message}");
			}
		}

		/// <summary>The EML team at least two of <paramref name="players"/> are on (the one with the most), if any.</summary>
		public LeagueTeam FindTeam(IReadOnlyCollection<string> players)
		{
			HashSet<string> lower = players.Select(p => p.ToLowerInvariant()).ToHashSet();
			var best = rosters
				.Select(r => (r, n: r.players.Count(p => lower.Contains(p.ToLowerInvariant()))))
				.Where(x => x.n >= 2)
				.OrderByDescending(x => x.n)
				.FirstOrDefault();
			if (best.r == null) return LeagueTeam.None;
			return new LeagueTeam
			{
				name = best.r.team,
				logo = logos.GetValueOrDefault(best.r.team.ToLowerInvariant(), ""),
				url = TeamUrl(best.r.team),
				full = best.n == players.Count,
			};
		}

		/// <summary>Every active team with its logo, page and roster, for the overlays' match setup.</summary>
		public List<Dictionary<string, object>> TeamList()
		{
			return rosters.Select(r => new Dictionary<string, object>
			{
				{ "team_name", r.team },
				{ "team_logo", logos.GetValueOrDefault(r.team.ToLowerInvariant(), "") },
				{ "team_url", TeamUrl(r.team) },
				{ "players", r.players },
			}).ToList();
		}

		/// <summary>The players on an EML team's roster (case-insensitive name), or null when there's no such team.</summary>
		public List<string> TeamPlayers(string team)
		{
			return rosters.FirstOrDefault(r => r.team.Equals(team, StringComparison.OrdinalIgnoreCase))?.players;
		}
	}

	/// <summary>
	/// Echo Combat George League (https://ecgleague.com): team names and player pages for Echo Combat
	/// matches, the way <see cref="EmlDirectory"/> does it for Arena. The site has two leagues
	/// (Varsity, <c>main</c>, and Rookie) and a public JSON API: <c>/api/teams</c> lists every team,
	/// <c>/api/team/&lt;id&gt;</c> has its roster and <c>/api/players</c> everyone registered.
	///
	/// ECGL knows players by their Discord names, so a player in the game matches when their game name
	/// equals the Discord username or display name, ignoring case and anything the display name adds
	/// after a space and a bracket ("Swingly (3) [18.38%]" is "Swingly").
	/// </summary>
	public class EcglDirectory
	{
		public const string Site = "https://ecgleague.com";
		private static readonly TimeSpan Refresh = TimeSpan.FromMinutes(30);

		/// <summary>Varsity (<c>main</c>) and Rookie.</summary>
		private static readonly string[] LeagueNames = { "main", "rookie" };

		public class Member
		{
			public string id;

			/// <summary>Lowercase names this player may be known by in the game.</summary>
			public List<string> names;
		}

		public class Team
		{
			public long id;
			public string name;
			public string logo;

			/// <summary><c>main</c> (Varsity) or <c>rookie</c>.</summary>
			public string league;

			public List<Member> roster;
		}

		private List<Team> teams = new List<Team>();
		private List<Member> players = new List<Member>();
		private DateTime? fetched;

		public bool IsStale => fetched == null || DateTime.UtcNow - fetched.Value > Refresh;

		/// <summary>The names a registered player may use in the game.</summary>
		public static List<string> Names(JToken v)
		{
			List<string> output = new List<string>();
			foreach (string key in new[] { "username", "display_name" })
			{
				string n = v[key]?.Type == JTokenType.String ? v[key].ToString().Trim().ToLowerInvariant() : null;
				if (string.IsNullOrEmpty(n)) continue;
				// "Swingly (3) [18.38%]" -> "swingly"
				int i = n.IndexOf(' ');
				if (i >= 0)
				{
					string rest = n[i..].TrimStart();
					if (rest.StartsWith("(") || rest.StartsWith("[")) output.Add(n[..i]);
				}

				output.Add(n);
			}

			// like Vec::dedup: drop consecutive duplicates
			return output.Where((n, i) => i == 0 || output[i - 1] != n).ToList();
		}

		private static Member ToMember(JToken v)
		{
			if (v?["id"]?.Type != JTokenType.String) return null;
			return new Member { id = v["id"].ToString(), names = Names(v) };
		}

		public static string TeamUrl(long id, string league) => league == "rookie" ? $"{Site}/teams/{id}?league=rookie" : $"{Site}/teams/{id}";

		public static string PlayerUrl(string id) => $"{Site}/players/{id}";

		/// <summary>Logos can be paths on the site (<c>/api/team/logo/722/...</c>).</summary>
		private static string Absolute(string url) => url.StartsWith("/") ? Site + url : url;

		/// <summary><c>/api/team/&lt;id&gt;</c>'s current roster.</summary>
		public static List<Member> ParseRoster(JToken team) =>
			(team?["roster"] as JArray ?? new JArray()).Select(ToMember).Where(m => m != null).ToList();

		/// <summary><c>/api/players</c>.</summary>
		public static List<Member> ParsePlayers(JToken list) =>
			(list as JArray ?? new JArray()).Select(ToMember).Where(m => m != null).ToList();

		/// <summary>For tests and previews.</summary>
		public static EcglDirectory FromParts(List<Team> teams, List<Member> players)
		{
			return new EcglDirectory { teams = teams, players = players, fetched = DateTime.UtcNow };
		}

		private static async Task<JToken> GetJson(HttpClient http, string path, string league)
		{
			using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, Site + path);
			req.Headers.Add("X-ECGL-League", league);
			using HttpResponseMessage resp = await http.SendAsync(req);
			resp.EnsureSuccessStatusCode();
			return JToken.Parse(await resp.Content.ReadAsStringAsync());
		}

		/// <summary>
		/// Download the active teams of both leagues and their rosters, and everyone registered. The
		/// site picks the league from the <c>X-ECGL-League</c> header. Keeps the old copy when no team
		/// list can be read.
		/// </summary>
		public async Task RefreshAsync(HttpClient http)
		{
			fetched = DateTime.UtcNow;
			List<Team> newTeams = new List<Team>();
			List<Member> newPlayers = new List<Member>();
			foreach (string league in LeagueNames)
			{
				JToken list;
				try
				{
					list = await GetJson(http, "/api/teams", league);
				}
				catch
				{
					continue;
				}

				foreach (JToken t in list as JArray ?? new JArray())
				{
					if (!string.Equals(t["status"]?.ToString(), "active", StringComparison.OrdinalIgnoreCase)) continue;
					if (t["id"]?.Type != JTokenType.Integer) continue;
					long id = t["id"].Value<long>();
					List<Member> roster;
					try
					{
						roster = ParseRoster(await GetJson(http, $"/api/team/{id}", league));
					}
					catch
					{
						roster = new List<Member>();
					}

					newTeams.Add(new Team
					{
						id = id,
						name = t["name"]?.ToString() ?? "",
						logo = Absolute(t["logo_url"]?.Type == JTokenType.String ? t["logo_url"].ToString() : ""),
						league = league,
						roster = roster,
					});
				}

				try
				{
					foreach (Member m in ParsePlayers(await GetJson(http, "/api/players", league)))
					{
						if (newPlayers.All(p => p.id != m.id)) newPlayers.Add(m);
					}
				}
				catch
				{
					// keep going without the player list
				}
			}

			if (newTeams.Count == 0) throw new Exception("no active ECGL teams");
			teams = newTeams;
			if (newPlayers.Count > 0) players = newPlayers;
		}

		/// <summary>The ECGL team at least two of <paramref name="playerNames"/> are on (the one with the most), if any.</summary>
		public LeagueTeam FindTeam(IReadOnlyCollection<string> playerNames)
		{
			List<string> lower = playerNames.Select(p => p.ToLowerInvariant()).ToList();
			var best = teams
				.Select(t => (t, n: lower.Count(p => t.roster.Any(m => m.names.Contains(p)))))
				.Where(x => x.n >= 2)
				.OrderByDescending(x => x.n)
				.FirstOrDefault();
			if (best.t == null) return LeagueTeam.None;
			return new LeagueTeam
			{
				name = best.t.name,
				logo = best.t.logo,
				url = TeamUrl(best.t.id, best.t.league),
				full = best.n == playerNames.Count,
			};
		}

		/// <summary>The player's ECGL page, when they're registered there.</summary>
		public string PlayerPage(string name)
		{
			if (string.IsNullOrEmpty(name)) return null;
			string key = name.ToLowerInvariant();
			Member m = players.Concat(teams.SelectMany(t => t.roster)).FirstOrDefault(m => m.names.Contains(key));
			return m == null ? null : PlayerUrl(m.id);
		}
	}

	/// <summary>
	/// The shared EML (Arena) and ECGL (Combat) directories, refreshed every 30 minutes on use.
	/// </summary>
	public static class Leagues
	{
		private static readonly HttpClient http = CreateClient();
		private static readonly SemaphoreSlim emlLock = new SemaphoreSlim(1, 1);
		private static readonly SemaphoreSlim ecglLock = new SemaphoreSlim(1, 1);

		public static EmlDirectory Eml { get; private set; } = new EmlDirectory();
		public static EcglDirectory Ecgl { get; private set; } = new EcglDirectory();

		private static HttpClient CreateClient()
		{
			HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
			client.DefaultRequestHeaders.Add("User-Agent", "Spark/" + Program.AppVersionString());
			return client;
		}

		public static async Task<EmlDirectory> GetEml()
		{
			await emlLock.WaitAsync();
			try
			{
				if (Eml.IsStale)
				{
					try
					{
						await Eml.RefreshAsync(http);
					}
					catch (Exception e)
					{
						Logger.LogRow(Logger.LogType.Error, $"EML rosters: {e.Message}");
					}
				}

				return Eml;
			}
			finally
			{
				emlLock.Release();
			}
		}

		public static async Task<EcglDirectory> GetEcgl()
		{
			await ecglLock.WaitAsync();
			try
			{
				if (Ecgl.IsStale)
				{
					try
					{
						await Ecgl.RefreshAsync(http);
					}
					catch (Exception e)
					{
						Logger.LogRow(Logger.LogType.Error, $"ECGL teams: {e.Message}");
					}
				}

				return Ecgl;
			}
			finally
			{
				ecglLock.Release();
			}
		}

		/// <summary>The league team for one side: EML in Arena, ECGL in Combat.</summary>
		public static async Task<LeagueTeam> FindTeam(IReadOnlyCollection<string> players, bool combat)
		{
			return combat ? (await GetEcgl()).FindTeam(players) : (await GetEml()).FindTeam(players);
		}

		/// <summary>A player's league page: EML in Arena, ECGL in Combat. Null when they aren't registered.</summary>
		public static async Task<string> PlayerPage(string name, bool combat)
		{
			return combat ? (await GetEcgl()).PlayerPage(name) : (await GetEml()).PlayerPage(name);
		}
	}
}
