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

## Nationality and national teams (4 Nov 2026)

- P + 0x68: NATION (+0x8 id, +0x18 name). Changing it moves his caps with it on FM's screens.
- P - 0x168: INTERNATIONAL_PLAYER_DATA. +0x2C senior debut date, +0x30 first goal date
  (day-of-year word, year word). No nation in it.
- P - 0x160: NATIONAL_TEAM of the squad he's in now (about 1 in 10 players; ~20 per team).
- NATIONAL_TEAM records are 0x108 long: +0x8 id, +0x88 captain, +0x90 vice-captain.

## Club debts and sponsorships (4 Nov 2026, Man Utd in the Test save)

Debts: club info (club + 0x100) + 0x50 is a vector of pointers to 0x1C-byte debt records:
+0x0 amount borrowed, +0x4 monthly repayment, +0x8 interest part of each repayment, +0xC 0,
+0x10 end date, +0x14 start date (repayments start), +0x18 flags (0x0101 / 0x07000101 seen).
FM's "remaining" is monthly x months left, and "interest" is the +0x8 part x months left.
Man Utd: 415,000,000 / 8,017,000 / 983,102, 2026-06-10 to 2031-06-10; 169,755,008 / 2,986,401;
200,000,000 / 5,534,745 from 2026-10-01 to 2029-12-31. The vector was full (end == capacity).
STARTING_CLUB_DEBT (0x68, factory at rva 0xd40700, added to its table by 0xd40950) is only the
new-game form; no live objects of that class exist in a running career.

Sponsorships: club + 0x130 points to a block whose first field is a vector of pointers to 0x18-byte
deal records: +0x0 start date, +0x4 end date, +0x8 total value, +0xC per season, +0x10 club
reputation when signed (high word), +0x14 type: 0x116 kit manufacturer, 0x101 main kit, 0x113 shirt
sleeve, 0x10D training kit, 4 / 0x104 / 6 general partners. Expired deals stay in the list.
The records sit in one big array for all clubs; the per-club vector only points into it.

