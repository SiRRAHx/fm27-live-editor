// Agrees a transfer the way FM's own database does at the start of a career: a
// db::STARTING_FUTURE_TRANSFER (player, new club, fee, date) handed to FM's start-up code, which
// turns it into a real agreed transfer (a FULL_TRANSFER_OFFER) that FM then completes on that
// date like any deal made in the game.
//
// FM's processor (rva 0xB7A9A0) walks the start-up table (global 0x53B81C8, list at +0x80) and
// clears it when done, so the form has to come from FM's own pool: the call below allocates it
// there (0xCFB60 on pool 0x53B77F0), runs FM's constructor (0xC83BD0), copies our fields in,
// adds it to the table with FM's own list code (0xD43220) and runs the processor with the game
// object. The addresses are for game_plugin.dll dated 10 Nov 2026, found by lining FM's code up
// with the 2 Nov build's (research/transfers.md); each is checked against the code it should hold
// before anything is called.
//
// FM marks offers made from the database with flag 0x2000 (offer +0x30), and when such a deal
// completes it moves the player but no money. Clearing the flag afterwards makes it a normal
// deal: on the day FM takes the fee from the buyer, books it as Players Sold for the seller and
// moves his wages (tested 4 Nov 2026). FM's own offers never carry that flag. The board's share
// of the fee for the selling club's budget is set when a deal is agreed in negotiation, which
// this skips, so the editor applies the budget changes itself. The selling club's share defaults to
// its finances +0x7D0, which holds 100, 25, 20 or 15 across clubs; Man Utd's 100 matched a sell-on
// payment going straight into their budget (5 Nov 2026).
//
// Instalments: FM keeps every club's monthly transfer payments as 0x28-byte records in a vector at
// [game] +0xD0: +0x0 the club that pays, +0x8 the club paid, +0x10 start date, +0x14 an empty date,
// +0x18 the amount each month, +0x1C the player's id, +0x20 12 (monthly), +0x21 payments left.
// FM's start-up code for db::STARTING_MONTHLY_INSTALLMENT (0xc3c2b0) builds them with operator new
// and adds them with its own vector code (0xc351e0); the call below does the same. FM's database
// deals take instalments off the fee paid up front, so the deal FM shows is the up-front part.
using System;
using System.Linq;

namespace FM27LiveEditor;

public static class FutureTransfer
{
    public class Request
    {
        public int player { get; set; }
        public int club { get; set; }
        public int fee { get; set; }
        public string date { get; set; }  // yyyy-MM-dd, when he joins; today or later
        public int? upFront { get; set; }  // paid when he joins; the rest is paid monthly. Empty for all of it
        public int months { get; set; }    // monthly instalments, 0 for none
        public System.Collections.Generic.Dictionary<string, string> patch { get; set; } // research only: form offset (hex) -> bytes (hex)
    }

    const long Alloc = 0xCFB60, Pool = 0x53B77F0, Ctor = 0xC83BD0, Grow = 0xD43220, Process = 0xB7A9A0, Table = 0x53B81C8, TableClass = 0x49F73C8;
    static readonly (long at, byte[] code)[] Code =
    {
        (Alloc, Convert.FromHexString("4156565753")),
        (Ctor, Convert.FromHexString("55564883EC28488D6C2420")),
        (Grow, Convert.FromHexString("41574156415541545657555348")),
        (Process, Convert.FromHexString("4157415641554154565755534881ECB8")),
    };
    // Only for instalments (research only). Still the 2 Nov build's: not found in this one yet.
    static readonly (long at, byte[] code)[] InstalmentCode =
    {
        (New, Convert.FromHexString("40534883EC20488B")),
        (Delete, Convert.FromHexString("E903BA3900")),
        (InstalmentGrow, Convert.FromHexString("4157415641554154565755534883EC28")),
    };
    const long New = 0x46FCA0C, Delete = 0x46FCBD8, InstalmentGrow = 0xC351E0;
    const int InstalmentsAt = 0xD0, InstalmentSize = 0x28, Monthly = 12, MaxMonths = 60;
    const int Size = 0xA0, CopyFrom = 0x18;
    const int PersonAt = 0x60, ToTeamAt = 0x68, FromTeamAt = 0x70, DateAt = 0x80, FeeAt = 0x84, DealAt = 0x9C;
    const int TeamAt = -0x158;
    // FM's offers: [rva 0x53A6240] +8 is a vector of them. Offer +0x30 flags, +0x38 player,
    // +0x40 buying team, +0x8D status (0x13 agreed).
    const long Offers = 0x53A6240;
    const int ShareAt = 0x7D0;
    const int OfferFlagsAt = 0x30, OfferPlayerAt = 0x38, OfferBuyerAt = 0x40, OfferStatusAt = 0x8D, Agreed = 0x13, FromDatabase = 0x2000;

