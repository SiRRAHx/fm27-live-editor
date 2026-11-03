# Feature gap: SI In-Game Editor (and FM Live Editor 26) vs ours

✅ built · 🧪 built, not yet tested in game · 🔬 needs research · ⬜ not started

## Players
| Feature | SI IGE | Ours |
|---|---|---|
| Attributes, CA/PA, positions | ✅ | ✅ |
| Freeze attributes | ✅ | ⬜ (needs a periodic re-write; cheap, no hooks) |
| Personality / hidden attributes, traits | ✅ | ⬜ 🔬 |
| Nationality (second nationality) | ✅ | ⬜ shown only, not editable yet |
| Reputation, morale, condition, sharpness | ✅ | ✅ morale and condition checked |
| Heal injuries, lift bans | ✅ | ✅ holds over a day; FM shows him fit (P−0x190 → injury record lists) |
| Clear unhappiness | ✅ | ⬜ 🔬 |
| Contract: wage, end date, squad status | ✅ | ✅ all three show after a day (FM's wage totals update at once) |
| Clauses (release, sell-on %, profit %) | ✅ | ✅ bonus edit and sell-on % (type 8, 0-100) checked in FM; profit % 🔬 |
| Squad number | ✅ | ✅ shows at once; taken numbers refused |
| Move to club, swap, release | ✅ | ✅ AI clubs; FM stays stable over days |
| Move players out of your own club | ✅ | ✅ U18 (Rooney) and bench GK (Darlow): FM drops them from tactic, bench and registration itself; a set-piece taker/captain is untested |
| Edit loan terms (wage, end, fees, future fee) | ✅ | ✅ shows at once in FM, holds over a day (P+0xB0 → holder → loan contract) |
| End a loan early (back to parent club) | ✅ | ✅ Shelton (AI loan) and Koné (our U21s) show at their parent clubs at once; stable over 2 days |
| Create a new loan | ✅ | ✅ Transfers → Loan: club, end date, wage share, monthly and future fee. Made by FM's own start-loan code (STARTING_LOAN form → 0xBF8740), so the contract is in FM's pool. Tested 3 Nov (Heaven, Munro, Lacey); End loan reverses it. Clause tick boxes not yet; build-checked, so an FM update makes it refuse |
| Tactical familiarity / cohesion | ✅ | 🔬 |
| Register squad any time | ✅ | 🔬 |
| Bulk edit / clone / templates | FMLE26 | ⬜ (easy once single edits are solid) |

## Clubs
| Feature | SI IGE | Ours |
|---|---|---|
| Balance, transfer and wage budgets | ✅ | ✅ (FM shows it after a day) |
| Reputation | ✅ | ✅ reads right; FM shows no number to compare |
| Training, youth, corporate facilities | ✅ | ✅ (FM shows it after a day) |
| Whole-squad morale / fitness | ✅ | ✅ 74 players set |
| Professional status (semi-pro / pro) | ✅ | ✅ club +0x16A: 1 pro, 2 semi, 3 amateur |
| Owner type / sugar daddy (benefactor) | ✅ | 🔬 FMLibre's info +0x90 isn't a pointer here; data in research/owners.json |
| Supporters (mix, loyalty, affluence), ticket price, season tickets | ✅ | ⬜ offsets known from FMLibre |
| Stadium capacity, expansion | ✅ | ⬜ offsets known from FMLibre |
| Attendance, affiliations, kits | ✅ | ⬜ |

## Beyond players and clubs
| Feature | SI IGE | Ours |
|---|---|---|
| Staff attributes, roles, move staff | ✅ | ⬜ |
| Unsackable manager | ✅ | 🔬 |
| Nations (reputation, rules) | ✅ | ⬜ |
| Edit history with undo | FMLE26 | Partial (transfer undo, save backup) |

## Where we can be better
- Free, and works on Game Pass, where SI's IGE doesn't.
- Every write is checked against expected bytes, so a stale edit is refused instead of corrupting the save.
- Automatic save backup before the first edit.
- Undo for transfers. This could become undo for every edit (keep the old bytes per write).
- Search across all clubs and players without opening FM screens.
