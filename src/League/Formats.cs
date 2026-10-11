using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace SlapRift.League
{
    // Plain-data readers for League's model formats. They run on a worker thread; ChampionAssets turns
    // the results into Unity objects on the main thread. League is converted to Unity space by mirroring X.
    internal static class Conv
    {
        public static Vector3 P(float x, float y, float z) => new Vector3(-x, y, z);
        public static Quaternion Q(float x, float y, float z, float w) => new Quaternion(x, -y, -z, w);
        public static Vector3 V3(BinaryReader br) => new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());

        public static uint Elf(string s)
        {
            uint h = 0;
            foreach (char ch in s.ToLowerInvariant())
            {
                h = (h << 4) + ch;
                uint g = h & 0xF0000000;
                if (g != 0) h ^= g >> 24;
                h &= ~g;
            }
            return h;
        }

        // 48-bit "smallest three" quaternion used by v5 and compressed animations
        public static Quaternion Quat48(ushort a0, ushort a1, ushort a2)
        {
            ulong bits = a0 | ((ulong)a1 << 16) | ((ulong)a2 << 32);
            int max = (int)((bits >> 45) & 3);
            const double s = 1.41421356237 / 32767.0, o = 0.70710678118;
            double a = ((bits >> 30) & 0x7FFF) * s - o, b = ((bits >> 15) & 0x7FFF) * s - o, c = (bits & 0x7FFF) * s - o;
            double d = Math.Sqrt(Math.Max(0.0, 1.0 - (a * a + b * b + c * c)));
            switch (max)
            {
                case 0: return Q((float)d, (float)a, (float)b, (float)c);
                case 1: return Q((float)a, (float)d, (float)b, (float)c);
                case 2: return Q((float)a, (float)b, (float)d, (float)c);
                default: return Q((float)a, (float)b, (float)c, (float)d);
            }
        }
    }

    internal sealed class SklData
    {
        public string[] Names; public int[] Parents; public uint[] Hashes;
        public Vector3[] Pos; public Quaternion[] Rot; public Vector3[] Scale;
        public ushort[] Influences;

        public static SklData Read(byte[] d)
        {
            var br = new BinaryReader(new MemoryStream(d));
            br.ReadUInt32();
            if (br.ReadUInt32() != 0x22FD4FC3) throw new InvalidDataException("unsupported skeleton (legacy .skl)");
            br.ReadUInt32(); br.ReadUInt16();
            int count = br.ReadUInt16();
            int influences = (int)br.ReadUInt32();
            int jointsOff = br.ReadInt32(); br.ReadInt32();
            int inflOff = br.ReadInt32();
            var s = new SklData
            {
                Names = new string[count], Parents = new int[count], Hashes = new uint[count],
                Pos = new Vector3[count], Rot = new Quaternion[count], Scale = new Vector3[count], Influences = new ushort[influences]
            };
            for (int i = 0; i < count; i++)
            {
                br.BaseStream.Position = jointsOff + i * 100;
                br.ReadUInt16();
                short id = br.ReadInt16();
                s.Parents[id] = br.ReadInt16(); br.ReadInt16();
                s.Hashes[id] = br.ReadUInt32(); br.ReadSingle();
                var t = Conv.V3(br); var sc = Conv.V3(br);
                float qx = br.ReadSingle(), qy = br.ReadSingle(), qz = br.ReadSingle(), qw = br.ReadSingle();
                s.Pos[id] = Conv.P(t.x, t.y, t.z); s.Scale[id] = sc; s.Rot[id] = Conv.Q(qx, qy, qz, qw);
                br.BaseStream.Position = jointsOff + i * 100 + 96;
                long at = br.BaseStream.Position; int nameOff = br.ReadInt32();
                s.Names[id] = CStr(d, (int)(at + nameOff));
            }
            br.BaseStream.Position = inflOff;
            for (int i = 0; i < influences; i++) s.Influences[i] = br.ReadUInt16();
            return s;
        }

        static string CStr(byte[] d, int at)
        {
            int e = at; while (e < d.Length && d[e] != 0) e++;
            return Encoding.ASCII.GetString(d, at, e - at);
        }
    }

    internal sealed class SknData
    {
        public struct Sub { public string Name; public int StartIndex, IndexCount; }
        public Sub[] Subs; public ushort[] Indices;
        public Vector3[] Pos, Nrm; public Vector2[] Uv; public byte[] Bones; public float[] Weights;

        public static SknData Read(byte[] d)
        {
            var br = new BinaryReader(new MemoryStream(d));
            if (br.ReadUInt32() != 0x00112233) throw new InvalidDataException("not a .skn");
            int major = br.ReadUInt16(); br.ReadUInt16();
            var s = new SknData();
            int subCount = major > 0 ? br.ReadInt32() : 0;
            s.Subs = new Sub[subCount];
            for (int i = 0; i < subCount; i++)
            {
                var name = Encoding.ASCII.GetString(br.ReadBytes(64)).Split('\0')[0];
                br.ReadUInt32(); br.ReadUInt32();
                s.Subs[i] = new Sub { Name = name, StartIndex = br.ReadInt32(), IndexCount = br.ReadInt32() };
            }
            if (major == 4) br.ReadUInt32();
            int ic = br.ReadInt32(), vc = br.ReadInt32();
            int vsize = 52, vtype = 0;
            if (major == 4) { vsize = br.ReadInt32(); vtype = br.ReadInt32(); br.ReadBytes(40); }
            s.Indices = new ushort[ic];
            for (int i = 0; i < ic; i++) s.Indices[i] = br.ReadUInt16();
            s.Pos = new Vector3[vc]; s.Nrm = new Vector3[vc]; s.Uv = new Vector2[vc];
            s.Bones = new byte[vc * 4]; s.Weights = new float[vc * 4];
            for (int v = 0; v < vc; v++)
            {
                long start = br.BaseStream.Position;
                var p = Conv.V3(br); s.Pos[v] = Conv.P(p.x, p.y, p.z);
                for (int k = 0; k < 4; k++) s.Bones[v * 4 + k] = br.ReadByte();
                for (int k = 0; k < 4; k++) s.Weights[v * 4 + k] = br.ReadSingle();
                var n = Conv.V3(br); s.Nrm[v] = Conv.P(n.x, n.y, n.z);
                s.Uv[v] = new Vector2(br.ReadSingle(), br.ReadSingle());
                br.BaseStream.Position = start + vsize;
            }
            if (subCount == 0) s.Subs = new[] { new Sub { Name = "Base", StartIndex = 0, IndexCount = ic } };
            return s;
        }
    }

    // One animation as per-joint keyframe lists (time in seconds), whatever the file format.
    internal sealed class AnmData
    {
        public sealed class Track
        {
            public uint Hash;
            public List<KeyValuePair<float, Quaternion>> Rot = new List<KeyValuePair<float, Quaternion>>();
            public List<KeyValuePair<float, Vector3>> Pos = new List<KeyValuePair<float, Vector3>>();
            public List<KeyValuePair<float, Vector3>> Scale = new List<KeyValuePair<float, Vector3>>();
        }
        public float Duration; public List<Track> Tracks = new List<Track>();

        public static AnmData Read(byte[] d)
        {
            var magic = Encoding.ASCII.GetString(d, 0, 8);
            uint version = BitConverter.ToUInt32(d, 8);
            if (magic == "r3d2canm") return ReadCompressed(d);
            if (magic == "r3d2anmd" && (version == 4 || version == 5)) return ReadUncompressed(d, version);
            throw new InvalidDataException("unsupported animation " + magic + " v" + version);
        }

        static AnmData ReadUncompressed(byte[] d, uint version)
        {
            var br = new BinaryReader(new MemoryStream(d)); br.BaseStream.Position = 12;
            br.ReadUInt32(); br.ReadUInt32(); br.ReadUInt32(); br.ReadUInt32();
            int tracks = br.ReadInt32(), frames = br.ReadInt32(); float fd = br.ReadSingle();
            int hashOff = br.ReadInt32(); br.ReadInt32(); br.ReadInt32();
            int vecOff = br.ReadInt32(), quatOff = br.ReadInt32(), frameOff = br.ReadInt32();
            var a = new AnmData { Duration = Math.Max(fd, frames > 1 ? (frames - 1) * fd : fd) };
            int vecEnd = quatOff, quatEnd = version == 4 ? frameOff : hashOff;
            var vecs = new Vector3[(vecEnd - vecOff) / 12];
            br.BaseStream.Position = vecOff + 12;
            for (int i = 0; i < vecs.Length; i++) vecs[i] = Conv.V3(br);
            int qsz = version == 4 ? 16 : 6;
            var quats = new Quaternion[(quatEnd - quatOff) / qsz];
            br.BaseStream.Position = quatOff + 12;
            for (int i = 0; i < quats.Length; i++)
                quats[i] = version == 4
                    ? Conv.Q(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle())
                    : Conv.Quat48(br.ReadUInt16(), br.ReadUInt16(), br.ReadUInt16());
            for (int t = 0; t < tracks; t++) a.Tracks.Add(new Track());
            if (version == 5)
            {
                br.BaseStream.Position = hashOff + 12;
                for (int t = 0; t < tracks; t++) a.Tracks[t].Hash = br.ReadUInt32();
            }
            br.BaseStream.Position = frameOff + 12;
            for (int f = 0; f < frames; f++)
            {
                float time = f * fd;
                for (int t = 0; t < tracks; t++)
                {
                    var tr = a.Tracks[t];
                    if (version == 4) tr.Hash = br.ReadUInt32();
                    int ti = br.ReadUInt16(), si = br.ReadUInt16(), ri = br.ReadUInt16();
                    if (version == 4) br.ReadUInt16();
                    var p = vecs[ti];
                    tr.Pos.Add(new KeyValuePair<float, Vector3>(time, Conv.P(p.x, p.y, p.z)));
                    tr.Scale.Add(new KeyValuePair<float, Vector3>(time, vecs[si]));
                    tr.Rot.Add(new KeyValuePair<float, Quaternion>(time, quats[ri]));
                }
            }
            return a;
        }

        static AnmData ReadCompressed(byte[] d)
        {
            var br = new BinaryReader(new MemoryStream(d)); br.BaseStream.Position = 12;
            br.ReadUInt32(); br.ReadUInt32(); br.ReadUInt32();
            int joints = br.ReadInt32(), frames = br.ReadInt32(); br.ReadInt32();
            float duration = br.ReadSingle(); br.ReadSingle();
            br.ReadBytes(24);
            Vector3 tMin = Conv.V3(br), tMax = Conv.V3(br), sMin = Conv.V3(br), sMax = Conv.V3(br);
            int frameOff = br.ReadInt32(); br.ReadInt32(); int hashOff = br.ReadInt32();
            var a = new AnmData { Duration = duration };
            br.BaseStream.Position = hashOff + 12;
            for (int j = 0; j < joints; j++) a.Tracks.Add(new Track { Hash = br.ReadUInt32() });
            br.BaseStream.Position = frameOff + 12;
            for (int f = 0; f < frames; f++)
            {
                float time = br.ReadUInt16() / 65535f * duration;
                int ji = br.ReadUInt16();
                ushort v0 = br.ReadUInt16(), v1 = br.ReadUInt16(), v2 = br.ReadUInt16();
                int joint = ji & 0x3FFF, type = ji >> 14;
                if (joint >= joints) continue;
                var tr = a.Tracks[joint];
                if (type == 0) tr.Rot.Add(new KeyValuePair<float, Quaternion>(time, Conv.Quat48(v0, v1, v2)));
                else
                {
                    bool isPos = type == 1;
                    var mn = isPos ? tMin : sMin; var mx = isPos ? tMax : sMax;
                    var v = new Vector3(mn.x + (mx.x - mn.x) * (v0 / 65535f), mn.y + (mx.y - mn.y) * (v1 / 65535f), mn.z + (mx.z - mn.z) * (v2 / 65535f));
                    if (isPos) tr.Pos.Add(new KeyValuePair<float, Vector3>(time, Conv.P(v.x, v.y, v.z)));
                    else tr.Scale.Add(new KeyValuePair<float, Vector3>(time, v));
                }
            }
            return a;
        }
    }

    internal sealed class TexData
    {
        public int Width, Height; public TextureFormat Format; public byte[] Top;

        public static TexData Read(byte[] d)
        {
            if (d[0] != 'T' || d[1] != 'E' || d[2] != 'X') throw new InvalidDataException("not a .tex");
            int w = BitConverter.ToUInt16(d, 4), h = BitConverter.ToUInt16(d, 6);
            byte fmt = d[9]; bool mips = (d[11] & 1) != 0;
            TextureFormat tf; int size;
            int bw = Math.Max(1, (w + 3) / 4), bh = Math.Max(1, (h + 3) / 4);
            switch (fmt)
            {
                case 10: tf = TextureFormat.DXT1; size = bw * bh * 8; break;
                case 12: tf = TextureFormat.DXT5; size = bw * bh * 16; break;
                case 20: tf = TextureFormat.BGRA32; size = w * h * 4; break;
                default: throw new InvalidDataException("unsupported texture format " + fmt);
            }
            // mip levels are stored smallest first, so the full-size image is the last block
            int at = mips ? d.Length - size : 12;
            var top = new byte[size];
            Buffer.BlockCopy(d, at, top, 0, size);
            return new TexData { Width = w, Height = h, Format = tf, Top = top };
        }
    }
}
