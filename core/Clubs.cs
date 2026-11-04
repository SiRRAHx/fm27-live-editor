// Clubs and their finances. Clubs are found through the players' teams while
// indexing (see Players.ClubOf), so every club with a player is listed.
//
// Layout (FM27 0.9.2409): club +8 club id, +0xC0 name, +0x150 the finances
// object (type marker at +0). Finances: +0xC balance, +0x7C4 transfer budget,
// +0x808 wage budget per week, +0x814 current wages per week (all int32, pounds).
using System;
using System.Collections.Generic;
using System.Linq;

namespace FM27LiveEditor;

public static class Clubs
{
    const int IdAt = 8, FinancesAt = 0x150;
    internal const int BalanceAt = 0xC, TransferBudgetAt = 0x7C4, WageBudgetAt = 0x808, WagesAt = 0x814;

    // From FMLibreEditor's layout, not yet checked in game: the senior team's reputation
    // (+0xA8, u16 0-10000), the club info record (club +0x100) with the founding year and
    // 1-20 facility ratings, and corporate facilities in the finances (+0x8A9).
    const int TeamsAt = 0x18, InfoAt = 0x100, ReputationAt = 0xA8, FoundedAt = 0xD8, CorporateAt = 0x8A9;

    // Professional status in the club object: 1 professional, 2 semi-professional, 3 amateur.
    // Found by comparing 5,500 clubs against FM's club screens (Man Utd and Altrincham
    // professional, Scarborough Athletic semi-professional); the write is not yet tested.
    const int StatusAt = 0x16A;
    public static readonly Dictionary<int, string> Statuses = new() { [1] = "Professional", [2] = "Semi-Professional", [3] = "Amateur" };
    public record Facility(string key, int at);
    public static readonly Facility[] Facilities =
    {
        new("training", 0x120), new("youthFacilities", 0x12B), new("youthCoaching", 0x12C), new("youthRecruitment", 0x12D),
    };

    internal static long SeniorTeam(long club)
    {
        var head = Mem.Read(club + TeamsAt, 16);
        long first = head == null ? 0 : BitConverter.ToInt64(head, 0);
        long team = first == 0 ? 0 : Mem.ReadPtr(first);
        return Players.IsTeam(team) ? team : 0;
    }

    // The info record, only when it looks right: a founding year and facilities in range.
    internal static long Info(long club)
    {
        long info = Mem.ReadPtr(club + InfoAt);
        var b = info == 0 ? null : Mem.Read(info, 0x130);
        if (b == null) return 0;
        int founded = BitConverter.ToUInt16(b, FoundedAt);
        bool ok = founded is >= 1800 and <= 2100 && Facilities.All(f => b[f.at] is >= 1 and <= 20);
        return ok ? info : 0;
    }

    static int? Reputation(long club)
    {
        long team = SeniorTeam(club);
        var b = team == 0 ? null : Mem.Read(team + ReputationAt, 2);
        int? v = b == null ? null : BitConverter.ToUInt16(b, 0);
        return v is >= 0 and <= 10000 ? v : null;
    }

    internal static long Finances(long club)
    {
        long f = Mem.ReadPtr(club + FinancesAt);
        return f != 0 && Addresses.Finances != 0 && Mem.ReadPtr(f) == Addresses.Finances ? f : 0;
    }

    internal static int Id(long club) => BitConverter.ToInt32(Mem.Read(club + IdAt, 4) ?? new byte[4], 0);

    // Where to look next for a badge: a women's club uses the men's club's badge
    // when the logo pack has none of its own.
    static int? BadgeUid(long club) => Players.MensClub(club) is var men && men != 0 ? Players.Uid(men) : null;

    public static List<Dictionary<string, object>> Search(string q)
    {
        q = (q ?? "").Trim();
        if (q.Length < 2) return new();
        var words = Text.Words(q);
        string fq = Text.Fold(q);
        return Players.KnownClubs()
            .Where(c => Text.Matches(words, c.name))
            .OrderBy(c => Text.Fold(c.name).StartsWith(fq) ? 0 : 1)
            .ThenBy(c => c.name, StringComparer.CurrentCultureIgnoreCase)
            .Take(100)
            .Select(c => new Dictionary<string, object> { ["id"] = Id(c.club), ["uid"] = Players.Uid(c.club), ["badgeUid"] = BadgeUid(c.club), ["name"] = c.name })
            .ToList();
    }

