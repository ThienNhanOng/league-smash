"""Dev tool: decode a League .tex from a WAD to a PNG in a scratch folder (outside the project) to look at it.

usage: texpeek.py <wad> <tex path> <out.png>   -> also prints per-channel ranges
"""
import struct, sys, io, importlib.util, os
from PIL import Image

spec = importlib.util.spec_from_file_location('wp', os.path.join(os.path.dirname(__file__), 'wadpeek.py'))
wp = importlib.util.module_from_spec(spec); spec.loader.exec_module(wp)
w = sys.argv[1].replace('\\', '/')
wp.TOCNAME = ('data/final/' + w.split('/DATA/FINAL/')[1]).lower().replace('.wad.client', '.wad.subchunktoc')
f, e, _ = wp.read_wad(sys.argv[1])
d = wp.data(f, e[wp.h(sys.argv[2])])
wd, ht = struct.unpack_from('<HH', d, 4); fmt = d[9]; mips = d[11] & 1
bw, bh = max(1, (wd + 3) // 4), max(1, (ht + 3) // 4)
size = {10: bw * bh * 8, 12: bw * bh * 16, 20: wd * ht * 4}[fmt]
top = d[len(d) - size:] if mips else d[12:12 + size]
if fmt == 20:
    im = Image.frombytes('RGBA', (wd, ht), top, 'raw', 'BGRA')
else:
    fourcc = b'DXT1' if fmt == 10 else b'DXT5'
    hdr = b'DDS ' + struct.pack('<7I44x', 124, 0x1007, ht, wd, size, 0, 1) + struct.pack('<2I4s5I', 32, 4, fourcc, 0, 0, 0, 0, 0) + struct.pack('<5I', 0x1000, 0, 0, 0, 0)
    im = Image.open(io.BytesIO(hdr + top)).convert('RGBA')
im.save(sys.argv[3])
print(sys.argv[2].split('/')[-1], wd, ht, 'fmt', fmt, 'channel ranges', [im.getchannel(c).getextrema() for c in 'RGBA'])
