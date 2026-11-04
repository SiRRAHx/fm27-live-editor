// Made-up players for trying the editor without FM running (npm run demo).
// Behaves like the in-game plugin's API so the window works the same way.
const { GROUPS, POSITIONS } = require('./attributes');

let seed = 7;
const rand = (lo, hi) => { seed = (seed * 16807) % 2147483647; return lo + (seed % (hi - lo + 1)); };

const PEOPLE = [
  ['Demo Keeper', 'Riverside FC', 27, 'GK'], ['Demo Full-Back', 'Riverside FC', 24, 'DR'],
  ['Demo Centre-Back', 'Riverside FC', 29, 'DC'], ['Demo Anchor', 'Riverside FC', 31, 'DM'],
  ['Demo Playmaker', 'Riverside FC', 22, 'AMC'], ['Demo Winger', 'Harbour United', 20, 'AMR'],
  ['Demo Striker', 'Harbour United', 25, 'ST'], ['Demo Wonderkid', 'Harbour United', 17, 'ST'],
];

const players = PEOPLE.map(([name, club, age, pos], i) => {
  const attributes = {};
  for (const g of GROUPS) for (const k of g.keys) {
    const keeperOnly = g.id === 'goalkeeping';
    attributes[k] = keeperOnly && pos !== 'GK' ? rand(1, 4) : rand(5, 17);
  }
  const positions = Object.fromEntries(POSITIONS.map((p) => [p, p === pos ? 20 : pos !== 'GK' && p !== 'GK' ? rand(1, 12) : 1]));
  return {
    id: 1000 + i, name, club, age, position: pos, ca: rand(90, 160), pa: rand(130, 185), attributes, positions,
    status: { morale: rand(8, 16), condition: rand(80, 100) * 100, sharpness: rand(60, 100) * 100, homeReputation: rand(30, 80) * 100, currentReputation: rand(30, 80) * 100, worldReputation: rand(10, 60) * 100 },
    contract: {
      wage: rand(10, 180) * 1000, ends: `${2027 + rand(0, 4)}-06-30`, joined: `${2019 + rand(0, 6)}-07-01`, squadStatus: [1, 2, 3, 4, 13][i % 5], squadNumber: i + 1,
      clauses: [
        { index: 0, type: 0, name: 'Minimum fee release clause', money: true, value: rand(20, 90) * 1000000 },
        { index: 1, type: 33, name: 'Goal bonus', money: true, value: rand(1, 20) * 1000 },
        { index: 2, type: 3, name: 'Yearly wage rise (%)', money: false, value: 10 },
        { index: 3, type: 18 },
      ],
    },
  };
});
players.forEach((p) => { if (p.pa < p.ca) p.pa = p.ca; });
// Case and accents ignored, as in the plugin's search.
const fold = (t) => (t || '').normalize('NFD').replace(/\p{Mn}/gu, '').toLowerCase();

// One demo player is on loan from another club.
Object.assign(players[1], { loan: { club: players[1].club, wage: 55000, starts: '2026-07-24', ends: '2027-06-30', monthlyFee: 150000, unusedFee: 300000, futureFee: null,
  clauses: { recall: true, cannotTerminate: true, cupForParent: false, againstParent: false, cupMatches: true }, futureFeeIf: null, league: 'Premier League' } });
players[1].contract.club = 'Harbour United';
players.forEach((p) => { p.health = { injuries: [], bans: [] }; });
players[2].health = { injuries: [{ name: 'calf strain', started: '2026-08-20', backFrom: '2026-09-06', backBy: '2026-10-03' }], bans: [{ matches: 2 }] };

let lastMove = null;
const clubs = [
  { id: 1, name: 'Riverside FC', status: 1, reputation: 7400, founded: 1886, facilities: { training: 17, youthFacilities: 15, youthCoaching: 16, youthRecruitment: 14, corporate: 18 }, finances: { balance: 48250000, transferBudget: 15000000, wageBudget: 950000, wages: 870000 } },
  { id: 2, name: 'Harbour United', status: 2, reputation: 4100, founded: 1902, facilities: { training: 9, youthFacilities: 8, youthCoaching: 10, youthRecruitment: 7, corporate: 6 }, finances: { balance: -3200000, transferBudget: 2500000, wageBudget: 310000, wages: 325000 } },
];

