const $ = (s) => document.querySelector(s);
function h(tag, attrs, ...kids) {
  const el = document.createElement(tag);
  for (const [k, v] of Object.entries(attrs || {})) {
    if (v == null || v === false) continue;
    if (k.startsWith('on')) el.addEventListener(k.slice(2), v);
    else if (k === 'class') el.className = v;
    else el.setAttribute(k, v === true ? '' : v);
  }
  for (const kid of kids.flat()) if (kid != null && kid !== false) el.append(kid);
  return el;
}
function toast(text, err) {
  // Errors from the main process arrive as "Error invoking remote method 'x': Error: ...".
  text = String(text).replace(/^Error invoking remote method '[^']*': (Error: )?/, '');
  const t = h('div', { class: 'toast' + (err ? ' err' : '') }, text);
  $('#toasts').append(t);
  setTimeout(() => t.remove(), 5000);
}

// The editor's own confirm box, in place of Windows' plain one. Resolves true for the
// main button, false for Cancel, Escape or a click outside.
function ask(text, { title = 'Are you sure?', ok = 'OK', danger = false } = {}) {
  const d = $('#ask-dialog');
  $('#ask-title').textContent = title;
  $('#ask-text').textContent = text;
  const yes = $('#ask-yes');
  yes.textContent = ok;
  yes.className = `btn ${danger ? 'danger' : 'primary'}`;
  return new Promise((resolve) => {
    const done = (v) => { d.onclose = null; if (d.open) d.close(); resolve(v); };
    yes.onclick = () => done(true);
    $('#ask-no').onclick = () => done(false);
    d.onclose = () => done(false);
    d.onclick = (e) => { if (e.target === d) done(false); };
    d.showModal();
    yes.focus();
  });
}
const discard = () => ask("You have changes that aren't applied yet. If you go on, they're thrown away.", { title: 'Unsaved changes', ok: 'Throw away', danger: true });

let info;
let status = { connected: false };
let current = null; // the player as the game has it
let edit = null; // the player with this session's changes

// --- Tabs ---
document.querySelectorAll('.nav').forEach((b) => b.addEventListener('click', () => {
  document.querySelectorAll('.nav').forEach((n) => n.classList.toggle('active', n === b));
  document.querySelectorAll('main > section').forEach((s) => s.classList.toggle('hidden', s.id !== `tab-${b.dataset.tab}`));
  updateApplyBar();
}));
document.querySelectorAll('.club-tabs .type-tab').forEach((b) => b.addEventListener('click', () => {
  document.querySelectorAll('.club-tabs .type-tab').forEach((t) => t.classList.toggle('active', t === b));
  document.querySelectorAll('.club-pane').forEach((p) => p.classList.toggle('hidden', p.id !== `club-pane-${b.dataset.clubPane}`));
  // Undo and Apply are for the Finances and Reputation tabs; the others act straight away or only show.
  $('.club-actions').classList.toggle('hidden', !['finances', 'reputation'].includes(b.dataset.clubPane));
}));
document.querySelectorAll('.editor-tabs .type-tab').forEach((b) => b.addEventListener('click', () => {
  document.querySelectorAll('.editor-tabs .type-tab').forEach((t) => t.classList.toggle('active', t === b));
  document.querySelectorAll('.pane').forEach((p) => p.classList.toggle('hidden', p.id !== `pane-${b.dataset.pane}`));
}));

// --- Connection ---
async function pollStatus() {
  try { status = await window.api.status(); } catch (e) { status = { connected: true, error: e.message }; }
  const g = $('#status-game');
  const s = $('#status-save');
  if (!status.connected || status.fmRunning === false) {
    g.className = 'status-item idle';
    g.textContent = 'Waiting for FM27…';
    s.classList.add('hidden');
  } else if (status.error || status.editing === false) {
    g.className = 'status-item bad';
    g.textContent = status.error || status.reason || 'Editing is switched off for this version of FM27.';
    s.classList.add('hidden');
  } else {
    g.className = 'status-item';
    g.textContent = status.demo ? 'Demo data' : `Connected to ${status.game || 'FM27'}`;
    s.className = 'status-item' + (status.saveLoaded ? '' : ' warn');
    s.textContent = status.saveLoaded ? `${status.players ? status.players.toLocaleString() + ' players' : 'Career loaded'}`
      : status.indexing ? 'Finding players…' : 'Load a career to start editing';
  }
  $('#status-backup').textContent = status.backup ? `Save backed up ${status.backup}` : '';
  const ready = status.connected && status.fmRunning !== false && !status.error && status.editing !== false && status.saveLoaded;
  const clubsHidden = $('#clubs-ui').classList.contains('hidden');
  $('#clubs-ui').classList.toggle('hidden', !ready);
  $('#clubs-offline').classList.toggle('hidden', ready);
  if (ready && clubsHidden) runClubSearch();
  $('#transfers-ui').classList.toggle('hidden', !ready);
  $('#transfers-offline').classList.toggle('hidden', ready);
  const wasHidden = $('#players-ui').classList.contains('hidden');
  $('#players-ui').classList.toggle('hidden', !ready);
  $('#offline').classList.toggle('hidden', ready);
  if (ready && wasHidden) runSearch();
}

// --- Search ---
let searchTimer;
$('#search').addEventListener('input', () => { clearTimeout(searchTimer); searchTimer = setTimeout(runSearch, 250); });
async function runSearch() {
  const box = $('#results');
  const q = $('#search').value.trim();
  try {
    const list = await window.api.search(q);
    const hint = q.length < 2 && !status.demo ? 'Type at least two letters of a name.' : 'No players found.';
    box.replaceChildren(...(list.length ? list.slice(0, 100).map(resultRow) : [h('div', { class: 'hint' }, hint)]));
  } catch (e) {
    box.replaceChildren(h('div', { class: 'hint' }, e.message));
  }
}
// Faces and badges come from the player's graphics packs, by FM's unique ID;
// the app shows a silhouette or a plain shield when there's none.
const imageUrl = (kind, uid, alt) => `fmimg://${kind}/${uid || 0}${alt ? `?alt=${alt}` : ''}`;
const thumb = (kind, uid, alt) => h('img', { class: `thumb ${kind}`, src: imageUrl(kind, uid, alt), alt: '', loading: 'lazy', draggable: 'false' });
function resultRow(p) {
  return h('button', { class: 'result' + (current?.id === p.id ? ' active' : ''), 'data-id': p.id, onclick: () => openPlayer(p.id) },
    thumb('person', p.uid),
    h('span', { class: 'pos-badge' + (p.position === 'GK' ? ' gk' : '') }, p.position || '–'),
    h('div', {}, h('b', {}, p.name), h('small', {}, [p.club, p.age && `${p.age} yrs`, p.born && `born ${p.born}`].filter(Boolean).join(' · '))));
}

// --- Editor ---
async function openPlayer(id) {
  if (changes() && !(await discard())) return;
  try {
    current = await window.api.player(id);
  } catch (e) { return toast(e.message, true); }
  edit = structuredClone(current);
  document.querySelectorAll('.result').forEach((r) => r.classList.toggle('active', +r.dataset.id === id));
  renderEditor();
}

const tier = (v) => (v <= 5 ? 1 : v <= 10 ? 2 : v <= 14 ? 3 : v <= 17 ? 4 : 5);
const clamp = (v, lo, hi) => Math.min(hi, Math.max(lo, Math.round(+v || lo)));

