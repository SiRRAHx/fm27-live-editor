# Finds every object in FM's memory that points at a player (his person, player
# part or attribute record) and labels the clubs, teams, people and dates near
# each reference. Used to find where FM keeps agreed future transfers. Read-only.
# Usage: python find_refs.py "Player Name" [club name to look for] ...
import base64, json, os, re, struct, subprocess, sys, urllib.parse, urllib.request

API = 'http://127.0.0.1:27270/api'
H = {'X-FM27-Editor': '1'}
HERE = os.path.dirname(os.path.abspath(__file__))
MEMSCAN = os.path.join(HERE, '..', 'tools', 'memscan', 'bin', 'out', 'memscan.exe')
VT = {0x4CD6848: 'person', 0x4CD6318: 'player', 0x4AFBF48: 'attrs', 0x4C90A78: 'team', 0x4C89B68: 'club', 0x4AC05F8: 'contract', 0x4C9FD78: 'finances', 0x4C8EFA8: 'nation'}


def call(p, body=None):
    r = urllib.request.Request(API + p, data=None if body is None else json.dumps(body).encode(), headers=H, method='POST' if body else 'GET')
    return json.loads(urllib.request.urlopen(r, timeout=600).read())


def read(addrs, n):
    out = []
    for i in range(0, len(addrs), 4000):
        out += [base64.b64decode(b) if b else None for b in call('/research/read', {'addrs': [f'{a:X}' for a in addrs[i:i + 4000]], 'len': n})]
    return out


q = lambda b, o: struct.unpack_from('<Q', b, o)[0]
isptr = lambda v: 0x10000 < v < 0x7FFF00000000 and v % 8 == 0

name = sys.argv[1]
hit = [x for x in call('/players?q=' + urllib.parse.quote(name)) if name.lower() in x['name'].lower()]
if not hit: sys.exit('no player matching ' + name)
for x in hit: print('player', x)
pl = hit[0]
P = int(call(f"/research/person?id={pl['id']}"), 16)
base = int(call('/research/module'), 16)
mine = {P: 'P', P - 0x288: 'P-0x288', P - 0x140: 'P-0x140'}
pid = subprocess.check_output(['powershell', '-NoProfile', '-Command', '(Get-Process fm).Id'], text=True).strip()
env = dict(os.environ, DOTNET_ROOT=os.path.join(os.environ['LOCALAPPDATA'], 'Microsoft', 'dotnet'))
out = subprocess.check_output([MEMSCAN, 'findptr', pid, ','.join(f'{a:X}' for a in mine)], text=True, env=env)
refs = [(int(m.group(1), 16), int(m.group(2), 16)) for m in re.finditer(r'0x([0-9A-F]+) -> 0x([0-9A-F]+)', out)]
print(out.strip().splitlines()[-1], '(memscan shows the first 60)')

# Label every pointer near each reference by the type marker it points to.
ctx = read([a - 0x100 for a, _ in refs], 0x200)
near = sorted({q(b, o) for b in ctx if b for o in range(0, 0x200, 8) if isptr(q(b, o))})
kinds = {}
for a, h in zip(near, read(near, 8)):
    if h and q(h, 0) - base in VT: kinds[a] = VT[q(h, 0) - base]
names = {}
for a, k in kinds.items():
    if k == 'club':
        b = read([a + 0xC0], 8)[0]
        s = read([q(b, 0)], 64)[0] if b else None
        if s: names[a] = s[4:4 + int.from_bytes(s[:4], 'little')].decode('utf-8', 'replace')
want = [w.lower() for w in sys.argv[2:]]
for (a, target), b in zip(refs, ctx):
    if not b: continue
    tags = []
    for o in range(0, 0x200, 8):
        v = q(b, o)
        if v in mine: tags.append(f'{o - 0x100:+#x}={mine[v]}')
        elif v in kinds: tags.append(f'{o - 0x100:+#x}={kinds[v]}' + (f'({names[v]})' if v in names else ''))
    flag = ' <==' if any(w in t.lower() for t in tags for w in want) else ''
    print(f'{a:X} -> {mine[target]}: {" ".join(tags)}{flag}')
