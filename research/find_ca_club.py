# Finds CA/PA and the club pointer by statistics over a few thousand players.
# Needs FM27 running with a career loaded and the research flag file in
# BepInEx\config. Saves everything it fetched to research\dump-*.json.
import base64, json, math, sys, time, urllib.request
from collections import Counter, defaultdict

API = 'http://127.0.0.1:27270/api'
H = {'X-FM27-Editor': '1'}
FROM, LEN = -0x288, 0x700
ATTR = 0x288 - 0x140 + 23   # first attribute byte inside the dump
OUT = sys.argv[1] if len(sys.argv) > 1 else 'research'


def call(path, body=None):
    req = urllib.request.Request(API + path, data=None if body is None else json.dumps(body).encode(), headers=H, method='POST' if body else 'GET')
    with urllib.request.urlopen(req, timeout=120) as r:
        return json.loads(r.read())


def read(addrs, n):
    out = []
    for i in range(0, len(addrs), 5000):
        out += [base64.b64decode(b) if b else None for b in call('/research/read', {'addrs': [f'{a:X}' for a in addrs[i:i + 5000]], 'len': n})]
    return out


def corr(xs, ys):
    n = len(xs); mx = sum(xs) / n; my = sum(ys) / n
    sx = math.sqrt(sum((x - mx) ** 2 for x in xs)); sy = math.sqrt(sum((y - my) ** 2 for y in ys))
    return 0 if sx == 0 or sy == 0 else sum((x - mx) * (y - my) for x, y in zip(xs, ys)) / (sx * sy)


while True:
    st = call('/status')
    if st.get('players'): break
    print('waiting for a career…', st.get('reason') or ''); time.sleep(5)
print('players indexed:', st['players'])

base = int(call('/research/module'), 16)
dump = call(f'/research/dump?n=5000&from=-0x288&len=0x{LEN:x}')
for d in dump: d['raw'] = base64.b64decode(d['bytes'])
stamp = time.strftime('%H%M%S')
json.dump({'module': base, 'from': FROM, 'players': [{k: v for k, v in d.items() if k != 'raw'} for d in dump]}, open(f'{OUT}/dump-{stamp}.json', 'w'))
print('dumped', len(dump), 'players')

# Skill level: the mean of the outfield attributes (skip goalkeeping and hidden).
skill = [sum(d['raw'][ATTR + i] for i in list(range(0, 11)) + list(range(17, 24)) + list(range(26, 30)) + [34, 36, 37, 38, 39, 42, 46, 52, 53]) for d in dump]

print('\n== CA candidates (int16, all 1..200, best correlation with skill) ==')
cands = []
for off in range(0, LEN - 1, 2):
    vals = [int.from_bytes(d['raw'][off:off + 2], 'little', signed=True) for d in dump]
    if min(vals) < 1 or max(vals) > 200 or len(set(vals)) < 50: continue
    cands.append((corr(vals, skill), off, min(vals), max(vals)))
for c, off, lo, hi in sorted(cands, reverse=True)[:8]:
    print(f'  dump+0x{off:03x} (P{off + FROM:+#x})  r={c:.3f}  range {lo}..{hi}')
if not cands: sys.exit('no CA candidate')
ca_off = sorted(cands, reverse=True)[0][1]
ca = [int.from_bytes(d['raw'][ca_off:ca_off + 2], 'little', signed=True) for d in dump]

print('\n== PA candidates (int16 near CA: >= CA when positive, or -1..-200) ==')
for off in range(max(0, ca_off - 16), min(LEN - 1, ca_off + 16), 2):
    if off == ca_off: continue
    vals = [int.from_bytes(d['raw'][off:off + 2], 'little', signed=True) for d in dump]
    ok = sum(1 for v, c in zip(vals, ca) if (c <= v <= 200) or (-200 <= v <= -1))
    if ok > 0.97 * len(dump): print(f'  dump+0x{off:03x} (P{off + FROM:+#x})  fits {ok}/{len(dump)}  negatives {sum(v < 0 for v in vals)}  e.g. {vals[:8]}')
print('  CA e.g.', ca[:8])

print('\n== Club pointer candidates (8-byte heap pointers shared by many players) ==')
ptr_offs = []
for off in range(0, LEN - 7, 8):
    vals = [int.from_bytes(d['raw'][off:off + 8], 'little') for d in dump]
    nz = [v for v in vals if 0x10000 < v < 0x7FFF00000000]
    if len(nz) < 0.5 * len(dump): continue
    distinct = len(set(nz))
    if 20 < distinct < 0.6 * len(nz): ptr_offs.append((off, nz, distinct))
results = []
for off, nz, distinct in ptr_offs:
    top = [a for a, _ in Counter(nz).most_common(40)]
    heads = read(top, 0x200)
    vts = Counter(int.from_bytes(h[:8], 'little') for h in heads if h)
    vt, cnt = vts.most_common(1)[0] if vts else (0, 0)
    inmod = 0 <= vt - base < 0x175DA000
    # Look for a readable name: pointers in the object that lead to an int32-length UTF-8 string.
    names = []
    for h in heads[:6]:
        if not h: continue
        subs = [int.from_bytes(h[i:i + 8], 'little') for i in range(8, 0x100, 8)]
        subs = [s for s in subs if 0x10000 < s < 0x7FFF00000000]
        got = read(subs, 64) if subs else []
        for s, b in zip(subs, got):
            for cand in (b, ):
                if not cand: continue
                n = int.from_bytes(cand[:4], 'little')
                if 2 <= n <= 40:
                    try:
                        t = cand[4:4 + n].decode('utf-8')
                        if t.isprintable(): names.append(t)
                    except UnicodeDecodeError: pass
    results.append((off, distinct, vt - base if inmod else None, cnt, names[:6]))
    print(f'  dump+0x{off:03x} (P{off + FROM:+#x})  distinct {distinct}  vtable rva {hex(vt - base) if inmod else "-"} x{cnt}/40  names {names[:6]}')
json.dump({'ca_off': ca_off, 'ptrs': results}, open(f'{OUT}/findings-{stamp}.json', 'w'), default=str)
print('\nsaved', f'{OUT}/dump-{stamp}.json')