function attrRow(label, get, set, orig) {
  const val = h('input', { class: 'val', type: 'text', inputmode: 'numeric', 'aria-label': label });
  const row = h('div', { class: 'attr' });
  const show = () => {
    const v = get();
    val.value = v;
    val.className = `val v${tier(v)}`;
    row.classList.toggle('changed', v !== orig);
    updateApplyBar();
  };
  const change = (v) => { set(clamp(v, 1, 20)); show(); };
  val.addEventListener('change', () => change(val.value));
  val.addEventListener('keydown', (e) => {
    if (e.key === 'ArrowUp') { e.preventDefault(); change(get() + 1); }
    if (e.key === 'ArrowDown') { e.preventDefault(); change(get() - 1); }
  });
  row.append(h('span', { class: 'name' }, label),
    h('button', { class: 'step', title: 'Lower', onclick: () => change(get() - 1) }, '−'), val,
    h('button', { class: 'step', title: 'Raise', onclick: () => change(get() + 1) }, '+'));
  show();
  return row;
}

function renderEditor() {
  $('#editor-empty').classList.add('hidden');
  $('#editor').classList.remove('hidden');
  $('#p-pos').textContent = edit.position || '';
  $('#p-face').src = imageUrl('person', edit.uid);
  $('#p-badge').src = imageUrl('club', edit.clubUid, edit.clubBadgeUid);
  $('#p-badge').classList.toggle('hidden', !edit.clubUid);
  $('#p-name').textContent = edit.name;
  $('#p-sub').textContent = [edit.fullName !== edit.name && edit.fullName, edit.club, edit.age && `${edit.age} years old`, edit.born && `born ${edit.born}`, edit.nation].filter(Boolean).join(' · ');
  for (const k of ['ca', 'pa']) {
    const input = $(`#p-${k}`);
    input.value = edit[k] ?? '';
    input.disabled = edit[k] == null;
    input.onchange = () => {
      edit[k] = clamp(input.value, 1, 200);
      // Potential can't be below current ability.
      if (edit.pa < edit.ca) { if (k === 'ca') edit.pa = edit.ca; else edit.ca = edit.pa; }
      $('#p-ca').value = edit.ca; $('#p-pa').value = edit.pa;
      updateApplyBar();
    };
  }
  const keeper = edit.position === 'GK';
  // The cards flow down columns, so each long group is followed by a short one
  // to keep the columns even. Keepers see their own attributes first.
  const order = keeper ? ['goalkeeping', 'hidden', 'mental', 'feet', 'physical', 'technical']
    : ['technical', 'hidden', 'mental', 'feet', 'physical', 'goalkeeping'];
  const rank = (g) => (order.includes(g.id) ? order.indexOf(g.id) : order.length);
  const groups = info.groups.filter((g) => g.keys.some((k) => k in edit.attributes)).sort((a, b) => rank(a) - rank(b));
  $('#pane-attributes').replaceChildren(...groups.map((g) => h('div', { class: 'attr-card' },
    h('h3', {}, g.name), g.note && h('p', { class: 'card-note' }, g.note),
    h('div', { class: 'attr-grid' }, g.keys.filter((k) => k in edit.attributes).map((k) =>
      attrRow(g.labels[k], () => edit.attributes[k], (v) => { edit.attributes[k] = v; }, current.attributes[k]))))));
  renderContract();
  renderStatus();
  renderHealth();
  $('#positions').replaceChildren(...info.positions.filter((p) => p in (edit.positions || {})).map((p) =>
    attrRow(p, () => edit.positions[p], (v) => { edit.positions[p] = v; }, current.positions[p])));
  updateApplyBar();
}

// Condition and sharpness are stored as 0-10000 and shown as percentages.
const STATUS = { morale: 1, condition: 100, sharpness: 100, homeReputation: 1, currentReputation: 1, worldReputation: 1 };
const STATUS_RANGE = { morale: [1, 20], condition: [0, 10000], sharpness: [3500, 10000], homeReputation: [0, 10000], currentReputation: [0, 10000], worldReputation: [0, 10000] };
function renderStatus() {
  for (const [k, scale] of Object.entries(STATUS)) {
    const input = $(`#s-${k}`);
    const have = edit.status && k in edit.status;
    input.disabled = !have;
    input.value = have ? Math.round(edit.status[k] / scale) : '';
    const mark = () => { input.parentElement.classList.toggle('changed', have && edit.status[k] !== current.status[k]); updateApplyBar(); };
    input.onchange = () => {
      const [lo, hi] = STATUS_RANGE[k];
      // Unchanged on screen keeps the stored value (it may have decimals).
      const v = clamp(+input.value * scale, lo, hi);
      edit.status[k] = Math.round(v / scale) === Math.round(current.status[k] / scale) ? current.status[k] : v;
      input.value = Math.round(edit.status[k] / scale);
      mark();
    };
    mark();
  }
  renderNation();
}

// Nationality: any nation a player holds in this career. The list is fetched once per career.
let nations = null;
async function renderNation() {
  const sel = $('#s-nation');
  const have = edit.nationId != null;
  sel.disabled = !have;
  if (have && !nations?.length) {
    try { nations = await window.api.nations(); } catch { nations = null; }
  }
  const list = nations?.length ? nations : have ? [{ id: edit.nationId, name: edit.nation }] : [];
  sel.replaceChildren(...list.map((n) => h('option', { value: n.id }, n.name)));
  if (have) sel.value = edit.nationId;
  const mark = () => { sel.parentElement.classList.toggle('changed', have && edit.nationId !== current.nationId); updateApplyBar(); };
  sel.onchange = () => { edit.nationId = +sel.value; mark(); };
  mark();
}

// Injuries and bans: healing or lifting is applied straight away, like FM does
// when a player recovers or serves a ban.
const day = (d) => (d ? new Date(d).toLocaleDateString() : '?');
function renderHealth() {
  const hl = current.health || { injuries: [], bans: [] };
  $('#h-list').replaceChildren(
    ...hl.injuries.map((x) => h('p', { class: 'health-row' }, h('b', {}, x.name || 'Injury'), ` · back between ${day(x.backFrom)} and ${day(x.backBy)}`)),
    ...hl.bans.map((x) => h('p', { class: 'health-row' }, h('b', {}, 'Ban'), ` · ${x.matches} match${x.matches === 1 ? '' : 'es'} left`)),
    ...(hl.injuries.length || hl.bans.length ? [] : [h('p', { class: 'card-note' }, 'Fit and available: no injuries or bans.')]));
  $('#h-heal').disabled = !hl.injuries.length;
  $('#h-lift').disabled = !hl.bans.length;
}
async function healthAction(change, done) {
  if (changes() && !(await discard())) return;
  try {
    const res = await window.api.savePlayer(current.id, change);
    current = res.player;
    edit = structuredClone(current);
    renderEditor();
    toast(status.demo ? done : `${done} Reopen the player's screen in FM to see it.`);
  } catch (e) { toast(e.message, true); }
}
$('#h-heal').addEventListener('click', () => healthAction({ heal: true }, 'Injuries healed.'));
$('#h-lift').addEventListener('click', () => healthAction({ liftBans: true }, 'Bans lifted.'));
$('#l-end').addEventListener('click', async () => {
  if (!(await ask(`He goes back to ${current.contract.club}. Are you sure?`, { title: `End ${current.name}'s loan?`, ok: 'Confirm' }))) return;
  try {
    const res = await window.api.transfer('end-loan', { player: current.id });
    toast(`${res.message}. ${status.demo ? '' : 'Reopen his profile in FM to see it.'}`);
    showLastMove(res.message);
    openPlayer(current.id);
  } catch (e) { toast(e.message, true); }
});

