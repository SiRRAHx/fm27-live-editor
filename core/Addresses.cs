// Where things are in the running game module. Nothing here is fixed to one build of FM27
// (Game Pass, Steam and Epic each ship their own): everything is found in the module itself
// when FM starts, and editing stays off if anything can't be found for sure.
//   - Classes: by the name the compiler records for each class (".?AVTEAM@db@@"), which is the
//     same in every build. Name -> type descriptor -> object locator -> class table (vtable).
//   - The game date and FM's game object: by a vote over code that uses them. The game date is
//     read as "movzx r32, word [date]; and r32, 0x1FF" (the day of the year) thousands of times;
//     the game object as "mov r64, [game]; mov r64, [r64+0x7D8]". The address most of those
//     point at wins, so a few differences between builds don't matter.
//   - FM's start-loan function: by the start of its code, and checked against the one place
//     that calls it with the game object.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FM27LiveEditor;

public static class Addresses
{
    // Class tables (absolute addresses).
    public static long Person, Attr, Player, Team, Club, Contract, Nation, Finances, Loan, StartingLoan;
    // The in-game date (day-of-year word, then year word), FM's game object pointer, start-loan code.
    public static long Today, Game, StartLoan;
    public static string Missing;

    static long foundFor;
    record Section(long va, byte[] data, bool code);

    // Finds everything once per game module. False (with Missing set) if anything is missing.
    public static bool Resolve(long mod)
    {
        if (mod == 0) return false;
        if (foundFor == mod) return Missing == null;
        var started = DateTime.Now;
        var secs = Sections(mod);
        var missing = new List<string>();
        long Vt(string name, int at = 0)
        {
            long v = Vtable(secs, mod, name, at);
            if (v == 0) missing.Add(name + (at != 0 ? $"+0x{at:X}" : ""));
            return v;
        }
        Person = Vt("ACTUAL_PLAYER", 0x288); Player = Vt("ACTUAL_PLAYER"); Attr = Vt("PLAYING_DATA");
        Team = Vt("TEAM"); Club = Vt("CLUB"); Contract = Vt("FULL_CONTRACT"); Nation = Vt("NATION");
        Finances = Vt("CLUB_FINANCE"); Loan = Vt("LOAN_CONTRACT"); StartingLoan = Vt("STARTING_LOAN");

        Today = Vote(secs, "game date", DateVotes);
        Game = Vote(secs, "game object", GameVotes);
        StartLoan = FindStartLoan(secs, Game);
        if (Today == 0) missing.Add("game date");
        if (Game == 0) missing.Add("game object");
        if (StartLoan == 0) missing.Add("start-loan code");

        foundFor = mod;
        Missing = missing.Count == 0 ? null : string.Join(", ", missing);
        string Rva(long a) => a == 0 ? "-" : $"0x{a - mod:X}";
        Log.Info($"Game module: {string.Join(" ", secs.Select(s => $"{s.va - mod:X}+{s.data.Length:X}{(s.code ? "x" : "")}"))}");
        Log.Info($"Found: person {Rva(Person)} player {Rva(Player)} attr {Rva(Attr)} team {Rva(Team)} club {Rva(Club)} " +
                 $"contract {Rva(Contract)} nation {Rva(Nation)} finances {Rva(Finances)} loan {Rva(Loan)} " +
                 $"startingLoan {Rva(StartingLoan)} date {Rva(Today)} game {Rva(Game)} startLoan {Rva(StartLoan)} " +
                 $"in {(DateTime.Now - started).TotalSeconds:0.0}s");
        if (Missing != null) Log.Warn("Not found in this FM27 build: " + Missing);
        return Missing == null;
    }

    // The module's sections as they are in memory. Very large sections (FM's packed code) are
    // skipped; unreadable pages read as zeros.
    static List<Section> Sections(long mod)
    {
        var list = new List<Section>();
        var mz = Mem.Read(mod + 0x3C, 4);
        if (mz == null) return list;
        long pe = mod + BitConverter.ToInt32(mz, 0);
        var head = Mem.Read(pe, 24);
        if (head == null) return list;
        int count = BitConverter.ToUInt16(head, 6), optSize = BitConverter.ToUInt16(head, 20);
        var table = Mem.Read(pe + 24 + optSize, count * 40);
        if (table == null) return list;
        for (int i = 0; i < count; i++)
        {
            int size = BitConverter.ToInt32(table, i * 40 + 8), va = BitConverter.ToInt32(table, i * 40 + 12);
            uint flags = BitConverter.ToUInt32(table, i * 40 + 36);
            if (size <= 0 || size > 160 << 20 || (flags & 0x40000000) == 0) continue; // readable only
            var data = new byte[size];
            const int Chunk = 1 << 20;
            for (int o = 0; o < size; o += Chunk)
                Mem.Read(mod + va + o, Math.Min(Chunk, size - o))?.CopyTo(data, o);
            list.Add(new Section(mod + va, data, (flags & 0x20000000) != 0));
        }
        return list;
    }

    static IEnumerable<(Section s, int at)> FindAll(List<Section> secs, byte[] needle, bool codeOnly = false)
    {
        foreach (var s in secs)
        {
            if (codeOnly && !s.code) continue;
            int from = 0;
            while (from < s.data.Length)
            {
                int i = s.data.AsSpan(from).IndexOf(needle);
                if (i < 0) break;
                yield return (s, from + i);
                from += i + 1;
            }
        }
    }

