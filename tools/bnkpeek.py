"""Dev tool: resolve Wwise event names to the .wem ids they play (prototype of the plugin's Audio/WwiseBank.cs).

usage: bnkpeek.py <events.bnk> [event names...]   -> every event id with its wem ids, and which names hit
"""
import struct, sys

def fnv1(s):
    h = 0x811c9dc5
    for c in s.lower().encode():
        h = (h * 0x01000193) & 0xffffffff
        h ^= c
    return h

def chunks(d):
    p = 0
    while p + 8 <= len(d):
        tag = d[p:p + 4]; sz = struct.unpack_from('<I', d, p + 4)[0]
        yield tag, d[p + 8:p + 8 + sz]; p += 8 + sz

def parse(d):
    objs = {}
    for tag, c in chunks(d):
        if tag != b'HIRC': continue
        n = struct.unpack_from('<I', c, 0)[0]; p = 4
        for _ in range(n):
            t = c[p]; sz = struct.unpack_from('<I', c, p + 1)[0]
            body = c[p + 5:p + 5 + sz]
            objs[struct.unpack_from('<I', body, 0)[0]] = (t, body)
            p += 5 + sz
    return objs

def playlist_children(body):
    # random/sequence containers end with a playlist: u16 count, then (u32 id, s32 weight) per item
    for k in range(1, 256):
        at = len(body) - 2 - 8 * k
        if at < 4: break
        if struct.unpack_from('<H', body, at)[0] == k:
            return [struct.unpack_from('<I', body, at + 2 + 8 * i)[0] for i in range(k)]
    return []

def wems(objs, oid, depth=0):
    if depth > 6 or oid not in objs: return []
    t, b = objs[oid]
    if t == 2: return [struct.unpack_from('<I', b, 9)[0]]          # sound: id, plugin(4), stream(1), sourceId
    if t == 5: return [w for ch in playlist_children(b) for w in wems(objs, ch, depth + 1)]
    if t == 3: return wems(objs, struct.unpack_from('<I', b, 6)[0], depth + 1)  # action: id, type(2), target
    if t == 4:
        cnt = b[4]
        return [w for i in range(cnt) for w in wems(objs, struct.unpack_from('<I', b, 5 + 4 * i)[0], depth + 1)]
    return []

if __name__ == '__main__':
    objs = parse(open(sys.argv[1], 'rb').read())
    events = {i: v for i, v in objs.items() if v[0] == 4}
    names = {fnv1(n): n for n in sys.argv[2:]}
    for eid in events:
        print(hex(eid), names.get(eid, ''), wems(objs, eid))