function renderContract() {
  const c = edit.contract;
  $('#no-contract').classList.toggle('hidden', !!c);
  $('#contract-form').classList.toggle('hidden', !c);
  if (!c) return;
  const wage = $('#c-wage');
  const ends = $('#c-ends');
  wage.value = c.wage;
  ends.value = c.ends || '';
  $('#c-title').textContent = edit.loan ? `Parent club contract${c.club ? ` (${c.club})` : ''}` : 'Contract';
  renderLoan();
  $('#c-joined').textContent = c.joined ? `Joined the club on ${new Date(c.joined).toLocaleDateString()}.` : '';
  const mark = () => {
    wage.parentElement.classList.toggle('changed', edit.contract.wage !== current.contract.wage);
    ends.parentElement.classList.toggle('changed', edit.contract.ends !== current.contract.ends);
    statusBox.parentElement.classList.toggle('changed', edit.contract.squadStatus !== current.contract.squadStatus);
    number.parentElement.classList.toggle('changed', (edit.contract.squadNumber ?? null) !== (current.contract.squadNumber ?? null));
    updateApplyBar();
  };
  const number = $('#c-number');
  number.value = c.squadNumber ?? '';
  number.onchange = () => {
    edit.contract.squadNumber = number.value === '' ? null : clamp(number.value, 1, 99);
    number.value = edit.contract.squadNumber ?? '';
    mark();
  };
  const statusBox = $('#c-status');
  const known = info.squadStatuses.some(([id]) => id === c.squadStatus);
  statusBox.replaceChildren(...(known ? [] : [h('option', { value: '' }, 'Other (can\'t be changed here)')]),
    ...info.squadStatuses.map(([id, name]) => h('option', { value: id }, name)));
  statusBox.value = c.squadStatus ?? '';
  statusBox.onchange = () => { edit.contract.squadStatus = statusBox.value ? +statusBox.value : null; mark(); };
  const clauses = (c.clauses || []).filter((x) => x.name);
  const others = (c.clauses || []).length - clauses.length;
  $('#c-clauses').replaceChildren(
    ...(clauses.length ? [] : [h('p', { class: 'card-note' }, 'This contract has no clauses the editor can change.')]),
    ...clauses.map((x) => {
      const input = h('input', { type: 'number', min: 0, max: x.money ? 2000000000 : 100, step: x.money ? 1000 : 1, value: x.value });
      const row = h('label', {}, x.money ? `${x.name} (£)` : x.name, input);
      input.onchange = () => {
        const cl = edit.contract.clauses.find((y) => y.index === x.index);
        cl.value = clamp(input.value, 0, x.money ? 2000000000 : 100);
        input.value = cl.value;
        row.classList.toggle('changed', cl.value !== current.contract.clauses.find((y) => y.index === x.index).value);
        updateApplyBar();
      };
      return row;
    }),
    ...(others ? [h('p', { class: 'clause-other' }, `Plus ${others} other clause${others === 1 ? '' : 's'} the editor doesn't understand yet.`)] : []));
  wage.onchange = () => { edit.contract.wage = clamp(wage.value, 0, 10000000); wage.value = edit.contract.wage; mark(); };
  ends.onchange = () => { if (ends.value) edit.contract.ends = ends.value; else ends.value = edit.contract.ends; mark(); };
  mark();
}

const LOAN_FEES = ['monthlyFee', 'unusedFee', 'futureFee'];
// FM's names for the loan clauses, in the order its contract screen lists them.
const LOAN_CLAUSES = [['recall', 'Can be recalled'], ['cannotTerminate', 'Loan cannot be terminated'], ['cupForParent', 'Can play for parent club in cup matches'],
  ['againstParent', 'Can play against own team'], ['cupMatches', 'Can play in cup matches']];
function renderLoan() {
  const l = edit.loan;
  $('#loan-form').classList.toggle('hidden', !l);
  if (!l) return;
  $('#l-title').textContent = `Loan${l.club ? ` at ${l.club}` : ''}`;
  $('#l-note').textContent = l.starts ? `On loan since ${new Date(l.starts).toLocaleDateString()}. Leave a fee empty for none.` : 'Leave a fee empty for none.';
  const wage = $('#l-wage');
  const ends = $('#l-ends');
  const mark = () => {
    wage.parentElement.classList.toggle('changed', edit.loan.wage !== current.loan.wage);
    ends.parentElement.classList.toggle('changed', edit.loan.ends !== current.loan.ends);
    for (const k of LOAN_FEES) $(`#l-${k}`).parentElement.classList.toggle('changed', (edit.loan[k] ?? null) !== (current.loan[k] ?? null));
    $('#l-futureFeeIf').parentElement.classList.toggle('changed', (edit.loan.futureFeeIf ?? null) !== (current.loan.futureFeeIf ?? null));
    LOAN_CLAUSES.forEach(([k], i) => $('#l-clauses').children[i]?.classList.toggle('changed', !!edit.loan.clauses?.[k] !== !!current.loan.clauses?.[k]));
    updateApplyBar();
  };
  wage.value = l.wage;
  ends.value = l.ends || '';
  wage.onchange = () => { edit.loan.wage = clamp(wage.value, 0, 10000000); wage.value = edit.loan.wage; mark(); };
  ends.onchange = () => { if (ends.value) edit.loan.ends = ends.value; else ends.value = edit.loan.ends; mark(); };
  // The future fee's condition, in the loan club's league. A condition the editor can't set (such as
  // qualifying for a competition) is shown but stays as it is unless another one is picked.
  l.clauses ||= {};
  const feeIf = $('#l-futureFeeIf');
  const league = l.league || 'league';
  feeIf.replaceChildren(h('option', { value: '' }, 'If the loan club wants (optional)'),
    ...Object.entries(FEE_IF).map(([v, name]) => h('option', { value: v }, name(league))),
    ...(['qualify', 'other'].includes(current.loan.futureFeeIf) ? [h('option', { value: current.loan.futureFeeIf, disabled: true },
      current.loan.futureFeeIf === 'qualify' ? 'If they qualify for a competition' : 'Another condition')] : []));
  feeIf.value = l.futureFeeIf || '';
  feeIf.onchange = () => { edit.loan.futureFeeIf = feeIf.value || null; mark(); };
  $('#l-clauses').replaceChildren(...LOAN_CLAUSES.map(([k, name]) => {
    const box = h('input', { type: 'checkbox', class: 'toggle' });
    box.checked = !!l.clauses?.[k];
    box.onchange = () => { edit.loan.clauses[k] = box.checked; mark(); };
    return h('label', { class: 'clause' }, box, ` ${name}`);
  }));
  for (const k of LOAN_FEES) {
    const input = $(`#l-${k}`);
    input.value = l[k] ?? '';
    input.onchange = () => {
      edit.loan[k] = input.value === '' ? null : clamp(input.value, 0, 2000000000);
      input.value = edit.loan[k] ?? '';
      mark();
    };
  }
  mark();
}