    static byte[] Date(DateTime d) => new[] { (byte)(d.DayOfYear & 0xFF), (byte)(d.DayOfYear >> 8), (byte)(d.Year & 0xFF), (byte)(d.Year >> 8) };

    public static object Start(Request req)
    {
        long mod = Mem.ModuleBase("game_plugin.dll");
        foreach (var (at, expect) in Code)
            if (Mem.Read(mod + at, expect.Length) is not { } live || !live.SequenceEqual(expect))
                throw new EditorError("This version of FM isn't one the editor knows how to agree transfers in yet, so nothing was changed.", 409);
        long table = Mem.ReadPtr(mod + Table);
        if (table == 0 || Mem.ReadPtr(table) != mod + TableClass)
            throw new EditorError("Couldn't find FM's transfer table, so nothing was changed.", 409);
        long list = Mem.ReadPtr(table + 0x80);
        var head = list == 0 ? null : Mem.Read(list, 16);
        if (head == null || BitConverter.ToInt64(head, 0) != BitConverter.ToInt64(head, 8))
            throw new EditorError("FM is still setting up transfers from the database. Try again after a day has passed.", 409);
        if (Addresses.Game == 0 || Mem.ReadPtr(Addresses.Game) == 0) throw new EditorError("FM isn't ready to agree transfers yet.", 503);

        long p = Players.Find(req.player);
        if (Mem.ReadPtr(p) != Addresses.Person) throw new EditorError("Only players can be transferred.", 409);
        long from = Mem.ReadPtr(p + TeamAt);
        if (!Players.IsTeam(from)) throw new EditorError("He has no club to be sold by.", 409);
        if (Loans.Find(p) != 0) throw new EditorError("He's on loan. End the loan first.", 409);
        long club = Clubs.Find(req.club), team = Clubs.SeniorTeam(club);
        if (team == 0 || Mem.Read(team + 0x2F, 1)?[0] != 0) throw new EditorError("Couldn't find that club's first team.", 409);
        if (Mem.ReadPtr(from + 0x30) == club) throw new EditorError("He already plays for that club.", 409);
        if (req.fee < 0 || req.fee > 2_000_000_000) throw new EditorError("The fee must be between 0 and 2,000,000,000.", 400);
        var today = Transfers.Today() ?? throw new EditorError("Couldn't read the game date.", 500);
        var date = today.AddDays(1);
        if (!string.IsNullOrEmpty(req.date) && (!DateTime.TryParseExact(req.date, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out date) || date < today))
            throw new EditorError("The transfer date must be today or later.", 400);
        if (date > today.AddYears(2)) throw new EditorError("A transfer can be agreed at most two years ahead.", 400);
        int upFront = req.upFront ?? req.fee;
        // FM counts the editor's instalment records down but doesn't pay them (research/transfers.md),
        // so they stay research only until the payment side is found.
        if (req.months != 0 && !Research.On) throw new EditorError("Instalments aren't available yet. The whole fee is paid when he joins.", 400);
        if (req.months > 0 && InstalmentCode.Any(c => Mem.Read(mod + c.at, c.code.Length) is not { } b || !b.SequenceEqual(c.code)))
            throw new EditorError("Instalments aren't available in this version of FM yet.", 409);
        if (req.months < 0 || req.months > MaxMonths) throw new EditorError($"Instalments can be paid over 0 to {MaxMonths} months.", 400);
        if (req.months == 0) upFront = req.fee;
        if (upFront < 0 || upFront > req.fee) throw new EditorError("The amount paid up front must be between 0 and the fee.", 400);
        if (req.months > 0 && upFront == req.fee) throw new EditorError("Pay less than the whole fee up front, or set no instalments.", 400);
        // Equal monthly payments; what doesn't divide evenly is paid up front.
        int monthly = req.months == 0 ? 0 : (req.fee - upFront) / req.months;
        upFront = req.fee - monthly * req.months;
        long buyerFin = Clubs.Finances(club), sellerFin = Clubs.Finances(Mem.ReadPtr(from + 0x30));
        if (req.fee > 0 && (buyerFin == 0 || sellerFin == 0)) throw new EditorError("One of the clubs has no finances, so no fee can be paid. Set the fee to 0.", 400);
        int share = ClubShare(sellerFin); // the selling board's own share of sales

