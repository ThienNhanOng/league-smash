"""Preflight: every sheet cell filled, every cross-sheet reference resolved, every League file and sound event
present in the local League install, every Slap City id/clip present in the game dump, every hook implemented.

usage: preflight.py [--league "D:/Riot Games/League of Legends"] [--dump "<Slap City>/BepInEx/slaprift-dump"]
Exit code 0 only when clean.
"""
import json, os, re, sys, struct, importlib.util

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
args = sys.argv[1:]
def opt(name, default):
    return args[args.index(name) + 1] if name in args else default
LEAGUE = opt('--league', 'D:/Riot Games/League of Legends')
DUMP = opt('--dump', 'D:/steam/steamapps/common/Slap City/BepInEx/slaprift-dump')

def load(name):
    return json.load(open(os.path.join(ROOT, 'sheets', name + '.json'), encoding='utf-8'))

S = {n: load(n) for n in ['champions', 'animations', 'abilities', 'effects', 'passives', 'objects', 'slap_clips', 'slap_fighters', 'hooks']}
problems = []
def bad(sheet, row, col, msg): problems.append(f'{sheet}[{row}].{col}: {msg}')

# 1. every row x column is filled
for name, sh in S.items():
    cols = list(sh['columns'].keys())
    for i, r in enumerate(sh['rows']):
        key = r.get('id') or r.get('champion') or r.get('pattern') or i
        if 'slot' in r and name == 'abilities': key = f"{r['champion']}/{r['slot']}"
        for c in cols:
            if c not in r: bad(name, key, c, 'missing'); continue
            v = r[c]
            if v is None or (isinstance(v, str) and v.strip() == '') or (c == 'effects' and isinstance(v, list) and len(v) == 0): bad(name, key, c, 'empty')
        for c in r:
            if c not in cols: bad(name, key, c, 'column not declared in sheet header')

champs = {r['id']: r for r in S['champions']['rows']}
fighters = {r['id']: r for r in S['slap_fighters']['rows']}
effects = {r['id']: r for r in S['effects']['rows']}
passives = {r['champion']: r for r in S['passives']['rows']}
roles = [c for c in S['animations']['columns'] if c != 'champion']
anims = {r['champion']: r for r in S['animations']['rows']}

# 2. references between sheets
slots = sorted(r['slot'] for r in champs.values())
if slots != list(range(len(champs))): bad('champions', '*', 'slot', f'slots must be 0..{len(champs)-1}, got {slots}')
for cid, r in champs.items():
    if not cid.startswith('lol_'): bad('champions', cid, 'id', 'must start with lol_')
    if r['base'] not in fighters: bad('champions', cid, 'base', f"unknown fighter {r['base']}")
    if cid not in anims: bad('animations', cid, 'champion', 'no animation row for this champion')
for cid in anims:
    if cid not in champs: bad('animations', cid, 'champion', 'unknown champion')
seen = {}
for r in S['abilities']['rows']:
    k = f"{r['champion']}/{r['slot']}"
    if r['champion'] not in champs: bad('abilities', k, 'champion', 'unknown champion')
    if r['slot'] not in ('neutral', 'side', 'up', 'down'): bad('abilities', k, 'slot', 'must be neutral/side/up/down')
    if r['key'] not in ('Q', 'W', 'E', 'R'): bad('abilities', k, 'key', 'must be Q/W/E/R')
    if r['role'] not in roles: bad('abilities', k, 'role', f"unknown role {r['role']}")
    for e in r['effects']:
        eid, _, val = e.partition(':')
        if eid not in effects: bad('abilities', k, 'effects', f'unknown effect {eid}')
        elif effects[eid]['when'] == 'passive': bad('abilities', k, 'effects', f'{eid} is a passive effect')
        try: float(val)
        except ValueError: bad('abilities', k, 'effects', f'{e}: value after the colon must be a number')
    if k in seen: bad('abilities', k, 'slot', 'duplicate')
    seen[k] = r
for cid in champs:
    for s in ('neutral', 'side', 'up', 'down'):
        if f'{cid}/{s}' not in seen: bad('abilities', f'{cid}/{s}', '*', 'missing row')
    keys = sorted(r['key'] for r in S['abilities']['rows'] if r['champion'] == cid)
    if keys != ['E', 'Q', 'R', 'W']: bad('abilities', cid, 'key', f'each of Q W E R once, got {keys}')
for cid in champs:
    pr = passives.get(cid)
    if not pr: bad('passives', cid, '*', 'champion has no passive row')
    elif pr['effect'] not in effects or effects[pr['effect']]['when'] != 'passive': bad('passives', cid, 'effect', f"{pr['effect']} is not a passive effect")
