# Agreed transfers through FM (4–5 Nov 2026, Test save)

game_plugin.dll 27.0 dated 2 Nov 2026. Addresses are rvas.

**10 Nov 2026 build.** The addresses below are the 2 Nov build's. FM's update moved them; found by lining the new processor up with the old disassembly (difflib on instructions, 588 of 914 matched) and confirming in the running game by class name. Data moved by about −0x2CBFE0 throughout; code by varying amounts. Form and offer layouts are unchanged (the constructor sets the same defaults).

| What | 2 Nov | 10 Nov |
|---|---|---|
| Processor | 0xBF6270 | 0xB7A9A0 (stack 0xB8, was 0xA8) |
| Table global | 0x56841A8 | 0x53B81C8 |
| Table class (DATABASE_TABLE<STARTING_FUTURE_TRANSFER>) | 0x4C5EF08 | 0x49F73C8 |
| Form pool / alloc | 0x56837D0 / 0x14FA40 | 0x53B77F0 / 0xCFB60 |
| Form ctor | 0xD08AA0 | 0xC83BD0 |
| List insert (unique_ptr vector) | 0xDD99F0 | 0xD43220 |
| Offers global | 0x5672240 | 0x53A6240 |
| Cross-border check | 0x1CEE1F0 | 0x1BC44B0 |
| Already-happened path | 0xBFB930 | 0xB7FE70 |

FM's own "make a start-up transfer form" helper is at 0xC9E5C0 in the new build (allocates 0xA0 from the form pool and fills it from its arguments). Instalment addresses (new, delete, instalment insert) aren't found in the new build yet.

## How the editor agrees a transfer

FM's start-up code turns database future transfers into real deals. `core/FutureTransfer.cs` hands it one.

- `db::STARTING_FUTURE_TRANSFER` table: global 0x56841A8 (vtable 0x4C5EF08), list at +0x80.
- Form: 0xA0 bytes, from pool 0x56837D0 (alloc 0x14FA40), ctor 0xD08AA0.

| Offset | Field |
|---|---|
| +0x18/+0x20 | Clause records |
| +0x60 | Person |
| +0x68 | Buying team |
| +0x70 | Selling team |
| +0x78 | Flags qword (bit 55: the already-happened path, 0xbfb930) |
| +0x7E | Flags byte |
| +0x80 | Join date |
| +0x84 | Fee |
| +0x88 | |
| +0x94 | Contract type |
| +0x9C | Deal (1) |

- Vector insert: 0xDD99F0(vec, pos, &value).
- Processor: 0xBF6270(rcx = [game]). It walks the list and makes a FULL_TRANSFER_OFFER for each form:
  - pool 0x5680e20, ctor 0x3600ca0;
  - fill 0x1c9d970;
  - register 0xaf1230;
  - status 0x13 via 0x1ca28a0;
  - schedule 0xb796e0;
  - then clears the list.
- FM shows it like any deal: an offer on his Transfer tab, "X make £N offer", then "X sign Y" on the day, his career history with the fee, and the Transfer Hub.

## Offers

- [0x5672240] +8 is a vector of offers. FULL_TRANSFER_OFFER is 0xD0 bytes.

| Offset | Field |
|---|---|
| +0x30 | Flags |
| +0x38 | Player |
| +0x40/+0x48 | Buying team |
| +0x50 | Selling team |
| +0x58 | Fee |
| +0x5C | Offer id |
| +0x8D | Status (0x13 agreed, 0x14 completed) |

- The processor sets flag 0x2000 (`or byte [offer+0x31], 0x20`), and it also clears the offer's clause vectors (+0x10..+0x30).
- **Flag 0x2000 means "from the database: no money".** The completion code (0xb98b20) checks it at 0xb9aa46 and 0xb9b0cc, and skips the seller's finance handling.
  - Dermot Mee, £10M with the flag: he moved, but neither club's balance or income changed for the fee.
  - Ayden Heaven, £5M with the flag cleared before the day: Burnley balance −5,000,000 exactly; Man Utd +4,758,224; Income "Players Sold" +£5,000,000; his wage left Man Utd's bill.
  - Josh Cullen, £2M to Man Utd: Man Utd −2,000,000 exactly; "Spent This Season" +£2M; Burnley +1,315,520 (FM's own cuts, likely a sell-on).
- None of 1,428 recent FM-made offers has 0x2000 set (flags seen: 0, 0x40, 0x10000000, 0x40000000), so clearing it leaves a normal offer.

## Transfer budgets

- Completion doesn't change either club's transfer budget: Burnley's stayed 4.4M after paying £5M, and Man Utd's stayed put after the Heaven sale. FM must set budgets when a deal is agreed in negotiation, so the editor does it: buyer −fee (not below 0), seller +fee × share.
- Finances +0x7D0 looks like the board's share of sales, in %.
  - Values seen: 100 (Man Utd, Burnley), 25, 20, 15.
  - Man Utd's 100 matched a £500K sell-on payment going straight into their budget on 5 Nov.
  - The share defaults to this.
- 0x13dc980(finance, club, delta, …) is FM's own "change transfer budget" routine (it uses the IGE switches at 0x567f9xx, so it isn't called).

