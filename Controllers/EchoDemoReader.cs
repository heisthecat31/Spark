// .echodemo recordings from the EchoReplay game plugin (github.com/heisthecat31/EchoReplay: docs/FORMAT.md,
// src/foundation/demofile, src/capture/streamcodec.*, src/foundation/bitio). Ported from the Rust reader in the
// spark-rust-rewrite branch (crates/spark-core/src/echodemo.rs), field for field, so the two can be diffed.
//
// Layout: a 128-byte header, then CHPT chapters (about 2 s each), each a list of stream blocks. What the analyser
// needs comes from:
//  * the manifest (stream 1): the roster, game type, start time;
//  * S_BODY_SAMPLED (7) / S_BODY_WIRE (6): every body's centre of mass and velocity: players by user slot, the
//    disc as a component;
//  * S_PLAYER_WIRE (4): hand offsets from the centre of mass;
//  * S_TICK (3): one record per engine frame, the clock frames are emitted on;
//  * S_GAMESTATE (19) and S_PLAYER_STATS (20): fixed records: phase, scores, clock, disc owner, and the scoreboard
//    per player;
//  * S_EVENT (9): goals (SR15NetReliablePostScoreUpdate) and stuns (SR15StunPlayerMsg).
//
// The decoder's delta state is kept as quantisation indices (not floats), so a decoded value is never re-quantised.
#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using EchoVRAPI;
using Newtonsoft.Json.Linq;

namespace Spark;

public static class EchoDemoReader
{
	private const int Header = 128;
	private const int ChapterHead = 48;
	private const int BlockHead = 16;

	private const ushort SectionManifest = 0;
	private const ushort SectionCore = 1;
	private const ushort SectionIndex = 5;

	private const ushort SManifest = 1;
	private const ushort STick = 3;
	private const ushort SPlayerWire = 4;
	private const ushort SPlayerSampled = 5;
	private const ushort SBodyWire = 6;
	private const ushort SBodySampled = 7;
	private const ushort SEvent = 9;
	private const ushort SKey = 11;
	private const ushort SGameState = 19;
	private const ushort SPlayerStats = 20;

	private const int MaxEntities = 64;

	private sealed class DemoException : Exception
	{
		public DemoException(string m) : base(m) { }
	}

	// ───────────────────────────── bits ─────────────────────────────

	private sealed class Bits
	{
		private readonly byte[] b;
		private readonly int start;
		private long pos;
		private readonly long end;

		public Bits(byte[] b, int start, int byteLen, long bits)
		{
			this.b = b;
			this.start = start;
			end = Math.Min(bits, (long)byteLen * 8);
		}

		public ulong Take(int n)
		{
			if (n == 0) return 0;
			if (pos + n > end) throw new DemoException("block overrun");
			ulong output = 0;
			int got = 0;
			int left = n;
			while (left > 0)
			{
				ulong by = b[start + (int)(pos >> 3)];
				int off = (int)(pos & 7);
				int take = Math.Min(left, 8 - off);
				output |= ((by >> off) & ((1UL << take) - 1)) << got;
				got += take;
				left -= take;
				pos += take;
			}

			return output;
		}

		public bool Flag() => Take(1) != 0;
	}

	private static long Unzigzag(ulong z) => (long)(z >> 1) ^ -(long)(z & 1);

	private static int BitLength(ulong v) => v == 0 ? 0 : BitOperations.Log2(v) + 1;

	/// <summary>A WIRE_Q field: (max − min) / step steps, quantised to an index.</summary>
	private readonly struct WireQ
	{
		public readonly float Min;
		public readonly float Step;
		public readonly ulong Steps;
		public readonly int BitCount;

		public WireQ(uint min, uint max, uint step)
		{
			Min = BitConverter.UInt32BitsToSingle(min);
			float mx = BitConverter.UInt32BitsToSingle(max);
			Step = BitConverter.UInt32BitsToSingle(step);
			float range = mx - Min;
			float n = range / Step;
			Steps = (ulong)MathF.Ceiling(n);
			BitCount = BitLength(Steps);
		}

		public float Value(ulong index) => (float)(long)index * Step + Min;
	}

	private static readonly WireQ Com = new(0xC37A0000, 0x437A0000, 0x3A83126F);
	private static readonly WireQ Vel = new(0xC2400000, 0x42400000, 0x3D000000);
	private static readonly WireQ Ang = new(0xC3100000, 0x43100000, 0x3F800000);
	private static readonly WireQ Bend = new(0xC0490FDB, 0x40490FDB, 0x3C8EFA35);
	private static readonly WireQ Rest = new(0x00000000, 0x3FE33333, 0x3BA3D70A);
	private static readonly WireQ Hand = new(0xC0000000, 0x40000000, 0x3A83126F);
	private static readonly WireQ Trig = new(0x00000000, 0x3F800000, 0x3DCCCCCD);

	private enum FKind { Raw, U64, Q, F32, Quat10 }

	/// <summary>
	/// How one field is coded. Each slot in a record is a raw u64: an index for WIRE_Q, the bit pattern for F32, the
	/// value for Raw; a quat10 takes four slots (selector, three components).
	/// </summary>
	private readonly struct F
	{
		public readonly FKind Kind;
		public readonly int N;
		public readonly WireQ Q;

		private F(FKind k, int n, WireQ q)
		{
			Kind = k;
			N = n;
			Q = q;
		}

		public static F Raw(int n) => new(FKind.Raw, n, default);
		public static readonly F U64 = new(FKind.U64, 0, default);
		public static F Of(WireQ q) => new(FKind.Q, 0, q);
		public static readonly F F32 = new(FKind.F32, 0, default);
		public static readonly F Quat10 = new(FKind.Quat10, 0, default);

		public int Slots => Kind == FKind.Quat10 ? 4 : 1;
	}

	private static readonly int[] DeltaClass = { 5, 9, 14 };

	private static int DeltaWidth(ulong k, int fieldBits) => k < 3 ? DeltaClass[k] : fieldBits + 1;

	private static void ReadField(Bits r, F f, ulong[] output, int o, ulong[] prev, int p, bool absolute)
	{
		switch (f.Kind)
		{
			case FKind.Raw:
				output[o] = absolute || r.Flag() ? r.Take(f.N) : prev[p];
				break;
			case FKind.U64:
				if (absolute) output[o] = r.Take(64);
				else if (!r.Flag()) output[o] = prev[p];
				else
				{
					int lz = (int)r.Take(6);
					int m = (int)r.Take(6) + 1;
					if (lz + m > 64) throw new DemoException("malformed u64 delta");
					ulong c = r.Take(m);
					output[o] = prev[p] ^ (c << (64 - lz - m));
				}

				break;
			case FKind.Q:
				if (absolute) output[o] = r.Take(f.Q.BitCount);
				else if (!r.Flag()) output[o] = prev[p];
				else
				{
					ulong k = r.Take(2);
					ulong zz = r.Take(DeltaWidth(k, f.Q.BitCount));
					long i = (long)prev[p] + Unzigzag(zz);
					if (i < 0 || (ulong)i > f.Q.Steps) throw new DemoException("index delta out of range");
					output[o] = (ulong)i;
				}

				break;
			case FKind.F32:
				if (absolute) output[o] = r.Take(32);
				else if (!r.Flag()) output[o] = prev[p];
				else
				{
					int lz = (int)r.Take(5);
					int m = (int)r.Take(5) + 1;
					ulong pp = r.Take(m);
					if (lz + m > 32) throw new DemoException("malformed float delta");
					output[o] = prev[p] ^ (pp << (32 - lz - m));
				}

				break;
			case FKind.Quat10:
				if (absolute)
				{
					output[o] = r.Take(2);
					for (int i = 1; i < 4; i++) output[o + i] = r.Take(10);
				}
				else
				{
					for (int i = 0; i < 4; i++) output[o + i] = prev[p + i];
					if (r.Flag())
					{
						if (r.Flag())
						{
							for (int i = 1; i < 4; i++)
							{
								ulong k = r.Take(2);
								ulong zz = r.Take(DeltaWidth(k, 10));
								long v = (long)prev[p + i] + Unzigzag(zz);
								if (v < 0 || v > 1023) throw new DemoException("quat delta out of range");
								output[o + i] = (ulong)v;
							}
						}
						else
						{
							output[o] = r.Take(2);
							for (int i = 1; i < 4; i++) output[o + i] = r.Take(10);
						}
					}
				}

				break;
		}
	}