    internal static long Find(int id)
    {
        foreach (var (club, _) in Players.KnownClubs())
            if (Players.IsClub(club) && Id(club) == id) return club;
        throw new EditorError("That club isn't in the loaded career. Search again.", 404);
    }

    public static Dictionary<string, object> Get(int id)
    {
        long club = Find(id);
        long f = Finances(club);
        var b = f == 0 ? null : Mem.Read(f, CorporateAt + 1);
        long info = Info(club);
        var ib = info == 0 ? null : Mem.Read(info, 0x130);
        var facilities = ib == null ? new Dictionary<string, int>() : Facilities.ToDictionary(x => x.key, x => (int)ib[x.at]);
        if (b != null && b[CorporateAt] is >= 1 and <= 20) facilities["corporate"] = b[CorporateAt];
        return new()
        {
            ["id"] = id,
            ["name"] = Players.ClubName(club), ["uid"] = Players.Uid(club), ["badgeUid"] = BadgeUid(club),
            ["reputation"] = Reputation(club),
            ["league"] = League(SeniorTeam(club)).name,
            ["founded"] = ib == null ? null : BitConverter.ToUInt16(ib, FoundedAt),
            ["status"] = Mem.Read(club + StatusAt, 1) is { } st && Statuses.ContainsKey(st[0]) ? st[0] : null,
            ["facilities"] = facilities,
            ["finances"] = b == null ? null : new Dictionary<string, object>
            {
                ["balance"] = BitConverter.ToInt32(b, BalanceAt),
                ["transferBudget"] = BitConverter.ToInt32(b, TransferBudgetAt),
                ["wageBudget"] = BitConverter.ToInt32(b, WageBudgetAt),
                ["wages"] = BitConverter.ToInt32(b, WagesAt),
            },
        };
    }

    // Every player in any of the club's teams (first team, youth sides), as person objects.
    internal static List<long> Players_(long club)
    {
        var teamsHead = Mem.Read(club + 0x18, 16);
        var found = new HashSet<long>();
        if (teamsHead == null) return new();
        long tb = BitConverter.ToInt64(teamsHead, 0), te = BitConverter.ToInt64(teamsHead, 8);
        if (tb == 0 || te < tb || te - tb > 64 * 8) return new();
        var teams = Mem.Read(tb, (int)(te - tb)) ?? Array.Empty<byte>();
        for (int i = 0; i < teams.Length; i += 8)
        {
            long team = BitConverter.ToInt64(teams, i);
            if (!Players.IsTeam(team)) continue;
            var head = Mem.Read(team + 0x38, 16);
            if (head == null) continue;
            long b = BitConverter.ToInt64(head, 0), e = BitConverter.ToInt64(head, 8);
            if (b == 0 || e < b || e - b > 512 * 8) continue;
            var list = Mem.Read(b, (int)(e - b)) ?? Array.Empty<byte>();
            for (int k = 0; k < list.Length; k += 8)
            {
                long p = BitConverter.ToInt64(list, k) + 0x288;
                // Only players under contract with this club (not those here on loan).
                if (Players.Valid(p) && Players.ClubOf(p) == club) found.Add(p);
            }
        }
        return found.ToList();
    }

    // Sets morale, condition or sharpness for the whole club. Returns how many players changed.
    public static int SetSquad(int id, Dictionary<string, int> values)
    {
        long club = Find(id);
        var allowed = new[] { "morale", "condition", "sharpness" };
        if (values.Keys.Any(k => !allowed.Contains(k))) throw new EditorError("Only morale, condition and sharpness can be set for a whole squad.", 400);
        var players = Players_(club);
        if (players.Count == 0) throw new EditorError("Couldn't find this club's players.", 400);
        Backup.EnsureBackedUp();
        int changed = players.Count(p => Players.WriteStatus(p, values) > 0);
        Log.Info($"Set {string.Join(", ", values.Keys)} for {changed} of {players.Count} players at {Players.ClubName(club)}");
        return changed;
    }