        // The form as FM's constructor leaves it (0xC83BD0), then our values. Only +0x18 on is copied.
        var f = new byte[Size];
        BitConverter.GetBytes(0x076C0001).CopyTo(f, 0x80);
        BitConverter.GetBytes(0x076C0001076C0001).CopyTo(f, 0x8C);
        BitConverter.GetBytes(unchecked((int)0xFF060000)).CopyTo(f, 0x96);
        f[0x9A] = 0xFF;
        BitConverter.GetBytes(p).CopyTo(f, PersonAt);
        BitConverter.GetBytes(team).CopyTo(f, ToTeamAt);
        BitConverter.GetBytes(from).CopyTo(f, FromTeamAt);
        Date(date).CopyTo(f, DateAt);
        BitConverter.GetBytes(upFront).CopyTo(f, FeeAt);
        f[DealAt] = 1;
        if (req.patch != null && Research.On)
            foreach (var (at, hex) in req.patch) Convert.FromHexString(hex).CopyTo(f, Convert.ToInt32(at, 16));

        // data: [0, 0xA0) the form, +0xA0 the new form's address (for the list), +0xA8 result.
        var stub = new System.Collections.Generic.List<byte>();
        void B(params byte[] b) => stub.AddRange(b);
        void MovRax(long v) { B(0x48, 0xB8); B(BitConverter.GetBytes(v)); }
        B(0x53, 0x56, 0x57);                         // push rbx, rsi, rdi
        B(0x48, 0x83, 0xEC, 0x30);                   // sub rsp, 0x30
        B(0x48, 0x89, 0xCE);                         // mov rsi, rcx            (data)
        MovRax(mod + Pool); B(0x48, 0x89, 0xC1);     // mov rcx, pool
        B(0xBA); B(BitConverter.GetBytes(Size));     // mov edx, 0xA0
        MovRax(mod + Alloc); B(0xFF, 0xD0);          // call alloc
        B(0x48, 0x85, 0xC0);                         // test rax, rax
        int failJump = stub.Count; B(0x0F, 0x84, 0, 0, 0, 0); // jz fail
        B(0x48, 0x89, 0xC7);                         // mov rdi, rax            (form)
        B(0x48, 0x89, 0xC1);                         // mov rcx, rax
        MovRax(mod + Ctor); B(0xFF, 0xD0);           // call ctor
        B(0xB9); B(BitConverter.GetBytes((Size - CopyFrom) / 8)); // mov ecx, count
        B(0x48, 0x8D, 0x56, CopyFrom);               // lea rdx, [rsi+0x18]
        B(0x4C, 0x8D, 0x47, CopyFrom);               // lea r8, [rdi+0x18]
        int loop = stub.Count;
        B(0x48, 0x8B, 0x02);                         // mov rax, [rdx]
        B(0x49, 0x89, 0x00);                         // mov [r8], rax
        B(0x48, 0x83, 0xC2, 0x08);                   // add rdx, 8
        B(0x49, 0x83, 0xC0, 0x08);                   // add r8, 8
        B(0xFF, 0xC9);                               // dec ecx
        B(0x75, (byte)(loop - (stub.Count + 2)));    // jnz loop
        B(0x48, 0x89, 0xBE, 0xA0, 0, 0, 0);          // mov [rsi+0xA0], rdi
        MovRax(mod + Table); B(0x48, 0x8B, 0x00);    // mov rax, [table]
        B(0x48, 0x8B, 0x88, 0x80, 0, 0, 0);          // mov rcx, [rax+0x80]     (the list)
        B(0x48, 0x8B, 0x51, 0x08);                   // mov rdx, [rcx+8]        (its end)
        B(0x4C, 0x8D, 0x86, 0xA0, 0, 0, 0);          // lea r8, [rsi+0xA0]
        MovRax(mod + Grow); B(0xFF, 0xD0);           // call grow-and-insert
        MovRax(Addresses.Game); B(0x48, 0x8B, 0x08); // mov rcx, [game]
        MovRax(mod + Process); B(0xFF, 0xD0);        // call processor
        B(0x48, 0x89, 0xBE, 0xA8, 0, 0, 0);          // mov [rsi+0xA8], rdi
        int fail = stub.Count;
        B(0x48, 0x83, 0xC4, 0x30);                   // add rsp, 0x30
        B(0x5F, 0x5E, 0x5B);                         // pop rdi, rsi, rbx
        B(0x31, 0xC0, 0xC3);                         // xor eax, eax; ret
        var code = stub.ToArray();
        BitConverter.GetBytes(fail - (failJump + 6)).CopyTo(code, failJump + 2);