for r in S['objects']['rows']:
    oid = r['id']
    if r['champion'] not in champs: bad('objects', oid, 'champion', 'unknown champion')
    slot, _, cond = r['trigger'].partition(':')
    if slot not in ('neutral', 'side', 'up', 'down') or cond not in ('', 'third'): bad('objects', oid, 'trigger', 'slot[:third]')
    if r['kind'] not in ('tornado', 'wall', 'barrel', 'spirit'): bad('objects', oid, 'kind', 'unknown kind')
    if (r['kind'] == 'barrel') != (r['model'] != 'none'): bad('objects', oid, 'model', 'barrels need a model, other kinds none')
    if (r['kind'] == 'tornado') != (r['mesh'] != 'none'): bad('objects', oid, 'mesh', 'tornadoes need a mesh, other kinds none')
    for col in ('forward', 'life', 'radius', 'speed', 'size', 'dmg', 'basekb', 'kbgain', 'angle', 'slow'):
        if not isinstance(r[col], (int, float)): bad('objects', oid, col, 'must be a number')
for cid in passives:
    if cid not in champs: bad('passives', cid, 'champion', 'unknown champion')
for r in S['slap_clips']['rows']:
    if not (r['role'] in roles or r['role'] in ('@neutral', '@side', '@up', '@down')):
        bad('slap_clips', r['pattern'], 'role', f"unknown role {r['role']}")
if S['slap_clips']['rows'][-1]['pattern'] != '*': bad('slap_clips', 'last', 'pattern', "last row must be the '*' fallback")

# 3. League files and sound events exist in the local install (read in place)
spec = importlib.util.spec_from_file_location('wp', os.path.join(ROOT, 'tools', 'wadpeek.py')); wp = importlib.util.module_from_spec(spec); spec.loader.exec_module(wp)
spec = importlib.util.spec_from_file_location('bk', os.path.join(ROOT, 'tools', 'bnkpeek.py')); bk = importlib.util.module_from_spec(spec); spec.loader.exec_module(bk)
CH = os.path.join(LEAGUE, 'Game', 'DATA', 'FINAL', 'Champions')
def open_wad(fn):
    wp.TOCNAME = ('data/final/champions/' + fn).lower().replace('.wad.client', '.wad.subchunktoc')
    f, e, _ = wp.read_wad(os.path.join(CH, fn))
    return (f, e, wp.TOC)
def entry(w, path):
    return w[1].get(wp.h(path))
def read(w, path):
    wp.TOC = w[2]
    return wp.data(w[0], w[1][wp.h(path)])
def submeshes(w, path):
    d = read(w, path); n = struct.unpack_from('<I', d, 8)[0]
    return [d[12 + i * 80:12 + i * 80 + 64].split(b'\0')[0].decode() for i in range(n)]

if not os.path.isdir(CH):
    problems.append(f'League install not found at {CH} (pass --league)')
