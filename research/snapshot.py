# One-shot research snapshot: saves contracts (with their clause lists), clubs
# and everything they point to, nations and cities, so later features can be
# worked out offline. Read-only. Output: research\snapshot-<time>\*.json
import base64, json, os, sys, time, urllib.parse, urllib.request
from collections import Counter

API = 'http://127.0.0.1:27270/api'
H = {'X-FM27-Editor': '1'}


def call(p, body=None):
    r = urllib.request.Request(API + p, data=None if body is None else json.dumps(body).encode(), headers=H, method='POST' if body else 'GET')
    return json.loads(urllib.request.urlopen(r, timeout=600).read())


def read(addrs, n):
    out = []
    for i in range(0, len(addrs), 4000):
        out += [base64.b64decode(b) if b else None for b in call('/research/read', {'addrs': [f'{a:X}' for a in addrs[i:i + 4000]], 'len': n})]
    return out


q = lambda b, o: int.from_bytes(b[o:o + 8], 'little')
isptr = lambda v: 0x10000 < v < 0x7FFF00000000 and v % 8 == 0
enc = lambda b: base64.b64encode(b).decode() if b else None


def lstr(b):
    if not b: return None
    n = int.from_bytes(b[:4], 'little')
    if not 0 < n < 120: return None
    try:
        t = b[4:4 + n].decode('utf-8')
        return t if t.isprintable() else None
    except UnicodeDecodeError: return None


while not call('/status').get('players'):
    print('waiting for the career to load…'); time.sleep(5)
out = os.path.join(os.path.dirname(__file__), 'snapshot-' + time.strftime('%H%M%S'))
os.makedirs(out)
save = lambda name, obj: json.dump(obj, open(os.path.join(out, name), 'w', encoding='utf-8'))
base = int(call('/research/module'), 16)
print('module', hex(base))

# 1. Players: a 1-in-5 sample, plus every Man Utd player found by search.
dump = call('/research/dump?n=20000&from=-0x3c8&len=0x790')
for d in dump: d['raw'] = base64.b64decode(d['bytes'])
P = {d['id']: int(d['p'], 16) for d in dump}
utd = call('/players?q=' + urllib.parse.quote('manchester united'))
for s in utd:
    if s['id'] not in P: P[s['id']] = int(call(f"/research/person?id={s['id']}"), 16)
names = {d['id']: d['name'] for d in dump} | {s['id']: s['name'] for s in utd}
print('players', len(P), 'man utd', len(utd))

# 2. Contracts and their clause lists (+0x68 begin, +0x70 end).
ids = list(P)
cptr = [q(b, 0) if b else 0 for b in read([P[i] + 0xA8 for i in ids], 8)]
crec = read([c for c in cptr if c], 0xC8)
cmap = dict(zip([i for i, c in zip(ids, cptr) if c], zip([c for c in cptr if c], crec)))
vec = [(i, q(r, 0x68), q(r, 0x70)) for i, (c, r) in cmap.items() if r and isptr(q(r, 0x68)) and 0 < q(r, 0x70) - q(r, 0x68) <= 0x400]
vb = read([b for _, b, _ in vec], 0x400)
clauses = {i: enc(b[:e - s]) for (i, s, e), b in zip(vec, vb) if b}
save('contracts.json', {str(i): {'name': names.get(i), 'p': f'{P[i]:X}', 'c': f'{c:X}', 'rec': enc(r), 'clauses': clauses.get(i)} for i, (c, r) in cmap.items()})
print('contracts', len(cmap), 'with clause lists', len(clauses))

# 3. Clubs: every club reached through a player's team.
tptr = [q(b, 0) if b else 0 for b in read([P[i] - 0x158 for i in ids], 8)]
teams = sorted({t for t in tptr if t})
th = dict(zip(teams, read(teams, 0x200)))
clubs = sorted({q(b, 0x30) for b in th.values() if b and isptr(q(b, 0x30))})
ch = dict(zip(clubs, read(clubs, 0x1000)))
cname = dict(zip(clubs, [lstr(b) for b in read([q(ch[c], 0xC0) if ch[c] else 0 for c in clubs], 128)]))
save('teams.json', {f'{t:X}': enc(b) for t, b in th.items()})
save('clubs.json', {f'{c:X}': {'name': cname[c], 'rec': enc(ch[c])} for c in clubs})
save('players.json', {str(i): {'name': names.get(i), 'p': f'{P[i]:X}', 'team': f'{t:X}'} for i, t in zip(ids, tptr)})
print('teams', len(teams), 'clubs', len(clubs))

# 4. Man Utd in depth: everything the club and its teams point to, two steps out.
mu = [c for c in clubs if cname[c] == 'Manchester United']
deep = {}
for c in mu + [t for t, b in th.items() if b and q(b, 0x30) in mu]:
    rec = ch.get(c) or th.get(c)
    l1 = sorted({v for o in range(0, len(rec), 8) if isptr(v := q(rec, o))})
    b1 = dict(zip(l1, read(l1, 0x800)))
    l2 = sorted({v for b in b1.values() if b for o in range(0, len(b), 8) if isptr(v := q(b, o))} - set(b1))
    b2 = dict(zip(l2, read(l2, 0x200)))
    deep[f'{c:X}'] = {'rec': enc(rec), 'l1': {f'{a:X}': enc(b) for a, b in b1.items()}, 'l2': {f'{a:X}': enc(b) for a, b in b2.items()}}
    print('man utd object', hex(c), 'level 1', len(b1), 'level 2', len(b2))
save('manutd-deep.json', deep)

# 5. Nations (P+0x68) and cities (P+0x80), with any strings they point to.
for label, off in (('nations', 0x68), ('cities', 0x80)):
    objs = sorted({q(d['raw'], 0x3c8 + off) for d in dump if isptr(q(d['raw'], 0x3c8 + off))})
    ob = dict(zip(objs, read(objs, 0x300)))
    strings = {}
    for a, b in ob.items():
        if not b: continue
        subs = [(o, q(b, o)) for o in range(8, len(b), 8) if isptr(q(b, o))]
        got = read([s for _, s in subs], 128) if subs else []
        strings[f'{a:X}'] = {f'{o:#x}': t for (o, _), g in zip(subs, got) if (t := lstr(g))}
    save(f'{label}.json', {f'{a:X}': {'rec': enc(b), 'strings': strings.get(f'{a:X}')} for a, b in ob.items()})
    print(label, len(ob), 'e.g.', list(strings.values())[:2])

json.dump({'module': base, 'when': time.ctime()}, open(os.path.join(out, 'meta.json'), 'w'))
print('saved to', out)