	// ───────────────────────────── schemas ─────────────────────────────

	/// <summary>Field order of WalkBody, wire or sampled (sampled turns WIRE_Q into F32).</summary>
	private static List<F> BodySchema(bool sampled)
	{
		F Q(WireQ q) => sampled ? F.F32 : F.Of(q);
		var v = new List<F> { F.Raw(16), F.Raw(11), F.U64, F.Raw(32) };
		v.AddRange(Enumerable.Repeat(Q(Com), 3));
		v.AddRange(sampled ? Enumerable.Repeat(F.F32, 4) : new[] { F.Quat10 });
		v.AddRange(Enumerable.Repeat(Q(Vel), 3));
		v.AddRange(Enumerable.Repeat(Q(Ang), 3));
		v.AddRange(Enumerable.Repeat(Q(Com), 3));
		if (!sampled) v.AddRange(new[] { F.U64, F.U64 });
		return v;
	}

	/// <summary>Field order of WalkPlayer, wire or sampled.</summary>
	private static List<F> PlayerSchema(bool sampled)
	{
		F Q(WireQ q) => sampled ? F.F32 : F.Of(q);
		IEnumerable<F> Quat() => sampled ? Enumerable.Repeat(F.F32, 4) : new[] { F.Quat10 };
		var v = new List<F> { F.Raw(16) };
		v.AddRange(Quat());
		v.AddRange(Quat());
		v.AddRange(Quat());
		v.AddRange(Enumerable.Repeat(Q(Vel), 3));
		v.AddRange(new[] { F.Raw(28), F.Raw(16), F.Raw(16), F.Raw(16), F.Raw(16) });
		v.AddRange(new[] { Q(Bend), Q(Bend), Q(Rest) });
		v.AddRange(Enumerable.Repeat(Q(Hand), 3));
		v.AddRange(Enumerable.Repeat(Q(Hand), 3));
		v.AddRange(new[] { Q(Trig), Q(Trig), F.Raw(3) });
		if (!sampled)
		{
			v.AddRange(Enumerable.Repeat(Q(Vel), 3));
			v.Add(F.U64);
		}

		return v;
	}

	/// <summary>Slot positions of the values read out of a decoded record.</summary>
	private sealed class Layout
	{
		public readonly F[] Fields;
		public readonly int[] Starts;
		public readonly int Width;

		public Layout(List<F> fields)
		{
			Fields = fields.ToArray();
			Starts = new int[Fields.Length];
			int w = 0;
			for (int i = 0; i < Fields.Length; i++)
			{
				Starts[i] = w;
				w += Fields[i].Slots;
			}

			Width = w;
		}

		public float Value(int field, ulong[] rec)
		{
			int s = Starts[field];
			return Fields[field].Kind switch
			{
				FKind.Q => Fields[field].Q.Value(rec[s]),
				FKind.F32 => BitConverter.UInt32BitsToSingle((uint)rec[s]),
				_ => rec[s],
			};
		}

		public Vector3 Vec3(int field, ulong[] rec) => new(Value(field, rec), Value(field + 1, rec), Value(field + 2, rec));

		public ulong Raw(int field, ulong[] rec) => rec[Starts[field]];
	}

	private sealed class Rec
	{
		public ushort Entity;
		public ulong Stamp;

		/// <summary>Null for a record stored verbatim (an engine body whose decode wasn't proven).</summary>
		public ulong[]? Values;
	}

	/// <summary>One stream's per-entity delta state (PlayerDecoder / BodyDecoder).</summary>
	private sealed class EntityDecoder
	{
		private readonly Layout layout;
		private readonly ulong[][] prev;
		private readonly ulong[] prevStamp = new ulong[MaxEntities];
		private ushort lastEntity;

		public EntityDecoder(Layout layout)
		{
			this.layout = layout;
			prev = new ulong[MaxEntities][];
			for (int i = 0; i < MaxEntities; i++) prev[i] = new ulong[layout.Width];
		}

		public Rec Decode(Bits r)
		{
			bool verbatim = r.Flag();
			bool same = r.Flag();
			ushort entity = same ? lastEntity : (ushort)r.Take(16);
			bool absolute = r.Flag();
			int slot = entity % MaxEntities;
			ulong stamp = VarKeyed(r, prevStamp[slot], absolute);
			prevStamp[slot] = stamp;
			lastEntity = entity;
			if (verbatim)
			{
				int n = (int)r.Take(16);
				for (int i = 0; i < n; i++) r.Take(8);
				return new Rec { Entity = entity, Stamp = stamp };
			}

			ulong[] p = prev[slot];
			ulong[] cur = absolute ? new ulong[layout.Width] : (ulong[])p.Clone();
			for (int i = 0; i < layout.Fields.Length; i++)
			{
				int s = layout.Starts[i];
				ReadField(r, layout.Fields[i], cur, s, p, s, absolute);
			}

			prev[slot] = cur;
			return new Rec { Entity = entity, Stamp = stamp, Values = cur };
		}
	}

	private static readonly int[] VarClass = { 8, 16, 24, 64 };

	private static ulong VarKeyed(Bits r, ulong prev, bool absolute)
	{
		if (absolute) return r.Take(64);
		ulong k = r.Take(2);
		ulong zz = r.Take(VarClass[k]);
		return unchecked((ulong)((long)prev + Unzigzag(zz)));
	}

	private static ulong U64Keyed(Bits r, ulong prev, bool absolute)
	{
		var output = new ulong[1];
		ReadField(r, F.U64, output, 0, new[] { prev }, 0, absolute);
		return output[0];
	}

	/// <summary>S_TICK (WalkTick): the source time is all that's kept.</summary>
	private sealed class TickDecoder
	{
		private bool started;
		private readonly ulong[] prev = new ulong[4];
		private readonly ulong[] delta = new ulong[4];
		private readonly ulong[] raw = new ulong[3];

		public ulong Decode(Bits r)
		{
			bool absolute = !started;
			var cur = new ulong[4];
			for (int i = 0; i < 4; i++)
			{
				if (absolute) cur[i] = r.Take(64);
				else
				{
					ulong predicted = unchecked(prev[i] + delta[i]);
					cur[i] = r.Flag() ? predicted : VarKeyed(r, predicted, false);
				}
			}

			// frameIndex is a u32 in the record
			cur[3] &= 0xFFFF_FFFF;
			for (int i = 0; i < 3; i++)
			{
				raw[i] = absolute || r.Flag() ? r.Take(32) : raw[i];
			}

			if (started)
			{
				for (int i = 0; i < 4; i++) delta[i] = unchecked(cur[i] - prev[i]);
				delta[3] &= 0xFFFF_FFFF;
			}

			Array.Copy(cur, prev, 4);
			started = true;
			return cur[0];
		}
	}

	// ───────────────────────────── container ─────────────────────────────