function diff() {
  if (!current || !edit) return {};
  const out = {};
  const pick = (key) => {
    const changed = Object.fromEntries(Object.entries(edit[key] || {}).filter(([k, v]) => current[key][k] !== v));
    if (Object.keys(changed).length) out[key] = changed;
  };
  pick('attributes');
  pick('positions');
  if (edit.ca !== current.ca) out.ca = edit.ca;
  if (edit.pa !== current.pa) out.pa = edit.pa;
  pick('status');
  if (edit.nationId != null && edit.nationId !== current.nationId) out.nation = edit.nationId;
  if (edit.contract && current.contract) {
    const c = {};
    if (edit.contract.wage !== current.contract.wage) c.wage = edit.contract.wage;
    if (edit.contract.ends !== current.contract.ends) c.ends = edit.contract.ends;
    if (edit.contract.squadStatus !== current.contract.squadStatus && edit.contract.squadStatus != null) c.squadStatus = edit.contract.squadStatus;
    const clauses = {};
    for (const x of edit.contract.clauses || []) {
      const was = current.contract.clauses.find((y) => y.index === x.index);
      if (x.name && was && x.value !== was.value) clauses[x.index] = x.value;
    }
    if (Object.keys(clauses).length) c.clauses = clauses;
    if ((edit.contract.squadNumber ?? null) !== (current.contract.squadNumber ?? null)) c.squadNumber = edit.contract.squadNumber ?? 0;
    if (Object.keys(c).length) out.contract = c;
  }
  if (edit.loan && current.loan) {
    const l = {};
    if (edit.loan.wage !== current.loan.wage) l.wage = edit.loan.wage;
    if (edit.loan.ends !== current.loan.ends) l.ends = edit.loan.ends;
    // The game stores "no fee" as -1.
    for (const k of LOAN_FEES) if ((edit.loan[k] ?? null) !== (current.loan[k] ?? null)) l[k] = edit.loan[k] ?? -1;
    if ((edit.loan.futureFeeIf ?? null) !== (current.loan.futureFeeIf ?? null)) l.futureFeeIf = edit.loan.futureFeeIf ?? '';
    const clauses = Object.fromEntries(LOAN_CLAUSES.map(([k]) => k).filter((k) => !!edit.loan.clauses?.[k] !== !!current.loan.clauses?.[k]).map((k) => [k, !!edit.loan.clauses[k]]));
    if (Object.keys(clauses).length) l.clauses = clauses;
    if (Object.keys(l).length) out.loan = l;
  }
  return out;
}
function changes() {
  const d = diff();
  return Object.keys(d.attributes || {}).length + Object.keys(d.positions || {}).length + ('ca' in d) + ('pa' in d)
    + Object.keys(d.status || {}).length + ('nation' in d)
    + Object.keys(d.contract || {}).filter((k) => k !== 'clauses').length + Object.keys(d.contract?.clauses || {}).length
    + Object.keys(d.loan || {}).filter((k) => k !== 'clauses').length + Object.keys(d.loan?.clauses || {}).length;
}
function updateApplyBar() {
  const n = changes();
  const onPlayers = !$('#tab-players').classList.contains('hidden');
  $('#apply-bar').classList.toggle('hidden', !n || !onPlayers);
  $('#apply-count').textContent = `${n} change${n === 1 ? '' : 's'} to ${edit?.name || ''}`;
}

$('#undo').addEventListener('click', () => { edit = structuredClone(current); renderEditor(); });
$('#apply').addEventListener('click', async () => {
  const btn = $('#apply');
  btn.disabled = true;
  try {
    const d = diff();
    const res = await window.api.savePlayer(current.id, d);
    current = res.player;
    edit = structuredClone(current);
    renderEditor();
    // FM's screens show ability stars and contract details from a summary it
    // only refreshes as days pass, so those changes appear a day later.
    const later = ['ca' in d || 'pa' in d ? 'ability stars' : null, d.contract || d.loan ? 'contract details' : null].filter(Boolean);
    toast(status.demo ? 'Saved to the demo player.'
      : `Done. Reopen the player's screen in FM to see the new values.${later.length ? ` The ${later.join(' and ')} show after the next game day.` : ''}`);
  } catch (e) {
    toast(e.message, true);
  } finally {
    btn.disabled = false;
  }
});

// --- Clubs ---
let club = null; // as the game has it
const FIELDS = ['balance', 'transferBudget', 'wageBudget'];
let clubTimer;
$('#club-search').addEventListener('input', () => { clearTimeout(clubTimer); clubTimer = setTimeout(runClubSearch, 250); });
async function runClubSearch() {
  const box = $('#club-results');
  const q = $('#club-search').value.trim();
  try {
    const list = await window.api.searchClubs(q);
    const hint = q.length < 2 && !status.demo ? 'Type at least two letters of a club.' : 'No clubs found.';
    box.replaceChildren(...(list.length ? list.map((c) => h('button', { class: 'result' + (club?.id === c.id ? ' active' : ''), 'data-id': c.id, onclick: () => openClub(c.id) },
      thumb('club', c.uid, c.badgeUid), h('div', {}, h('b', {}, c.name)))) : [h('div', { class: 'hint' }, hint)]));
  } catch (e) {
    box.replaceChildren(h('div', { class: 'hint' }, e.message));
  }
}
const FACILITIES = ['training', 'youthFacilities', 'youthCoaching', 'youthRecruitment', 'corporate'];
function clubChanges() {
  if (!club) return {};
  const out = club.finances ? Object.fromEntries(FIELDS.map((k) => [k, +$(`#f-${k}`).value]).filter(([k, v]) => v !== club.finances[k])) : {};
  if (club.reputation != null && +$('#r-reputation').value !== club.reputation) out.reputation = +$('#r-reputation').value;
  if (club.status != null && +$('#r-status').value !== club.status) out.status = +$('#r-status').value;
  if (club.owner != null && +$('#f-owner').value !== club.owner) out.owner = +$('#f-owner').value;
  const fac = Object.fromEntries(FACILITIES.filter((k) => k in (club.facilities || {}))
    .map((k) => [k, +$(`#fac-${k}`).value]).filter(([k, v]) => v !== club.facilities[k]));
  if (Object.keys(fac).length) out.facilities = fac;
  return out;
}
async function openClub(id) {
  if (Object.keys(clubChanges()).length && !(await discard())) return;
  try { club = await window.api.club(id); } catch (e) { return toast(e.message, true); }
  document.querySelectorAll('#club-results .result').forEach((r) => r.classList.toggle('active', +r.dataset.id === id));
  renderClub();
}
const money = (v) => `£${Math.round(v).toLocaleString()}`;
const short = (v) => (Math.abs(v) >= 1e6 ? `£${(v / 1e6).toFixed(Math.abs(v) >= 1e8 ? 0 : 1)}M` : Math.abs(v) >= 1e3 ? `£${Math.round(v / 1e3)}K` : `£${v}`);

