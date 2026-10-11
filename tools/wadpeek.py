"""Dev tool: inspect a League .wad.client in place (nothing is copied out of the game folder).

usage: wadpeek.py <wad> list            -> entry count, types, sizes
       wadpeek.py <wad> has <path>...   -> which guessed paths exist
       wadpeek.py <wad> strings <path>  -> printable strings in one entry (for .bin files)
       wadpeek.py <wad> head <path>     -> first bytes of one entry
       wadpeek.py <wad> extract <path> <out> -> one entry to a scratch file (dev only)
"""
import struct, sys, re, xxhash, zstandard, gzip, io

def read_wad(fn):
    f = open(fn, 'rb')
    magic, major, minor = struct.unpack('<2sBB', f.read(4))
    assert magic == b'RW', magic
    if major >= 3:
        f.seek(4 + 256 + 8)
    count = struct.unpack('<I', f.read(4))[0]
    entries = {}
    for _ in range(count):
        h, off, csize, size, typ, dup, sub0, chk = struct.unpack('<QIIIBBHQ', f.read(32))
        entries[h] = (off, csize, size, typ & 0xF, typ >> 4, sub0)
    global TOC
    TOC = []
    toc = entries.get(xxhash.xxh64(TOCNAME.encode()).intdigest()) if TOCNAME else None
    if toc:
        d = data(f, toc)
        TOC = [struct.unpack_from('<IIQ', d, i * 16)[:2] for i in range(len(d) // 16)]
    return f, entries, (major, minor)

TOC = []
TOCNAME = None

def data(f, e):
    off, csize, size, typ, nsub, sub0 = e
    f.seek(off)
    raw = f.read(csize)
    if typ == 0: return raw
    if typ == 1: return gzip.decompress(raw)
    if typ == 3:
        return zstandard.ZstdDecompressor().decompress(raw, max_output_size=size)
    if typ == 4:
        out, p = bytearray(), 0
        for i in range(sub0, sub0 + nsub):
            c, u = TOC[i]
            chunk = raw[p:p + c]; p += c
            out += chunk if c == u else zstandard.ZstdDecompressor().decompress(chunk, max_output_size=u)
        return bytes(out)
    return raw

def h(p): return xxhash.xxh64(p.lower().encode()).intdigest()

if __name__ == '__main__':
    import os
    w = sys.argv[1].replace('\\', '/')
    TOCNAME = ('data/final/' + w.split('/DATA/FINAL/')[1]).lower().replace('.wad.client', '.wad.subchunktoc') if '/DATA/FINAL/' in w else None
    f, ents, ver = read_wad(sys.argv[1])
    cmd = sys.argv[2]
    if cmd == 'list':
        from collections import Counter
        print('version', ver, 'entries', len(ents), Counter(e[3] for e in ents.values()))
    elif cmd == 'has':
        for p in sys.argv[3:]:
            e = ents.get(h(p)); print(('YES ' if e else 'no  ') + p + ('' if not e else f' size={e[2]} type={e[3]}'))
    elif cmd == 'strings':
        d = data(f, ents[h(sys.argv[3])])
        for m in sorted(set(re.findall(rb'[ -~]{6,}', d))): print(m.decode())
    elif cmd == 'extract':  # dev only: <entry> <outfile>, into a scratch folder outside the project
        open(sys.argv[4], 'wb').write(data(f, ents[h(sys.argv[3])]))
    elif cmd == 'head':
        d = data(f, ents[h(sys.argv[3])]); print(len(d), d[:64])