	private static ushort U16(byte[] b, int at) => BitConverter.ToUInt16(b, at);
	private static uint U32(byte[] b, int at) => BitConverter.ToUInt32(b, at);
	private static ulong U64(byte[] b, int at) => BitConverter.ToUInt64(b, at);
	private static float F32(byte[] b, int at) => BitConverter.ToSingle(b, at);
	private static int I32(byte[] b, int at) => BitConverter.ToInt32(b, at);

	private readonly struct Chapter
	{
		public readonly ushort Section;
		public readonly int Offset;
		public readonly int Len;
		public readonly bool Zstd;
		public readonly int Uncompressed;

		public Chapter(ushort section, int offset, int len, bool zstd, int uncompressed)
		{
			Section = section;
			Offset = offset;
			Len = len;
			Zstd = zstd;
			Uncompressed = uncompressed;
		}
	}

	/// <summary>The chapters in file order, found by walking the heads (works on a file never closed).</summary>
	private static List<Chapter> Chapters(byte[] b)
	{
		var v = new List<Chapter>();
		long i = Header;
		while (i + ChapterHead <= b.Length && b[i] == 'C' && b[i + 1] == 'H' && b[i + 2] == 'P' && b[i + 3] == 'T')
		{
			int at = (int)i;
			long len = U32(b, at + 8);
			if (i + ChapterHead + len > b.Length) break;
			v.Add(new Chapter(U16(b, at + 4), at + ChapterHead, (int)len, U16(b, at + 6) == 1, (int)U32(b, at + 36)));
			i += ChapterHead + len;
		}

		return v;
	}

	private static byte[] Payload(byte[] b, Chapter c)
	{
		var raw = new byte[c.Len];
		Buffer.BlockCopy(b, c.Offset, raw, 0, c.Len);
		if (!c.Zstd) return raw;
		using var d = new ZstdNet.Decompressor();
		return d.Unwrap(raw, Math.Max(c.Uncompressed, 1) + 64);
	}

	private readonly struct Block
	{
		public readonly ushort Id;
		public readonly uint Stored;
		public readonly byte[] Buf;
		public readonly int Offset;
		public readonly int Len;

		public Block(ushort id, uint stored, byte[] buf, int offset, int len)
		{
			Id = id;
			Stored = stored;
			Buf = buf;
			Offset = offset;
			Len = len;
		}

		public byte[] ToArray()
		{
			var a = new byte[Len];
			Buffer.BlockCopy(Buf, Offset, a, 0, Len);
			return a;
		}
	}

	private static List<Block> Blocks(byte[] p)
	{
		var v = new List<Block>();
		long j = 0;
		while (j + BlockHead <= p.Length)
		{
			int at = (int)j;
			ushort id = U16(p, at);
			long len = U32(p, at + 4);
			uint stored = U32(p, at + 12);
			if (j + BlockHead + len > p.Length) break;
			v.Add(new Block(id, stored, p, at + BlockHead, (int)len));
			j += BlockHead + len;
		}

		return v;
	}

	/// <summary>A bit-packed block: [u32 bit length][records].</summary>
	private static Bits? BitBlock(Block d)
	{
		if (d.Len < 4) return null;
		uint bits = U32(d.Buf, d.Offset);
		return new Bits(d.Buf, d.Offset + 4, d.Len - 4, bits);
	}

	// ───────────────────────────── manifest ─────────────────────────────

	public sealed class RosterRow
	{
		public ulong XPlatformId;
		public string Name = "";
		public ushort UserSlot;
		public ushort UserIndex;

		/// <summary>0 blue, 1 orange, anything else spectator / none</summary>
		public ushort Team;

		public ushort Number;
		public int Level;
	}

	public sealed class Manifest
	{
		public ulong LevelId;
		public ulong GameTypeId;
		public ulong StartEpoch;
		public List<RosterRow> Roster = new();
	}

	private static string CStr(byte[] b, int at, int len)
	{
		int end = at;
		int stop = Math.Min(at + len, b.Length);
		while (end < stop && b[end] != 0) end++;
		return Encoding.UTF8.GetString(b, at, end - at).Trim();
	}

	private static Manifest ParseManifest(Block blk)
	{
		byte[] p = blk.ToArray();
		var m = new Manifest();
		int fix = (int)U32(p, 0);
		if (fix < 32 || 4 + fix > p.Length) throw new DemoException("manifest: fixed block is short");
		m.LevelId = U64(p, 4);
		m.GameTypeId = U64(p, 12);
		m.StartEpoch = U64(p, 20);
		int c = 4 + fix;
		ushort count = U16(p, c);
		c += 2;
		for (int k = 0; k < count; k++)
		{
			int rec = (int)U32(p, c);
			c += 4;
			if (c + rec > p.Length || rec < 254) throw new DemoException("manifest: player record overruns");
			var d = new byte[rec];
			Buffer.BlockCopy(p, c, d, 0, rec);
			int entrantLen = (int)U32(d, 246);
			int unlocksLen = (int)U32(d, 250);
			int ext = Math.Max(0, rec - (254 + entrantLen + unlocksLen));
			var row = new RosterRow
			{
				XPlatformId = U64(d, 0),
				Name = CStr(d, 40, 32),
				UserSlot = U16(d, 72),
				UserIndex = U16(d, 74),
				Team = d[78],
				Number = U16(d, 82),
			};
			if (row.Team == 0xFF) row.Team = 0xFFFF;
			if (ext >= 102)
			{
				string full = CStr(d, 254, 36);
				if (full.Length > 0) row.Name = full;
				// alignment16 is the colour; the team id otherwise
				ushort team16 = U16(d, 254 + 72), align16 = U16(d, 254 + 76);
				row.Team = align16 != 0xFFFF ? align16 : team16;
			}

			int jsonAt = rec - entrantLen - unlocksLen;
			if (jsonAt >= 0 && entrantLen > 0 && jsonAt + entrantLen <= rec)
			{
				try
				{
					JObject v = JObject.Parse(Encoding.UTF8.GetString(d, jsonAt, entrantLen));
					row.Level = (int?)v["level"] ?? 0;
					if (row.Name.Length == 0) row.Name = (string?)v["displayname"] ?? "";
				}
				catch
				{
					// no level or name from the entrant JSON
				}
			}

			m.Roster.Add(row);
			c += rec;
		}

		return m;
	}

	private const ulong EchoArena = 0xCB60A4DE7E1CAF73;
	private const ulong EchoArenaPrivate = 0x09990965F4DB8C03;
	private const ulong EchoCombat = 0x3D5C3976578A321A;
	private const ulong EchoCombatPrivate = 0x33BBF6842DF97A3F;
	private const ulong Social = 0x042D9CF9CFDDCF76;

	/// <summary>(map name, match type, private) for a game type id.</summary>
	public static (string map, string matchType, bool isPrivate) GameType(ulong id) => id switch
	{
		EchoArena => ("mpl_arena_a", "Echo_Arena", false),
		EchoArenaPrivate => ("mpl_arena_a", "Echo_Arena_Private", true),
		EchoCombat => ("mpl_combat", "Echo_Combat", false),
		EchoCombatPrivate => ("mpl_combat", "Echo_Combat_Private", true),
		Social => ("mpl_lobby_b2", "Social_2.0", false),
		_ => ("", "Unknown", false),
	};

	private static string PhaseStatus(byte p) => p switch
	{
		3 or 4 => "pre_match",
		5 => "round_start",
		6 => "playing",
		7 => "score",
		8 => "round_over",
		9 => "post_match",
		10 => "pre_sudden_death",
		11 => "sudden_death",
		12 => "post_sudden_death",
		_ => "",
	};

