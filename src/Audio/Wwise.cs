using System;
using System.Collections.Generic;
using System.Text;

namespace SlapRift.Audio
{
    // Reads Wwise banks (v145, as League ships them): which .wem ids an event plays, and the .wem bytes
    // embedded in a bank (DIDX/DATA) or packed in a League .wpk.
    internal static class Wwise
    {
        public static uint Fnv1(string s)
        {
            uint h = 0x811c9dc5;
            foreach (byte c in Encoding.ASCII.GetBytes(s.ToLowerInvariant())) { h *= 0x01000193; h ^= c; }
            return h;
        }

        static IEnumerable<KeyValuePair<string, ArraySegment<byte>>> Chunks(byte[] d)
        {
            int p = 0;
            while (p + 8 <= d.Length)
            {
                string tag = Encoding.ASCII.GetString(d, p, 4);
                int sz = BitConverter.ToInt32(d, p + 4);
                yield return new KeyValuePair<string, ArraySegment<byte>>(tag, new ArraySegment<byte>(d, p + 8, Math.Min(sz, d.Length - p - 8)));
                p += 8 + sz;
            }
        }

        public sealed class Hirc
        {
            readonly Dictionary<uint, KeyValuePair<byte, ArraySegment<byte>>> objs = new Dictionary<uint, KeyValuePair<byte, ArraySegment<byte>>>();

            public Hirc(byte[] bank)
            {
                foreach (var c in Chunks(bank))
                {
                    if (c.Key != "HIRC") continue;
                    var a = c.Value.Array; int p = c.Value.Offset;
                    uint n = BitConverter.ToUInt32(a, p); p += 4;
                    for (uint i = 0; i < n; i++)
                    {
                        byte t = a[p]; int sz = BitConverter.ToInt32(a, p + 1);
                        var body = new ArraySegment<byte>(a, p + 5, sz);
                        objs[BitConverter.ToUInt32(a, p + 5)] = new KeyValuePair<byte, ArraySegment<byte>>(t, body);
                        p += 5 + sz;
                    }
                }
            }

            public List<uint> EventWems(string eventName)
            {
                var r = new List<uint>();
                Collect(Fnv1(eventName), r, 0);
                return r;
            }

            void Collect(uint id, List<uint> into, int depth)
            {
                if (depth > 6 || !objs.TryGetValue(id, out var o)) return;
                var b = o.Value; var a = b.Array; int s = b.Offset;
                switch (o.Key)
                {
                    case 2: into.Add(BitConverter.ToUInt32(a, s + 9)); break;               // sound -> source wem
                    case 3: Collect(BitConverter.ToUInt32(a, s + 6), into, depth + 1); break; // action -> target
                    case 4:                                                                   // event -> actions
                        int n = a[s + 4];
                        for (int i = 0; i < n; i++) Collect(BitConverter.ToUInt32(a, s + 5 + 4 * i), into, depth + 1);
                        break;
                    case 5:                                                                   // random/sequence container
                        foreach (var ch in Playlist(b)) Collect(ch, into, depth + 1);
                        break;
                }
            }

            // a random/sequence container ends with its playlist: u16 count, then (u32 id, s32 weight)
            static IEnumerable<uint> Playlist(ArraySegment<byte> b)
            {
                for (int k = 1; k < 256; k++)
                {
                    int at = b.Count - 2 - 8 * k;
                    if (at < 4) yield break;
                    if (BitConverter.ToUInt16(b.Array, b.Offset + at) == k)
                    {
                        for (int i = 0; i < k; i++) yield return BitConverter.ToUInt32(b.Array, b.Offset + at + 2 + 8 * i);
                        yield break;
                    }
                }
            }
        }

        public static Dictionary<uint, ArraySegment<byte>> BankMedia(byte[] bank)
        {
            var r = new Dictionary<uint, ArraySegment<byte>>();
            ArraySegment<byte> didx = default, data = default;
            foreach (var c in Chunks(bank)) { if (c.Key == "DIDX") didx = c.Value; else if (c.Key == "DATA") data = c.Value; }
            if (didx.Array == null || data.Array == null) return r;
            for (int i = 0; i + 12 <= didx.Count; i += 12)
            {
                uint id = BitConverter.ToUInt32(didx.Array, didx.Offset + i);
                int off = BitConverter.ToInt32(didx.Array, didx.Offset + i + 4), sz = BitConverter.ToInt32(didx.Array, didx.Offset + i + 8);
                r[id] = new ArraySegment<byte>(data.Array, data.Offset + off, sz);
            }
            return r;
        }

        public static Dictionary<uint, ArraySegment<byte>> WpkMedia(byte[] wpk)
        {
            var r = new Dictionary<uint, ArraySegment<byte>>();
            if (Encoding.ASCII.GetString(wpk, 0, 4) != "r3d2") return r;
            int n = BitConverter.ToInt32(wpk, 8);
            for (int i = 0; i < n; i++)
            {
                int o = BitConverter.ToInt32(wpk, 12 + 4 * i);
                if (o == 0) continue;
                int off = BitConverter.ToInt32(wpk, o), sz = BitConverter.ToInt32(wpk, o + 4), nl = BitConverter.ToInt32(wpk, o + 8);
                var name = Encoding.Unicode.GetString(wpk, o + 12, nl * 2);
                if (uint.TryParse(name.Replace(".wem", ""), out var id)) r[id] = new ArraySegment<byte>(wpk, off, sz);
            }
            return r;
        }
    }
}