    public static Dictionary<string, object> Set(int id, FinanceChanges changes)
    {
        long club = Find(id);
        long f = Finances(club);
        var writes = new List<(int at, int value)>();
        void Add(int? v, int at, string name, int min)
        {
            if (v == null) return;
            if (v < min || v > 2_000_000_000) throw new EditorError($"{name} must be between {min:N0} and 2,000,000,000.", 400);
            writes.Add((at, v.Value));
        }
        Add(changes.balance, BalanceAt, "Balance", -2_000_000_000);
        Add(changes.transferBudget, TransferBudgetAt, "Transfer budget", 0);
        Add(changes.wageBudget, WageBudgetAt, "Wage budget", 0);

        // Reputation and facilities: checked first, written after the finances.
        var other = new List<(long at, byte[] value)>();
        if (changes.reputation is int rep)
        {
            long team = SeniorTeam(club);
            if (team == 0 || Reputation(club) == null) throw new EditorError("Couldn't find this club's reputation.", 400);
            if (rep < 0 || rep > 10000) throw new EditorError("Reputation must be 0 to 10,000.", 400);
            other.Add((team + ReputationAt, BitConverter.GetBytes((ushort)rep)));
        }
        if (changes.status is int status)
        {
            if (!Statuses.ContainsKey(status)) throw new EditorError("Status must be professional, semi-professional or amateur.", 400);
            var cur = Mem.Read(club + StatusAt, 1);
            if (cur == null || !Statuses.ContainsKey(cur[0])) throw new EditorError("Couldn't find this club's status.", 400);
            other.Add((club + StatusAt, new[] { (byte)status }));
        }
        foreach (var (key, v) in changes.facilities ?? new())
        {
            if (v < 1 || v > 20) throw new EditorError("Facilities must be 1 to 20.", 400);
            if (key == "corporate")
            {
                if (f == 0) throw new EditorError("Couldn't find this club's finances.", 400);
                other.Add((f + CorporateAt, new[] { (byte)v }));
                continue;
            }
            var fac = Facilities.FirstOrDefault(x => x.key == key) ?? throw new EditorError($"Unknown facility {key}.", 400);
            long info = Info(club);
            if (info == 0) throw new EditorError("Couldn't find this club's facilities.", 400);
            other.Add((info + fac.at, new[] { (byte)v }));
        }
        if (writes.Count > 0 && f == 0) throw new EditorError("Couldn't find this club's finances.", 400);
        if (writes.Count > 0 || other.Count > 0)
        {
            Backup.EnsureBackedUp();
            if (Id(club) != id) throw new EditorError("The game moved its data. Nothing was changed.", 409);
            foreach (var (at, value) in other)
                if (!Mem.Write(at, value)) throw new EditorError("The game refused the change. Nothing more was written.", 500);
            if (other.Count > 0) Log.Info($"Edited reputation/facilities of {Players.ClubName(club)} ({id}): {other.Count} values");
        }
        if (writes.Count > 0)
        {
            Backup.EnsureBackedUp();
            if (Finances(club) != f || Id(club) != id) throw new EditorError("The game moved its data. Nothing was changed.", 409);
            foreach (var (at, value) in writes)
                if (!Mem.Write(f + at, BitConverter.GetBytes(value))) throw new EditorError("The game refused the change. Nothing more was written.", 500);
            Log.Info($"Edited the finances of {Players.ClubName(club)} ({id}): {writes.Count} values");
        }
        return Get(id);
    }

    // The league a team plays in: team +0x50 -> db::COMP (+0x8 its number, +0x48 its short name,
    // as FM's screens show it). (0, null) if it isn't there.
    internal static (int id, string name) League(long team)
    {
        long comp = team == 0 ? 0 : Mem.ReadPtr(team + 0x50);
        long mod = Mem.ModuleBase("game_plugin.dll");
        if (comp == 0 || LoanStart.ClassOf(mod, Mem.ReadPtr(comp)) != ".?AVCOMP@db@@") return (0, null);
        var b = Mem.Read(comp + 8, 4);
        return b == null ? (0, null) : (BitConverter.ToInt32(b, 0), Mem.ReadLenString(Mem.ReadPtr(comp + 0x48)));
    }
}

public class FinanceChanges
{
    public int? balance { get; set; }
    public int? transferBudget { get; set; }
    public int? wageBudget { get; set; }
    // 0-10000, the senior team's reputation.
    public int? reputation { get; set; }
    // 1 professional, 2 semi-professional, 3 amateur.
    public int? status { get; set; }
    // 1-20: training, youthFacilities, youthCoaching, youthRecruitment, corporate.
    public Dictionary<string, int> facilities { get; set; }
}