	/// <summary>kGoalTypeNames (import_facts.h): the code in a goal message.</summary>
	private static readonly string[] GoalTypes =
	{
		"unknown", "SLAM DUNK", "INSIDE SHOT", "LONG SHOT", "BOUNCE SHOT", "LONG BOUNCE SHOT", "HEADBUTT", "LONG HEADBUTT",
		"BUMPER SHOT", "LONG BUMPER SHOT", "SELF GOAL",
	};

	// ───────────────────────────── reading ─────────────────────────────

	/// <summary>
	/// A networked body's identity (SR15NetPhysicsSimState +0x48): the high dword is the tag, 0 = a user id (u16 slot,
	/// u16 generation), 1 = a component id (the disc). Encoded here as the slot, or -1 for the disc.
	/// </summary>
	private const int DiscWho = -1;

	private static int? Who(ulong idValue) => (idValue >> 32) switch
	{
		0 => (int)(idValue & 0xFFFF),
		1 => DiscWho,
		_ => null,
	};

	private enum UKind { Body, Hands, Game, Stats, Goal, Stun, Entrant, Removed, TeamChanged, Identity }

	/// <summary>One timed change, applied in time order between ticks.</summary>
	private sealed class Update
	{
		public UKind Kind;
		public int Who; // body: the slot, DiscWho, or LocalWho (the recording player's own, slot not yet known)
		public Vector3 A; // body: com; hands: left
		public Vector3 B; // body: vel; hands: right
		public ushort Slot; // hands, entrant, removed, team changed
		public byte[] Data = Array.Empty<byte>();
		public ulong Xp;
		public string Name = "";
		public ushort Team;
	}

	private const int LocalWho = ushort.MaxValue;

	/// <summary>A wire row before its clock is known: At is how far through its block it came (0..1).</summary>
	private sealed class WireRow
	{
		public ulong Stamp;
		public double At;
		public Update Update = null!;
	}

	/// <summary>
	/// Wire rows (other players' bodies and hands, and the disc) are stamped on some peer's clock: before the match
	/// each player's rows carry their own, and mid-play everything is relayed on one peer's. Ticks are always on ours.
	/// There are only a handful of clocks, minutes apart, so each row goes on the known clock that lands it inside its
	/// chapter's ticks.
	/// </summary>
	private sealed class PeerClocks
	{
		/// <summary>Slack around a chapter's ticks, and how close two offsets must be to be the same clock.</summary>
		private const long Slack = 1_000_000;

		/// <summary>Ours minus theirs, for every clock seen; ours (0) is implied.</summary>
		private readonly List<long> offsets = new();

		private long Learn(long o)
		{
			foreach (long k in offsets)
			{
				if (Math.Abs(k - o) < Slack) return k;
			}

			offsets.Add(o);
			return o;
		}

		public List<(ulong, Update)> Place(List<ulong> ticks, List<WireRow> rows)
		{
			if (ticks.Count == 0) return rows.Select(r => (r.Stamp, r.Update)).ToList();
			ulong t0 = ticks.Min(), t1 = ticks.Max();
			long lo = (long)t0 - Slack, hi = (long)t1 + Slack;
			bool Inside(long s) => s >= lo && s <= hi;

			// Measure clocks exactly where a disc row on our clock sits right before a player's rows (the pre-match
			// lobby): their gap is that player's offset to within a frame.
			long? reference = null;
			var gaps = new Dictionary<int, List<long>>();
			foreach (WireRow r in rows)
			{
				if (r.Update.Kind != UKind.Body) continue;
				if (r.Update.Who == DiscWho)
				{
					reference = Inside((long)r.Stamp) ? (long)r.Stamp : null;
				}
				else if (reference is long f && !Inside((long)r.Stamp))
				{
					if (!gaps.TryGetValue(r.Update.Who, out var g)) gaps[r.Update.Who] = g = new List<long>();
					g.Add(f - (long)r.Stamp);
				}
			}

			foreach (List<long> g in gaps.Values)
			{
				g.Sort();
				Learn(g[g.Count / 2]);
			}

			double span = t1 - t0;
			var output = new List<(ulong, Update)>(rows.Count);
			foreach (WireRow r in rows)
			{
				long s = (long)r.Stamp;
				if (Inside(s))
				{
					output.Add((r.Stamp, r.Update));
					continue;
				}

				long? found = null;
				foreach (long o in offsets)
				{
					if (Inside(s + o))
					{
						found = o;
						break;
					}
				}

				// a clock never measured: place the row by where it came in the chapter
				long off = found ?? Learn((long)t0 + (long)(r.At * span) - s);
				output.Add(((ulong)Math.Max(s + off, 0), r.Update));
			}

			return output;
		}
	}

	private sealed class BodyState
	{
		public Vector3 Com;
		public Vector3 Vel;
		public ulong Seen;
	}

	private sealed class HandState
	{
		public Vector3 Left;
		public Vector3 Right;
		public ulong Seen;
	}

	/// <summary>One entrant of the match, by user slot.</summary>
	private sealed class Person
	{
		public ulong Xp;
		public string Name = "";
		public ushort Team;

		public Person Clone() => new() { Xp = Xp, Name = Name, Team = Team };
	}

	/// <summary>Read the header and the last manifest (the trailer's copy holds everyone who played).</summary>
	public static (Manifest manifest, ulong t0, string session, bool open) ReadManifest(byte[] bytes)
	{
		if (bytes.Length < Header || Encoding.ASCII.GetString(bytes, 0, 8) != "ECHODEMO") throw new DemoException("Not an .echodemo file.");
		if (U16(bytes, 8) != 1) throw new DemoException($"Unsupported .echodemo format version {U16(bytes, 8)}.{U16(bytes, 10)}");
		ulong flags = U64(bytes, 16);
		ulong t0 = U64(bytes, 40);
		var hex = new StringBuilder(32);
		for (int i = 63; i >= 48; i--) hex.Append(bytes[i].ToString("X2"));
		string h = hex.ToString();
		string session = $"{h[..8]}-{h[8..12]}-{h[12..16]}-{h[16..20]}-{h[20..]}";

		Manifest? man = null;
		List<Chapter> ch = Chapters(bytes);
		for (int i = ch.Count - 1; i >= 0 && man == null; i--)
		{
			Chapter c = ch[i];
			if (c.Section != SectionIndex && c.Section != SectionManifest) continue;
			byte[] p = Payload(bytes, c);
			foreach (Block blk in Blocks(p))
			{
				if (blk.Id != SManifest) continue;
				try
				{
					man = ParseManifest(blk);
				}
				catch (Exception)
				{
					// try an earlier copy
				}

				break;
			}
		}

		if (man == null) throw new DemoException("No manifest in the file.");
		return (man, t0, session, (flags & 1) != 0);
	}

	/// <summary>
	/// S_KEY (capture/keyframe.h): [u32 magic 'KEY1'][u32 version][u64 tUs][u16 n][u16] then [u16 id][u32 len][bytes]
	/// blocks. The record sits behind a short prefix, so the magic is searched.
	/// </summary>
	private static List<(ushort id, byte[] b)> KeyBlocks(byte[] d)
	{
		var v = new List<(ushort, byte[])>();
		int limit = Math.Max(0, Math.Min(d.Length, 32) - 4);
		int at = -1;
		for (int i = 0; i < limit; i++)
		{
			if (U32(d, i) == 0x3159_454B)
			{
				at = i;
				break;
			}
		}

		if (at < 0) return v;
		long j = at + 20;
		while (j + 6 <= d.Length)
		{
			ushort id = U16(d, (int)j);
			long len = U32(d, (int)j + 2);
			j += 6;
			if (j + len > d.Length) break;
			var b = new byte[len];
			Buffer.BlockCopy(d, (int)j, b, 0, (int)len);
			v.Add((id, b));
			j += len;
		}

		return v;
	}

