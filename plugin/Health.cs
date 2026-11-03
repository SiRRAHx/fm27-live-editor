// A player's injuries and bans, and healing or lifting them as FM does when he
// gets over an injury or serves a ban. Layout from FMLibre (layout.toml), checked
// in the Test save on Matthijs de Ligt (damaged spine), Dejan Kulusevski
// (patella dislocation) and Elliot Watt (calf strain, bans of 1 and 4 matches).
//
// P - 0x190 points to the player's injury record, null if he was never injured.
// The record holds two lists of pointers (begin, end, capacity): at +0x00 his
// current injuries, at +0x18 his absences. An absence with 0xFFFF at +0x16 is a
// ban (matches left at +0x1C); others, such as call-ups, have 0 and are kept.
using System;
using System.Collections.Generic;
using System.Linq;

namespace FM27LiveEditor;

public static class Health
{
    const int RecordAt = -0x190, InjuriesAt = 0x00, AbsencesAt = 0x18;
    const int KindAt = 0x08, KindNameAt = 0x18, StartedAt = 0x20, LongestAt = 0x28, ShortestAt = 0x2A;
    const int BanMarkerAt = 0x16, BanMatchesAt = 0x1C;

    static long Record(long p) => Mem.ReadPtr(p + RecordAt);

    // The pointers in one of the record's lists, or null if it doesn't look like a list.
    static List<long> List(long rec, int at)
    {
        var b = Mem.Read(rec + at, 24);
        if (b == null) return null;
        long begin = BitConverter.ToInt64(b, 0), end = BitConverter.ToInt64(b, 8), cap = BitConverter.ToInt64(b, 16);
        if (begin == 0 && end == 0) return new();
        if (begin == 0 || end < begin || cap < end || (end - begin) % 8 != 0 || end - begin > 8 * 32) return null;
        var items = Mem.Read(begin, (int)(end - begin));
        if (items == null) return null;
        return Enumerable.Range(0, items.Length / 8).Select(i => BitConverter.ToInt64(items, i * 8)).ToList();
    }

    static bool IsBan(long absence) => Mem.Read(absence + BanMarkerAt, 2) is { } m && BitConverter.ToUInt16(m, 0) == 0xFFFF;

    public static Dictionary<string, object> Info(long p)
    {
        long rec = Record(p);
        var injuries = rec == 0 ? new() : List(rec, InjuriesAt) ?? new();
        var absences = rec == 0 ? new() : List(rec, AbsencesAt) ?? new();
        return new()
        {
            ["injuries"] = injuries.Select(a =>
            {
                var b = Mem.Read(a, 0x30);
                if (b == null) return null;
                long kind = BitConverter.ToInt64(b, KindAt);
                string started = Players.Date(b, StartedAt);
                int longest = BitConverter.ToUInt16(b, LongestAt), shortest = BitConverter.ToUInt16(b, ShortestAt);
                DateTime? from = started == null ? null : DateTime.Parse(started);
                return new Dictionary<string, object>
                {
                    ["name"] = kind == 0 ? null : Mem.ReadLenString(Mem.ReadPtr(kind + KindNameAt)),
                    ["started"] = started,
                    ["backFrom"] = from?.AddDays(shortest).ToString("yyyy-MM-dd"),
                    ["backBy"] = from?.AddDays(longest).ToString("yyyy-MM-dd"),
                };
            }).Where(x => x != null).ToList(),
            ["bans"] = absences.Where(IsBan).Select(a => new Dictionary<string, object>
            {
                ["matches"] = Mem.Read(a + BanMatchesAt, 1)?[0],
            }).ToList(),
        };
    }

    // Empties his list of current injuries. Returns how many were healed.
    public static int Heal(long p)
    {
        long rec = Record(p);
        var injuries = rec == 0 ? new() : List(rec, InjuriesAt) ?? throw new EditorError("Couldn't read the player's injuries.", 500);
        if (injuries.Count == 0) return 0;
        var head = Mem.Read(rec + InjuriesAt, 16);
        Backup.EnsureBackedUp();
        if (Record(p) != rec || !Same(Mem.Read(rec + InjuriesAt, 16), head)) throw new EditorError("The game changed his injuries. Nothing was written.", 409);
        // end = begin
        if (!Mem.Write(rec + InjuriesAt + 8, head.Take(8).ToArray())) throw new EditorError("The game refused the change.", 500);
        Plugin.Logger.LogInfo($"Healed {injuries.Count} injuries of the player at 0x{p:X}");
        return injuries.Count;
    }

    // Takes the bans out of his absences, keeping the others in order. Returns how many were lifted.
    public static int LiftBans(long p)
    {
        long rec = Record(p);
        var absences = rec == 0 ? new() : List(rec, AbsencesAt) ?? throw new EditorError("Couldn't read the player's bans.", 500);
        var keep = absences.Where(a => !IsBan(a)).ToList();
        int lifted = absences.Count - keep.Count;
        if (lifted == 0) return 0;
        var head = Mem.Read(rec + AbsencesAt, 16);
        long begin = BitConverter.ToInt64(head, 0);
        Backup.EnsureBackedUp();
        if (Record(p) != rec || !Same(Mem.Read(rec + AbsencesAt, 16), head)) throw new EditorError("The game changed his bans. Nothing was written.", 409);
        if (keep.Count > 0 && !Mem.Write(begin, keep.SelectMany(BitConverter.GetBytes).ToArray())) throw new EditorError("The game refused the change.", 500);
        if (!Mem.Write(rec + AbsencesAt + 8, BitConverter.GetBytes(begin + 8L * keep.Count))) throw new EditorError("The game refused the change.", 500);
        Plugin.Logger.LogInfo($"Lifted {lifted} bans of the player at 0x{p:X}");
        return lifted;
    }

    static bool Same(byte[] a, byte[] b) => a != null && b != null && a.SequenceEqual(b);
}
