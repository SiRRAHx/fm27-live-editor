// Finds every player in the loaded career and reads or changes their attributes.
//
// Layout (FM27 0.9.2409, found by research; see research\ notes). Each player is
// three objects at fixed distances from the person object P, each starting with
// a type marker (a vtable inside game_plugin.dll):
//   P - 0x288  player part: its last fields (P - 0x158) point to the player's
//              club team; team +0x30 is the club, club +0xC0 its name
//   P - 0x140  attribute record: +8 15 position ratings (1-20), then 54 attributes
//              stored as 1-100 (FM shows value / 5)
//   P          person: +8 index, +0x40 full name, +0x50/+0x58 first name and
//              surname entries, +0x88 date of birth (day of year, year)
//   P - 0x24   current ability, P - 0x22 potential ability (int16, 1-200)
//   P + 0xA8   the player's contract: +8 points back to P, +0x20 weekly wage
//              (int32, pounds), +0x48 end date, +0x4C date joined (both day
//              of year, year), +0x54 squad status (copy at +0x56), +0x68/+0x70
//              the clause list: 8-byte entries of int32 amount, int16 extra
//              (a percentage or years, else -1), int16 clause type
//   P + 0x68   nationality: +0x18 points to its name
// Every write first checks all three markers and the value it replaces, so
// nothing is written unless it's exactly where we expect.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace FM27LiveEditor;

public record PlayerSummary(int id, string name, int born, string position, string club, int? uid = null);

public static class Players
{
    const int AttrOffset = -0x140, PlayerOffset = -0x288;
    const int TeamAt = -0x158, CaAt = -0x24, PaAt = -0x22;
    const int ContractAt = 0xA8, WageAt = 0x20, EndsAt = 0x48, JoinedAt = 0x4C, StatusAt = 0x54, StatusCopyAt = 0x56;
    const int ClausesAt = 0x68, NationAt = 0x68, SquadNumberAt = 0x5D;

    // Day-to-day values in the player part, as offsets from P (checked on 5,000 players and
    // against FMLibreEditor's layout): morale 1-20; condition and match sharpness 0-10000
    // (100.00%, sharpness never below 3500); reputations 0-10000.
    public record StatusField(string key, int at, int size, int min, int max);
    public static readonly StatusField[] StatusFields =
    {
        new("morale", -0x1C, 1, 1, 20), new("condition", -0x2C, 2, 0, 10000), new("sharpness", -0x30, 2, 3500, 10000),
        new("homeReputation", -0x2A, 2, 0, 10000), new("currentReputation", -0x28, 2, 0, 10000), new("worldReputation", -0x26, 2, 0, 10000),
    };

    // Squad status codes, matched against FM's wages screen. Only these can be set.
    public static readonly Dictionary<int, string> SquadStatuses = new()
    {
        [1] = "Star Player", [2] = "Important Player", [3] = "Regular Starter", [4] = "Squad Player", [7] = "Fringe Player",
        [10] = "Breakthrough Prospect", [11] = "Future Prospect", [13] = "Youngster", [15] = "First-Choice Goalkeeper",
        [17] = "Domestic Cup Goalkeeper", [20] = "Backup", [21] = "Emergency Backup",
    };

    // Clause types we understand, and whether the number is money (the amount) or
    // a percentage (the extra field). Other clauses are listed but can't be edited.
    public static readonly Dictionary<int, (string name, bool money)> ClauseTypes = new()
    {
        [0] = ("Minimum fee release clause", true), [32] = ("Appearance fee", true), [33] = ("Goal bonus", true),
        [34] = ("Clean sheet bonus", true), [38] = ("Unused substitute fee", true),
        [3] = ("Yearly wage rise (%)", false), [15] = ("Wage drop on relegation (%)", false),
        // Matched against FM's contract screens in the Test save (Bruno Fernandes, Branthwaite).
        [39] = ("Assist bonus", true), [8] = ("Sell-on fee (%)", false), [22] = ("Optional extension by club (years)", false),
    };
    const int PositionsAt = 8, AttributesAt = 8 + 15;