// Bank loans and sponsorship deals, as FM's Debts and Sponsorship screens show them (not editable yet).
function dealRow(title, value, note) {
  return h('div', { class: 'deal-row' }, h('div', { class: 'deal-head' }, h('b', {}, title), h('span', {}, value)), h('p', { class: 'card-note' }, note));
}
function renderDeals() {
  const debts = club.debts || [];
  $('#debt-list').replaceChildren(...(debts.length ? debts.map((d) => dealRow(`${short(d.amount)} borrowed`, `${short(d.monthly)} a month`,
    d.toRepay ? `${d.monthsLeft} repayment${d.monthsLeft === 1 ? '' : 's'} left until ${day(d.ends)}: ${short(d.toRepay)} to repay, ${short(d.interestLeft)} of it interest. Repayments from ${day(d.starts)}.` : 'Paid off.'))
    : [h('p', { class: 'card-note' }, 'This club has no bank loans.')]));
  // Like FM's screen: the named deals and the five biggest general ones, then the smaller partners.
  const row = (d) => dealRow(d.type, `${short(d.perSeason)} a season`, `${d.ended ? 'Ended' : 'Runs'} ${day(d.starts)} to ${day(d.ends)}, ${short(d.total)} in all.`);
  const deals = club.sponsorships || [];
  const live = deals.filter((d) => !d.ended), ended = deals.filter((d) => d.ended);
  const general = live.filter((d) => d.type === 'General').sort((a, b) => b.perSeason - a.perSeason);
  const shown = live.filter((d) => d.type !== 'General' || general.indexOf(d) < 5), partners = general.slice(5);
  const fold = (list, text) => (list.length ? [h('details', { class: 'deal-ended' }, h('summary', {}, text), list.map(row))] : []);
  $('#deal-list').replaceChildren(
    ...(live.length ? shown.map(row) : [h('p', { class: 'card-note' }, 'This club has no current sponsorship deals.')]),
    ...fold(partners, `${partners.length} more partner deal${partners.length === 1 ? '' : 's'}, ${short(partners.reduce((t, d) => t + d.perSeason, 0))} a season`),
    ...fold(ended, `${ended.length} ended deal${ended.length === 1 ? '' : 's'}`));
}
function renderClub() {
  $('#club-empty').classList.add('hidden');
  $('#club-editor').classList.remove('hidden');
  $('#club-name').textContent = club.name;
  $('#club-logo').src = imageUrl('club', club.uid, club.badgeUid);
  const f = club.finances;
  $('#club-sub').textContent = f ? `Balance ${money(f.balance)} · wages ${money(f.wages)} a week` : '';
  $('#no-finances').classList.toggle('hidden', !!f);
  $('#finance-form').classList.toggle('hidden', !f);
  const num = (input, have, value, lo, hi) => {
    input.disabled = !have;
    input.value = have ? value : '';
    input.parentElement.classList.remove('changed');
    input.onchange = () => {
      input.value = Math.round(Math.min(hi, Math.max(lo, +input.value || lo)));
      input.parentElement.classList.toggle('changed', +input.value !== value);
    };
  };
  num($('#r-reputation'), club.reputation != null, club.reputation, 0, 10000);
  const st = $('#r-status');
  st.disabled = club.status == null;
  st.value = club.status ?? 1;
  st.parentElement.classList.remove('changed');
  st.onchange = () => st.parentElement.classList.toggle('changed', +st.value !== club.status);
  const own = $('#f-owner');
  own.disabled = club.owner == null;
  own.value = club.owner ?? 0;
  own.parentElement.classList.remove('changed');
  own.onchange = () => own.parentElement.classList.toggle('changed', +own.value !== club.owner);
  for (const k of FACILITIES) num($(`#fac-${k}`), k in (club.facilities || {}), club.facilities?.[k], 1, 20);
  $('#club-founded').textContent = club.founded ? `Founded ${club.founded}.` : '';
  renderDeals();
  if (!f) return;
  for (const k of FIELDS) {
    const input = $(`#f-${k}`);
    input.value = f[k];
    input.parentElement.classList.remove('changed');
    input.onchange = () => {
      // FM keeps the balance and transfer budget within a billion; the wage budget can go higher.
      const max = k === 'wageBudget' ? 2e9 : 1e9;
      input.value = Math.round(Math.min(max, Math.max(k === 'balance' ? -max : 0, +input.value || 0)));
      input.parentElement.classList.toggle('changed', +input.value !== f[k]);
    };
  }
  $('#f-wages').textContent = `The club currently pays ${money(f.wages)} a week in wages (${f.wageBudget ? Math.round((f.wages / f.wageBudget) * 100) : 0}% of the wage budget).`;
}
$('#club-undo').addEventListener('click', () => club && renderClub());
document.querySelectorAll('[data-squad]').forEach((b) => b.addEventListener('click', async () => {
  if (!club) return;
  const sel = $('#squad-morale');
  const morale = b.dataset.squad === 'morale';
  const values = morale ? { morale: +sel.value } : b.dataset.squad === 'bans' ? { bans: 1 } : { condition: 10000, sharpness: 10000 };
  const what = morale ? `${sel.selectedOptions[0].textContent} morale` : b.textContent;
  if (!(await ask(`Every player at ${club.name} is changed.`, { title: `${what}?`, ok: morale ? 'Set morale' : b.textContent }))) return;
  b.disabled = true;
  try {
    const res = await window.api.saveSquad(club.id, values);
    toast(`Done: ${res.players} player${res.players === 1 ? '' : 's'} changed.`);
    if (current) openPlayer(current.id);
  } catch (e) { toast(e.message, true); } finally { b.disabled = false; }
}));
$('#club-apply').addEventListener('click', async () => {
  const d = clubChanges();
  if (!Object.keys(d).length) return toast('Nothing has changed yet.');
  const btn = $('#club-apply');
  btn.disabled = true;
  try {
    club = (await window.api.saveClub(club.id, d)).club;
    renderClub();
    toast(status.demo ? 'Saved to the demo club.' : 'Done. Reopen the club\'s finances screen in FM to see the new values.');
  } catch (e) {
    toast(e.message, true);
  } finally {
    btn.disabled = false;
  }
});

// --- Transfers ---
// A search box that turns into the picked item. search(q) returns rows; label(row) gives [title, detail].
function picker(box, placeholder, search, label, onPick) {
  let timer;
  const show = (row) => {
    if (!row) {
      const input = h('input', { type: 'search', placeholder });
      const results = h('div', { class: 'pick-results' });
      input.addEventListener('input', () => {
        clearTimeout(timer);
        timer = setTimeout(async () => {
          const q = input.value.trim();
          if (q.length < 2 && !status.demo) return results.replaceChildren();
          try {
            const rows = (await search(q)).slice(0, 30);
            results.replaceChildren(...(rows.length ? rows.map((r) => {
              const [t, d] = label(r);
              return h('button', { class: 'result', onclick: () => { show(r); onPick(r); } }, h('div', {}, h('b', {}, t), d ? h('small', {}, d) : null));
            }) : [h('div', { class: 'hint' }, 'Nothing found.')]));
          } catch (e) { results.replaceChildren(h('div', { class: 'hint' }, e.message)); }
        }, 250);
      });
      box.replaceChildren(input, results);
    } else {
      const [t, d] = label(row);
      box.replaceChildren(h('div', { class: 'picked' }, h('b', {}, t), d ? h('small', {}, d) : null,
        h('button', { title: 'Change', onclick: () => { show(null); onPick(null); } }, '×')));
    }
  };
  show(null);
  return { clear: () => { show(null); onPick(null); }, set: (row) => { show(row); onPick(row); } };
}

let mode = 'deal';
const pick = { player: null, club: null, other: null };
const playerLabel = (p) => [p.name, [p.club || 'No club', p.position, p.born && `born ${p.born}`].filter(Boolean).join(' · ')];
const clubLabel = (c) => [c.name, null];

const pickers = {
  player: picker($('#t-player'), 'Search for a player', (q) => window.api.search(q), playerLabel, (p) => { pick.player = p; checkOnLoan(); updateMove(); }),
  club: picker($('#t-club'), 'Search for a club', (q) => window.api.searchClubs(q), clubLabel, (c) => { pick.club = c; showLeague(c); updateMove(); }),
  other: picker($('#t-other'), 'Search for the other player', (q) => window.api.search(q), playerLabel, (p) => { pick.other = p; updateMove(); }),
};

