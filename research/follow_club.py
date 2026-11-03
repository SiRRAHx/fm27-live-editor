# Follows each pointer near the player one step and looks for something
# shared by all the Man Utd players and different for the Arsenal players.
import json, base64, urllib.request, sys
from collections import Counter
API='http://127.0.0.1:27270/api'; H={'X-FM27-Editor':'1'}
def call(p, body=None):
    r=urllib.request.Request(API+p, data=None if body is None else json.dumps(body).encode(), headers=H, method='POST' if body else 'GET')
    return json.loads(urllib.request.urlopen(r, timeout=300).read())
def read(addrs, n): return [base64.b64decode(b) if b else None for b in call('/research/read', {'addrs':[f'{x:X}' for x in addrs], 'len':n})]
D=json.load(open('research/dump-club.json',encoding='utf-8')); FROM=D['from']; dump=D['players']
for d in dump: d['raw']=base64.b64decode(d['bytes'])
U=['Mason Mount','Diogo Dalot','Tyler Fredricson','Luis Lacey','Jack Fletcher']; A=['Leandro Trossard','Gabriel Martinelli','Christian Nørgaard','David Raya','Ethan Nwaneri']
u=[d for d in dump if d['name'] in U]; a=[d for d in dump if d['name'] in A]
P=lambda d:int(d['p'],16)
isptr=lambda v:0x10000<v<0x7FFF00000000
LEN=len(dump[0]['raw']); N=0x400
hits=[]
for off in range(0, LEN-7, 8):
    up=[int.from_bytes(d['raw'][off:off+8],'little') for d in u]; ap=[int.from_bytes(d['raw'][off:off+8],'little') for d in a]
    if not all(isptr(x) for x in up+ap) or len(set(up))<2: continue
    ut=read(up,N); at=read(ap,N)
    if any(t is None for t in ut+at): continue
    for k in range(0,N-7,8):
        uv=[int.from_bytes(t[k:k+8],'little') for t in ut]; av=[int.from_bytes(t[k:k+8],'little') for t in at]
        cu=Counter(uv).most_common(1)[0]; ca=Counter(av).most_common(1)[0]
        if cu[0] and cu[1]>=4 and ca[1]>=4 and ca[0]!=cu[0] and isptr(cu[0]) and isptr(ca[0]):
            hits.append((off+FROM,k,cu,ca)); print(f'P{off+FROM:+#x} -> +{k:#x}: United {cu[1]}/5 {cu[0]:X}  Arsenal {ca[1]}/5 {ca[0]:X}')
# name check for each hit: read target object and look for len-strings via its pointers
base=int(call('/research/module'),16)
for po,k,cu,ca in hits[:12]:
    for label,addr in (('United',cu[0]),('Arsenal',ca[0])):
        h=read([addr],0x200)[0]
        vt=int.from_bytes(h[:8],'little')
        subs=[int.from_bytes(h[i:i+8],'little') for i in range(0,0x200,8)]
        idx=[i for i,s in enumerate(subs) if isptr(s)]
        got=read([subs[i] for i in idx],48)
        names=[]
        for i,g in zip(idx,got):
            if not g: continue
            n=int.from_bytes(g[:4],'little')
            if 2<=n<=40:
                try:
                    t=g[4:4+n].decode(); 
                    if t.isprintable(): names.append((hex(i*8),t))
                except UnicodeDecodeError: pass
        print(f'  P{po:+#x}->+{k:#x} {label}: vtable rva {vt-base:#x}  names {names[:6]}')