    // The class table for db::NAME (at = where this part sits in the whole object), or 0.
    static long Vtable(List<Section> secs, long mod, string name, int at)
    {
        var found = new HashSet<long>();
        foreach (var (ts, ti) in FindAll(secs, Encoding.ASCII.GetBytes($".?AV{name}@db@@\0")).ToList())
        {
            uint td = (uint)(ts.va + ti - 16 - mod);
            foreach (var (cs, ci) in FindAll(secs, BitConverter.GetBytes(td)).ToList())
            {
                int c = ci - 12;
                if (c < 0 || c + 24 > cs.data.Length) continue;
                long col = cs.va + c;
                if (BitConverter.ToInt32(cs.data, c) != 1 || BitConverter.ToInt32(cs.data, c + 4) != at
                    || BitConverter.ToUInt32(cs.data, c + 20) != (uint)(col - mod)) continue;
                foreach (var (vs, vi) in FindAll(secs, BitConverter.GetBytes(col))) found.Add(vs.va + vi + 8);
            }
        }
        if (found.Count == 1) return found.First();
        if (found.Count > 1) Log.Warn($"{name}: {found.Count} class tables, expected one");
        return 0;
    }

    static bool RipModrm(byte b) => (b & 0xC7) == 0x05;

    // movzx r32, word [rip+d]; and r32, 0x1FF  (25 FF 01 00 00, or 81 E0+r FF 01 00 00)
    static IEnumerable<long> DateVotes(List<Section> secs)
    {
        foreach (var (s, i) in FindAll(secs, new byte[] { 0x0F, 0xB7 }, codeOnly: true))
        {
            var d = s.data;
            if (i + 13 > d.Length || !RipModrm(d[i + 2])) continue;
            int k = i + 7;
            bool and = d[k] == 0x25 && d[k + 1] == 0xFF && d[k + 2] == 0x01 && d[k + 3] == 0 && d[k + 4] == 0
                || d[k] == 0x81 && d[k + 1] >= 0xE0 && d[k + 1] <= 0xE7 && d[k + 2] == 0xFF && d[k + 3] == 0x01 && d[k + 4] == 0 && d[k + 5] == 0;
            if (and) yield return s.va + k + BitConverter.ToInt32(d, i + 3);
        }
    }

    // mov r64, [rip+d]; mov r64, [r64+0x7D8]
    static IEnumerable<long> GameVotes(List<Section> secs)
    {
        foreach (var (s, k) in FindAll(secs, new byte[] { 0xD8, 0x07, 0x00, 0x00 }, codeOnly: true))
        {
            var d = s.data;
            int i = k - 10;
            if (i < 0 || (d[i] != 0x48 && d[i] != 0x4C) || d[i + 1] != 0x8B || !RipModrm(d[i + 2])) continue;
            if ((d[k - 3] != 0x48 && d[k - 3] != 0x4C) || d[k - 2] != 0x8B || d[k - 1] < 0x80 || d[k - 1] > 0xBF) continue;
            yield return s.va + i + 7 + BitConverter.ToInt32(d, i + 3);
        }
    }

    // The clear winner of a vote: at least 20 votes and five times the runner-up.
    static long Vote(List<Section> secs, string what, Func<List<Section>, IEnumerable<long>> votes)
    {
        var top = votes(secs).GroupBy(a => a).Select(g => (a: g.Key, n: g.Count())).OrderByDescending(x => x.n).Take(2).ToList();
        int first = top.Count > 0 ? top[0].n : 0, second = top.Count > 1 ? top[1].n : 0;
        if (first >= 20 && first >= second * 5) return top[0].a;
        Log.Warn($"{what}: no clear address ({first} votes vs {second})");
        return 0;
    }

    static readonly byte[] SaveRegs = { 0x41, 0x57, 0x41, 0x56, 0x41, 0x55, 0x41, 0x54, 0x56, 0x57, 0x55, 0x53, 0x48, 0x81, 0xEC };

    // Saves all registers, then soon after: mov [rsp+x], r8b; test byte [rdx+0x54], 0x10
    // (the "make it now" flag, and a check on the loan form's flags).
    static long FindStartLoan(List<Section> secs, long game)
    {
        var byCode = new List<long>();
        foreach (var (s, i) in FindAll(secs, SaveRegs, codeOnly: true))
        {
            var d = s.data;
            for (int k = i + 19; k < Math.Min(i + 80, d.Length - 12); k++)
                if (d[k] == 0x44 && d[k + 1] == 0x88 && d[k + 2] == 0x84 && d[k + 3] == 0x24
                    && d[k + 8] == 0xF6 && d[k + 9] == 0x42 && d[k + 10] == 0x54 && d[k + 11] == 0x10) { byCode.Add(s.va + i); break; }
        }
        // mov rcx, [game]; lea rdx, [rsp+x]; mov r8b, 1; call start-loan
        var byCall = new HashSet<long>();
        if (game != 0)
            foreach (var (s, k) in FindAll(secs, new byte[] { 0x41, 0xB0, 0x01, 0xE8 }, codeOnly: true))
            {
                var d = s.data;
                int i = k - 12;
                if (i < 0 || k + 8 > d.Length || d[i] != 0x48 || d[i + 1] != 0x8B || d[i + 2] != 0x0D) continue;
                if (s.va + i + 7 + BitConverter.ToInt32(d, i + 3) != game) continue;
                if (d[k - 5] != 0x48 || d[k - 4] != 0x8D || d[k - 3] != 0x54 || d[k - 2] != 0x24) continue;
                byCall.Add(s.va + k + 8 + BitConverter.ToInt32(d, k + 4));
            }
        if (byCode.Count == 1 && (byCall.Count == 0 || byCall.SetEquals(byCode))) return byCode[0];
        Log.Warn($"start-loan code: {byCode.Count} by its code, {byCall.Count} by its caller");
        return 0;
    }
}