    // FM's internal attribute order, as stored in the record.
    public static readonly string[] AttributeOrder =
    {
        "Crossing", "Dribbling", "Finishing", "Heading", "LongShots", "Marking", "OffTheBall", "Passing", "Penalties", "Tackling",
        "Vision", "Handling", "Aerial", "CommandOfArea", "Communication", "Kicking", "Throwing", "Anticipation", "Decisions", "OneOnOnes",
        "Positioning", "Reflexes", "FirstTouch", "Technique", "LeftFoot", "RightFoot", "Flair", "Corners", "Teamwork", "WorkRate",
        "LongThrows", "Eccentricity", "RushingOut", "Punching", "Acceleration", "FreeKicks", "Strength", "Stamina", "Pace", "Jumping",
        "Leadership", "Dirtiness", "Balance", "Bravery", "Consistency", "Aggression", "Agility", "ImportantMatches", "InjuryProneness", "Versatility",
        "NaturalFitness", "Determination", "Composure", "Concentration",
    };

    // Position ratings in stored order. Checked against Maguire (DC 20, DR 10)
    // and McHugh (DM 20, MC 19, DC 17); the rest follow FM's usual order.
    public static readonly string[] PositionOrder = { "GK", "SW", "DL", "DC", "DR", "DM", "ML", "MC", "MR", "AML", "AMC", "AMR", "ST", "WBL", "WBR" };

    static long personVt, attrVt, playerVt, teamVt, clubVt, contractVt, nationVt;
    static readonly object gate = new();
    static Dictionary<int, long> byId = new();
    static List<PlayerSummary> list = new();
    public static volatile bool Indexing;
    public static DateTime IndexedAt;
    public static string LastError;

    // Takes the class tables found in this build of FM (see Addresses).
    internal static void UseAddresses()
    {
        personVt = Addresses.Person; attrVt = Addresses.Attr; playerVt = Addresses.Player;
        teamVt = Addresses.Team; clubVt = Addresses.Club; contractVt = Addresses.Contract; nationVt = Addresses.Nation;
    }

    public static int Count { get { lock (gate) return list.Count; } }

    // True if the person at P still has all three markers (so it's still a player
    // and the game hasn't moved it, e.g. after loading another save).
    internal static bool Valid(long p) =>
        Mem.ReadPtr(p) == personVt && Mem.ReadPtr(p + AttrOffset) == attrVt && Mem.ReadPtr(p + PlayerOffset) == playerVt;

    // A cheap check that the index still matches the loaded career.
    public static bool StillValid()
    {
        // Loading a save can put new people at the same addresses, so the ids must match too.
        (int id, long p)[] sample;
        lock (gate)
        {
            int step = Math.Max(1, byId.Count / 20);
            sample = byId.Where((_, i) => i % step == 0).Take(20).Select(kv => (kv.Key, kv.Value)).ToArray();
        }
        return sample.Length > 0 && sample.All(x => Valid(x.p) && BitConverter.ToInt32(Mem.Read(x.p + 8, 4) ?? new byte[4], 0) == x.id);
    }

    // An index built while FM is still loading a save finds only some people, so each
    // index is followed by one recheck a little later (see Server.Status).
    public static bool Rechecked;

    public static void StartIndex(bool recheck = false)
    {
        if (Indexing) return;
        Indexing = true;
        Rechecked = recheck;
        new Thread(() =>
        {
            try { BuildIndex(recheck); }
            catch (Exception e) { LastError = e.Message; Log.Warn("index: " + e); }
            finally { Indexing = false; }
        }) { IsBackground = true, Name = "FM27LiveEditor index" }.Start();
    }

