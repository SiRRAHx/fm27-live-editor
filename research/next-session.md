# Next FM session (Test save only, never save)

One launch covers all of this. Plugin build installed: transfers, status values, kit numbers, squad-wide morale/fitness.

## 1. Test what's built
- Player: change morale, condition, sharpness, a reputation, kit number; continue one day and check FM shows them.
- Club: "Superb morale" and "Full fitness" for a squad.
- Transfers: set "Your club" to Manchester United, then
  - move an AI club's player to Man Utd (fee and wage set),
  - swap two players between two AI clubs,
  - release an AI club's player, then Undo,
  - continue a few days: FM must stay stable and show the moved players at their new clubs.
- Earlier features not yet tested in game: squad status, clause edit, club finances, nationality.

## 2. Research (read-only snapshots)
- **Loan:** the user loans a player in or out through FM; `transfer_watch.py before/after` around it. Look for the loan record (P+0xB8 was cleared when El Hilali signed).
- **Sell-on clauses:** open the contract screen of players with clause types 10, 13, 19, 40, 41 (e.g. Cyriaque Irié, type 13, 25%; Christoph Lang, type 40, 10%) and match FM's words to each type. Check whether the amount is the owed club's id.
- **Tactical familiarity:** snapshot the Man Utd club and senior team, change one familiarity value through normal play is not possible quickly, so look for 0-20 byte tables in the club/team objects that match the Tactics > Familiarity screen (user reads out the values for one tactic).
- **Squad registration:** the user opens a competition's squad registration screen; find the competition's registered-player list (pointers to Man Utd players) with `find_refs.py "<player>"`, comparing a registered and an unregistered player.
- **Club reputation and facilities (built, check):** open Man Utd's club info and facilities screens; check the editor's reputation, training, youth facilities/coaching/recruitment, corporate and "Founded" (should be 1878) match. Change one facility and check FM shows it.
- **Professional status:** compare a semi-pro club (e.g. a National League side) with a professional one; `GET /api/research/club?id=` for both and diff the club and info-record bytes for a flag that differs.
- **Owner / sugar daddy:** the info record's owner (+0x90) points somewhere; read it for a known benefactor club (e.g. Man City, Newcastle) and a normal one, and look for the benefactor type byte.
- **Heal / bans / freeze:** note the injury list of an injured player (find_refs on the player), and a suspended player, for later.

## Done on 3 Nov (Test save)
- Index bug fixed: the editor now notices when FM rebuilds its people at the same addresses (ids compared).
- Verified in FM: transfer budget, training facilities (shown after a day), condition/morale, move/swap/release/undo, professional status write (+0x16A, shown at once).
- Menu button height and chevron match; the top-bar pencil sits after Search and opens the app.
- Still to do: sugar daddy/owner type (owner record not found yet), kit number and squad status in game, loans, familiarity, registration.
- Cosmetic: a moved player's appearances this season show under the new club in career stats.
- Later on 3 Nov: kit number (shows at once, clashes refused), squad status (after a day), whole-squad morale, bonus clause edit all verified.
- Clause types matched on FM's contract screens: 8 = sell-on fee %, 22 = optional extension by club, 39 = assist bonus. Next: check a sell-on write on a player we own who has one (Branthwaite, 10% to Everton), and find "profit from next sale".
- Other clubs' clauses show "Scouting Required" in FM, so clause wording can only be read on our own players.
- Competition bonuses (e.g. winning the Champions League) aren't in the clause vector.
- Sell-on % write verified: Branthwaite 10% → 25% showed on FM's contract page; 100% accepted, 101% refused; restored to 10%.
- Bug found: the index can run while FM is still loading a save and catch only some people (11,044 of 95,362). Fixed in the build waiting to install: one recheck 30 s after each index, keeping the larger result.
- Verified after a day: wage and end date (Branthwaite £180K to 2032), release clause (Balde £859M → £50M), bank balance (+£10M kept).
- Loan contract found and mapped: research/loan-contract.md (Balde, Barcelona → Man Utd).
- Fix needed: for a loanee the editor names the loan club but edits the parent contract; the Contract tab should say "Parent club contract (Barcelona)".
- Nationality is display-only; editing it is still to build.
- find_refs.py fails under the Store Python (can't see .NET); run memscan.exe findptr from the shell with DOTNET_ROOT set.

## Moving players out of the human club (started 3 Nov, Test save, unsaved)
- Pointers to Man Utd players: Bruno ~97 to P (tactic/training/squad lists, stride-0x30 records), Kai Rooney
  (U18) ~30, an AI player ~21. The person object is never freed, so nothing dangles; the risk is FM's logic.
- Experiment: moved Kai Rooney (U18) Man Utd → Grimsby via /transfers/move with humanClubs=[] (no code change).
  FM showed him at Grimsby; Training and Individual Training screens fine; played to Wed 2/9 with no crash.
- The user then played through the Everton match and checked every screen: all fine.
- Next: play through the Everton match; open Tactics/Squad screens; then try a first-team squad player
  (e.g. Karl Darlow). If both hold, allow moving out of your club for players not in the matchday squad/tactic.
