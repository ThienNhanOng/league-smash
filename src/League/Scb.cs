using System;
using System.IO;
using UnityEngine;

namespace SlapRift.League
{
    // League's static particle meshes (.scb, "r3d2Mesh" v2/v3): positions plus per-face corners with their own UVs.
    // Built straight into a Unity mesh, one vertex per face corner, mirrored on X like everything else.
    internal static class Scb
    {
        public static Mesh Read(byte[] d, string name)
        {
            var br = new BinaryReader(new MemoryStream(d));
            if (System.Text.Encoding.ASCII.GetString(br.ReadBytes(8)) != "r3d2Mesh") throw new InvalidDataException("not an .scb");
            int major = br.ReadUInt16(), minor = br.ReadUInt16();
            br.ReadBytes(128);
            int vc = br.ReadInt32(), fc = br.ReadInt32();
            uint flags = br.ReadUInt32();
            br.ReadBytes(24);
            int vtype = major == 3 && minor == 2 ? br.ReadInt32() : 0;
            var pos = new Vector3[vc];
            for (int i = 0; i < vc; i++) { var p = Conv.V3(br); pos[i] = Conv.P(p.x, p.y, p.z); }
            if (vtype == 1) br.ReadBytes(vc * 4);
            br.ReadBytes(12);
            var verts = new Vector3[fc * 3]; var uvs = new Vector2[fc * 3]; var tris = new int[fc * 3];
            for (int f = 0; f < fc; f++)
            {
                uint a = br.ReadUInt32(), b = br.ReadUInt32(), c = br.ReadUInt32();
                br.ReadBytes(64);
                float u0 = br.ReadSingle(), u1 = br.ReadSingle(), u2 = br.ReadSingle(), v0 = br.ReadSingle(), v1 = br.ReadSingle(), v2 = br.ReadSingle();
                int o = f * 3;
                verts[o] = pos[a]; verts[o + 1] = pos[b]; verts[o + 2] = pos[c];
                uvs[o] = new Vector2(u0, v0); uvs[o + 1] = new Vector2(u1, v1); uvs[o + 2] = new Vector2(u2, v2);
                tris[o] = o; tris[o + 1] = o + 2; tris[o + 2] = o + 1;   // mirrored, so reverse the winding
            }
            var m = new Mesh { name = name, vertices = verts, uv = uvs, triangles = tris };
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }
    }
}
