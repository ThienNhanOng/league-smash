using System.Text;

namespace SlapRift.League
{
    // xxHash64 (seed 0): WAD entries are keyed by the hash of their lower-case path.
    internal static class XxHash64
    {
        const ulong P1 = 11400714785074694791UL, P2 = 14029467366897019727UL, P3 = 1609587929392839161UL,
                    P4 = 9650029242287828579UL, P5 = 2870177450012600261UL;

        public static ulong OfPath(string path) => Hash(Encoding.UTF8.GetBytes(path.ToLowerInvariant()));

        static ulong Rotl(ulong x, int r) => (x << r) | (x >> (64 - r));
        static ulong Round(ulong acc, ulong input) { acc += input * P2; acc = Rotl(acc, 31); return acc * P1; }
        static ulong Merge(ulong acc, ulong val) { acc ^= Round(0, val); return acc * P1 + P4; }
        static ulong U64(byte[] b, int i) => System.BitConverter.ToUInt64(b, i);
        static uint U32(byte[] b, int i) => System.BitConverter.ToUInt32(b, i);

        public static ulong Hash(byte[] b)
        {
            int len = b.Length, p = 0;
            ulong h;
            if (len >= 32)
            {
                ulong v1 = unchecked(P1 + P2), v2 = P2, v3 = 0, v4 = unchecked(0UL - P1);
                int limit = len - 32;
                do
                {
                    v1 = Round(v1, U64(b, p)); v2 = Round(v2, U64(b, p + 8));
                    v3 = Round(v3, U64(b, p + 16)); v4 = Round(v4, U64(b, p + 24));
                    p += 32;
                } while (p <= limit);
                h = Rotl(v1, 1) + Rotl(v2, 7) + Rotl(v3, 12) + Rotl(v4, 18);
                h = Merge(h, v1); h = Merge(h, v2); h = Merge(h, v3); h = Merge(h, v4);
            }
            else h = P5;
            h += (ulong)len;
            while (p + 8 <= len) { h ^= Round(0, U64(b, p)); h = Rotl(h, 27) * P1 + P4; p += 8; }
            if (p + 4 <= len) { h ^= U32(b, p) * P1; h = Rotl(h, 23) * P2 + P3; p += 4; }
            while (p < len) { h ^= b[p] * P5; h = Rotl(h, 11) * P1; p++; }
            h ^= h >> 33; h *= P2; h ^= h >> 29; h *= P3; h ^= h >> 32;
            return h;
        }
    }
}