	/// <summary>The event vocabulary a keyframe carries (kKeyBlockTapTable): tap index → struct name.</summary>
	private static Dictionary<string, ushort> TapTable(byte[] key)
	{
		var m = new Dictionary<string, ushort>();
		foreach ((ushort id, byte[] b) in KeyBlocks(key))
		{
			if (id != 8 || b.Length < 2) continue;
			ushort n = U16(b, 0);
			int q = 2;
			for (int k = 0; k < n; k++)
			{
				if (q + 6 > b.Length) break;
				ushort i = U16(b, q);
				int l = U16(b, q + 4);
				if (q + 6 + l > b.Length) break;
				m[Encoding.UTF8.GetString(b, q + 6, l)] = i;
				q += 6 + l;
			}
		}

		return m;
	}

	/// <summary>The tap indices this reader understands.</summary>
	private sealed class Taps
	{
		public ushort Goal = 11, Stun = 13, Entrant = 21, Removed = 22, TeamChanged = 23, Identity = 37;

		/// <summary>kTaps order in capture/manifestbuild.cpp, for a file whose keyframes carry no table.</summary>
		public static Taps Default => new();

		public static Taps From(Dictionary<string, ushort> t)
		{
			var d = new Taps();
			ushort G(string n, ushort def) => t.TryGetValue(n, out ushort v) ? v : def;
			return new Taps
			{
				Goal = G("SR15NetReliablePostScoreUpdate", d.Goal),
				Stun = G("SR15StunPlayerMsg", d.Stun),
				Entrant = G("SNSLobbyEntrantStatev2", d.Entrant),
				Removed = G("SNSLobbyEntrantRemoved", d.Removed),
				TeamChanged = G("SNSLobbyEntrantTeamChanged", d.TeamChanged),
				Identity = G("ReplayIdentitySnapshot", d.Identity),
			};
		}
	}

	/// <summary>S_EVENT rows: (stamp, tap index, body), and whether the block decoded to its end.</summary>
	private static (List<(ulong stamp, ushort ty, byte[] body)> rows, bool whole) Events(Block data)
	{
		var v = new List<(ulong, ushort, byte[])>();
		Bits? r = BitBlock(data);
		if (r == null) return (v, true);
		bool started = false;
		ulong prevStamp = 0, prevDst = 0;
		for (uint k = 0; k < data.Stored; k++)
		{
			try
			{
				ulong stamp = VarKeyed(r, prevStamp, !started);
				ulong dst = U64Keyed(r, prevDst, !started);
				ushort ty = (ushort)r.Take(16);
				int n = (int)r.Take(16);
				var body = new byte[n];
				for (int i = 0; i < n; i++) body[i] = (byte)r.Take(8);
				prevStamp = stamp;
				prevDst = dst;
				started = true;
				v.Add((stamp, ty, body));
			}
			catch (DemoException)
			{
				return (v, false);
			}
		}

		return (v, true);
	}

	/// <summary>
	/// SNSLobbyEntrantStatev2 (events/events_facts.h): platform code, account, …, name at +60, global entrant index
	/// (= the user slot bodies are keyed by) at +98, team at +102.
	/// </summary>
	private static (ushort slot, ulong xp, string name, ushort team)? Entrant(byte[] b)
	{
		if (b.Length < 104) return null;
		return (U16(b, 98), U64(b, 8), CStr(b, 60, 36), U16(b, 102));
	}

	/// <summary>ReplayIdentitySnapshot (capture/manifestbuild.h): the recording client's own entrant(s).</summary>
	private static List<(ushort slot, ulong xp, string name, ushort team)> Identity(byte[] b)
	{
		var v = new List<(ushort, ulong, string, ushort)>();
		if (b.Length < 28 || U32(b, 0) != 0x314E_4449) return v;
		uint version = U32(b, 4);
		int count = U16(b, 16);
		int head, slotStride, slotCount, stride;
		if (version == 1) (head, slotStride, slotCount, stride) = (28, 16, 20, 92);
		else if (version == 2 && b.Length >= 32) (head, slotStride, slotCount, stride) = (32, U16(b, 28), U16(b, 30), 92);
		else if (version != 1 && version != 2 && b.Length >= 34) (head, slotStride, slotCount, stride) = (34, U16(b, 28), U16(b, 30), U16(b, 32));
		else return v;

		long at = head;
		for (int k = 0; k < count; k++)
		{
			if (stride < 60 || at + stride + (long)slotStride * slotCount + 4 > b.Length) break;
			int e = (int)at;
			ushort team = b[e + 16] == 0xFF ? (ushort)0xFFFF : b[e + 16];
			v.Add((U16(b, e + 8), U64(b, e), CStr(b, e + 28, 32), team));
			long json = U32(b, (int)(at + stride + (long)slotStride * slotCount));
			at += stride + (long)slotStride * slotCount + 4 + json;
		}

		return v;
	}

	/// <summary>
	/// Which sampled entity is the recording player's own body: the one no networked body follows. The sampled stream
	/// carries no identities; every other entity tracks a wire body exactly.
	/// </summary>
	private static ushort? PickLocal(SortedDictionary<ushort, List<(ulong t, Vector3 c)>> sampled,
		Dictionary<int, List<(ulong t, Vector3 c)>> wire, ushort? prev)
	{
		if (sampled.Count == 0) return null;
		if (wire.Count == 0)
		{
			if (prev is ushort p && sampled.ContainsKey(p)) return p;
			return sampled.Keys.First();
		}

		(float score, ushort e)? best = null;
		foreach ((ushort e, List<(ulong t, Vector3 c)> s) in sampled)
		{
			int n = Math.Min(s.Count, 9);
			int matched = 0;
			for (int k = 0; k < n; k++)
			{
				(ulong t, Vector3 c) = s[k * (s.Count - 1) / Math.Max(n - 1, 1)];
				bool hit = wire.Values.Any(w =>
				{
					int i = PartitionPoint(w, t);
					for (int j = i - 1; j <= i; j++)
					{
						if (j < 0 || j >= w.Count) continue;
						(ulong wt, Vector3 wc) = w[j];
						ulong diff = wt > t ? wt - t : t - wt;
						if (diff < 40_000 && Vector3.Distance(wc, c) < 0.3f) return true;
					}

					return false;
				});
				if (hit) matched++;
			}

			float score = (float)matched / n - (prev == e ? 0.05f : 0f);
			if (best == null || score < best.Value.score) best = (score, e);
		}

		return best is { } bb && bb.score < 0.4f ? bb.e : null;
	}

	/// <summary>The first index whose time is not below t (the list is sorted by time).</summary>
	private static int PartitionPoint(List<(ulong t, Vector3 c)> w, ulong t)
	{
		int lo = 0, hi = w.Count;
		while (lo < hi)
		{
			int mid = (lo + hi) / 2;
			if (w[mid].t < t) lo = mid + 1;
			else hi = mid;
		}

		return lo;
	}

	/// <summary>A user id in an event (SR15NetUserId: u16 slot, u16 generation); 0xFFFF is nobody.</summary>
	private static ushort? SlotOf(uint id)
	{
		ushort s = (ushort)(id & 0xFFFF);
		return s != 0xFFFF ? s : null;
	}

	/// <summary>True when the file starts with the .echodemo magic.</summary>
	public static bool IsEchoDemo(string path)
	{
		try
		{
			using FileStream fs = File.OpenRead(path);
			var head = new byte[8];
			return fs.Read(head, 0, 8) == 8 && Encoding.ASCII.GetString(head) == "ECHODEMO";
		}
		catch
		{
			return false;
		}
	}