module.exports = {
  async status() {
    return { connected: true, demo: true, saveLoaded: true, editing: true, game: 'Demo data', backup: null };
  },
  async search(q) {
    const s = fold(q);
    return players.filter((p) => !s || fold(p.name).includes(s) || fold(p.club).includes(s))
      .map(({ id, name, club, age, position, ca, pa }) => ({ id, name, club, age, position, ca, pa }));
  },
  async player(id) {
    const p = players.find((x) => x.id === id);
    if (!p) throw new Error('Player not found');
    return structuredClone(p);
  },
  async savePlayer(id, changes) {
    const p = players.find((x) => x.id === id);
    if (!p) throw new Error('Player not found');
    Object.assign(p.attributes, changes.attributes || {});
    Object.assign(p.positions, changes.positions || {});
    Object.assign(p.status, changes.status || {});
    if (changes.ca != null) p.ca = changes.ca;
    if (changes.pa != null) p.pa = changes.pa;
    const { clauses, ...rest } = changes.contract || {};
    Object.assign(p.contract, rest);
    for (const [i, v] of Object.entries(clauses || {})) p.contract.clauses[i].value = v;
    if (p.contract.squadNumber === 0) p.contract.squadNumber = null;
    for (const [k, v] of Object.entries(changes.loan || {})) {
      if (k === 'clauses') Object.assign(p.loan.clauses, v);
      else if (k === 'futureFeeIf') p.loan.futureFeeIf = v || null;
      else p.loan[k] = v === -1 ? null : v;
    }
    if (changes.heal) p.health.injuries = [];
    if (changes.liftBans) p.health.bans = [];
    return { ok: true, player: structuredClone(p) };
  },
  async saveSquad(id, values) {
    const c = clubs.find((x) => x.id === id);
    const squad = players.filter((p) => p.club === c.name);
    for (const p of squad) Object.assign(p.status, values);
    return { ok: true, players: squad.length };
  },
  async transferStatus() {
    return { today: '2026-09-09', lastMove: lastMove?.text || null };
  },
  async transfer(action, req) {
    if (action === 'undo') {
      if (!lastMove) throw new Error("There's no move to undo.");
      for (const [p, club] of lastMove.before) p.club = club;
      const text = `Undid: ${lastMove.text}`;
      lastMove = null;
      return { ok: true, message: text };
    }
    const p = players.find((x) => x.id === req.player);
    if (!p) throw new Error('Player not found');
    if (!p.club) throw new Error('This player has no club.');
    if (action === 'end-loan') {
      if (!p.loan) throw new Error("This player isn't on loan.");
      const text = `Ended ${p.name}'s loan at ${p.loan.club}`;
      lastMove = { text, before: [[p, p.club]] };
      p.loan = null;
      return { ok: true, message: text, canUndo: true };
    }
    if (action === 'loan') {
      const to = clubs.find((c) => c.id === req.club);
      if (p.loan) {
        if (to.name === p.loan.club) throw new Error('He already plays for that club.');
        if (to.name === p.contract.club) throw new Error("That's his own club. Use End loan to send him back.");
        p.club = p.contract.club;
        p.loan = null;
      }
      if (to.name === p.club) throw new Error('He already plays for that club.');
      const wage = Math.round(p.contract.wage * req.wageShare / 100);
      p.loan = { club: to.name, wage, starts: '2026-09-09', ends: req.ends, monthlyFee: req.monthlyFee || null, unusedFee: null, futureFee: req.futureFee >= 0 ? req.futureFee : null };
      p.contract.club = p.club;
      p.club = to.name;
      lastMove = null;
      return { ok: true, message: `Loaned ${p.name} to ${to.name} until ${req.ends}`, canUndo: false };
    }
    let text;
    const before = [[p, p.club]];
    if (action === 'move') {
      const to = clubs.find((c) => c.id === req.club);
      if (to.name === p.club) throw new Error("He's already at that club.");
      text = `Moved ${p.name} from ${p.club} to ${to.name}`;
      p.club = to.name;
    } else if (action === 'swap') {
      const o = players.find((x) => x.id === req.other);
      before.push([o, o.club]);
      text = `Swapped ${p.name} (${p.club}) and ${o.name} (${o.club})`;
      [p.club, o.club] = [o.club, p.club];
    } else {
      text = `Released ${p.name} from ${p.club}`;
      p.club = null;
    }
    lastMove = { text, before };
    return { ok: true, message: text, canUndo: true };
  },
  async searchClubs(q) {
    const s = fold(q);
    return clubs.filter((c) => fold(c.name).includes(s)).map(({ id, name }) => ({ id, name }));
  },
  async club(id) {
    const c = clubs.find((x) => x.id === id);
    if (!c) throw new Error('Club not found');
    return structuredClone(c);
  },
  async saveClub(id, changes) {
    const c = clubs.find((x) => x.id === id);
    if (!c) throw new Error('Club not found');
    const { reputation, facilities, status, ...money } = changes;
    if (status != null) c.status = status;
    Object.assign(c.finances, money);
    if (reputation != null) c.reputation = reputation;
    Object.assign(c.facilities, facilities || {});
    return { ok: true, club: structuredClone(c) };
  },
};
