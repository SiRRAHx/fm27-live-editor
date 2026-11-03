# Finds the club link by comparing players known to share a club.
import base64, json, urllib.request
from collections import defaultdict
API='http://127.0.0.1:27270/api'; H={'X-FM27-Editor':'1'}
def call(p, body=None):
    r=urllib.request.Request(API+p, data=None if body is None else json.dumps(body).encode(), headers=H, method='POST' if body else 'GET')
    return json.loads(urllib.request.urlopen(r, timeout=300).read())
FROM=-0x3c8; LEN=0x3c8*2
dump=call(f'/research/dump?n=20000&from=-0x3c8&len=0x{LEN:x}')
for d in dump: d['raw']=base64.b64decode(d['bytes'])
json.dump({'from':FROM,'players':[{k:v for k,v in d.items() if k!='raw'} for d in dump]}, open('research/dump-club.json','w'))
clubs={'United':['Lammens','Mount','Maguire','Mbeumo','Cunha','Šeško','Mainoo','Dalot','Shaw','Bruno Fernandes','Amad','Yoro','Mazraoui','Heaven','Ugarte','Casemiro','Zirkzee','Bayındır','Dorgu','Fredricson','Malacia','Kobbie','Diallo','Martínez','De Ligt','Bastoni','Fernández','Wheatley','Amass','Lacey','Obi'],
       'Arsenal':['Saka','Declan Rice','Ødegaard','Saliba','Gabriel Magalhães','Raya','Timber','Havertz','Martinelli','Trossard','Gyökeres','Zubimendi','Eze','Madueke','Ben White','Calafiori','Lewis-Skelly','Merino','Nørgaard','Mosquera','Hincapié','Nwaneri','Kepa']}
got={c:[d for d in dump if any(n.lower() in d['name'].lower() for n in ns)] for c,ns in clubs.items()}
for c,ds in got.items(): print(c, [d['name'] for d in ds])
u, a = got['United'], got['Arsenal']
for off in range(0, LEN-7, 8):
    uv=[int.from_bytes(d['raw'][off:off+8],'little') for d in u]
    av=[int.from_bytes(d['raw'][off:off+8],'little') for d in a]
    from collections import Counter
    cu=Counter(uv).most_common(1)[0]; ca=Counter(av).most_common(1)[0] if av else (None,0)
    if cu[1]>=max(3,0.6*len(u)) and cu[0]!=0 and (not av or (ca[1]>=0.6*len(a) and ca[0]!=cu[0])):
        allv=[int.from_bytes(d['raw'][off:off+8],'little') for d in dump]
        print(f'P{off+FROM:+#x}: United {cu[1]}/{len(u)} -> {cu[0]:X}  Arsenal {ca[1]}/{len(a)} -> {ca[0] if ca[0] is None else hex(ca[0])}  shared by {allv.count(cu[0])} of {len(dump)}')
