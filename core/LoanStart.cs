// Starts a real loan the way FM's own database loader does at the start of a career: it fills
// in a db::STARTING_LOAN (a short-lived form; FM's loader keeps it on the stack) and hands it to
// FM's start-loan function, which takes a LOAN_CONTRACT from FM's own pool and links it to the
// player and both clubs (tested 3 Nov: Heaven to Wigan, Munro to Swansea at 50%).
// The addresses are for game_plugin.dll 27.0 dated 2 Nov 2026 (research/loan-contract.md). Each
// call first checks they still point at the same classes and code, so after a game update it
// refuses instead of calling the wrong thing.
using System;
using System.Linq;
using System.Text;

namespace FM27LiveEditor;

public static class LoanStart
{
    // The form's default clause values, as FM keeps them (-1, 0, -1, -1).
    static readonly byte[] Defaults = { 0xFF, 0xFF, 0xFF, 0xFF, 0, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
    static readonly byte[] StartLoanStart = { 0x41, 0x57, 0x41, 0x56, 0x41, 0x55, 0x41, 0x54, 0x56, 0x57, 0x55, 0x53, 0x48, 0x81, 0xEC, 0x88, 0x01, 0x00, 0x00 };
    const int Size = 0x70;
    const int PersonAt = 0x18, LoanTeamAt = 0x20, StartsAt = 0x30, EndsAt = 0x34, OtherDateAt = 0x38;
    const int MonthlyFeeAt = 0x5C, FutureFeeAt = 0x60, WageShareAt = 0x6C;
    const int TeamAt = -0x158;

    public class Request
    {
        public int player { get; set; }
        public int club { get; set; }
        public string ends { get; set; }
        public int wageShare { get; set; } = 100; // % of his wage the loan club pays
        public int monthlyFee { get; set; }
        public int futureFee { get; set; } = -1;  // agreed price to buy him, -1 for none
    }

    // An FM date with no flags: day of the year, then the year.
    static byte[] Date(DateTime d) => new[] { (byte)(d.DayOfYear & 0xFF), (byte)(d.DayOfYear >> 8), (byte)(d.Year & 0xFF), (byte)(d.Year >> 8) };

    // The class name MSVC records for the vtable at this address (".?AVNAME@db@@").
    static string ClassOf(long mod, long vt)
    {
        long col = Mem.ReadPtr(vt - 8);
        var b = col == 0 ? null : Mem.Read(col + 12, 4);
        if (b == null) return null;
        var name = Mem.Read(mod + BitConverter.ToUInt32(b, 0) + 16, 40);
        return name == null ? null : Encoding.ASCII.GetString(name.TakeWhile(c => c != 0).ToArray());
    }

    static void CheckBuild(long mod)
    {
        bool ok = Addresses.Game != 0 && ClassOf(mod, Addresses.StartingLoan) == ".?AVSTARTING_LOAN@db@@"
            && ClassOf(mod, Addresses.Person) == ".?AVACTUAL_PLAYER@db@@"
            && Mem.Read(Addresses.StartLoan, StartLoanStart.Length) is { } code && code.SequenceEqual(StartLoanStart);
        if (!ok) throw new EditorError("This version of FM isn't one the editor knows how to make loans in yet, so nothing was changed.", 409);
    }

    public static object Start(Request req)
    {
        long mod = Mem.ModuleBase("game_plugin.dll");
        CheckBuild(mod);
        long p = Players.Find(req.player);
        if (Mem.ReadPtr(p) != Addresses.Person) throw new EditorError("Only players can be loaned.", 409);
        if (Loans.Find(p) != 0) throw new EditorError("He's already on loan. End that loan first.", 409);
        long current = Mem.ReadPtr(p + TeamAt);
        if (!Players.IsTeam(current)) throw new EditorError("He has no club to be loaned from.", 409);
        long club = Clubs.Find(req.club), team = Clubs.SeniorTeam(club);
        if (team == 0 || Mem.Read(team + 0x2F, 1)?[0] != 0) throw new EditorError("Couldn't find that club's first team.", 409);
        if (current == team || Mem.ReadPtr(current + 0x30) == club) throw new EditorError("He already plays for that club.", 409);
        var today = Transfers.Today() ?? throw new EditorError("Couldn't read the game date.", 500);
        if (!DateTime.TryParseExact(req.ends, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var ends) || ends <= today)
            throw new EditorError("The loan must end after today.", 400);
        if (ends > today.AddYears(5)) throw new EditorError("A loan can last at most five years.", 400);
        if (req.wageShare < 0 || req.wageShare > 100) throw new EditorError("The wage share must be between 0% and 100%.", 400);
        if (req.monthlyFee < 0 || req.monthlyFee > 2_000_000_000) throw new EditorError("The monthly fee must be between 0 and 2,000,000,000.", 400);
        if (req.futureFee < -1 || req.futureFee > 2_000_000_000) throw new EditorError("The future fee must be between 0 and 2,000,000,000, or none.", 400);
        long manager = Mem.ReadPtr(Addresses.Game);
        if (manager == 0) throw new EditorError("FM isn't ready to make loans yet.", 503);

        // The form, set up as FM's loader sets it up (0x298EDA0), then our values.
        var f = new byte[Size];
        BitConverter.GetBytes(Addresses.StartingLoan).CopyTo(f, 0);
        BitConverter.GetBytes(-1L).CopyTo(f, 0x08);
        foreach (var at in new[] { StartsAt, EndsAt, OtherDateAt }) BitConverter.GetBytes(0x076C0001).CopyTo(f, at);
        BitConverter.GetBytes(-1L).CopyTo(f, 0x3C);
        BitConverter.GetBytes(-1L).CopyTo(f, 0x41);
        Defaults.CopyTo(f, 0x58);
        BitConverter.GetBytes(unchecked((long)0xFF00000000000000)).CopyTo(f, 0x68);
        BitConverter.GetBytes(p).CopyTo(f, PersonAt);
        BitConverter.GetBytes(team).CopyTo(f, LoanTeamAt);
        Date(today).CopyTo(f, StartsAt);
        Date(ends).CopyTo(f, EndsAt);
        BitConverter.GetBytes(req.monthlyFee).CopyTo(f, MonthlyFeeAt);
        BitConverter.GetBytes(req.futureFee).CopyTo(f, FutureFeeAt);
        BitConverter.GetBytes((short)req.wageShare).CopyTo(f, WageShareAt);

        Backup.EnsureBackedUp();
        Log.Info($"Starting a loan: player 0x{p:X} to team 0x{team:X} until {req.ends}");
        // FM runs its own start-loan function on a short-lived thread of its own (Mem.CallInGame),
        // with the form copied into its memory: start-loan(manager, form, 1).
        var stub = new byte[]
        {
            0x48, 0x83, 0xEC, 0x28,                         // sub rsp, 0x28
            0x48, 0x89, 0xCA,                               // mov rdx, rcx       (the form)
            0x48, 0xB8, 0, 0, 0, 0, 0, 0, 0, 0,             // mov rax, &manager
            0x48, 0x8B, 0x08,                               // mov rcx, [rax]
            0x41, 0xB8, 0x01, 0x00, 0x00, 0x00,             // mov r8d, 1
            0x48, 0xB8, 0, 0, 0, 0, 0, 0, 0, 0,             // mov rax, start-loan
            0xFF, 0xD0,                                     // call rax
            0x48, 0x83, 0xC4, 0x28,                         // add rsp, 0x28
            0x31, 0xC0,                                     // xor eax, eax
            0xC3,                                           // ret
        };
        BitConverter.GetBytes(Addresses.Game).CopyTo(stub, 9);
        BitConverter.GetBytes(Addresses.StartLoan).CopyTo(stub, 28);
        var form = new byte[0x100];
        f.CopyTo(form, 0);
        Mem.CallInGame(stub, form, 15000);
        Players.Refresh(req.player);
        string name = Players.Summary(p)?.name ?? "The player", to = Players.TeamClub(team) ?? "the new club";
        if (Loans.Find(p) == 0) throw new EditorError($"FM didn't start the loan. {name} is still at his club.", 500);
        Log.Info($"Loaned {name} to {to}");
        return new { ok = true, message = $"Loaned {name} to {to} until {ends:d MMM yyyy}", canUndo = false };
    }
}