document.querySelectorAll('#move-tabs .type-tab').forEach((b) => b.addEventListener('click', () => {
  mode = b.dataset.mode;
  document.querySelectorAll('#move-tabs .type-tab').forEach((t) => t.classList.toggle('active', t === b));
  document.querySelectorAll('#transfers-ui [data-modes]').forEach((el) => el.classList.toggle('hidden', !el.dataset.modes.split(' ').includes(mode)));
  $('#t-club-label').textContent = mode === 'loan' ? 'Loan club' : 'New club';
  if (mode === 'loan' && !$('#loan-ends').value) $('#loan-ends').value = seasonEnd();
  if (mode === 'deal' && !$('#t-date').value) $('#t-date').value = tomorrow();
  updateMove();
}));

// The future fee's conditions are about the loan club's own league, so they're named after it.
const FEE_IF = { win: (l) => `If they win the ${l}`, promotion: (l) => `If they're promoted from the ${l}`, relegation: (l) => `If they avoid relegation from the ${l}` };
async function showLeague(club) {
  let league = null;
  try { if (club) league = (await window.api.club(club.id)).league; } catch { /* generic names stay */ }
  if (pick.club !== club) return;
  for (const o of $('#loan-buy-if').options) {
    if (!o.value) continue;
    o.textContent = FEE_IF[o.value](league || 'league');
  }
}

// On the Loan tab, a player who is already out on loan gets an End loan button instead.
let onLoan = null;
async function checkOnLoan() {
  onLoan = null;
  const p = pick.player;
  if (!p) return;
  try {
    const full = await window.api.player(p.id);
    if (pick.player === p && full.loan) onLoan = { at: full.loan.club, from: full.contract?.club };
  } catch { /* the Loan button stays */ }
  updateMove();
}


function updateMove() {
  const btn = $('#t-go');
  // A player already out on loan can have that loan ended, or be loaned somewhere else.
  const onLoanNow = mode === 'loan' && !!onLoan;
  $('#t-end-loan').classList.toggle('hidden', !onLoanNow);
  btn.textContent = { deal: 'Agree transfer', move: 'Move player', loan: 'Loan player', swap: 'Swap players', release: 'Release player' }[mode];
  const hints = {
    deal: "FM agrees the deal as if both clubs had made it: it's on his Transfer tab and in the news, and he joins on that date. The whole fee is paid that day and shows in both clubs' finances. The buying club's transfer budget drops by the fee now.",
    move: "He joins the club's first team straight away and for free, on his current contract unless you set a new wage or end date. To pay a fee, use Transfer.",
    loan: "FM makes the loan itself, as if both clubs had agreed it: he joins the loan club's first team, stays under contract with his club and returns when the loan ends. The wage share is how much of his wage the loan club pays.",
    swap: "Each player joins the other's club on his own contract. This works even when both squads are full.",
    release: 'He leaves his club and becomes a free agent, with no contract.',
  };
  let why = '';
  if (!pick.player) why = 'Pick a player.';
  else if ((mode === 'move' || mode === 'deal') && !pick.club) why = 'Pick the club he moves to.';
  else if (mode === 'deal' && !$('#t-date').value) why = 'Pick when he joins.';
  else if (mode === 'loan' && !pick.club) why = 'Pick the club he goes on loan to.';
  else if (mode === 'loan' && !$('#loan-ends').value) why = 'Pick when the loan ends.';
  else if (mode === 'loan' && $('#loan-buy-if').value && !(+$('#loan-buy').value > 0)) why = 'Set the future fee the condition is for.';
  else if (mode === 'swap' && !pick.other) why = 'Pick the player to swap with.';
  btn.disabled = !!why;
  const loanNote = onLoanNow ? `${pick.player.name} is on loan at ${onLoan.at}. End loan sends him back to ${onLoan.from || 'his club'}; loaning him elsewhere ends that loan first.` : '';
  $('#t-hint').textContent = [loanNote, why, loanNote ? '' : hints[mode]].filter(Boolean).join(' ');
}

$('#t-go').addEventListener('click', async () => {
  const req = { player: pick.player.id };
  const num = (id, none) => ($(id).value === '' ? none : Math.max(0, Math.round(+$(id).value)));
  if (mode === 'deal') Object.assign(req, { club: pick.club.id, fee: num('#t-fee', 0), date: $('#t-date').value });
  if (mode === 'move') {
    req.club = pick.club.id;
    req.terms = { wage: num('#t-wage'), ends: $('#t-ends').value || undefined };
  }
  if (mode === 'loan') {
    Object.assign(req, { club: pick.club.id, ends: $('#loan-ends').value, wageShare: Math.min(100, num('#loan-share', 100)), monthlyFee: num('#loan-fee', 0), futureFee: num('#loan-buy', -1) });
    Object.assign(req, {
      cupMatches: $('#lc-cups').checked, againstParent: $('#lc-parent').checked, cupForParent: $('#lc-cup-parent').checked,
      futureFeeIf: $('#loan-buy-if').value || undefined,
      cannotTerminate: $('#lc-fixed').checked, recall: $('#lc-recall').checked, recallFrom: $('#lc-recall').checked ? $('#lc-recall-from').value || undefined : undefined,
    });
  }
  if (mode === 'swap') req.other = pick.other.id;
  const what = mode === 'deal' ? `Agree ${pick.player.name}'s move to ${pick.club.name} for £${req.fee.toLocaleString('en-GB')}?`
    : mode === 'move' ? `Move ${pick.player.name} to ${pick.club.name} now?`
    : mode === 'loan' ? `Loan ${pick.player.name} to ${pick.club.name}?`
    : mode === 'swap' ? `Swap ${pick.player.name} and ${pick.other.name}?` : `Release ${pick.player.name} from ${pick.player.club}?`;
  if (!(await ask('Are you sure?', { title: what, ok: 'Confirm', danger: mode === 'release' }))) return;
  const btn = $('#t-go');
  btn.disabled = true;
  try {
    const res = await window.api.transfer(mode === 'deal' ? 'future' : mode, req);
    toast(`${res.message}. ${status.demo ? '' : 'Reopen his profile in FM to see it.'}`);
    // A loan can't be undone here; End loan on his profile reverses it.
    if (res.canUndo === false) showLastMove(null); else showLastMove(res.message);
    Object.values(pickers).forEach((p) => p.clear());
    ['#t-fee', '#t-wage', '#t-ends', '#loan-fee', '#loan-buy'].forEach((id) => { $(id).value = ''; });
    $('#loan-share').value = 100;
    resetClauses();
    $('#loan-buy-if').value = '';
    $('#loan-ends').value = mode === 'loan' ? seasonEnd() : '';
    $('#t-date').value = mode === 'deal' ? tomorrow() : '';
    if (current && (current.id === req.player || current.id === req.other)) openPlayer(current.id);
  } catch (e) {
    toast(e.message, true);
  } finally {
    updateMove();
  }
});

['#t-date', '#t-fee'].forEach((id) => $(id).addEventListener('input', updateMove));
$('#t-end-loan').addEventListener('click', () => endLoanFromTransfers({ player: pick.player.id }));
async function endLoanFromTransfers(req) {
  const name = pick.player.name;
  if (!(await ask(`He goes back to ${onLoan.from || 'his club'}. Are you sure?`, { title: `End ${name}'s loan?`, ok: 'Confirm' }))) return;
  const btn = $('#t-end-loan');
  btn.disabled = true;
  try {
    const res = await window.api.transfer('end-loan', req);
    toast(`${res.message}. ${status.demo ? '' : 'Reopen his profile in FM to see it.'}`);
    showLastMove(res.message);
    pickers.player.clear();
    if (current && current.id === req.player) openPlayer(current.id);
  } catch (e) {
    toast(e.message, true);
  } finally {
    updateMove();
  }
}

