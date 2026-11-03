// Moving players between clubs, swapping two players, and releasing a player.
//
// The editor never asks the game for memory. A move points the player and his own
// contract at the new club's senior team, adds him to that team's player list and takes
// him out of every list of his old club. A team's list is (begin, end, capacity) at team
// +0x38; the game's allocator hands out blocks in fixed sizes with no headers, so a list
// can take one more player only below its capacity or in the zero bytes up to the end of
// its block. A swap reuses the two players' slots, so full lists are no obstacle.
//
// Only simple cases move: a player under contract with the club he plays for (not on loan,
// not a player-coach). Players can leave the manager's own club too: its tactic, bench, training
// and squad registration still point at him, but FM drops him from them itself (tested 3 Nov with
// Kai Rooney, U18, and Karl Darlow, first-team bench and Premier League registration).
//
// Layout and approach from FMLibreEditor (https://github.com/tozmoz/FMLibreEditor, GPL-3.0)
// and fm-editor by barak14, checked against our own snapshots of a real transfer
// (research\transfer-elhilali*). Every write carries the bytes it expects to replace, and
// nothing is written unless all of them still match.
using System;
using System.Collections.Generic;
using System.Linq;

namespace FM27LiveEditor;

public static class Transfers
{
    const int PlayerPart = -0x288, TeamAt = -0x158, ContractAt = 0xA8;
    const int OwnerAt = 0x08, EmployerAt = 0x10, SeniorAt = 0x18, WageAt = 0x20, JobAt = 0x26;
    const int SignedAt = 0x44, ExpiresAt = 0x48, JoinedAt = 0x4C, ListingAt = 0x57, NumberAt = 0x5D;
    const int TeamClubAt = 0x30, TeamPlayersAt = 0x38, CaptainAt = 0x88, ViceCaptainAt = 0x90;
    const int ClubTeamsAt = 0x18, ClubWomenAt = 0xB9;
    const long CurrentDateRva = 0x56257C8;
    const byte PlayerJob = 1, NoSquadNumber = 255, Listed = 0b11;
    const int LongestList = 512;
    static readonly int[] BlockSizes = { 144, 160, 192, 224, 256, 288, 320, 384, 448, 512 };

    record Patch(long at, byte[] was, byte[] now);
    static List<Patch> last;
    static string lastText;
    static readonly object gate = new();

    class PtrList
    {
        public long header, begin, end, capacity;
        public int Count => (int)((end - begin) / 8);

        public static PtrList Read(long header)
        {
            var b = Mem.Read(header, 24);
            if (b == null) return null;
            var l = new PtrList { header = header, begin = BitConverter.ToInt64(b, 0), end = BitConverter.ToInt64(b, 8), capacity = BitConverter.ToInt64(b, 16) };
            bool sane = l.begin > 0x10000 && l.begin <= l.end && l.end <= l.capacity && (l.end - l.begin) % 8 == 0
                && (l.capacity - l.begin) % 8 == 0 && l.capacity - l.begin <= 8 * LongestList;
            return sane ? l : null;
        }

        public long[] Elements()
        {
            if (end == begin) return Array.Empty<long>();
            var b = Mem.Read(begin, (int)(end - begin)) ?? throw new EditorError("Couldn't read a squad list.", 500);
            return Enumerable.Range(0, Count).Select(i => BitConverter.ToInt64(b, i * 8)).ToArray();
        }

        // How many pointers the block holds: its capacity, or the whole allocator block when
        // its size is known and nothing has been written past the capacity.
        public int Room()
        {
            int cap = (int)((capacity - begin) / 8), bytes = (int)(capacity - begin);
            if (bytes <= BlockSizes[0]) return cap;
            int block = BlockSizes.FirstOrDefault(s => s >= bytes);
            if (block == 0 || block == bytes) return cap;
            var spare = Mem.Read(capacity, block - bytes);
            return spare != null && spare.All(x => x == 0) ? block / 8 : cap;
        }
    }

    class Departure
    {
        public int id; public long p, obj, team, contract, employer, club; public byte[] record;
        public List<long> teams;
        public List<(long team, PtrList list, int index, long[] elements)> leaving = new();
    }

    class Arrival { public long club, senior; public PtrList list; public long[] elements; }

    class Patches
    {
        public readonly List<Patch> list = new();
        public void Add(long at, byte[] was, byte[] now) { if (!was.SequenceEqual(now)) list.Add(new Patch(at, was, now)); }
        public void Add(long at, long was, long now) => Add(at, BitConverter.GetBytes(was), BitConverter.GetBytes(now));
    }

