# Loan contract (found 3 Nov 2026, Test save, Alejandro Balde on loan Barcelona → Man Utd)

A contract subclass, vtable RVA 0x4D78AF8 (ordinary contract 0x4AC05F8). Same base layout as a contract,
plus loan terms. Found by scanning for pointers to Balde's person object (memscan findptr) and checking
each holder. The parent contract (Barcelona, wage 109,956, ends 30/6/2028) stays at P+0xA8.

| Offset | Type | Balde | FM's Loan Club tab |
|---|---|---|---|
| +0x08 | ptr | person P | |
| +0x10, +0x18 | ptr | Man Utd senior team | loan club |
| +0x20 | i32 | 54,978 | wage £55K p/w |
| +0x26 | u8 | 1 | job: player |
| +0x44 | date | 205/2026 | start 24/7/2026 |
| +0x48 | date | 181/2027 | expiry 30/6/2027 |
| +0x4C | date | 205/2026 | joined |
| +0xC0 | i32 | 156,195,328 | optional future fee £156M |
| +0xCC | i32 | 150,000 | monthly playing fee £150K |
| +0xD0 | i32 | 300,000 | unused monthly fee £300K |
| +0xD4 | i32 | 54,978 | wage again (the loan club's share?) |
| +0xA0..+0xB8 | bytes | 37 19 02 02 02 02 02 02 8c 0c .. 06 10 | likely the loan clauses (can be recalled, cannot be terminated, can play in cup matches) |

Not yet known: what points to the loan contract from the player, team or club (P+0xB0 and P+0xB8 don't);
which bytes are the three loan clauses. Next: find_refs on the loan contract's address, and compare two loanees.

Editor note: for a loanee the editor shows the current (loan) club but edits the parent contract.

## 3 Nov, later: how a loan hangs together (1,183 loans sampled)
- P+0xB0 → a 32-byte holder: slot 0 = loan contract, slot 1 = some other contract-like object (775 players
  have slot 0 = 0 and slot 1 set, so "slot 0 = 0" is FM's own no-loan state). Holders are separate blocks.
- memscan: the holder is the only thing pointing at a loan contract (Mark Shelton, York → Grimsby).
- A loanee is in both clubs' first-team lists (team +0x38); P−0x158 names the loan club's team; P+0xA8 is
  still the parent contract (employer = parent first team).
- End loan (Transfers.EndLoan): off the loan club's lists and captaincies, P−0x158 → parent first team,
  holder slot 0 → 0. Allowed for the user's own club too since 3 Nov (FM tidies its tactic and registration).
- Clause bits at +0xB0: Balde 0x06 (can be recalled, cup matches), Koné 0x02 (cup matches, not vs parent).
  Setting 0x04 on Koné did not enable FM's "Recall From Loan", so recall needs more than that bit.
- Creating a loan needs a new loan contract and holder from FM's allocator; the editor never allocates
  game memory, so new loans stay out of scope.

## Where FM keeps loan contracts (3 Nov, Mantato's new loan at St Mirren)
- Person, holder, loan contract and parent contract all sit inside process-heap blocks (process heap =
  CRT heap), but as pieces of big blocks: the loan contract is at +0x4F00 of a 32 KB block.
- That block is a pool of 256-byte slots, one per loan contract: 114 of its 128 slots start with the
  loan vtable (RVA 0x4D78AF8), at a steady 0x100 stride. The holder is likewise inside a 2 KB block.
- So FM allocates loan contracts from its own fixed-size pool. An object the editor allocated itself
  would be foreign to that pool; if FM frees it in the same session (loan ends, recall, cancel), the
  pool gets corrupted. Taking a free slot would need the pool's bookkeeping, which means reading game
  code (off limits). FMRTE presumably calls FM's own functions to create loans.
- Untested idea: an editor-made loan is only foreign until the save is reloaded (FM rebuilds every
  object from the save file into its own pools). Needs testing on a copy of a save before considering.

## Finding FM's own loan code (3 Nov, static reading of game_plugin.dll 27.0, dated 2 Nov 16:28)
User lifted the stay-out-of-game-code rule for this. Read-only, from the file on disk; nothing patched.
- Class: `db::LOAN_CONTRACT` (RTTI). vtable RVA 0x4D78AF8 (section .trace).
- Code Fusion has moved ~6,000 functions into the RWX `.rsrc` section and scrambles their constants;
  the rest (134k functions) are plain in `.text1`. Exception table at 0x172CF000.
- Constructor: RVA 0xE9FA8F0 (in .rsrc), reached through the jump stub 0x1CB3030 (.text1).
- Three functions call the constructor:
  - 0xBE33C0 (size 0x54B): uses names CONTRACT_OFFER, LOAN_CONTRACT.
  - 0xBF8740 (size 0x206A): uses LOAN_OFFER, LOAN_CONTRACT, CONTRACT_OFFER. Best candidate for
    "turn an agreed loan offer into a loan". Called from 0xBF5E20 and 0x298EDA0.
  - 0x1A490F0 (size 0x66C): not yet identified.
- Scripts: session scratchpad re/dll.py (helpers), ctor.py, callers.py, refs.py.

### How FM creates a loan (3 Nov, continued; disassembled with capstone)
- 0xBF8740 = "start a loan from an agreed loan": (rcx = loan manager, rdx = offer, r8b = flag).
  Inside it: `alloc = 0x14FA40(pool @ RVA 0x5680738, 0x100)` then LOAN_CONTRACT ctor (0x1CB3030),
  so FM allocates the contract from its own pool itself. Offer fields read: +0x54 flags (bit 0x10 =
  skip), +0x18 person (if [+0xC] == -1, index [+8] into the people table at global 0x5683D28),
  +0x20 loan club/team, +0x28 parent club/team (falls back to the player's current club).
- Callers:
  - 0xBF5E20: daily; walks the agreed-loans list at global 0x5684190 (+0x80 vector) and starts each
    whose start date has arrived (date words at offer +0x30/+0x32 vs today), r8 = 0.
  - 0x298EDA0: database start-up. Builds a `db::STARTING_LOAN` ON THE STACK (vtable RVA 0x4C60D58,
    ~0x70 bytes, dates default 0x076C0001 = 1 Jan 1900), fills it through its vfunc +0x10
    SetField(code, value) from (code, value) pairs, requires +0x18 and +0x20 set, then calls
    0xBF8740(rcx = [global 0x5670D68], rdx = &starting_loan, r8b = 1).
  => Plan: build a STARTING_LOAN in our own memory the same way and call 0xBF8740 on FM's main
     thread. FM allocates and links the LOAN_CONTRACT itself.
- STARTING_LOAN SetField (0x370A3F0) codes -> offsets (values are typed-value objects):
  plyr person (+0x18, not yet confirmed); PLcl loan club (+0x20, Ttea = team); PLFl club (+0x28);
  PLsd start date, PLed end date (dates around +0x30..+0x38, to confirm); PLwc wage share (+0x6C, word);
  PLmf monthly fee (+0x5C); PLfb future fee (+0x60); Pfbb buy-back fee (+0x64); PLAC comp (+0x3C);
  PLAS (+0x40); PLAT (+0x48 byte); PLsn (+0x6E); PLNG (+0x6F); flag bits at +0x50 from PLbr, PLmc,
  PLcm, PLcr, PLct, PLfl, PLic, PLob, PLoc (clauses: recall, play in cups, etc.; to name).
- Confirmed (3 Nov): STARTING_LOAN layout (offsets from the object start; caller 0x298EDA0 init):
  +0x00 vtable (RVA 0x4C60D58); +0x08 qword -1; +0x10 dword 0; +0x18 person (db::ACTUAL_PLAYER*,
  the plugin's P is the object start); +0x20 loan TEAM*; +0x28 parent TEAM* (0 = current);
  +0x30 start, +0x34 end, +0x38 PcrE date (FM dates, default 0x076C0001); +0x3C qword -1;
  +0x41 qword -1; +0x50 qword clause flags (PLoc 0x1, PLic 0x2, PLbr 0x4, PLct 0x1000, PLcr 0x2000,
  PLob 0x8000, PLmc 0x10000, PLcm 0x100000); +0x58 16 bytes copied from RVA 0x4C3C660; +0x5C monthly
  fee; +0x60 future fee (PLfb); +0x64 buy-back (Pfbb); +0x68 qword 0xFF00000000000000; +0x6C wage
  share (word, PLwc); +0x6E PLsn; +0x6F PLNG.
  TEAM: +0x2F byte must be 0, +0x30 -> db::CLUB. Mantato's live LOAN_CONTRACT: +0x8 player, +0x10/+0x18 team.
- 0xBF8740 flag (r8b): 0 = in-game path, also sets up pro-rata monthly fee payments from +0x5C;
  1 = database start-up path (skips that, calls 0xB0A310 and 0xAF1230). Try 1 first.

## Shipped in the editor (3 Nov)
plugin/LoanStart.cs, route POST transfers/loan {player, club, ends, wageShare 0-100 (default 100),
monthlyFee, futureFee (-1 none)}; app Transfers → Loan tab. Calls 0xBF8740 with flag 1 on the main thread.
Before each call it checks the STARTING_LOAN and ACTUAL_PLAYER class names and the start-loan code's first
19 bytes, so a game update makes it refuse. Results: wage share works (Munro 50% → £600 of £1,200; Lacey
75% → £2,625 of £3,500); monthly and future fee come through; FM also copies the monthly fee into the
"unused fee". wageShare -1 gives a £0 loan wage on this path. End loan works on these loans.
Still to do: clause tick boxes (+0x50 bits, names not matched yet); re-loaning a player already on loan.

## 4 Nov: loan clauses matched to FM's contract screen (Loan Club tab)
Form flags (+0x50) -> loan contract bits (+0xB0), checked by making loans and reading FM's screen:
- 0x1 "Can play against own team" (loan 0x1)
- 0x2 "Can play in cup matches" (0x2)
- 0x4 "Can be recalled" (0x4); form +0x38 = recall-from date -> loan +0xF0 (1900-01-01 = any time,
  as on FM's own loans such as Balde's). FM greys out "Recall From Loan" on a loan started that day.
- 0x1000 "Loan cannot be terminated" (0x1000)
- 0x100000 "Can play for parent club in cup matches" (0x100000)
- FM adds 0x100 "Cannot play against parent club in cup matches" by itself (Heaven: form 0x2 -> 0x102;
  seen on loans between clubs in the same country).
- 0x20000000 came with a future fee tied to a condition (Danso: "Avoiding relegation from the Premier
  League"), but Balerdi's conditional fee ("Qualifying for the Champions League") has no such bit; the
  condition is stored elsewhere. 0x8, 0x8000, 0x40000000, 0x80000000 aren't shown on that screen.

## 4 Nov: future fee conditions
Loan form -> loan contract, checked on FM's Loan Club screen with test loans:
- form +0x48 byte -> loan +0x9C: what has to happen. 14 or 0 "Winning the [league]" (Broja 14; tests
  Lacey 0, Mantato 14), 3 "Promotion from the [league]" (Mainoo to Swansea), 6 "Avoiding relegation
  from the [league]" (Kukonki, Heaton), 9 "Qualifying for the [comp]" with the comp itself (Nunez, 637 =
  Champions League), 10 qualifying through the league (Balerdi: +0xC4 Serie A, +0x90 637). 255 = none
  (an optional future fee).
- form +0x3C -> loan +0xC4: the competition's number (db::COMP +0x8). A team's league: team +0x50 ->
  db::COMP; its short name (as FM shows it) at COMP +0x48 ("EFL League One"). Wigan 9, Swansea 8 (EFL
  Championship), Man Utd 7 (Premier League).
- form +0x40 -> loan +0x94 and form +0x44 -> loan +0x98: not shown on that screen (12 and 15 tried);
  probably the minimum games / stage FM's language file mentions, not used yet.
The editor offers win / promotion / avoid relegation, always in the loan club's own league.