        Backup.EnsureBackedUp();
        string name = Players.Summary(p)?.name ?? "The player", to = Players.TeamClub(team) ?? "the new club";
        Log.Info($"Agreeing a transfer: player 0x{p:X} to team 0x{team:X} for {req.fee} on {date:yyyy-MM-dd}");
        var before = AgreedOffers(mod, p, team);
        var data = new byte[0x100];
        f.CopyTo(data, 0);
        Mem.CallInGame(code, data, 15000);

        long offer = AgreedOffers(mod, p, team).FirstOrDefault(o => !before.Contains(o));
        if (offer == 0) throw new EditorError($"FM didn't agree the move, so {name} stays where he is.", 409);
        var flags = Mem.Read(offer + OfferFlagsAt, 4);
        if (flags != null && (BitConverter.ToUInt32(flags, 0) & FromDatabase) != 0)
            Mem.Write(offer + OfferFlagsAt, BitConverter.GetBytes(BitConverter.ToUInt32(flags, 0) & ~(uint)FromDatabase));
        if (req.months > 0)
        {
            var rec = new byte[InstalmentSize];
            BitConverter.GetBytes(club).CopyTo(rec, 0x0);                     // pays
            BitConverter.GetBytes(Mem.ReadPtr(from + 0x30)).CopyTo(rec, 0x8); // is paid
            Date(date).CopyTo(rec, 0x10);
            BitConverter.GetBytes(0x076C0001).CopyTo(rec, 0x14);
            BitConverter.GetBytes(monthly).CopyTo(rec, 0x18);
            BitConverter.GetBytes(req.player).CopyTo(rec, 0x1C);
            rec[0x20] = Monthly;
            rec[0x21] = (byte)req.months;
            AddInstalment(mod, rec);
        }
        if (req.fee > 0)
        {
            AddToBudget(buyerFin, -(long)req.fee);
            AddToBudget(sellerFin, (long)req.fee * share / 100);
        }
        string terms = req.months == 0 ? "" : $" (£{upFront:N0} up front, then £{monthly:N0} a month for {req.months} months)";
        return new { ok = true, message = $"{name} agreed to join {to} on {date:d MMM yyyy} for £{req.fee:N0}{terms}", canUndo = false };
    }