    static byte[] Words(IEnumerable<long> v) => v.SelectMany(BitConverter.GetBytes).ToArray();

    // The in-game date, or null if it doesn't look like one.
    internal static DateTime? Today()
    {
        var b = Mem.Read(Mem.ModuleBase("game_plugin.dll") + CurrentDateRva, 4);
        if (b == null) return null;
        int day = BitConverter.ToUInt16(b, 0) & 0x1FF, year = BitConverter.ToInt16(b, 2);
        if (year < 2000 || year > 2200 || day < 1 || day > 366) return null;
        return new DateTime(year, 1, 1).AddDays(day - 1);
    }

    static Departure Leaving(int id)
    {
        long p = Players.Find(id);
        var d = new Departure { id = id, p = p, obj = p + PlayerPart };
        d.team = Mem.ReadPtr(p + TeamAt);
        d.contract = Mem.ReadPtr(p + ContractAt);
        if (!Players.IsTeam(d.team) || d.contract == 0) throw new EditorError("This player has no club.", 400);
        d.record = Mem.Read(d.contract, 0x60) ?? throw new EditorError("Couldn't read the contract.", 500);
        d.employer = BitConverter.ToInt64(d.record, EmployerAt);
        if (BitConverter.ToInt64(d.record, OwnerAt) != p || d.record[JobAt] != PlayerJob || BitConverter.ToInt64(d.record, SeniorAt) != d.employer || !Players.IsTeam(d.employer))
            throw new EditorError("This player's contract is unusual (a player-coach, or shared between teams), so he can't be moved.", 400);
        d.club = Mem.ReadPtr(d.team + TeamClubAt);
        if (!Players.IsClub(d.club) || Mem.ReadPtr(d.employer + TeamClubAt) != d.club)
            throw new EditorError("This player is on loan, so he can't be moved.", 400);
        d.teams = Teams(d.club);
        if (!d.teams.Contains(d.team) || !d.teams.Contains(d.employer)) throw new EditorError("The player isn't in his club's squads as expected.", 409);
        foreach (var t in d.teams)
        {
            var list = PtrList.Read(t + TeamPlayersAt);
            if (list == null) continue;
            var el = list.Elements();
            var at = Enumerable.Range(0, el.Length).Where(i => el[i] == d.obj).ToList();
            if (at.Count > 1) throw new EditorError("The player is listed twice in one squad, so he can't be moved.", 409);
            if (at.Count == 1) d.leaving.Add((t, list, at[0], el));
        }
        if (!d.leaving.Any(x => x.team == d.team)) throw new EditorError("The player isn't in his team's squad list.", 409);
        return d;
    }

    static List<long> Teams(long club)
    {
        var list = PtrList.Read(club + ClubTeamsAt);
        return list == null ? new() : list.Elements().Where(Players.IsTeam).Distinct().ToList();
    }

    static Arrival Joining(long club, Departure d)
    {
        if (!Players.IsClub(club)) throw new EditorError("That isn't a club in the loaded career.", 400);
        if (club == d.club) throw new EditorError("He's already at that club.", 400);
        if (Mem.Read(club + ClubWomenAt, 1)?[0] != Mem.Read(d.club + ClubWomenAt, 1)?[0]) throw new EditorError("Players can't move between men's and women's clubs.", 400);
        var teams = PtrList.Read(club + ClubTeamsAt)?.Elements();
        long senior = teams?.FirstOrDefault() ?? 0;
        if (!Players.IsTeam(senior) || Mem.ReadPtr(senior + TeamClubAt) != club) throw new EditorError("Couldn't find that club's first team.", 400);
        var list = PtrList.Read(senior + TeamPlayersAt) ?? throw new EditorError("Couldn't read that club's squad list.", 500);
        var el = list.Elements();
        if (el.Contains(d.obj)) throw new EditorError("He's already in that club's squad.", 409);
        return new Arrival { club = club, senior = senior, list = list, elements = el };
    }

    static void Append(PtrList list, long obj, Patches ps)
    {
        ps.Add(list.end, Mem.Read(list.end, 8) ?? throw new EditorError("Couldn't read a squad list.", 500), BitConverter.GetBytes(obj));
        long end = list.end + 8;
        ps.Add(list.header + 8, Words(new[] { list.end, list.capacity }), Words(new[] { end, Math.Max(list.capacity, end) }));
    }