    static void BuildIndex(bool recheck)
    {
        var started = DateTime.Now;
        lock (clubNames) clubNames.Clear();
        var found = new Dictionary<int, long>();
        var summaries = new List<PlayerSummary>();
        var nationsFound = new Dictionary<int, (long ptr, string name)>();
        var buf = new byte[4 << 20];
        foreach (var (start, size) in Mem.HeapRegions())
        {
            for (long off = 0; off < size; off += buf.Length)
            {
                int len = (int)Math.Min(buf.Length, size - off);
                if (!Mem.Read(start + off, buf, len)) continue;
                for (int k = 0; k + 8 <= len; k += 8)
                {
                    if (BitConverter.ToInt64(buf, k) != personVt) continue;
                    long p = start + off + k;
                    if (!Valid(p)) continue;
                    var s = Summary(p);
                    if (s == null || found.ContainsKey(s.id)) continue;
                    found[s.id] = p;
                    summaries.Add(s);
                    // Every nation a player holds, so nationality can be changed to any of them.
                    long n = Mem.ReadPtr(p + NationAt);
                    if (n != 0 && Mem.ReadPtr(n) == nationVt && Mem.Read(n + 8, 4) is { } nb && !nationsFound.ContainsKey(BitConverter.ToInt32(nb, 0)))
                        nationsFound[BitConverter.ToInt32(nb, 0)] = (n, Mem.ReadLenString(Mem.ReadPtr(n + 0x18)));
                }
            }
        }
        summaries.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.CurrentCultureIgnoreCase));
        lock (gate)
        {
            if (recheck && summaries.Count <= list.Count) { Log.Info($"Index recheck found {summaries.Count} players; keeping {list.Count}"); return; }
            byId = found; list = summaries; nations = nationsFound;
        }
        IndexedAt = DateTime.Now;
        Log.Info($"Indexed {summaries.Count} players in {(DateTime.Now - started).TotalSeconds:F1}s");
    }

    static string EntryName(long entryPtr) => entryPtr == 0 ? null : Mem.ReadLenString(Mem.ReadPtr(entryPtr));

    internal static PlayerSummary Summary(long p)
    {
        var head = Mem.Read(p, 0x90);
        if (head == null) return null;
        int id = BitConverter.ToInt32(head, 8);
        string first = EntryName(BitConverter.ToInt64(head, 0x50));
        string last = EntryName(BitConverter.ToInt64(head, 0x58));
        string name = string.Join(" ", new[] { first, last }.Where(x => !string.IsNullOrEmpty(x)));
        if (name.Length == 0) name = Mem.ReadLenString(BitConverter.ToInt64(head, 0x40));
        if (string.IsNullOrEmpty(name)) return null;
        int born = BitConverter.ToInt16(head, 0x8A);
        var pos = Mem.Read(p + AttrOffset + PositionsAt, 15);
        string best = null;
        if (pos != null) { int i = Array.IndexOf(pos, pos.Max()); best = PositionOrder[i]; }
        return new PlayerSummary(id, name, born, best, Club(p), Uid(p));
    }

    // The player's club name, or null for players without a club. Each step
    // checks the object's type marker, so a wrong guess reads as "no club".
    static readonly Dictionary<long, string> clubNames = new();
    static string Club(long p) => ClubName(ClubOf(p));
    internal static long ClubOf(long p)
    {
        long team = Mem.ReadPtr(p + TeamAt);
        if (team == 0 || Mem.ReadPtr(team) != teamVt) return 0;
        long club = Mem.ReadPtr(team + 0x30);
        return IsClub(club) ? club : 0;
    }
    internal static bool IsClub(long club) => club != 0 && Mem.ReadPtr(club) == clubVt;
    internal static bool IsTeam(long team) => team != 0 && Mem.ReadPtr(team) == teamVt;
    // The name of the club a team belongs to, or null.
    internal static string TeamClub(long team) => IsTeam(team) && Mem.ReadPtr(team + 0x30) is var club && IsClub(club) ? ClubName(club) : null;
    // Every club seen while indexing players, with its name. An index run (the regular recheck
    // too) starts the list again, so this waits for one that's under way rather than answer
    // with half the clubs.
    internal static List<(long club, string name)> KnownClubs()
    {
        for (int i = 0; i < 100 && Indexing; i++) Thread.Sleep(100);
        lock (clubNames) return clubNames.Where(kv => kv.Value != null).Select(kv => (kv.Key, kv.Value)).ToList();
    }
    // Club + 0xB9 is 1 for a women's club: set on all 1,243 women's sides in
    // the Test save (e.g. Manchester United 26574) and on no men's club.
    // FM's unique ID (the one graphics packs name files by) is at +0xC on both people
    // and clubs: Balde 67296654 has face_67296654.png, Manchester United 680 has 680_club.png.
    internal static int? Uid(long obj) => Mem.Read(obj + 0xC, 4) is { } b && BitConverter.ToInt32(b, 0) > 0 ? BitConverter.ToInt32(b, 0) : null;
    // For a women's club, the men's club with the same name (their badges match), or 0.
    internal static long MensClub(long women)
    {
        string name = ClubName(women);
        if (name == null || !name.EndsWith(" (W)")) return 0;
        string men = name[..^4];
        lock (clubNames) return clubNames.FirstOrDefault(kv => kv.Value == men).Key;
    }
    internal static bool IsWomen(long club) => Mem.Read(club + 0xB9, 1) is { } b && b[0] == 1;
    internal static string ClubName(long club)
    {
        if (club == 0) return null;
        lock (clubNames) if (clubNames.TryGetValue(club, out var known)) return known;
        string name = Mem.ReadLenString(Mem.ReadPtr(club + 0xC0));
        // Women's clubs share their name with the men's club, so mark them.
        if (name != null && IsWomen(club)) name += " (W)";
        lock (clubNames) clubNames[club] = name;
        return name;
    }

    // The contract, or 0 if the player has none (or it isn't where we expect).
    internal static long Contract(long p)
    {
        long c = Mem.ReadPtr(p + ContractAt);
        return c != 0 && Mem.ReadPtr(c) == contractVt && Mem.ReadPtr(c + 8) == p ? c : 0;
    }

    // FM's dates: u16 day of year, with flags in the bits above the ninth, then u16 year.
    internal static string Date(byte[] b, int at)
    {
        int day = BitConverter.ToUInt16(b, at) & 0x1FF, year = BitConverter.ToInt16(b, at + 2);
        if (year < 1900 || year > 2200 || day < 1 || day > 366) return null;
        return new DateTime(year, 1, 1).AddDays(day - 1).ToString("yyyy-MM-dd");
    }

    static Dictionary<string, object> ContractInfo(long p)
    {
        long c = Contract(p);
        var b = c == 0 ? null : Mem.Read(c, 0x78);
        if (b == null) return null;
        int status = b[StatusAt];
        return new()
        {
            ["club"] = TeamClub(BitConverter.ToInt64(b, 0x10)),
            ["wage"] = BitConverter.ToInt32(b, WageAt), ["ends"] = Date(b, EndsAt), ["joined"] = Date(b, JoinedAt),
            ["squadStatus"] = SquadStatuses.ContainsKey(status) ? status : null,
            ["squadNumber"] = b[SquadNumberAt] is >= 1 and <= 99 ? b[SquadNumberAt] : null,
            ["clauses"] = Clauses(b).Select((x, i) => ClauseTypes.TryGetValue(x.type, out var t)
                ? new Dictionary<string, object> { ["index"] = i, ["type"] = (int)x.type, ["name"] = t.name, ["money"] = t.money, ["value"] = t.money ? x.amount : x.extra }
                : new Dictionary<string, object> { ["index"] = i, ["type"] = (int)x.type }).ToList(),
        };
    }

    // The clause list, or empty if it doesn't look like one (at most 64 entries).
    static List<(int amount, short extra, short type)> Clauses(byte[] contract)
    {
        long begin = BitConverter.ToInt64(contract, ClausesAt), end = BitConverter.ToInt64(contract, ClausesAt + 8);
        var list = new List<(int, short, short)>();
        if (begin == 0 || end <= begin || (end - begin) % 8 != 0 || end - begin > 64 * 8) return list;
        var b = Mem.Read(begin, (int)(end - begin));
        if (b == null) return list;
        for (int i = 0; i < b.Length; i += 8) list.Add((BitConverter.ToInt32(b, i), BitConverter.ToInt16(b, i + 4), BitConverter.ToInt16(b, i + 6)));
        return list;
    }

    static Dictionary<int, (long ptr, string name)> nations = new();

    // The nations players hold in this career, by name.
    public static List<Dictionary<string, object>> Nations()
    {
        lock (gate) return nations.Where(kv => kv.Value.name != null).OrderBy(kv => kv.Value.name, StringComparer.CurrentCultureIgnoreCase)
            .Select(kv => new Dictionary<string, object> { ["id"] = kv.Key, ["name"] = kv.Value.name }).ToList();
    }

    // Changes his nationality (P + 0x68) to another nation. FM keeps no nation with his caps
    // (international data at P - 0x168), so they show for the new nation. The national squad he's in
    // now (P - 0x160) and any second nationalities are left as they are. Returns 1 if it changed.
    public static int SetNation(long p, int id)
    {
        (long ptr, string name) to;
        lock (gate) if (!nations.TryGetValue(id, out to)) throw new EditorError("That nation isn't in this career.", 400);
        long now = Mem.ReadPtr(p + NationAt);
        if (now == to.ptr) return 0;
        if (now == 0 || Mem.ReadPtr(now) != nationVt || Mem.ReadPtr(to.ptr) != nationVt) throw new EditorError("His nationality isn't where the editor expects, so it wasn't changed.", 409);
        Backup.EnsureBackedUp();
        if (!Mem.Write(p + NationAt, BitConverter.GetBytes(to.ptr))) throw new EditorError("The game refused the change.", 500);
        Log.Info($"Nationality of player at 0x{p:X}: {Nation(p)}");
        return 1;
    }

    static int? NationId(long p)
    {
        long n = Mem.ReadPtr(p + NationAt);
        return n != 0 && Mem.ReadPtr(n) == nationVt && Mem.Read(n + 8, 4) is { } b ? BitConverter.ToInt32(b, 0) : null;
    }

    static string Nation(long p)
    {
        long n = Mem.ReadPtr(p + NationAt);
        return n != 0 && Mem.ReadPtr(n) == nationVt ? Mem.ReadLenString(Mem.ReadPtr(n + 0x18)) : null;
    }

    // A date as FM stores it, keeping the flags of the date it replaces.
    internal static byte[] EncodeDate(DateTime d, byte[] old, int at)
    {
        int flags = BitConverter.ToUInt16(old, at) & ~0x1FF;
        return BitConverter.GetBytes((ushort)(flags | d.DayOfYear)).Concat(BitConverter.GetBytes((short)d.Year)).ToArray();
    }

    static (int ca, int pa) Ability(long p)
    {
        var b = Mem.Read(p + CaAt, 4) ?? throw new EditorError("Couldn't read the player.", 500);
        return (BitConverter.ToInt16(b, 0), BitConverter.ToInt16(b, 2));
    }

    public static List<PlayerSummary> Search(string q)
    {
        q = (q ?? "").Trim();
        lock (gate)
        {
            if (q.Length < 2) return new();
            var words = Text.Words(q);
            string fq = Text.Fold(q);
            // Every word must be in the name or the club ("mount", "man utd mount"...).
            return list.Where(s => Text.Matches(words, s.name, s.club))
                .OrderBy(s => Text.Fold(s.name).Contains(fq) ? 0 : 1)
                .ThenBy(s => Text.Fold(s.name).StartsWith(fq) ? 0 : 1)
                .Take(100).ToList();
        }
    }

    // After a transfer: the search list shows the player's new club.
    internal static void Refresh(int id)
    {
        long p;
        lock (gate) if (!byId.TryGetValue(id, out p)) return;
        var s = Summary(p);
        if (s == null) return;
        lock (gate) { int i = list.FindIndex(x => x.id == id); if (i >= 0) list[i] = s; }
    }

    internal static long Find(int id)
    {
        long p;
        lock (gate) if (!byId.TryGetValue(id, out p)) throw new EditorError("That player isn't in the loaded career. Search again.", 404);
        if (!Valid(p) || BitConverter.ToInt32(Mem.Read(p + 8, 4) ?? new byte[4], 0) != id)
            throw new EditorError("The game has moved its data (did you load another save?). Search again.", 409);
        return p;
    }

    public static Dictionary<string, object> Get(int id)
    {
        long p = Find(id);
        var s = Summary(p);
        var rec = Mem.Read(p + AttrOffset, AttributesAt + AttributeOrder.Length) ?? throw new EditorError("Couldn't read the player.", 500);
        var attributes = new Dictionary<string, int>();
        for (int i = 0; i < AttributeOrder.Length; i++) attributes[AttributeOrder[i]] = Shown(rec[AttributesAt + i]);
        var positions = new Dictionary<string, int>();
        for (int i = 0; i < PositionOrder.Length; i++) positions[PositionOrder[i]] = rec[PositionsAt + i];
        return new()
        {
            ["id"] = id, ["uid"] = Uid(p), ["clubUid"] = ClubOf(p) is var club && club != 0 ? Uid(club) : null, ["clubBadgeUid"] = club != 0 && MensClub(club) is var men && men != 0 ? Uid(men) : null, ["name"] = s.name, ["born"] = s.born, ["position"] = s.position, ["club"] = s.club,
            ["ca"] = Ability(p).ca, ["pa"] = Ability(p).pa, ["contract"] = ContractInfo(p), ["loan"] = Loans.Info(p), ["health"] = Health.Info(p), ["status"] = Status(p),
            ["fullName"] = Mem.ReadLenString(Mem.ReadPtr(p + 0x40)), ["nation"] = Nation(p), ["nationId"] = NationId(p),
            ["attributes"] = attributes, ["positions"] = positions,
        };
    }

    // Research only (see Research.cs): every player's id and address.
    internal static List<(int id, long p)> All() { lock (gate) return byId.Select(kv => (kv.Key, kv.Value)).ToList(); }

    // Research only (see Research.cs): the raw bytes around a spread of players.
    public static List<Dictionary<string, object>> Dump(int n, int from, int len)
    {
        List<(int id, long p)> all;
        lock (gate) all = byId.Select(kv => (kv.Key, kv.Value)).ToList();
        int step = Math.Max(1, all.Count / Math.Max(1, n));
        var outList = new List<Dictionary<string, object>>();
        for (int i = 0; i < all.Count && outList.Count < n; i += step)
        {
            var (id, p) = all[i];
            if (!Valid(p)) continue;
            var bytes = Mem.Read(p + from, len);
            var s = Summary(p);
            if (bytes == null || s == null) continue;
            outList.Add(new() { ["id"] = id, ["p"] = p.ToString("X"), ["name"] = s.name, ["born"] = s.born, ["bytes"] = Convert.ToBase64String(bytes) });
        }
        return outList;
    }

    // 1-100 stored, 1-20 shown.
    static int Shown(byte v) => Math.Clamp((int)Math.Round(v / 5.0, MidpointRounding.AwayFromZero), 1, 20);

    internal static Dictionary<string, int> Status(long p)
    {
        var b = Mem.Read(p - 0x40, 0x40);
        if (b == null) return null;
        return StatusFields.ToDictionary(f => f.key, f => f.size == 1 ? (int)b[f.at + 0x40] : BitConverter.ToUInt16(b, f.at + 0x40));
    }

    // Writes checked status values (see StatusFields) for one player; returns how many changed.
    internal static int WriteStatus(long p, Dictionary<string, int> values)
    {
        var writes = new List<(int at, byte[] value)>();
        var cur = Status(p) ?? throw new EditorError("Couldn't read the player.", 500);
        foreach (var (key, v) in values ?? new())
        {
            var f = StatusFields.FirstOrDefault(x => x.key == key) ?? throw new EditorError($"Unknown value {key}.", 400);
            if (v < f.min || v > f.max) throw new EditorError($"{key} must be {f.min} to {f.max}.", 400);
            if (v != cur[key]) writes.Add((f.at, f.size == 1 ? new[] { (byte)v } : BitConverter.GetBytes((ushort)v)));
        }
        if (writes.Count == 0) return 0;
        if (!Valid(p)) throw new EditorError("The game moved its data. Nothing was changed.", 409);
        foreach (var (at, value) in writes)
            if (!Mem.Write(p + at, value)) throw new EditorError("The game refused the change. Nothing more was written.", 500);
        return writes.Count;
    }

    // The squad numbers other players of the same team wear.
    static HashSet<int> NumbersInTeam(long p, long contract)
    {
        var taken = new HashSet<int>();
        long team = Mem.ReadPtr(contract + 0x10);
        var head = team == 0 ? null : Mem.Read(team + 0x38, 16);
        if (head == null) return taken;
        long begin = BitConverter.ToInt64(head, 0), end = BitConverter.ToInt64(head, 8);
        if (begin == 0 || end < begin || end - begin > 512 * 8) return taken;
        var list = Mem.Read(begin, (int)(end - begin));
        for (int i = 0; list != null && i < list.Length; i += 8)
        {
            long other = BitConverter.ToInt64(list, i) + 0x288;
            if (other == p || !Valid(other)) continue;
            long c = Contract(other);
            var n = c == 0 ? null : Mem.Read(c + SquadNumberAt, 1);
            if (n != null && n[0] is >= 1 and <= 99) taken.Add(n[0]);
        }
        return taken;
    }

    public static Dictionary<string, object> Set(int id, Dictionary<string, int> attributes, Dictionary<string, int> positions, int? ca = null, int? pa = null, ContractChanges contract = null, Dictionary<string, int> status = null)
    {
        long p = Find(id);
        long rec = p + AttrOffset;
        var cur = Mem.Read(rec, AttributesAt + AttributeOrder.Length) ?? throw new EditorError("Couldn't read the player.", 500);
        var writes = new List<(int at, byte value)>();
        foreach (var (key, v) in attributes ?? new())
        {
            int i = Array.IndexOf(AttributeOrder, key);
            if (i < 0) throw new EditorError($"Unknown attribute {key}.", 400);
            if (v < 1 || v > 20) throw new EditorError($"{key} must be 1 to 20.", 400);
            // Unchanged on screen: leave the stored value (it may have decimals).
            if (Shown(cur[AttributesAt + i]) != v) writes.Add((AttributesAt + i, (byte)(v * 5)));
        }
        foreach (var (key, v) in positions ?? new())
        {
            int i = Array.IndexOf(PositionOrder, key);
            if (i < 0) throw new EditorError($"Unknown position {key}.", 400);
            if (v < 1 || v > 20) throw new EditorError($"{key} must be 1 to 20.", 400);
            if (cur[PositionsAt + i] != v) writes.Add((PositionsAt + i, (byte)v));
        }
        // Ability: 1-200, and potential can't be below current ability.
        var (curCa, curPa) = Ability(p);
        int newCa = ca ?? curCa, newPa = pa ?? curPa;
        if (newCa < 1 || newCa > 200 || newPa < 1 || newPa > 200) throw new EditorError("Ability must be 1 to 200.", 400);
        if (newPa < newCa) throw new EditorError("Potential ability can't be lower than current ability.", 400);
        var abilityWrites = new List<(int at, short value)>();
        if (newCa != curCa) abilityWrites.Add((CaAt, (short)newCa));
        if (newPa != curPa) abilityWrites.Add((PaAt, (short)newPa));

        // Contract: weekly wage and end date.
        var contractWrites = new List<(int at, byte[] value)>();
        long c = 0;
        var clauseWrites = new List<(long entry, int at, byte[] value, byte[] was)>();
        if (contract != null && (contract.wage != null || contract.ends != null || contract.squadStatus != null || contract.squadNumber != null || contract.clauses?.Count > 0))
        {
            c = Contract(p);
            if (c == 0) throw new EditorError("This player has no contract to change.", 400);
            var cur2 = Mem.Read(c, 0x78) ?? throw new EditorError("Couldn't read the contract.", 500);
            // Kit number 1-99, or 0 for none (stored as 255); not one a teammate wears.
            if (contract.squadNumber is int num)
            {
                byte stored = num == 0 ? (byte)255 : (byte)num;
                if (num < 0 || num > 99) throw new EditorError("Squad number must be 1 to 99, or none.", 400);
                if (num != 0 && NumbersInTeam(p, c).Contains(num)) throw new EditorError($"A teammate already wears number {num}.", 400);
                if (stored != cur2[SquadNumberAt]) contractWrites.Add((SquadNumberAt, new[] { stored }));
            }
            if (contract.squadStatus is int st && st != cur2[StatusAt])
            {
                if (!SquadStatuses.ContainsKey(st)) throw new EditorError("That squad status isn't supported.", 400);
                contractWrites.Add((StatusAt, new[] { (byte)st }));
                // The copy normally matches; leave it alone if FM is using it for something else.
                if (cur2[StatusCopyAt] == cur2[StatusAt]) contractWrites.Add((StatusCopyAt, new[] { (byte)st }));
            }
            // Clauses: change the number on an existing clause of a known type. Never adds or removes clauses.
            var have = Clauses(cur2);
            long begin = BitConverter.ToInt64(cur2, ClausesAt);
            foreach (var (key, v) in contract.clauses ?? new())
            {
                if (!int.TryParse(key, out int i) || i < 0 || i >= have.Count) throw new EditorError("That clause isn't on the contract any more.", 409);
                var (amount, extra, type) = have[i];
                if (!ClauseTypes.TryGetValue(type, out var t)) throw new EditorError("That clause can't be edited.", 400);
                var was = BitConverter.GetBytes(amount).Concat(BitConverter.GetBytes(extra)).Concat(BitConverter.GetBytes(type)).ToArray();
                long entry = begin + i * 8;
                if (t.money)
                {
                    if (v < 0 || v > 2_000_000_000) throw new EditorError($"{t.name} must be between 0 and 2,000,000,000.", 400);
                    if (v != amount) clauseWrites.Add((entry, 0, BitConverter.GetBytes(v), was));
                }
                else
                {
                    if (v < 0 || v > 100) throw new EditorError($"{t.name} must be 0 to 100.", 400);
                    if (v != extra) clauseWrites.Add((entry, 4, BitConverter.GetBytes((short)v), was));
                }
            }
            if (contract.wage != null)
            {
                if (contract.wage < 0 || contract.wage > 10_000_000) throw new EditorError("Wage must be between 0 and 10,000,000 a week.", 400);
                if (contract.wage != BitConverter.ToInt32(cur2, WageAt)) contractWrites.Add((WageAt, BitConverter.GetBytes(contract.wage.Value)));
            }
            if (contract.ends != null)
            {
                if (!DateTime.TryParseExact(contract.ends, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var ends) || ends.Year < 2000 || ends.Year > 2100)
                    throw new EditorError("The contract end date isn't valid.", 400);
                var start = Date(cur2, JoinedAt);
                if (start != null && ends <= DateTime.Parse(start)) throw new EditorError("The contract can't end before the player joined.", 400);
                if (contract.ends != Date(cur2, EndsAt)) contractWrites.Add((EndsAt, EncodeDate(ends, cur2, EndsAt)));
            }
        }

        // Status values are checked before anything is written.
        foreach (var (key, v) in status ?? new())
        {
            var f = StatusFields.FirstOrDefault(x => x.key == key) ?? throw new EditorError($"Unknown value {key}.", 400);
            if (v < f.min || v > f.max) throw new EditorError($"{key} must be {f.min} to {f.max}.", 400);
        }
        bool statusChanges = status != null && status.Any(kv => Status(p)?[kv.Key] != kv.Value);
        if (writes.Count > 0 || abilityWrites.Count > 0 || contractWrites.Count > 0 || clauseWrites.Count > 0 || statusChanges)
        {
            Backup.EnsureBackedUp();
            // Check once more right before writing, then write each byte.
            if (!Valid(p)) throw new EditorError("The game moved its data. Nothing was changed.", 409);
            foreach (var (at, value) in writes)
                if (!Mem.Write(rec + at, new[] { value })) throw new EditorError("The game refused the change. Nothing more was written.", 500);
            foreach (var (at, value) in abilityWrites)
                if (!Mem.Write(p + at, BitConverter.GetBytes(value))) throw new EditorError("The game refused the change. Nothing more was written.", 500);
            WriteStatus(p, status);
            if ((contractWrites.Count > 0 || clauseWrites.Count > 0) && Contract(p) != c) throw new EditorError("The game moved the contract. It wasn't changed.", 409);
            foreach (var (at, value) in contractWrites)
                if (!Mem.Write(c + at, value)) throw new EditorError("The game refused the change. Nothing more was written.", 500);
            foreach (var (entry, at, value, was) in clauseWrites)
            {
                // The clause must still be exactly what we read (same place, same values).
                var now = Mem.Read(entry, 8);
                if (now == null || !now.SequenceEqual(was)) throw new EditorError("The game changed the contract's clauses. The rest wasn't changed.", 409);
                if (!Mem.Write(entry + at, value)) throw new EditorError("The game refused the change. Nothing more was written.", 500);
            }
            Log.Info($"Edited {Summary(p)?.name} ({id}): {writes.Count + abilityWrites.Count + contractWrites.Count + clauseWrites.Count} values");
        }
        return Get(id);
    }
}

// Whether this build of FM27 (any store, any update) has everything the editor needs: all of
// it is found in the running game module (see Addresses), and editing stays off if not.
public static class Layout
{
    public static bool Matches()
    {
        if (!Addresses.Resolve(Mem.ModuleBase("game_plugin.dll"))) return false;
        Players.UseAddresses();
        return true;
    }
}

public class ContractChanges
{
    public int? wage { get; set; }
    public string ends { get; set; }
    public int? squadStatus { get; set; }
    // 1-99, or 0 for no number.
    public int? squadNumber { get; set; }
    // Clause position on the contract (a string, as JSON keys are) -> new amount or percentage.
    public Dictionary<string, int> clauses { get; set; }
}

public class EditorError : Exception
{
    public int Status;
    public EditorError(string message, int status) : base(message) { Status = status; }
}