Scouting budget: not in the club, club info, finances or senior team. "Adjust Scouting Budget"
changes only the transfer budget at finances + 0x7C4 there; the scouting figure was only found
in UI copies (SCOUTING_INFO_UI_HANDLER + 0x264, next to the season's starting figure).

Tested 4 Nov 2026: writing to those debt and sponsorship records (pay off the 169M loan, Training Kit
to 25M a season) changed nothing on FM's Debts and Sponsorship screens, even after leaving the Club
section and coming back. Both were put back byte for byte. The screens were built from somewhere
else: the only other places with the old figures were a stack copy (the screen's own) and two
identical packed blobs (count 3, then the debts, then what looks like the sponsorship deals), e.g.
at an odd address like ...DF2. The live records are still to be found.

Parked 4 Nov 2026. After one day of Continue the heap debt and deal records (and the packed blobs)
were at new addresses with my test edits gone, so all of them are rebuilt by FM each day. Not yet
found: what they're rebuilt from. Searching the heap for the loan amounts as int64 only turned up
generic value nodes (all pointing at one shared node). Next ideas: the code that fills club info +
0x50 and club + 0x130, PROCESS_CLUB_LOANS_AND_DEBTS, and the CLUB_BUDGET_ADJUSTMENT_* and
SCOUTING_INFO_UI_HANDLER_SERVER handlers (where the server side gets its figures).

National team test (Test save, 4 Nov 2026): Bellingham and Mainoo switched to Wales on 2 Sep 2026.
FM's profile then says "Wales, 56 caps" and "Eligibility: Plays for Wales", but Bellamy's squad for
the late-September window (named 21 Sep) had neither, and they're in no other squad either.
NATIONAL_TEAM + 0x38 is the current squad (vector of person pointers, P - 0x288); the international
data's +0x50 vector holds the current call-up (empty when not selected). No per-nation candidate
list was found in NATIONAL_TEAM, NATION or the international data. A TEAM object (0xB8 long, id at
+0x8) keeps a 23-player list at +0x38 that still includes Bellingham alongside England's earlier
squad. Still to find: how the AI chooses its candidates (maybe players it has watched or knows).

Morale (same day): FM's words for the 1-20 value: 20 Perfect, 16 Extremely Good, 13 Good,
11 Fairly Good, 10 Okay, 8 Fair, 7 Slightly Poor, 4 Poor, 1 Abysmal. The squad screen keeps the
old icons until the day moves on. FM moves low morale back up about 5 points a day.

Tactical familiarity (4 Nov 2026): in FM27 it's "Tactical Understanding", shown per player on the
Tactics screen (Selection Info > Tactical Understanding) as pies for Passing Style, Build Up, Chance
Creation, In Possession Shape, Pressing Intensity, Pressing Trap, Out Of Possession Shape, Line of
Engagement, plus Player Tactical Ability and Tactical Complexity. No numbers are shown. One day of
Continue (2-4 Sep, no match) changed nothing that looked like it in memory around 10 Man Utd players
(P - 0x1000 .. P + 0x1000 and the objects it points to) or in the club's objects 3 levels deep, as
floats, doubles or small ints. It may only move after matches or weekly. FM has an in-game-editor
action TEAM_MAXIMISE_TACTICAL_UNDERSTANDING_ACTION (vt rva 0x4c77658); its slots only build text
and dispatch, so where it writes wasn't found. Not called (it's SI's paid editor).
Later the same day: a week of training (4 Tactical Understanding sessions) and a match (Everton 4-2
Man Utd, instant result) still changed nothing in memory that matched the pies, around the players
(two levels deep) or the club (three levels, 129k objects). FM's tooltips say each area is made from
"recent training sessions", "recent matches", the player's tactical ability and the tactic's
complexity, so it looks worked out each time from history rather than stored. Shelved.

## Squad registration at any time (4 Nov 2026, shelved for now)

- SI's IGE has TEAM_CONTAINER_ALLOW_SQUAD_REGISTRATION_ANYTIME_ACTION. Its menu code (rva 0x103b600/0x103b800)
  only builds the tick box; the flag it shows is bit 0 of byte +0x3FD on the object FM passes in.
- FM reads that bit in two places (rva 0x313f802, inside the registration-window check at 0x313f650, and
  0x3594a1b). Both only look at it when the IGE's global switches are on (bytes at rva 0x567f93b, 0x567f94f,
  0x567f900, 0x567f951, 0x56843d0). In a normal game they are off, so the bit does nothing.
- Turning those switches on would be unlocking the paid IGE, so that route is out.
- The other route: the same check compares today against window dates at +0x3C/+0x3E of an object it gets
  from the competition (0x3b00b40 on [0x56759b0]+0x18). Moving those dates would open the window for every
  club in that competition. Not tried yet.

## Owner money / sugar daddy (4 Nov 2026)

- CLUB_FINANCE + 0x34 (byte). FM's monthly owner-funding code (rva 0x13df8a0, called from the club's month
  routine at 0x377d840 that also makes SUGAR_DADDY_WITHDRAWS_FUNDING_NEWS) skips owner money when it's 0 and
  moves it itself (1 -> 2 or 3, -> 3 when the owner withdraws).
- Values seen (Test save): Man City and PSG 1, Como, Wolves, Hull, AEK 2, Chelsea, Leeds, Bournemouth and
  most owner-backed clubs 3, Monaco, Nantes, Hartlepool 4, Man Utd and 244 of 313 clubs 0. That fits the
  community names: 1 foreground, 2 background, 3 underwriter, 4 underwriter that expects returns.
- FM's Club Info "Owner Status" ("Loves the club") is something else: it didn't change.
- Test: Man Utd set to 1, then 2 months on holiday (once with £169M, once from -£50M). FM kept the value at 1,
  but no owner money showed up (Investments £10k and £7k, same as before). FM may only pay out at other times
  (transfer windows, board requests, end of season), so the effect is not shown yet.