    // The players after him move up one, the list ends one earlier and the freed slot is cleared.
    static void Remove(PtrList list, int index, long[] el, Patches ps)
    {
        int lastIdx = el.Length - 1;
        if (index < lastIdx) ps.Add(list.begin + index * 8, Words(el[index..lastIdx]), Words(el[(index + 1)..]));
        ps.Add(list.header + 8, list.end, list.end - 8);
        ps.Add(list.begin + lastIdx * 8, el[lastIdx], 0);
    }

    static void Captaincies(Departure d, Patches ps)
    {
        foreach (var t in d.teams)
            foreach (var at in new[] { CaptainAt, ViceCaptainAt })
                if (Mem.ReadPtr(t + at) == d.p) ps.Add(t + at, d.p, 0);
    }

    // He plays for `senior` under his own contract from today, with no squad number and off the lists.
    static void NewContract(Departure d, long senior, MoveTerms terms, Patches ps)
    {
        var r = d.record;
        ps.Add(d.contract + EmployerAt, d.employer, senior);
        ps.Add(d.contract + SeniorAt, d.employer, senior);
        var today = Today();
        if (today != null)
            foreach (var at in new[] { SignedAt, JoinedAt })
                if (BitConverter.ToInt32(r, at) != 0) ps.Add(d.contract + at, r[at..(at + 4)], Players.EncodeDate(today.Value, r, at));
        if (terms?.wage is int wage)
        {
            if (wage < 0 || wage > 10_000_000) throw new EditorError("Wage must be between 0 and 10,000,000 a week.", 400);
            ps.Add(d.contract + WageAt, r[WageAt..(WageAt + 4)], BitConverter.GetBytes(wage));
        }
        if (terms?.ends != null)
        {
            if (!DateTime.TryParseExact(terms.ends, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var ends) || ends.Year > 2100 || (today != null && ends <= today))
                throw new EditorError("The new contract must end after today's date in the game.", 400);
            ps.Add(d.contract + ExpiresAt, r[ExpiresAt..(ExpiresAt + 4)], Players.EncodeDate(ends, r, ExpiresAt));
        }
        ps.Add(d.contract + NumberAt, new[] { r[NumberAt] }, new[] { NoSquadNumber });
        ps.Add(d.contract + ListingAt, new[] { r[ListingAt] }, new[] { (byte)(r[ListingAt] & ~Listed) });
        ps.Add(d.p + TeamAt, d.team, senior);
    }

    // The buying club pays out of its balance and transfer budget; the selling club's balance takes it.
    static void Fee(long buyer, long seller, int fee, Patches ps)
    {
        if (fee == 0) return;
        if (fee < 0 || fee > 2_000_000_000) throw new EditorError("The fee must be between 0 and 2,000,000,000.", 400);
        long fb = Clubs.Finances(buyer), fs = Clubs.Finances(seller);
        if (fb == 0 || fs == 0) throw new EditorError("One of the clubs has no finances the editor can change, so no fee can be paid. Move him for free instead.", 400);
        int Get(long f, int at) => BitConverter.ToInt32(Mem.Read(f + at, 4) ?? throw new EditorError("Couldn't read the finances.", 500), 0);
        int Clamp(long v) => (int)Math.Clamp(v, int.MinValue, int.MaxValue);
        int bal = Get(fb, Clubs.BalanceAt), budget = Get(fb, Clubs.TransferBudgetAt), sbal = Get(fs, Clubs.BalanceAt);
        ps.Add(fb + Clubs.BalanceAt, BitConverter.GetBytes(bal), BitConverter.GetBytes(Clamp((long)bal - fee)));
        ps.Add(fb + Clubs.TransferBudgetAt, BitConverter.GetBytes(budget), BitConverter.GetBytes(Clamp(Math.Max(0L, (long)budget - fee))));
        ps.Add(fs + Clubs.BalanceAt, BitConverter.GetBytes(sbal), BitConverter.GetBytes(Clamp((long)sbal + fee)));
    }

    // Checks every patch still finds the bytes it expects, then writes them in order.
    static void Write(List<Patch> ps, string text, Func<bool> stillValid)
    {
        Backup.EnsureBackedUp();
        if (!stillValid()) throw new EditorError("The game moved its data. Nothing was changed.", 409);
        foreach (var x in ps)
        {
            var now = Mem.Read(x.at, x.was.Length);
            if (now == null || !now.SequenceEqual(x.was)) throw new EditorError("The game changed something the move depends on. Nothing was changed; try again.", 409);
        }
        int done = 0;
        foreach (var x in ps)
        {
            if (!Mem.Write(x.at, x.now)) throw new EditorError($"The game refused a write after {done} of {ps.Count}. Use Undo, or load your backup.", 500);
            done++;
        }
        last = ps; lastText = text;
        Plugin.Logger.LogInfo($"{text}: {ps.Count} writes");
    }

