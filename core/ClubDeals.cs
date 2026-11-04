// A club's bank loans and sponsorship deals, read only. The records below hold the same figures FM's
// Debts and Sponsorship screens show, but writing to them changes nothing on those screens (tested
// 4 Nov 2026), so FM keeps the live ones somewhere else and these are copies.
//
// Debts: club info (club + 0x100) + 0x50 is a vector of pointers to 0x1C-byte records:
//   +0x0 amount borrowed, +0x4 monthly repayment, +0x8 the interest part of each repayment,
//   +0x10 end date, +0x14 start date (when repayments start), +0x18 flags.
// FM shows what's left as the repayment times the months left.
//
// Sponsorships: club + 0x130 points to a block whose first field is a vector of pointers to
// 0x18-byte records: +0x0 start date, +0x4 end date, +0x8 total value, +0xC value per season,
// +0x10 the club's reputation when it was signed (high word), +0x14 type.
// Found in the Test save against Man Utd's Debts and Sponsorship screens (4 Nov 2026).
using System;
using System.Collections.Generic;
using System.Linq;

namespace FM27LiveEditor;

public static class ClubDeals
{
    const int DebtsAt = 0x50, SponsorsAt = 0x130, DebtSize = 0x1C, DealSize = 0x18, MaxItems = 200;
    const int DebtMonthlyAt = 0x4, DebtInterestAt = 0x8, DebtEndsAt = 0x10, DebtStartsAt = 0x14;
    const int DealStartsAt = 0x0, DealEndsAt = 0x4, DealTotalAt = 0x8, DealPerSeasonAt = 0xC, DealTypeAt = 0x14;

    // The low byte of the type, as FM's Sponsorship screen names it. 0x100 marks most current deals.
    static readonly Dictionary<int, string> DealTypes = new()
    {
        [0x16] = "Kit Manufacturer", [0x01] = "Main Kit", [0x13] = "Shirt Sleeve", [0x0D] = "Training Kit",
        [0x04] = "General", [0x06] = "General",
    };

    // The record pointers of a vector at `at`, or an empty list if it doesn't look right.
    static List<long> Pointers(long at)
    {
        var head = at == 0 ? null : Mem.Read(at, 16);
        if (head == null) return new();
        long b = BitConverter.ToInt64(head, 0), e = BitConverter.ToInt64(head, 8);
        if (b == 0 || e < b || (e - b) % 8 != 0 || (e - b) / 8 > MaxItems) return new();
        var list = e == b ? Array.Empty<byte>() : Mem.Read(b, (int)(e - b));
        if (list == null) return new();
        return Enumerable.Range(0, list.Length / 8).Select(i => BitConverter.ToInt64(list, i * 8)).Where(p => p != 0).ToList();
    }

    static List<long> DebtRecords(long club) { long info = Clubs.Info(club); return info == 0 ? new() : Pointers(info + DebtsAt); }
    static List<long> DealRecords(long club) => Pointers(Mem.ReadPtr(club + SponsorsAt));

    // Repayments left, counting this month's (as FM does).
    static int Months(DateTime from, DateTime to) => to < from ? 0 : (to.Year - from.Year) * 12 + to.Month - from.Month + 1;

    public static List<Dictionary<string, object>> Debts(long club)
    {
        var today = Transfers.Today() ?? DateTime.Today;
        var list = new List<Dictionary<string, object>>();
        foreach (var (p, i) in DebtRecords(club).Select((p, i) => (p, i)))
        {
            var b = Mem.Read(p, DebtSize);
            if (b == null) continue;
            string ends = Players.Date(b, DebtEndsAt), starts = Players.Date(b, DebtStartsAt);
            if (ends == null || starts == null) continue;
            int monthly = BitConverter.ToInt32(b, DebtMonthlyAt), interest = BitConverter.ToInt32(b, DebtInterestAt);
            var from = DateTime.Parse(starts) > today ? DateTime.Parse(starts) : today;
            int left = Months(from, DateTime.Parse(ends));
            list.Add(new()
            {
                ["index"] = i, ["amount"] = BitConverter.ToInt32(b, 0), ["monthly"] = monthly, ["monthlyInterest"] = interest,
                ["starts"] = starts, ["ends"] = ends, ["monthsLeft"] = left, ["toRepay"] = (long)monthly * left, ["interestLeft"] = (long)interest * left,
            });
        }
        return list;
    }

    public static List<Dictionary<string, object>> Sponsorships(long club)
    {
        var today = Transfers.Today() ?? DateTime.Today;
        var list = new List<Dictionary<string, object>>();
        foreach (var (p, i) in DealRecords(club).Select((p, i) => (p, i)))
        {
            var b = Mem.Read(p, DealSize);
            if (b == null) continue;
            string starts = Players.Date(b, DealStartsAt), ends = Players.Date(b, DealEndsAt);
            if (starts == null || ends == null) continue;
            int type = BitConverter.ToInt32(b, DealTypeAt);
            list.Add(new()
            {
                ["index"] = i, ["type"] = DealTypes.TryGetValue(type & 0xFF, out var t) ? t : "Other",
                ["starts"] = starts, ["ends"] = ends, ["total"] = BitConverter.ToInt32(b, DealTotalAt),
                ["perSeason"] = BitConverter.ToInt32(b, DealPerSeasonAt), ["ended"] = DateTime.Parse(ends) < today,
            });
        }
        return list;
    }
}