	/// <summary>Decode the file into API-shaped frames, one per engine tick.</summary>
	public static ReplayReadResult Read(string path, string selfName = "")
	{
		var result = new ReplayReadResult { SourcePath = path, Format = ReplayFormat.EchoDemo, FormatVersion = 1 };
		byte[] bytes = File.ReadAllBytes(path);
		try
		{
			Read(bytes, selfName, StartTime(path), result);
		}
		catch (DemoException e)
		{
			result.Diagnostics.Add(e.Message);
		}

		result.SessionId ??= result.Frames.FirstOrDefault()?.sessionid;
		return result;
	}

	/// <summary>
	/// When the recording started, as local time. Live captures leave the manifest's start time at 0 (the header's
	/// clock is the game process's), so the file name EchoReplay gives a capture (yyyy-MM-dd_HH-mm-ss_…, local time)
	/// is the best record of it; failing that, when the file was created.
	/// </summary>
	private static DateTime StartTime(string path)
	{
		string name = Path.GetFileName(path);
		if (name.Length >= 19 && DateTime.TryParseExact(name[..19], "yyyy-MM-dd_HH-mm-ss", System.Globalization.CultureInfo.InvariantCulture,
			    System.Globalization.DateTimeStyles.AssumeLocal, out DateTime fromName))
		{
			return fromName;
		}

		try
		{
			return File.GetCreationTime(path);
		}
		catch
		{
			return DateTime.MinValue;
		}
	}

	private static Dictionary<ulong, string>? localProfiles;

	/// <summary>
	/// Display names of the accounts that have played on this PC, by platform id, from the game's own profiles
	/// (%LOCALAPPDATA%\rad\echovr\users\*\*\serverprofile.json, "xplatformid": "OVR-ORG-123…"). The recording player's
	/// name is never in the file, but their platform id is.
	/// </summary>
	public static Dictionary<ulong, string> LocalProfileNames()
	{
		if (localProfiles != null) return localProfiles;
		var found = new Dictionary<ulong, string>();
		try
		{
			string users = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "rad", "echovr", "users");
			if (Directory.Exists(users))
			{
				foreach (string file in Directory.EnumerateFiles(users, "serverprofile.json", SearchOption.AllDirectories))
				{
					try
					{
						JObject p = JObject.Parse(File.ReadAllText(file));
						string? name = (string?)p["displayname"];
						// skip the game's placeholder profiles (no id, or the default "AccountName")
						if (string.IsNullOrWhiteSpace(name) || name == "AccountName" || PlatformId((string?)p["xplatformid"]) is not ulong id || id == 0) continue;
						found[id] = name;
					}
					catch
					{
						// an unreadable profile names nobody
					}
				}
			}
		}
		catch
		{
			// no profiles
		}