    public static Dictionary<string, object> Move(MoveRequest req)
    {
        lock (gate)
        {
            var d = Leaving(req.player);
            var a = Joining(Clubs.Find(req.club), d);
            if (a.list.Count + 1 > a.list.Room())
                throw new EditorError($"{Players.ClubName(a.club)}'s squad list has no spare room in memory ({a.list.Count} players). Try a swap with one of their players instead.", 400);
            var ps = new Patches();
            Append(a.list, d.obj, ps);
            NewContract(d, a.senior, req.terms, ps);
            Captaincies(d, ps);
            foreach (var (_, list, index, el) in d.leaving) Remove(list, index, el, ps);
            Fee(a.club, d.club, req.terms?.fee ?? 0, ps);
            string text = $"Moved {Name(d.p)} from {Players.ClubName(d.club)} to {Players.ClubName(a.club)}";
            Write(ps.list, text, () => Players.Valid(d.p) && Players.Contract(d.p) == d.contract);
            Players.Refresh(req.player);
            return Done(text);
        }
    }

    public static Dictionary<string, object> Swap(MoveRequest req)
    {
        lock (gate)
        {
            var first = Leaving(req.player);
            var second = Leaving(req.other ?? throw new EditorError("Pick the second player.", 400));
            var intoSecond = Joining(second.club, first);
            var intoFirst = Joining(first.club, second);
            var ps = new Patches();
            var kept = new List<long>();
            foreach (var (joining, joiner, leaver) in new[] { (intoSecond, first, second), (intoFirst, second, first) })
            {
                int i = Array.IndexOf(joining.elements, leaver.obj);
                if (i >= 0) { ps.Add(joining.list.begin + i * 8, leaver.obj, joiner.obj); kept.Add(joining.list.header); }
                else
                {
                    if (joining.list.Count + 1 > joining.list.Room()) throw new EditorError($"{Players.ClubName(joining.club)}'s first-team list has no spare room in memory.", 400);
                    Append(joining.list, joiner.obj, ps);
                }
            }
            NewContract(first, intoSecond.senior, null, ps);
            NewContract(second, intoFirst.senior, null, ps);
            foreach (var leaver in new[] { first, second })
            {
                Captaincies(leaver, ps);
                foreach (var (_, list, index, el) in leaver.leaving)
                    if (!kept.Contains(list.header)) Remove(list, index, el, ps);
            }
            string text = $"Swapped {Name(first.p)} ({Players.ClubName(first.club)}) and {Name(second.p)} ({Players.ClubName(second.club)})";
            Write(ps.list, text, () => Players.Valid(first.p) && Players.Valid(second.p));
            Players.Refresh(first.id); Players.Refresh(second.id);
            return Done(text);
        }
    }

    public static Dictionary<string, object> Release(MoveRequest req)
    {
        lock (gate)
        {
            var d = Leaving(req.player);
            var ps = new Patches();
            Captaincies(d, ps);
            foreach (var (_, list, index, el) in d.leaving) Remove(list, index, el, ps);
            // Only once no list holds him: the game may follow a listed player to his contract.
            ps.Add(d.p + TeamAt, d.team, 0);
            ps.Add(d.p + ContractAt, d.contract, 0);
            string text = $"Released {Name(d.p)} from {Players.ClubName(d.club)}";
            Write(ps.list, text, () => Players.Valid(d.p));
            Players.Refresh(req.player);
            return Done(text);
        }
    }