    // Adds a monthly instalment record (0x28 bytes, as above) to FM's list, the way FM's own
    // start-up code does: operator new, then push_back with FM's vector code.
    static void AddInstalment(long mod, byte[] rec)
    {
        // data: [0, 0x28) the record, +0x28 its address once made.
        var stub = new System.Collections.Generic.List<byte>();
        void B(params byte[] b) => stub.AddRange(b);
        void MovRax(long v) { B(0x48, 0xB8); B(BitConverter.GetBytes(v)); }
        B(0x53, 0x56, 0x57);                         // push rbx, rsi, rdi
        B(0x48, 0x83, 0xEC, 0x30);                   // sub rsp, 0x30
        B(0x48, 0x89, 0xCE);                         // mov rsi, rcx            (data)
        B(0xB9); B(BitConverter.GetBytes(InstalmentSize)); // mov ecx, 0x28
        MovRax(mod + New); B(0xFF, 0xD0);            // call operator new
        B(0x48, 0x89, 0xC3);                         // mov rbx, rax
        for (int k = 0; k < InstalmentSize; k += 8)
        {
            B(0x48, 0x8B, 0x86); B(BitConverter.GetBytes(k)); // mov rax, [rsi+k]
            B(0x48, 0x89, 0x43, (byte)k);                     // mov [rbx+k], rax
        }
        B(0x48, 0x89, 0x5E, InstalmentSize);         // mov [rsi+0x28], rbx
        MovRax(Addresses.Game); B(0x48, 0x8B, 0x00); // mov rax, [game]
        B(0x48, 0x8B, 0x90); B(BitConverter.GetBytes(InstalmentsAt + 8)); // mov rdx, [rax+0xD8]  (end)
        B(0x48, 0x3B, 0x90); B(BitConverter.GetBytes(InstalmentsAt + 16)); // cmp rdx, [rax+0xE0] (capacity)
        int full = stub.Count; B(0x74, 0);           // je grow
        B(0x48, 0x89, 0x1A);                         // mov [rdx], rbx
        B(0x48, 0x83, 0x80); B(BitConverter.GetBytes(InstalmentsAt + 8)); B(0x08); // add qword [rax+0xD8], 8
        int added = stub.Count; B(0xEB, 0);          // jmp done
        stub[full + 1] = (byte)(stub.Count - (full + 2));
        B(0x48, 0x8D, 0x88); B(BitConverter.GetBytes(InstalmentsAt)); // lea rcx, [rax+0xD0]
        B(0x4C, 0x8D, 0x46, InstalmentSize);         // lea r8, [rsi+0x28]
        MovRax(mod + InstalmentGrow); B(0xFF, 0xD0); // call grow-and-insert (takes the record)
        B(0x48, 0x8B, 0x4E, InstalmentSize);         // mov rcx, [rsi+0x28]
        B(0x48, 0x85, 0xC9);                         // test rcx, rcx
        B(0x74, 12);                                 // jz done
        MovRax(mod + Delete); B(0xFF, 0xD0);         // call delete (not taken, as FM does)
        stub[added + 1] = (byte)(stub.Count - (added + 2));
        B(0x48, 0x83, 0xC4, 0x30);                   // add rsp, 0x30
        B(0x5F, 0x5E, 0x5B);                         // pop rdi, rsi, rbx
        B(0x31, 0xC0, 0xC3);                         // xor eax, eax; ret
        var data = new byte[0x40];
        rec.CopyTo(data, 0);
        Mem.CallInGame(stub.ToArray(), data, 15000);
    }

    // Every agreed transfer of him to that team. FM's list isn't in the order offers were made
    // (Cunha's new deals were at 6,167 of 17,040), so the whole list is searched.
    static System.Collections.Generic.List<long> AgreedOffers(long mod, long p, long team)
    {
        var found = new System.Collections.Generic.List<long>();
        long table = Mem.ReadPtr(mod + Offers);
        var head = table == 0 ? null : Mem.Read(table + 8, 16);
        if (head == null) return found;
        long b = BitConverter.ToInt64(head, 0), e = BitConverter.ToInt64(head, 8);
        if (b == 0 || e < b || (e - b) % 8 != 0 || (e - b) / 8 > 1_000_000) return found;
        var ptrs = Mem.Read(b, (int)(e - b));
        if (ptrs == null) return found;
        for (int i = 0; i < ptrs.Length; i += 8)
        {
            long o = BitConverter.ToInt64(ptrs, i);
            var r = o == 0 ? null : Mem.Read(o, OfferStatusAt + 1);
            if (r == null || BitConverter.ToInt64(r, OfferPlayerAt) != p || BitConverter.ToInt64(r, OfferBuyerAt) != team || r[OfferStatusAt] != Agreed) continue;
            if (LoanStart.ClassOf(mod, BitConverter.ToInt64(r, 0)) == ".?AVFULL_TRANSFER_OFFER@@") found.Add(o);
        }
        return found;
    }

    // The share of sales the selling club's board gives back to its budget, or none if unreadable.
    static int ClubShare(long fin)
    {
        var b = fin == 0 ? null : Mem.Read(fin + ShareAt, 4);
        int v = b == null ? 0 : BitConverter.ToInt32(b, 0);
        return v is >= 0 and <= 100 ? v : 0;
    }

    // Changes a club's transfer budget, never below 0.
    static void AddToBudget(long fin, long amount)
    {
        if (amount == 0) return;
        var b = Mem.Read(fin + Clubs.TransferBudgetAt, 4);
        if (b == null) return;
        long now = Math.Clamp(BitConverter.ToInt32(b, 0) + amount, 0, int.MaxValue);
        Mem.Write(fin + Clubs.TransferBudgetAt, BitConverter.GetBytes((int)now));
    }
}