## Instalments (6 Nov 2026)

### Form clause records

- 0x30-byte plain structs made with operator new (0x46FCA0C; delete is 0x46FCBD8) by SetField (0x370eb40).
- Layout:

| Offset | Field |
|---|---|
| +0x0 | 0 |
| +0x8 | -1 |
| +0xC | Type |
| +0x18 | PFmm (months) |
| +0x1C | Amount |
| +0x2D | PFmi (byte) |

- The type comes from the database code via 0x370e600. Emulating it for every code gave:
  - 13 is `PFmC`/`PFmc`/`PFmi`/`PFmm`, the monthly instalments;
  - 12 is `PFp?`, 11 is `PFl?`, 10 is `PFi?`;
  - 21 is `PFSF`/`PSOF`/`PSPC`/`PSPP`;
  - 9 is `PFns`.
- The processor only sums types 13/64/65 to take them off the up-front fee. It then clears the offer's clause vectors, and 0xbfb930 just sets up his personal terms (a CONTRACT_OFFER). So FM's database deals show the up-front part as the fee.

### Instalment records

- Monthly instalments live in a vector at [game] +0xD0 (1,220 records in the Test save). Each is a 0x28-byte plain struct:

| Offset | Field |
|---|---|
| +0x0 | Club that pays |
| +0x8 | Club paid |
| +0x10 | Start date |
| +0x14 | Empty date |
| +0x18 | Amount each payment |
| +0x1C | Player id (P+8) |
| +0x20 | 12, 6 or 1 (12 in 1,169 records) |
| +0x21 | Payments left |

- Checked against Cunha, Tielemans and Mbeumo (Man Utd pays) and Antony (Betis pays Man Utd).
- db::STARTING_MONTHLY_INSTALLMENT rows (0x40 bytes, vtable 0x4C616A8; the table class is 0x4C5F7F8; creator 0xd3d710) become these through INITIALISE_STARTING_MONTHLY_INSTALLMENT. That's a std::function, functor vtable 0x4C47058, call slot 0xc3c2b0. It does operator new(0x28), fills the record and push_backs it onto [game]+0xD0, using 0xc351e0 when the vector is full.
- The editor builds the same record after the deal is made (FutureTransfer.AddInstalment).
- **But no money moves for it (checked 4 Nov 2026, Test save).**
  - The deal: Ellis Clark, Burnley to Man Utd on 2 Dec, £10M as £1M up front then £3M a month × 3.
  - FM counted the record down from 3 to 2 on the join day, so FM takes the first payment on the start date, not a month later.
  - Man Utd's December Transfer Expenditure was £1,917,816. That's his £1M up front plus about £854K of FM's own instalments due on 1–3 Dec (records 11690, 19258, 76878, 88886 and 99398). No £3M.
  - Burnley got no £3M either.
  - So FM's own records are paid and booked as Transfer Expenditure, and ours is only counted down. Whatever pays them is still unknown: the decrement is in the obfuscated code at 0x13b2548, and the payer may also check the transfer history entry ("contains further transfer clauses").
- The dates' day field counts from 1 (0x150 = 2 Dec 2026).
- Club +0xB0 is a pointer to a vector of INFO object pointers, not a vector in place.
  - Man Utd's holds 96 objects: 50 APPEARANCE_MONEY, 20 COMPETITION_BONUS, 17 NEXT_SALE_PERCENTAGE and 1 EXTRA_TRANSFER_PAYMENT, among others.
  - So the instalments aren't paid from there.
- Club Finances → Debts "Transfer Debt" doesn't count these records; it counts the club's own records at club +0xB0 (EXTRA_TRANSFER_PAYMENT_INFO, NEXT_SALE_PERCENTAGE_INFO, APPEARANCE_MONEY_INFO, …).

### Other clauses (not done)

- Sell-ons, appearance fees and the like live in the club's +0xB0 list, as db INFO objects with vtables. They need their own work.

Loans on the 10 Nov build: start-loan code found by Addresses at 0xB7CD80; only its stack frame changed (sub rsp 0x188 -> 0x198), form offsets read are unchanged, so only the signature in LoanStart.cs was updated. Tested: Mainoo loaned to Al Hilal 2 Dec 2026 - 30 Jun 2027.