    // Sends a loanee back to his parent club. While on loan he is in both clubs' first-team
    // lists and P - 0x158 names the loan club's team; his own contract stays with the parent
    // club. Only the holder at P + 0xB0 points at the loan contract (memscan, Mark Shelton),
    // and players with no loan keep a holder whose first slot is 0, so that is all we clear.
    // The loan contract itself is left where it is.
    public static Dictionary<string, object> EndLoan(MoveRequest req)
    {
        lock (gate)
        {
            long p = Players.Find(req.player);
            long loan = Loans.Find(p);
            if (loan == 0) throw new EditorError("This player isn't on loan.", 400);
            long holder = Mem.ReadPtr(p + Loans.HolderAt);
            long loanTeam = Mem.ReadPtr(loan + EmployerAt), team = Mem.ReadPtr(p + TeamAt);
            long contract = Mem.ReadPtr(p + ContractAt);
            long home = contract == 0 ? 0 : Mem.ReadPtr(contract + EmployerAt);
            if (!Players.IsTeam(loanTeam) || team != loanTeam || !Players.IsTeam(home) || Mem.ReadPtr(contract + OwnerAt) != p)
                throw new EditorError("This loan isn't set up the usual way, so it can't be ended here.", 400);
            long loanClub = Mem.ReadPtr(loanTeam + TeamClubAt), parentClub = Mem.ReadPtr(home + TeamClubAt);
            if (!Players.IsClub(loanClub) || !Players.IsClub(parentClub) || loanClub == parentClub)
                throw new EditorError("This loan isn't set up the usual way, so it can't be ended here.", 400);
            long obj = p + PlayerPart;
            // He goes back to the parent team that still lists him: usually the contract's
            // first team, but a youngster may be in the club's second team (Koné, Man Utd).
            bool Lists(long t) => PtrList.Read(t + TeamPlayersAt)?.Elements().Contains(obj) ?? false;
            if (!Lists(home)) home = Teams(parentClub).FirstOrDefault(Lists);
            if (home == 0) throw new EditorError("He isn't in any of his parent club's squad lists as expected.", 409);

            var d = new Departure { id = req.player, p = p, obj = obj, team = loanTeam, club = loanClub, teams = Teams(loanClub) };
            foreach (var t in d.teams)
            {
                var list = PtrList.Read(t + TeamPlayersAt);
                if (list == null) continue;
                var el = list.Elements();
                var at = Enumerable.Range(0, el.Length).Where(i => el[i] == obj).ToList();
                if (at.Count > 1) throw new EditorError("He's listed twice in one squad, so the loan can't be ended.", 409);
                if (at.Count == 1) d.leaving.Add((t, list, at[0], el));
            }
            var ps = new Patches();
            Captaincies(d, ps);
            foreach (var (_, list, index, el) in d.leaving) Remove(list, index, el, ps);
            ps.Add(p + TeamAt, loanTeam, home);
            ps.Add(holder, loan, 0);
            string text = $"Ended {Name(p)}'s loan at {Players.ClubName(loanClub)}; back at {Players.ClubName(parentClub)}";
            Write(ps.list, text, () => Players.Valid(p) && Loans.Find(p) == loan);
            Players.Refresh(req.player);
            return Done(text);
        }
    }

    // Puts back what the last move wrote, as long as the game still holds exactly that.
    public static Dictionary<string, object> Undo()
    {
        lock (gate)
        {
            if (last == null) throw new EditorError("There's no move to undo.", 400);
            foreach (var x in last)
            {
                var now = Mem.Read(x.at, x.now.Length);
                if (now == null || !now.SequenceEqual(x.now)) throw new EditorError("The game has changed things since the move (time has probably passed), so it can't be undone. Load your backup instead.", 409);
            }
            foreach (var x in Enumerable.Reverse(last))
                if (!Mem.Write(x.at, x.was)) throw new EditorError("The game refused the undo part-way. Load your backup.", 500);
            string text = "Undid: " + lastText;
            Plugin.Logger.LogInfo(text);
            last = null; lastText = null;
            return new() { ["ok"] = true, ["message"] = text };
        }
    }

    public static Dictionary<string, object> Status() => new() { ["today"] = Today()?.ToString("yyyy-MM-dd"), ["lastMove"] = lastText };

    // His full name, or the name FM shows when the database has no full name (Sékou Koné).
    static string Name(long p) => Mem.ReadLenString(Mem.ReadPtr(p + 0x40)) is { Length: > 0 } full ? full : Players.Summary(p)?.name ?? "the player";
    static Dictionary<string, object> Done(string text) => new() { ["ok"] = true, ["message"] = text, ["canUndo"] = true };
}

public class MoveTerms
{
    public int? wage { get; set; }
    public string ends { get; set; }
    public int? fee { get; set; }
}

public class MoveRequest
{
    public int player { get; set; }
    public int club { get; set; }
    public int? other { get; set; }
    // No longer checked here: the app uses your clubs only to warn before a player leaves one.
    public int[] humanClubs { get; set; }
    public MoveTerms terms { get; set; }
}
