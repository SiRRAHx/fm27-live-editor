// A player's current loan: the loan contract FM keeps beside his parent club's
// contract. P + 0xB0 points to a small holder whose first entry is the loan
// contract (its own class, see research/loan-contract.md). Checked on 422 loans
// in the Test save: each points back to its player and names a different club.
//
// Loan contract (offsets from its start): +0x08 the person, +0x10 the loan
// club's team, +0x20 wage per week (+0xD4 usually the same), +0x44 start,
// +0x48 end (FM dates), +0xC0 optional future fee, +0xCC monthly playing fee,
// +0xD0 monthly fee when unused (-1 for no fee or option).
using System;
using System.Collections.Generic;

namespace FM27LiveEditor;

public static class Loans
{
    internal const int HolderAt = 0xB0;
    const int TeamAt = 0x10, WageAt = 0x20, WageCopyAt = 0xD4, StartsAt = 0x44, EndsAt = 0x48;
    const int FutureFeeAt = 0xC0, MonthlyFeeAt = 0xCC, UnusedFeeAt = 0xD0, Size = 0xD8;

    // The loan contract, or 0 if the player isn't on loan.
    internal static long Find(long p)
    {
        long holder = Mem.ReadPtr(p + HolderAt);
        long loan = holder == 0 ? 0 : Mem.ReadPtr(holder);
        return loan != 0 && Addresses.Loan != 0 && Mem.ReadPtr(loan) == Addresses.Loan && Mem.ReadPtr(loan + 8) == p ? loan : 0;
    }

    static int? Fee(byte[] b, int at) => BitConverter.ToInt32(b, at) is var v && v >= 0 ? v : null;

    public static Dictionary<string, object> Info(long p)
    {
        long loan = Find(p);
        var b = loan == 0 ? null : Mem.Read(loan, Size);
        if (b == null) return null;
        return new()
        {
            ["club"] = Players.TeamClub(BitConverter.ToInt64(b, TeamAt)),
            ["wage"] = BitConverter.ToInt32(b, WageAt),
            ["starts"] = Players.Date(b, StartsAt), ["ends"] = Players.Date(b, EndsAt),
            ["monthlyFee"] = Fee(b, MonthlyFeeAt), ["unusedFee"] = Fee(b, UnusedFeeAt), ["futureFee"] = Fee(b, FutureFeeAt),
        };
    }

    // Checks every change and returns the writes, without writing anything.
    public static (long loan, List<(int at, byte[] value)> writes) Plan(long p, LoanChanges changes)
    {
        if (changes == null) return (0, new());
        long loan = Find(p);
        if (loan == 0) throw new EditorError("This player isn't on loan.", 400);
        var b = Mem.Read(loan, Size) ?? throw new EditorError("Couldn't read the loan.", 500);
        var writes = new List<(int at, byte[] value)>();
        if (changes.wage is int wage)
        {
            if (wage < 0 || wage > 10_000_000) throw new EditorError("The loan wage must be between 0 and 10,000,000 a week.", 400);
            if (wage != BitConverter.ToInt32(b, WageAt))
            {
                writes.Add((WageAt, BitConverter.GetBytes(wage)));
                if (BitConverter.ToInt32(b, WageCopyAt) == BitConverter.ToInt32(b, WageAt)) writes.Add((WageCopyAt, BitConverter.GetBytes(wage)));
            }
        }
        if (changes.ends != null)
        {
            if (!DateTime.TryParseExact(changes.ends, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var ends))
                throw new EditorError("The loan end date isn't valid.", 400);
            var starts = Players.Date(b, StartsAt);
            if (starts != null && ends <= DateTime.Parse(starts)) throw new EditorError("The loan can't end before it starts.", 400);
            if (ends > DateTime.Parse(starts ?? "2000-01-01").AddYears(5)) throw new EditorError("A loan can last at most five years.", 400);
            if (changes.ends != Players.Date(b, EndsAt)) writes.Add((EndsAt, Players.EncodeDate(ends, b, EndsAt)));
        }
        // Fees: a number of pounds, or -1 for none.
        void Fee(int? v, int at, string name)
        {
            if (v == null) return;
            if (v < -1 || v > 2_000_000_000) throw new EditorError($"The {name} must be between 0 and 2,000,000,000, or none.", 400);
            if (v != BitConverter.ToInt32(b, at)) writes.Add((at, BitConverter.GetBytes(v.Value)));
        }
        Fee(changes.monthlyFee, MonthlyFeeAt, "monthly fee");
        Fee(changes.unusedFee, UnusedFeeAt, "unused fee");
        Fee(changes.futureFee, FutureFeeAt, "future fee");
        return (loan, writes);
    }

    // Writes a checked plan. Returns how many values changed.
    public static int Write(long p, (long loan, List<(int at, byte[] value)> writes) plan)
    {
        var (loan, writes) = plan;
        if (writes.Count == 0) return 0;
        Backup.EnsureBackedUp();
        if (Find(p) != loan) throw new EditorError("The game moved the loan. It wasn't changed.", 409);
        foreach (var (at, value) in writes)
            if (!Mem.Write(loan + at, value)) throw new EditorError("The game refused the change. Nothing more was written.", 500);
        Log.Info($"Edited the loan of player at 0x{p:X}: {writes.Count} values");
        return writes.Count;
    }
}

public class LoanChanges
{
    public int? wage { get; set; }
    public string ends { get; set; }
    // Pounds, or -1 for no fee or option.
    public int? monthlyFee { get; set; }
    public int? unusedFee { get; set; }
    public int? futureFee { get; set; }
}
