# Finds a player's contract by its expiry date (int16 day of year, int16 year)
# and wage, up to two pointer steps from the person object.
# Usage: python find_contract.py "Ayden Heaven" 30/6/2029 12000
import base64, datetime, json, sys, urllib.parse, urllib.request

API = 'http://127.0.0.1:27270/api'
H = {'X-FM27-Editor': '1'}


def call(p, body=None):
    r = urllib.request.Request(API + p, data=None if body is None else json.dumps(body).encode(), headers=H, method='POST' if body else 'GET')
    return json.loads(urllib.request.urlopen(r, timeout=300).read())


def read(addrs, n):
    out = []
    for i in range(0, len(addrs), 4000):
        out += [base64.b64decode(b) if b else None for b in call('/research/read', {'addrs': [f'{a:X}' for a in addrs[i:i + 4000]], 'len': n})]
    return out


name, date, wage = sys.argv[1], sys.argv[2], int(sys.argv[3])
d, m, y = map(int, date.split('/'))
doy = datetime.date(y, m, d).timetuple().tm_yday
pattern = doy.to_bytes(2, 'little') + y.to_bytes(2, 'little')
isptr = lambda v: 0x10000 < v < 0x7FFF00000000 and v % 8 == 0

hit = [x for x in call('/players?q=' + urllib.parse.quote(name)) if x['name'] == name]
if not hit: sys.exit('no player called ' + name)
P = int(call(f"/research/person?id={hit[0]['id']}"), 16)
print(f'{name} at {P:X}; looking for day {doy} year {y} and wage ~{wage}')


def scan(label, base, b):
    i = b.find(pattern)
    while i >= 0:
        near = []
        for k in range(max(0, i - 0x80), min(len(b) - 4, i + 0x80), 4):
            v = int.from_bytes(b[k:k + 4], 'little')
            for scale, unit in ((1, 'week'), (52, 'year'), (4, 'month')):
                if abs(v - wage * scale) <= wage * scale * 0.06: near.append((k - i, v, unit))
        print(f'  {label} +{i:#x} (addr {base + i:X}): date found; wage-like values {near}')
        i = b.find(pattern, i + 1)


W = 0x400
win = read([P - W], 2 * W)[0]
scan('P-0x400', P - W, win)
lvl1 = [(o - W, int.from_bytes(win[o:o + 8], 'little')) for o in range(0, len(win), 8)]
lvl1 = [(o, v) for o, v in lvl1 if isptr(v)]
t1 = read([v for _, v in lvl1], 0x400)
lvl2 = []
for (o, v), b in zip(lvl1, t1):
    if not b: continue
    scan(f'P{o:+#x} ->', v, b)
    lvl2 += [(o, k, x) for k in range(0, len(b), 8) if isptr(x := int.from_bytes(b[k:k + 8], 'little'))]
print('level 2:', len(lvl2), 'pointers')
t2 = read([x for *_, x in lvl2], 0x300)
for (o, k, x), b in zip(lvl2, t2):
    if b: scan(f'P{o:+#x} -> +{k:#x} ->', x, b)
