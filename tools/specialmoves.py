"""Dev tool: which Slap City move ids play each special clip, per base fighter (from the in-game moveset dump)."""
import json, os, re, sys

DUMP = sys.argv[1] if len(sys.argv) > 1 else r'D:\steam\steamapps\common\Slap City\BepInEx\slaprift-dump'
for f in ['bcman', 'ruby', 'fishbunjin', 'goddess', 'remedy', 'ittledew']:
    j = json.loads(open(os.path.join(DUMP, 'moveset-%s.json' % f), encoding='utf-8').read())
    out = {}
    for v in j.values():
        s = json.dumps(v.get('move', {}))
        for clip in re.findall(r'"t": "SAPA"[^}]*?"a": "(special[a-z]*)', s):
            slot = next((n for n in ('neutral', 'forward', 'up', 'down') if clip.startswith('special' + n)), None)
            if slot: out.setdefault(slot, set()).add(v.get('id'))
    print(f, {k: sorted(v) for k, v in sorted(out.items())})