// Loan clauses start as FM's usual ones: cup matches only.
function resetClauses() {
  $('#lc-cups').checked = true;
  ['#lc-parent', '#lc-cup-parent', '#lc-fixed', '#lc-recall'].forEach((id) => { $(id).checked = false; });
  $('#lc-recall-from').value = '';
  $('.recall-from').classList.add('hidden');
}
['#loan-buy', '#loan-buy-if'].forEach((id) => $(id).addEventListener('input', updateMove));
$('#lc-recall').addEventListener('change', () => $('.recall-from').classList.toggle('hidden', !$('#lc-recall').checked));

// 30 June at the end of the current season (FM's usual loan end), from the game date.
let gameToday = null;
// The day after the game's date, as yyyy-mm-dd.
function tomorrow() {
  const d = gameToday ? new Date(gameToday) : new Date();
  d.setUTCDate(d.getUTCDate() + 1);
  return d.toISOString().slice(0, 10);
}
function seasonEnd() {
  const d = gameToday ? new Date(gameToday) : new Date();
  return `${d.getMonth() >= 6 ? d.getFullYear() + 1 : d.getFullYear()}-06-30`;
}

function showLastMove(text) {
  $('#last-move').classList.toggle('hidden', !text);
  $('#last-move-text').textContent = text || '';
}
$('#t-undo').addEventListener('click', async () => {
  try {
    const res = await window.api.transfer('undo');
    toast(res.message);
    showLastMove(null);
  } catch (e) { toast(e.message, true); }
});
async function refreshTransfers() {
  try {
    const t = await window.api.transferStatus();
    showLastMove(t.lastMove);
    if (t.today) { $('#t-ends').min = $('#loan-ends').min = $('#lc-recall-from').min = $('#t-date').min = t.today; gameToday = t.today; }
    if (mode === 'deal' && !$('#t-date').value) $('#t-date').value = tomorrow();
  } catch { /* the game isn't ready yet */ }
}
document.querySelector('[data-tab=transfers]').addEventListener('click', refreshTransfers);

// The player page's Transfer and Loan buttons open that tab with him already picked.
document.querySelectorAll('[data-move]').forEach((b) => b.addEventListener('click', () => {
  if (!current) return;
  document.querySelector('.nav[data-tab=transfers]').click();
  document.querySelector(`#move-tabs [data-mode=${b.dataset.move}]`).click();
  pickers.player.set(current);
}));

// --- Appearance ---
function applyTheme(theme = 'emerald') {
  if (theme === 'emerald') delete document.documentElement.dataset.theme;
  else document.documentElement.dataset.theme = theme;
  document.querySelectorAll('.theme-option').forEach((b) => b.classList.toggle('active', b.dataset.theme === theme));
}
function applyBackground(bg = 'carbon') {
  $('.backdrop').className = `backdrop bg-${bg}`;
  document.querySelectorAll('.bg-option').forEach((b) => b.classList.toggle('active', b.dataset.bg === bg));
}
document.querySelectorAll('.theme-option').forEach((b) => b.addEventListener('click', () => {
  applyTheme(b.dataset.theme);
  window.api.saveSettings({ theme: b.dataset.theme });
}));
document.querySelectorAll('.bg-option').forEach((b) => b.addEventListener('click', () => {
  applyBackground(b.dataset.bg);
  window.api.saveSettings({ background: b.dataset.bg });
}));

// --- Date picker ---
// Clicking a date box opens the editor's own calendar: the year, then the month, each with arrows,
// then the days. Typing a date still works, and the box keeps its yyyy-mm-dd value and min/max.
const MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];
const datePicker = h('div', { class: 'datepick hidden', role: 'dialog' });
document.body.append(datePicker);
let pickFor = null, pickView = null; // the date box, and the { y, m } on show
const isoDate = (y, m, d) => `${y}-${String(m + 1).padStart(2, '0')}-${String(d).padStart(2, '0')}`;
const daysIn = (y, m) => new Date(Date.UTC(y, m + 1, 0)).getUTCDate();

function renderDatePicker() {
  const { y, m } = pickView, min = pickFor.min || '', max = pickFor.max || '';
  const inRange = (first, last) => (!min || last >= min) && (!max || first <= max);
  const go = (dy, dm) => {
    const t = new Date(Date.UTC(y + dy, m + dm, 1));
    let v = { y: t.getUTCFullYear(), m: t.getUTCMonth() };
    // Stepping a year can land outside the range; show the nearest month that's allowed.
    if (min && isoDate(v.y, v.m, daysIn(v.y, v.m)) < min) v = { y: +min.slice(0, 4), m: +min.slice(5, 7) - 1 };
    if (max && isoDate(v.y, v.m, 1) > max) v = { y: +max.slice(0, 4), m: +max.slice(5, 7) - 1 };
    pickView = v;
    renderDatePicker();
  };
  const arrow = (text, title, ok, fn) => h('button', { type: 'button', class: 'dp-arrow', title, disabled: !ok, onclick: fn }, text);
  const monthOk = (dm) => { const t = new Date(Date.UTC(y, m + dm, 1)); const ty = t.getUTCFullYear(), tm = t.getUTCMonth(); return inRange(isoDate(ty, tm, 1), isoDate(ty, tm, daysIn(ty, tm))); };
  const yearOk = (dy) => inRange(`${y + dy}-01-01`, `${y + dy}-12-31`);
  const lead = (new Date(Date.UTC(y, m, 1)).getUTCDay() + 6) % 7; // Monday first
  const days = [];
  for (let i = 0; i < lead; i++) days.push(h('span'));
  for (let d = 1; d <= daysIn(y, m); d++) {
    const v = isoDate(y, m, d);
    const cls = ['dp-day', v === pickFor.value && 'sel', v === gameToday && 'today'].filter(Boolean).join(' ');
    days.push(h('button', { type: 'button', class: cls, disabled: !!((min && v < min) || (max && v > max)), title: v === gameToday ? "Today in FM" : null, onclick: () => pickDate(v) }, String(d)));
  }
  datePicker.replaceChildren(
    h('div', { class: 'dp-row dp-year' }, arrow('‹', 'Previous year', yearOk(-1), () => go(-1, 0)), h('b', {}, String(y)), arrow('›', 'Next year', yearOk(1), () => go(1, 0))),
    h('div', { class: 'dp-row dp-month' }, arrow('‹', 'Previous month', monthOk(-1), () => go(0, -1)), h('b', {}, MONTHS[m]), arrow('›', 'Next month', monthOk(1), () => go(0, 1))),
    h('div', { class: 'dp-days' }, ...['Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa', 'Su'].map((d) => h('span', { class: 'dp-dow' }, d)), ...days),
    h('div', { class: 'dp-foot' }, h('button', { type: 'button', class: 'btn', onclick: () => pickDate('') }, 'Clear')),
  );
}

function pickDate(v) {
  const input = pickFor;
  closeDatePicker();
  input.value = v;
  input.dispatchEvent(new Event('input', { bubbles: true }));
  input.dispatchEvent(new Event('change', { bubbles: true }));
}

function openDatePicker(input) {
  pickFor = input;
  const from = input.value || gameToday || (input.min && input.min > '2001' ? input.min : new Date().toISOString().slice(0, 10));
  pickView = { y: +from.slice(0, 4), m: +from.slice(5, 7) - 1 };
  renderDatePicker();
  datePicker.classList.remove('hidden');
  // Below the box, or above it when there's no room underneath.
  const r = input.getBoundingClientRect(), w = datePicker.offsetWidth, ht = datePicker.offsetHeight;
  datePicker.style.left = `${Math.max(8, Math.min(r.left, innerWidth - w - 8))}px`;
  datePicker.style.top = `${r.bottom + 6 + ht > innerHeight && r.top - 6 - ht > 0 ? r.top - 6 - ht : r.bottom + 6}px`;
}

