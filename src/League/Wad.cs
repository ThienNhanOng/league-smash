using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace SlapRift.League
{
    // Reads one League .wad.client (v3) in place from the player's install. Entries are zstd, gzip, raw,
    // or split into sub-chunks listed in the WAD's own .subchunktoc entry.
    internal sealed class Wad : IDisposable
    {
        struct Entry { public uint Offset, CSize, Size; public byte Type, SubCount; public ushort SubStart; }

        readonly FileStream fs;
        readonly Dictionary<ulong, Entry> entries = new Dictionary<ulong, Entry>();
        readonly List<KeyValuePair<uint, uint>> toc = new List<KeyValuePair<uint, uint>>();
        readonly object gate = new object();

        public Wad(string file)
        {
            fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var br = new BinaryReader(fs);
            if (br.ReadByte() != 'R' || br.ReadByte() != 'W') throw new InvalidDataException("not a WAD: " + file);
            byte major = br.ReadByte(); br.ReadByte();
            if (major < 3) throw new InvalidDataException("WAD version " + major + " is not supported");
            fs.Position = 4 + 256 + 8;
            uint count = br.ReadUInt32();
            for (uint i = 0; i < count; i++)
            {
                ulong h = br.ReadUInt64();
                var e = new Entry { Offset = br.ReadUInt32(), CSize = br.ReadUInt32(), Size = br.ReadUInt32() };
                byte t = br.ReadByte();
                e.Type = (byte)(t & 0xF); e.SubCount = (byte)(t >> 4);
                br.ReadByte();
                e.SubStart = br.ReadUInt16();
                br.ReadUInt64();
                entries[h] = e;
            }
            // the sub-chunk table is an entry named after the WAD itself
            var rel = file.Replace('\\', '/');
            int at = rel.IndexOf("/DATA/FINAL/", StringComparison.OrdinalIgnoreCase);
            if (at >= 0)
            {
                var tocName = ("data/final/" + rel.Substring(at + 12)).Replace(".wad.client", ".wad.subchunktoc");
                if (TryRead(tocName, out var t))
                    for (int i = 0; i + 16 <= t.Length; i += 16)
                        toc.Add(new KeyValuePair<uint, uint>(BitConverter.ToUInt32(t, i), BitConverter.ToUInt32(t, i + 4)));
            }
        }

        public bool Has(string path) => entries.ContainsKey(XxHash64.OfPath(path));

        public byte[] Read(string path)
        {
            if (!TryRead(path, out var d)) throw new FileNotFoundException("not in WAD: " + path);
            return d;
        }

        public bool TryRead(string path, out byte[] data)
        {
            data = null;
            if (!entries.TryGetValue(XxHash64.OfPath(path), out var e)) return false;
            byte[] raw = new byte[e.CSize];
            lock (gate)
            {
                fs.Position = e.Offset;
                int n = 0;
                while (n < raw.Length) { int r = fs.Read(raw, n, raw.Length - n); if (r <= 0) break; n += r; }
            }
            switch (e.Type)
            {
                case 0: data = raw; break;
                case 1:
                    using (var gz = new GZipStream(new MemoryStream(raw), CompressionMode.Decompress))
                    { var ms = new MemoryStream((int)e.Size); gz.CopyTo(ms); data = ms.ToArray(); }
                    break;
                case 3: data = Zstd.Decompress(raw, raw.Length, (int)e.Size); break;
                case 4:
                {
                    data = new byte[e.Size];
                    int src = 0, dst = 0;
                    for (int i = e.SubStart; i < e.SubStart + e.SubCount; i++)
                    {
                        uint c = toc[i].Key, u = toc[i].Value;
                        if (c == u) Buffer.BlockCopy(raw, src, data, dst, (int)c);
                        else
                        {
                            var chunk = new byte[c];
                            Buffer.BlockCopy(raw, src, chunk, 0, (int)c);
                            var outp = Zstd.Decompress(chunk, (int)c, (int)u);
                            Buffer.BlockCopy(outp, 0, data, dst, (int)u);
                        }
                        src += (int)c; dst += (int)u;
                    }
                    break;
                }
                default: return false;
            }
            return true;
        }

        public void Dispose() => fs.Dispose();
    }
}
