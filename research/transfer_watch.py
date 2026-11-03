# Records what FM changes in memory when a real transfer goes through. Read-only.
#   python transfer_watch.py before <name> "<player>" ... --clubs "<club>" ...
#   (do the transfer in FM, in the Test save)
#   python transfer_watch.py after <name>
#   python transfer_watch.py diff <name>
# "before" saves every object around the players and clubs, two pointer steps
# out; "after" reads the same addresses again, plus anything new the players and
# clubs point to. "diff" lists the 8-byte words that changed.
import base64, json, os, struct, sys, time, urllib.parse, urllib.request

API = 'http://127.0.0.1:27270/api'
H = {'X-FM27-Editor': '1'}
HERE = os.path.dirname(os.path.abspath(__file__))


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
enc = lambda b: base64.b64encode(b).decode() if b else None


def roots(players, clubs):
    """The objects to start from: each player's person window, contract and teams; each club and its finances."""
    out = {}
    for name in players:
        hit = [x for x in call('/players?q=' + urllib.parse.quote(name)) if x['name'] == name]
        if not hit: sys.exit('no player called ' + name)
        P = int(call(f"/research/person?id={hit[0]['id']}"), 16)
        out[f'person {name}'] = (P - 0x400, 0x800)
        win = read([P - 0x400], 0x800)[0]
        c = q(win, 0x400 + 0xA8)
        if isptr(c): out[f'contract {name}'] = (c, 0xC8)
        for k in range(3):
            t = q(win, 0x400 - 0x158 + 8 * k)
            if isptr(t): out[f'team{k} {name}'] = (t, 0x200)
    for name in clubs:
        hit = [x for x in call('/clubs?q=' + urllib.parse.quote(name)) if x['name'] == name]
        if not hit: sys.exit('no club called ' + name)
        for h in hit:
            C = int(call(f"/research/club?id={h['id']}"), 16)
            out[f'club {name} {h["id"]}'] = (C, 0x1000)
            f = q(read([C + 0x150], 8)[0], 0)
            if isptr(f): out[f'finances {name} {h["id"]}'] = (f, 0x900)
    return out


def grab(rootmap, known=None):
    """Read the roots, then everything they point to, two steps out."""
    mem = {}
    for label, (a, n) in rootmap.items():
        mem[a] = read([a], n)[0]
    l1 = sorted({v for b in list(mem.values()) if b for o in range(0, len(b) - 7, 8) if isptr(v := q(b, o))} - set(mem))
    for a, b in zip(l1, read(l1, 0x400)): mem[a] = b
    l2 = sorted({v for a in l1 if mem.get(a) for o in range(0, len(mem[a]) - 7, 8) if isptr(v := q(mem[a], o))} - set(mem))
    for a, b in zip(l2, read(l2, 0x200)): mem[a] = b
    if known:  # read again everything saved before, at the same size
        missing = [a for a in known if a not in mem]
        for a, b in zip(missing, read(missing, 0x400)): mem[a] = b
    return mem


def save(name, data):
    json.dump(data, open(os.path.join(HERE, f'transfer-{name}.json'), 'w', encoding='utf-8'))


def load(name):
    return json.load(open(os.path.join(HERE, f'transfer-{name}.json'), encoding='utf-8'))


cmd, name = sys.argv[1], sys.argv[2]
if cmd == 'before':
    args = sys.argv[3:]
    ci = args.index('--clubs') if '--clubs' in args else len(args)
    players, clubs = args[:ci], args[ci + 1:]
    r = roots(players, clubs)
    mem = grab(r)
    save(name, {'when': time.ctime(), 'players': players, 'clubs': clubs, 'roots': {k: [f'{a:X}', n] for k, (a, n) in r.items()},
                'before': {f'{a:X}': enc(b) for a, b in mem.items()}, 'events_before': len(call('/research/events'))})
    print(f'saved {len(mem)} objects; roots:'); [print(' ', k, f'{a:X}') for k, (a, _) in r.items()]
elif cmd == 'after':
    d = load(name)
    r = roots(d['players'], d['clubs'])
    known = [int(a, 16) for a in d['before']]
    mem = grab(r, known)
    d['roots_after'] = {k: [f'{a:X}', n] for k, (a, n) in r.items()}
    d['after'] = {f'{a:X}': enc(b) for a, b in mem.items()}
    d['events'] = call('/research/events')[d['events_before']:]
    save(name, d)
    print(f'saved {len(mem)} objects and {len(d["events"])} game events')
    for k in r: print(' ', k, d['roots'].get(k, ['(new)'])[0], '->', d['roots_after'][k][0])
elif cmd == 'diff':
    d = load(name)
    label = {int(a, 16): k for k, (a, _) in d['roots'].items()} | {int(a, 16): k + ' (after)' for k, (a, _) in d.get('roots_after', {}).items()}
    changed = 0
    for a, b in d['before'].items():
        x, y = base64.b64decode(b) if b else None, base64.b64decode(d['after'].get(a) or '') or None
        if x is None or y is None:
            if (x is None) != (y is None): print(f'{a}: {"appeared" if x is None else "gone"}')
            continue
        diffs = [o for o in range(0, min(len(x), len(y)) - 7, 8) if x[o:o + 8] != y[o:o + 8]]
        if not diffs: continue
        changed += 1
        print(f'{a} {label.get(int(a, 16), "")}: {len(diffs)} words changed')
        for o in diffs[:24]:
            print(f'   +{o:#05x}  {x[o:o + 8].hex(" ")}  ->  {y[o:o + 8].hex(" ")}')
    print(changed, 'objects changed')
    for e in d.get('events', []): print('event', e)