function closeDatePicker() {
  datePicker.classList.add('hidden');
  pickFor = null;
}

document.addEventListener('mousedown', (e) => {
  const input = e.target.closest?.('input[type="date"]');
  if (input && !input.disabled) {
    e.preventDefault();
    input.focus();
    if (pickFor === input) closeDatePicker(); else openDatePicker(input);
  } else if (pickFor && !datePicker.contains(e.target)) closeDatePicker();
}, true);
document.addEventListener('keydown', (e) => {
  if (pickFor && e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); const input = pickFor; closeDatePicker(); input.focus(); return; }
  const input = e.target.closest?.('input[type="date"]');
  if (input && (e.key === 'Enter' || (e.altKey && e.key === 'ArrowDown'))) { e.preventDefault(); openDatePicker(input); }
}, true);
// Typing in the box keeps an open calendar in step.
document.addEventListener('input', (e) => { if (pickFor && e.target === pickFor && pickFor.value) { pickView = { y: +pickFor.value.slice(0, 4), m: +pickFor.value.slice(5, 7) - 1 }; renderDatePicker(); } });
addEventListener('resize', closeDatePicker);
document.addEventListener('scroll', (e) => { if (pickFor && !datePicker.contains(e.target)) closeDatePicker(); }, true);

// --- What's new (after an app update) ---
async function showWhatsNew() {
  const w = await window.api.whatsNew();
  if (!w) return;
  $('#whats-new-title').textContent = `What's new in ${w.version}`;
  $('#whats-new-sub').textContent = w.from ? `You've been updated from ${w.from}.` : "You've been updated.";
  $('#whats-new-body').replaceChildren(...w.changes.map((c) => h('div', {},
    w.changes.length > 1 && h('b', {}, c.version),
    h('ul', { class: 'whats-new-list' }, ...c.items.map((t) => h('li', {}, t))))));
  $('#whats-new-dialog').showModal();
}

// --- Opening on what FM shows (the in-game pencil) ---
// The player or club FM had on screen. On a fresh start the engine is still finding the
// players, so this keeps trying for a while.
async function openOn(at) {
  if (!at) return;
  const club = at.kind === 'club';
  for (let tries = 0; tries < 60; tries++) {
    try { await (club ? window.api.club(at.id) : window.api.player(at.id)); } catch { await new Promise((r) => setTimeout(r, 1000)); continue; }
    document.querySelector(`.nav[data-tab=${club ? 'clubs' : 'players'}]`).click();
    return club ? openClub(at.id) : openPlayer(at.id);
  }
}
window.api.onOpenAt(openOn);

(async () => {
  info = await window.api.info();
  const settings = await window.api.settings();
  applyTheme(settings.theme);
  applyBackground(settings.background);
  updateMove();
  $('#app-version').textContent = info.version;
  $('#demo-banner').classList.toggle('hidden', !info.demo);
  showWhatsNew();
  window.api.openAt().then(openOn);
  await pollStatus();
  setInterval(pollStatus, 3000);
})();

// --- App updates ---
// The app checks on start and from "Check for app updates", asks before downloading,
// shows progress in a banner, then offers to restart.
let appUpdateAsked = null; // the state we last asked about, so we ask once
async function showAppUpdate(u) {
  const box = $('#app-update');
  box.classList.add('hidden');
  if (u?.state === 'available') {
    if (u.error) toast(u.error, true);
    if (appUpdateAsked === 'available:' + u.version) return;
    appUpdateAsked = 'available:' + u.version;
    if (await ask(`Version ${u.version} of FM27 Live Editor is available (you have ${u.current}). Download and install it?`,
      { title: 'App update available', ok: 'Download update' })) window.api.downloadAppUpdate();
  } else if (u?.state === 'downloading') {
    $('#app-update-text').textContent = `Downloading the app update… ${Math.round((u.pct || 0) * 100)}%`;
    $('#app-update-restart').classList.add('hidden');
    box.classList.remove('hidden');
  } else if (u?.state === 'ready') {
    $('#app-update-text').textContent = `Version ${u.version} is ready to install.`;
    $('#app-update-restart').classList.remove('hidden');
    box.classList.remove('hidden');
    if (appUpdateAsked === 'ready:' + u.version) return;
    appUpdateAsked = 'ready:' + u.version;
    if (await ask(`Version ${u.version} is ready. Restart now to install it, or it installs the next time you close the editor.`,
      { title: 'Update downloaded', ok: 'Restart now' })) window.api.restartToUpdate();
  }
}
window.api.onAppUpdate(showAppUpdate);
window.api.appInfo().then((i) => {
  if (!i.autoUpdates) $('#check-app-update').classList.add('hidden');
  if (i.update) showAppUpdate(i.update);
});
$('#app-update-restart').addEventListener('click', () => window.api.restartToUpdate());
$('#check-app-update').addEventListener('click', async () => {
  const btn = $('#check-app-update');
  btn.disabled = true;
  btn.textContent = 'Checking…';
  appUpdateAsked = null; // a manual check always asks again
  const r = await window.api.checkAppUpdate();
  btn.disabled = false;
  btn.textContent = 'Check for app updates';
  if (r.state === 'none') toast("You're on the latest version.");
  else if (r.state === 'error') toast(r.message, true);
});

// --- In-game button (Settings) ---
async function renderAddon(promise) {
  const state = $('#addon-state'), actions = $('#addon-actions');
  let a;
  try { a = await (promise || window.api.addonStatus()); } catch (e) { toast(e.message, true); a = await window.api.addonStatus(); }
  const btn = (text, fn, cls = 'btn') => h('button', { class: cls, onclick: async (ev) => { ev.target.disabled = true; await renderAddon(fn()); } }, text);
  if (status.demo) { state.textContent = 'Not available in demo mode.'; actions.replaceChildren(); return; }
  if (!a.folder) {
    state.textContent = "Couldn't find FM27 by itself. Pick the folder that has fm.exe in it.";
    actions.replaceChildren(btn('Find FM27 folder…', () => window.api.addonInstall(null), 'btn primary'));
    return;
  }
  const where = `FM27 is in ${a.folder}.`;
  const closeFirst = a.fmRunning ? ' Close FM27 to change this.' : '';
  if (!a.installed) {
    state.textContent = `${where} The button isn't set up.${closeFirst}`;
    actions.replaceChildren(btn('Set up the button', () => window.api.addonInstall(a.folder), 'btn primary'), btn('Other folder…', () => window.api.addonInstall(null)));
  } else if (a.on) {
    state.textContent = `${where} The button is on: it appears next time FM27 starts.${closeFirst}`;
    actions.replaceChildren(btn('Turn off', () => window.api.addonOn(false)), btn('Remove button', () => window.api.addonRemove()), btn('Update', () => window.api.addonInstall(a.folder)));
  } else {
    state.textContent = `${where} The button is turned off: FM27 starts without BepInEx or any other mods.${closeFirst}`;
    actions.replaceChildren(btn('Turn on', () => window.api.addonOn(true), 'btn primary'), btn('Remove button', () => window.api.addonRemove()));
  }
  actions.querySelectorAll('button').forEach((b) => { b.disabled = a.fmRunning || (!a.available && /Set up|Update/.test(b.textContent)); });
}
document.querySelector('[data-tab=settings]').addEventListener('click', () => renderAddon());