else:
    for cid, r in champs.items():
        try:
            w = open_wad(r['wad']); vw = open_wad(r['voWad'])
        except Exception as ex:
            bad('champions', cid, 'wad', f'cannot open: {ex}'); continue
        for col in ('skn', 'skl', 'tex', 'icon', 'portrait', 'sfxBank', 'sfxEvents'):
            if not entry(w, r[col]): bad('champions', cid, col, f'not in {r["wad"]}: {r[col]}')
        for sm, tex in r['texBySubmesh'].items():
            if not entry(w, tex): bad('champions', cid, 'texBySubmesh', f'not in wad: {tex}')
        if entry(w, r['skn']):
            subs = submeshes(w, r['skn'])
            for sm in list(r['texBySubmesh']) + r['hideSubmeshes']:
                if sm not in subs: bad('champions', cid, 'texBySubmesh/hideSubmeshes', f'no submesh {sm} (has {subs})')
        if not entry(vw, r['voAudio']): bad('champions', cid, 'voAudio', f'not in {r["voWad"]}')
        vo_src = vw if entry(vw, r['voEvents']) else (w if entry(w, r['voEvents']) else None)
        if not vo_src: bad('champions', cid, 'voEvents', 'not in voWad or wad'); vo = {}
        else: vo = bk.parse(read(vo_src, r['voEvents']))
        sfx = bk.parse(read(w, r['sfxEvents'])) if entry(w, r['sfxEvents']) else {}
        for col in ('voSelect', 'voKO', 'voTaunt'):
            if not bk.wems(vo, bk.fnv1(r[col])): bad('champions', cid, col, f'voice event plays nothing: {r[col]}')
        a = anims.get(cid, {})
        for role in roles:
            nm = a.get(role)
            if nm and not entry(w, f'assets/characters/{cid[4:]}/skins/base/animations/{nm}.anm'):
                bad('animations', cid, role, f'no {nm}.anm')
        for ab in [x for x in S['abilities']['rows'] if x['champion'] == cid]:
            k = f"{cid}/{ab['slot']}"
            if not bk.wems(sfx, bk.fnv1(ab['sfx'])): bad('abilities', k, 'sfx', f"sfx event plays nothing: {ab['sfx']}")
            if not bk.wems(vo, bk.fnv1(ab['vo'])): bad('abilities', k, 'vo', f"voice event plays nothing: {ab['vo']}")
            if ab['sfxProc'] != 'none' and not bk.wems(sfx, bk.fnv1(ab['sfxProc'])): bad('abilities', k, 'sfxProc', f"sfx event plays nothing: {ab['sfxProc']}")
        for ob in [o for o in S['objects']['rows'] if o['champion'] == cid]:
            paths = [ob['tex']] + ([ob['mesh']] if ob['mesh'] != 'none' else [])
            if ob['model'] != 'none':
                mb = f"assets/characters/{ob['model']}/skins/base/"
                paths += [mb + ob['model'] + '.skn', mb + ob['model'] + '.skl'] + [mb + 'animations/' + ob['model'] + '_' + a + '.anm' for a in ('idle1', 'spawn', 'death')]
            for pth in paths:
                if not entry(w, pth): bad('objects', ob['id'], 'files', f'not in {r["wad"]}: {pth}')
            for col in ('sfxSpawn', 'sfxHit'):
                if ob[col] != 'none' and not bk.wems(sfx, bk.fnv1(ob[col])): bad('objects', ob['id'], col, f"sfx event plays nothing: {ob[col]}")
        pr = passives.get(cid)
        if pr and pr['sfx'] != 'none' and not bk.wems(sfx, bk.fnv1(pr['sfx'])): bad('passives', cid, 'sfx', f"sfx event plays nothing: {pr['sfx']}")

# 4. Slap City side, from the in-game dump (Dev/Dump.cs)
if not os.path.isdir(DUMP):
    problems.append(f'Slap City dump not found at {DUMP} (run the prefabs dev command)')
else:
    chars = open(os.path.join(DUMP, 'characters.txt'), encoding='utf-8').read()
    allclips = set()
    for fid, r in fighters.items():
        if f'== {fid} path={r["prefab"]}' not in chars: bad('slap_fighters', fid, 'prefab', 'not in the game registry')
        pf = os.path.join(DUMP, f'prefab-{fid}.txt')
        clips = set(re.findall(r'^clip (\S+)', open(pf, encoding='utf-8').read(), re.M)) if os.path.exists(pf) else set()
        mv = os.path.join(DUMP, f'moveset-{fid}.json')
        ids = set(re.findall(r'^"id":"([^"]+)"', open(mv, encoding='utf-8').read(), re.M)) if os.path.exists(mv) else set()
        for slot in ('neutral', 'side', 'up', 'down'):
            if r['moves'].get(slot) not in ids: bad('slap_fighters', fid, 'moves', f"no move {r['moves'].get(slot)} for {slot}")
        for sp in r['specials']:
            if sp not in clips and not any(c.startswith(sp) for c in clips): bad('slap_fighters', fid, 'specials', f'no clip {sp}')
    for fn in os.listdir(DUMP):
        if fn.startswith('prefab-'):
            allclips |= set(re.findall(r'^clip (\S+)', open(os.path.join(DUMP, fn), encoding='utf-8').read(), re.M))
    def match(p, c):
        if p == '*': return True
        if p.startswith('*'): return c.endswith(p[1:])
        if p.endswith('*'): return c.startswith(p[:-1])
        return p == c
    for r in S['slap_clips']['rows']:
        if not any(match(r['pattern'], c) for c in allclips): bad('slap_clips', r['pattern'], 'pattern', 'matches no Slap City clip')

# 5. every hook is implemented where the sheet says
for r in S['hooks']['rows']:
    fp = os.path.join(ROOT, r['file'])
    if not os.path.exists(fp) or f"// Hook:{r['id']}" not in open(fp, encoding='utf-8').read():
        bad('hooks', r['id'], 'file', f"no // Hook:{r['id']} marker in {r['file']}")

cells = sum(len(sh['rows']) * len(sh['columns']) for sh in S.values())
print(f'{cells} cells across {len(S)} sheets')
if problems:
    print(f'{len(problems)} problem(s):')
    for p in problems: print('  ' + p)
    sys.exit(1)
print('preflight clean')