		return localProfiles = found;
	}

	/// <summary>The number in a platform id string such as "OVR-ORG-6032896500068788".</summary>
	public static ulong? PlatformId(string? xplatformId)
	{
		if (string.IsNullOrEmpty(xplatformId)) return null;
		int dash = xplatformId.LastIndexOf('-');
		return ulong.TryParse(dash >= 0 ? xplatformId[(dash + 1)..] : xplatformId, out ulong id) ? id : null;
	}

	private static void Read(byte[] bytes, string selfName, DateTime start, ReplayReadResult output)
	{
		(Manifest man, ulong t0, string session, bool open) = ReadManifest(bytes);
		output.SessionId = session;
		if (open)
		{
			output.TruncatedTail = true;
			output.Diagnostics.Add("The recording was never closed (the game stopped mid-match); read up to the last whole chapter.");
		}

		(string map, string matchType, bool isPrivate) = GameType(man.GameTypeId);
		// seconds since the epoch at the header's clock zero (t0): the manifest's when it has one (imported files),
		// else the file name's, which is when the first frame was written (fixed up at that frame)
		double epoch = man.StartEpoch > 0
			? man.StartEpoch
			: start == DateTime.MinValue ? 0 : new DateTimeOffset(start).ToUnixTimeMilliseconds() / 1000.0;
		bool anchored = man.StartEpoch > 0;

		var payloads = new List<byte[]>();
		foreach (Chapter c in Chapters(bytes).Where(c => c.Section == SectionCore))
		{
			try
			{
				payloads.Add(Payload(bytes, c));
			}
			catch (Exception e)
			{
				output.FailedChunks++;
				output.Diagnostics.Add($"A chapter didn't decompress ({e.Message}).");
			}
		}

		// names by platform id, from every lobby push in the file, so a player whose push came late (or before a
		// mid-match start) still has a name from the first frame
		Taps taps = Taps.Default;
		var names = new Dictionary<ulong, string>(LocalProfileNames());
		foreach (RosterRow r in man.Roster.Where(r => r.Name.Length > 0)) names[r.XPlatformId] = r.Name;
		foreach (byte[] p in payloads)
		{
			foreach (Block blk in Blocks(p))
			{
				if (blk.Id == SKey)
				{
					Dictionary<string, ushort> t = TapTable(blk.ToArray());
					if (t.Count > 0) taps = Taps.From(t);
				}
				else if (blk.Id == SEvent)
				{
					foreach ((_, ushort ty, byte[] b) in Events(blk).rows)
					{
						if (ty != taps.Entrant) continue;
						if (Entrant(b) is { } e && e.name.Length > 0) names[e.xp] = e.name;
					}
				}
			}
		}

		var people = new SortedDictionary<ushort, Person>();
		var ever = new SortedDictionary<ushort, Person>();
		var bodies = new Dictionary<ushort, BodyState>();
		var disc = new BodyState();
		var hands = new Dictionary<ushort, HandState>();
		var stats = new Dictionary<ulong, (Stats stats, int ping)>();
		byte[]? game = null;
		var lastScore = new LastScore();
		var stunnedUntil = new Dictionary<ushort, ulong>();
		ushort? local = null;
		ushort? localEntity = null;
		int verbatim = 0, failedBlocks = 0, failedEvents = 0;

		var wireBody = new Layout(BodySchema(false));
		var sampledBody = new Layout(BodySchema(true));
		var wirePlayer = new Layout(PlayerSchema(false));
		var sampledPlayer = new Layout(PlayerSchema(true));
		// field index of lHandOff: fill, 3 quats, refVel×3, bits28, holds×4, bend×2, rest
		int HandField(bool sampled) => sampled ? 1 + 12 + 3 + 5 + 3 : 1 + 3 + 3 + 5 + 3;
		int VelField(bool sampled) => 4 + 3 + (sampled ? 4 : 1);

		var clocks = new PeerClocks();
		foreach (byte[] p in payloads)
		{
			var ticks = new List<ulong>();
			// wire rows wait here until the chapter's ticks say which clock each is on
			var wireRows = new List<WireRow>();
			var updates = new List<(ulong stamp, Update u)>();
			var wireTracks = new Dictionary<int, List<(ulong t, Vector3 c)>>();
			var sampled = new SortedDictionary<ushort, List<(ulong t, Vector3 c)>>();
			var sampledRecs = new List<(ulong stamp, ushort e, Vector3 com, Vector3 vel)>();
			foreach (Block blk in Blocks(p))
			{
				try
				{
					switch (blk.Id)
					{
						case STick:
						{
							Bits? r = BitBlock(blk);
							if (r == null) break;
							var d = new TickDecoder();
							for (uint i = 0; i < blk.Stored; i++) ticks.Add(d.Decode(r));
							break;
						}
						case SBodyWire:
						case SBodySampled:
						{
							bool isSampled = blk.Id == SBodySampled;
							Bits? r = BitBlock(blk);
							if (r == null) break;
							Layout l = isSampled ? sampledBody : wireBody;
							var d = new EntityDecoder(l);
							for (uint i = 0; i < blk.Stored; i++)
							{
								Rec rec = d.Decode(r);
								if (rec.Values is not { } v)
								{
									verbatim++;
									continue;
								}

								Vector3 com = l.Vec3(4, v), vel = l.Vec3(VelField(isSampled), v);
								double at = (double)i / Math.Max(blk.Stored, 1);
								if (isSampled)
								{
									if (!sampled.TryGetValue(rec.Entity, out var list)) sampled[rec.Entity] = list = new();
									list.Add((rec.Stamp, com));
									sampledRecs.Add((rec.Stamp, rec.Entity, com, vel));
								}
								else if (Who(l.Raw(2, v)) is int w)
								{
									wireRows.Add(new WireRow { Stamp = rec.Stamp, At = at, Update = new Update { Kind = UKind.Body, Who = w, A = com, B = vel } });
								}
							}

							break;
						}
						case SPlayerWire:
						case SPlayerSampled:
						{
							bool isSampled = blk.Id == SPlayerSampled;
							Bits? r = BitBlock(blk);
							if (r == null) break;
							Layout l = isSampled ? sampledPlayer : wirePlayer;
							var d = new EntityDecoder(l);
							for (uint i = 0; i < blk.Stored; i++)
							{
								Rec rec = d.Decode(r);
								if (rec.Values is not { } v)
								{
									verbatim++;
									continue;
								}

								double at = (double)i / Math.Max(blk.Stored, 1);
								// wire rows are keyed by the message header (the user slot); only the recording player
								// is sampled, under their own slot
								ushort slot;
								if (isSampled)
								{
									local ??= rec.Entity;
									slot = rec.Entity;
								}
								else
								{
									slot = (ushort)(l.Raw(l.Fields.Length - 1, v) & 0xFFFF);
								}

								int h = HandField(isSampled);
								var u = new Update { Kind = UKind.Hands, Slot = slot, A = l.Vec3(h, v), B = l.Vec3(h + 3, v) };
								if (isSampled) updates.Add((rec.Stamp, u));
								else wireRows.Add(new WireRow { Stamp = rec.Stamp, At = at, Update = u });
							}

							break;
						}
						case SGameState:
						{
							for (int o = 0; o + 84 <= blk.Len; o += 84)
							{
								var rec = new byte[84];
								Buffer.BlockCopy(blk.Buf, blk.Offset + o, rec, 0, 84);
								updates.Add((U64(rec, 8), new Update { Kind = UKind.Game, Data = rec }));
							}

							break;
						}
						case SPlayerStats:
						{
							for (int o = 0; o + 88 <= blk.Len; o += 88)
							{
								var rec = new byte[88];
								Buffer.BlockCopy(blk.Buf, blk.Offset + o, rec, 0, 88);
								updates.Add((U64(rec, 16), new Update { Kind = UKind.Stats, Data = rec }));
							}

							break;
						}
						case SEvent:
						{
							(var evs, bool whole) = Events(blk);
							if (!whole) failedEvents++;
							foreach ((ulong stamp, ushort ty, byte[] body) in evs)
							{
								int n = body.Length;
								Update u;
								if (ty == taps.Goal && n >= 28) u = new Update { Kind = UKind.Goal, Data = body };
								else if (ty == taps.Stun && n >= 16) u = new Update { Kind = UKind.Stun, Data = body };
								else if (ty == taps.Entrant)
								{
									if (Entrant(body) is not { } e) continue;
									u = new Update { Kind = UKind.Entrant, Slot = e.slot, Xp = e.xp, Name = e.name, Team = e.team };
								}
								else if (ty == taps.Removed && n >= 8) u = new Update { Kind = UKind.Removed, Slot = (ushort)U64(body, 0) };
								else if (ty == taps.TeamChanged && n >= 10) u = new Update { Kind = UKind.TeamChanged, Slot = (ushort)U64(body, 0), Team = U16(body, 8) };
								else if (ty == taps.Identity) u = new Update { Kind = UKind.Identity, Data = body };
								else continue;
								updates.Add((stamp, u));
							}

							break;
						}
					}
				}
				catch (Exception e) when (e is DemoException or ArgumentException or IndexOutOfRangeException)
				{
					failedBlocks++;
				}
			}

			foreach ((ulong stamp, Update u) in clocks.Place(ticks, wireRows))
			{
				if (u.Kind == UKind.Body)
				{
					if (!wireTracks.TryGetValue(u.Who, out var list)) wireTracks[u.Who] = list = new();
					list.Add((stamp, u.A));
				}

				updates.Add((stamp, u));
			}

			// the recording player's own body is only in the sampled stream
			foreach (var w in wireTracks.Values) w.Sort((x, y) => x.t.CompareTo(y.t));
			localEntity = PickLocal(sampled, wireTracks, localEntity);
			if (localEntity is ushort le)
			{
				foreach ((ulong stamp, ushort e, Vector3 com, Vector3 vel) in sampledRecs)
				{
					if (e == le) updates.Add((stamp, new Update { Kind = UKind.Body, Who = LocalWho, A = com, B = vel }));
				}
			}

			// stable, like Rust's sort_by_key
			updates = updates.Select((x, i) => (x, i)).OrderBy(x => x.x.stamp).ThenBy(x => x.i).Select(x => x.x).ToList();
			int ui = 0;
			foreach (ulong tick in ticks)
			{
				while (ui < updates.Count && updates[ui].stamp <= tick)
				{
					(ulong stamp, Update u) = updates[ui];
					ui++;
					switch (u.Kind)
					{
						case UKind.Body:
							if (u.Who == DiscWho)
							{
								disc.Com = u.A;
								disc.Vel = u.B;
								disc.Seen = stamp;
							}
							else
							{
								ushort s;
								if (u.Who == LocalWho)
								{
									if (local is not ushort l) continue;
									s = l;
								}
								else s = (ushort)u.Who;

								if (!bodies.TryGetValue(s, out BodyState? b)) bodies[s] = b = new BodyState();
								b.Com = u.A;
								b.Vel = u.B;
								b.Seen = stamp;
							}

							break;
						case UKind.Hands:
						{
							if (!hands.TryGetValue(u.Slot, out HandState? h)) hands[u.Slot] = h = new HandState();
							h.Left = u.A;
							h.Right = u.B;
							h.Seen = stamp;
							break;
						}
						case UKind.Game:
							if (u.Data.Length > 0) game = u.Data;
							break;
						case UKind.Stats:
						{
							byte[] s = u.Data;
							ulong xp = U64(s, 0);
							uint fill = U32(s, 80);
							int Int(int off) => Math.Max(I32(s, off), 0);
							var st = new Stats
							{
								possession_time = (fill & 1) != 0 ? Math.Max(F32(s, 24), 0f) : 0f,
								points = Int(28),
								goals = Int(32),
								stuns = Int(36),
								blocks = Int(40),
								assists = Int(44),
								saves = Int(48),
								interceptions = Int(52),
								steals = Int(56),
								catches = Int(60),
								passes = Int(64),
								shots_taken = Int(68),
							};
							stats[xp] = (st, Math.Max(I32(s, 72), 0));
							break;
						}
						case UKind.Goal:
						{
							byte[] b = u.Data;
							int gt = U16(b, 4);
							string NameOf(uint id)
							{
								if (SlotOf(id) is not ushort s) return "";
								if (people.TryGetValue(s, out Person? pp) || ever.TryGetValue(s, out pp)) return pp.Name;
								return "";
							}

							lastScore = new LastScore
							{
								team = U16(b, 0) == 0 ? "blue" : "orange",
								point_amount = U16(b, 2),
								goal_type = gt < GoalTypes.Length ? GoalTypes[gt] : "unknown",
								person_scored = NameOf(U32(b, 6)),
								assist_scored = NameOf(U32(b, 10)),
								disc_speed = F32(b, 16),
								distance_thrown = F32(b, 20),
							};
							break;
						}
						case UKind.Stun:
						{
							byte[] b = u.Data;
							// flag 1: the punch was blocked and the puncher is the one stunned
							uint victim = U32(b, 12) == 1 ? U32(b, 4) : U32(b, 0);
							float secs = Math.Clamp(F32(b, 8), 0f, 30f);
							if (float.IsNaN(secs)) secs = 0;
							if (SlotOf(victim) is ushort s) stunnedUntil[s] = stamp + (ulong)(secs * 1e6);
							break;
						}
						case UKind.Entrant:
						{
							string name = u.Name.Length > 0 ? u.Name : names.GetValueOrDefault(u.Xp, "");
							var person = new Person { Xp = u.Xp, Name = name, Team = u.Team };
							ever[u.Slot] = person.Clone();
							people[u.Slot] = person;
							break;
						}
						case UKind.Removed:
							people.Remove(u.Slot);
							break;
						case UKind.TeamChanged:
							if (people.TryGetValue(u.Slot, out Person? changed)) changed.Team = u.Team;
							break;
						case UKind.Identity:
							foreach ((ushort slot, ulong xp, string idName, ushort team) in Identity(u.Data))
							{
								string name = new[] { idName, names.GetValueOrDefault(xp, ""), selfName }.FirstOrDefault(n => n.Length > 0) ?? "";
								local ??= slot;
								var person = new Person { Xp = xp, Name = name, Team = team };
								ever[slot] = person.Clone();
								people[slot] = person;
							}

							break;
					}
				}

				if (game == null) continue;
				Frame f = BuildFrame(tick, game, people, bodies, disc, hands, stats, lastScore, stunnedUntil, local, map, matchType, isPrivate, session);
				if (!anchored)
				{
					epoch -= ((double)tick - t0) / 1e6;
					anchored = true;
				}

				double unixSeconds = epoch + ((double)tick - t0) / 1e6;
				f.recorded_time = DateTimeOffset.FromUnixTimeMilliseconds((long)(unixSeconds * 1000)).LocalDateTime;
				output.Frames.Add(f);
			}
		}

		if (local is ushort me && ever.TryGetValue(me, out Person? self)) output.ClientName = self.Name;
		foreach (Frame f in output.Frames) f.client_name = output.ClientName ?? "";

		if (verbatim > 0) output.Diagnostics.Add($"{verbatim} engine record(s) were stored undecoded and skipped.");
		if (failedBlocks > 0)
		{
			output.FailedChunks += failedBlocks;
			output.Diagnostics.Add($"{failedBlocks} stream block(s) could not be decoded and were skipped.");
		}

		if (failedEvents > 0) output.Diagnostics.Add($"{failedEvents} event block(s) were only partly readable.");
	}

	private static List<float> L(Vector3 v) => new() { v.X, v.Y, v.Z };

	private static Frame BuildFrame(ulong tick, byte[] g, SortedDictionary<ushort, Person> people, Dictionary<ushort, BodyState> bodies,
		BodyState disc, Dictionary<ushort, HandState> hands, Dictionary<ulong, (Stats stats, int ping)> stats, LastScore lastScore,
		Dictionary<ushort, ulong> stunnedUntil, ushort? local, string map, string matchType, bool isPrivate, string session)
	{
		uint present = U32(g, 76);
		uint owner = (present & (1 << 5)) != 0 ? U32(g, 52) : 0xFFFF_FFFF;
		var f = new Frame
		{
			sessionid = session,
			map_name = map,
			match_type = matchType,
			private_match = isPrivate,
			game_status = PhaseStatus(g[80]),
			game_clock = (present & (1 << 6)) != 0 ? F32(g, 56) : 0f,
			blue_points = U32(g, 36),
			orange_points = U32(g, 40),
			blue_round_score = (int)U32(g, 44),
			orange_round_score = (int)U32(g, 48),
			last_score = lastScore,
		};
		float clock = Math.Max(f.game_clock, 0f);
		if (float.IsNaN(clock)) clock = 0;
		f.game_clock_display = $"{(int)clock / 60:00}:{(int)clock % 60:00}.{(int)((clock - MathF.Floor(clock)) * 100):00}";
		if (local is ushort l && people.TryGetValue(l, out Person? me)) f.client_name = me.Name;

		// the disc's body is only replicated while it moves; a stale one is still where it stopped
		f.disc = new Disc
		{
			position = L(disc.Com),
			velocity = L(disc.Vel),
			forward = new List<float> { 0, 0, 1 },
			left = new List<float> { 1, 0, 0 },
			up = new List<float> { 0, 1, 0 },
		};

		var teams = new List<Team>
		{
			new() { team = "BLUE TEAM", players = new List<Player>() },
			new() { team = "ORANGE TEAM", players = new List<Player>() },
			new() { team = "SPECTATORS", players = new List<Player>() },
		};
		// a body not updated for a second has left (or never was a player)
		const ulong gone = 1_000_000;
		foreach ((ushort slot, Person r) in people)
		{
			int t = r.Team switch
			{
				0 => 0,
				1 => 1,
				2 => 2,
				_ => -1,
			};
			if (t < 0) continue;
			BodyState? body = bodies.TryGetValue(slot, out BodyState? bs) && tick - Math.Min(tick, bs.Seen) < gone ? bs : null;
			if (t < 2 && body == null) continue;

			var p = new Player
			{
				name = r.Name,
				playerid = slot,
				userid = (long)r.Xp,
				stats = new Stats(),
				head = new Transform(),
				body = new Transform(),
				lhand = new Transform(),
				rhand = new Transform(),
				velocity = new List<float> { 0, 0, 0 },
			};
			if (stats.TryGetValue(r.Xp, out var sp))
			{
				p.stats = sp.stats;
				p.ping = sp.ping;
			}

			Vector3 lh = Vector3.Zero, rh = Vector3.Zero;
			if (body != null)
			{
				p.head.position = L(body.Com);
				p.body.position = L(body.Com);
				p.velocity = L(body.Vel);
				lh = body.Com;
				rh = body.Com;
				if (hands.TryGetValue(slot, out HandState? hs) && tick - Math.Min(tick, hs.Seen) < gone)
				{
					lh = body.Com + hs.Left;
					rh = body.Com + hs.Right;
				}
			}
			else
			{
				p.head.position = L(Vector3.Zero);
				p.body.position = L(Vector3.Zero);
			}

			p.lhand.position = L(lh);
			p.rhand.position = L(rh);
			p.stunned = stunnedUntil.TryGetValue(slot, out ulong until) && tick < until;
			p.holding_left = "none";
			p.holding_right = "none";
			if (SlotOf(owner) == slot)
			{
				p.possession = true;
				// physically holding: the disc is in (or right by) one of the hands
				float dl = Vector3.Distance(lh, disc.Com);
				float dr = Vector3.Distance(rh, disc.Com);
				if (Math.Min(dl, dr) < 0.6f)
				{
					if (dl < dr) p.holding_left = "disc";
					else p.holding_right = "disc";
				}
			}

			teams[t].players.Add(p);
		}

		foreach (Team team in teams) team.possession = team.players.Any(p => p.possession);
		f.teams = teams;
		return f;
	}
}
