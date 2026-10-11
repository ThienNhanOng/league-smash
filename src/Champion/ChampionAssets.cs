using System;
using System.Collections.Generic;
using System.Linq;
using SlapRift.Generated;
using SlapRift.League;
using UnityEngine;

namespace SlapRift.Champion
{
    // One champion's League content, read from the player's install: parsed on a worker thread (Load),
    // turned into Unity objects on the main thread the first time it is needed (Build).
    internal sealed class ChampionAssets
    {
        public readonly ChampionDef Def;
        public volatile bool Parsed;
        public string Error;

        SklData skl; SknData skn;
        readonly Dictionary<string, TexData> texData = new Dictionary<string, TexData>();
        readonly Dictionary<Role, AnmData> anmData = new Dictionary<Role, AnmData>();
        TexData iconData, portraitData;

        Texture2D icon, portrait;
        Mesh mesh; Dictionary<string, Texture2D> textures; List<string> subTex;
        public readonly Dictionary<Role, AnimationClip> Clips = new Dictionary<Role, AnimationClip>();
        string[] bonePaths; Matrix4x4[] bindposes;
        public float MeshHeight;
        bool built;

        public ChampionAssets(ChampionDef def) { Def = def; }

        static readonly Dictionary<string, ChampionAssets> all = new Dictionary<string, ChampionAssets>();
        public static ChampionAssets Get(string id) { lock (all) { all.TryGetValue(id, out var a); return a; } }
        public static void Register(ChampionAssets a) { lock (all) all[a.Def.Id] = a; }

        // worker thread: the cheap HUD art first so the select screen fills quickly
        public void LoadArt()
        {
            var w = LeagueFiles.Champion(Def.Wad);
            iconData = TexData.Read(w.Read(Def.Icon));
            portraitData = TexData.Read(w.Read(Def.Portrait));
        }

        public void Load()
        {
            try
            {
                var w = LeagueFiles.Champion(Def.Wad);
                skl = SklData.Read(w.Read(Def.Skl));
                skn = SknData.Read(w.Read(Def.Skn));
                foreach (var t in new[] { Def.Tex }.Concat(Def.TexBySubmesh.Values).Distinct())
                    texData[t] = TexData.Read(w.Read(t));
                foreach (var kv in Def.Anims)
                {
                    try { anmData[kv.Key] = AnmData.Read(w.Read(Def.AnimPath(kv.Key))); }
                    catch (Exception e) { Plugin.Log.LogWarning(Def.Name + " " + kv.Value + ": " + e.Message); }
                }
                Parsed = true;
            }
            catch (Exception e) { Error = e.Message; Plugin.Log.LogError(Def.Name + " could not be read from League: " + e); }
        }

        internal static Texture2D MakeTex(TexData t, string name)
        {
            if (t == null) return null;
            var tex = new Texture2D(t.Width, t.Height, t.Format, false) { name = name, wrapMode = TextureWrapMode.Clamp };
            tex.LoadRawTextureData(t.Top);
            tex.Apply(false, true);
            return tex;
        }

        public Texture2D Icon => icon ?? (icon = MakeTex(iconData, Def.Id + "_icon"));
        public Texture2D Portrait => portrait ?? (portrait = MakeTex(portraitData, Def.Id + "_portrait"));
        public bool ArtReady => iconData != null && portraitData != null;

        void Build()
        {
            if (built) return;
            built = true;
            textures = texData.ToDictionary(kv => kv.Key, kv => MakeTex(kv.Value, kv.Key));

            int n = skl.Names.Length;
            bonePaths = new string[n];
            for (int i = 0; i < n; i++) bonePaths[i] = BonePath(i);
            // bind pose: world matrices from the local transforms, relative to the skeleton root
            var world = new Matrix4x4[n];
            for (int i = 0; i < n; i++) world[i] = WorldOf(i, world);
            bindposes = world.Select(m => m.inverse).ToArray();

            // mesh: shown submeshes only, one material slot each
            var shown = skn.Subs.Where(s => !Def.HideSubmeshes.Contains(s.Name)).ToList();
            mesh = new Mesh { name = Def.Id, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.vertices = skn.Pos; mesh.normals = skn.Nrm; mesh.uv = skn.Uv;
            var bw = new BoneWeight[skn.Pos.Length];
            for (int v = 0; v < bw.Length; v++)
            {
                int J(int k) { int inf = skn.Bones[v * 4 + k]; return inf < skl.Influences.Length ? skl.Influences[inf] : 0; }
                float sum = skn.Weights[v * 4] + skn.Weights[v * 4 + 1] + skn.Weights[v * 4 + 2] + skn.Weights[v * 4 + 3];
                if (sum <= 0) sum = 1;
                bw[v] = new BoneWeight
                {
                    boneIndex0 = J(0), weight0 = skn.Weights[v * 4] / sum, boneIndex1 = J(1), weight1 = skn.Weights[v * 4 + 1] / sum,
                    boneIndex2 = J(2), weight2 = skn.Weights[v * 4 + 2] / sum, boneIndex3 = J(3), weight3 = skn.Weights[v * 4 + 3] / sum
                };
            }
            mesh.boneWeights = bw;
            mesh.bindposes = bindposes;
            mesh.subMeshCount = shown.Count;
            subTex = new List<string>();
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int s = 0; s < shown.Count; s++)
            {
                var sub = shown[s];
                var tri = new int[sub.IndexCount];
                // mirroring X flips handedness, so reverse each triangle's winding
                for (int i = 0; i + 2 < sub.IndexCount; i += 3)
                {
                    tri[i] = skn.Indices[sub.StartIndex + i];
                    tri[i + 1] = skn.Indices[sub.StartIndex + i + 2];
                    tri[i + 2] = skn.Indices[sub.StartIndex + i + 1];
                }
                foreach (var ix in tri) { var y = skn.Pos[ix].y; if (y < minY) minY = y; if (y > maxY) maxY = y; }
                mesh.SetTriangles(tri, s);
                subTex.Add(Def.TexBySubmesh.TryGetValue(sub.Name, out var t) ? t : Def.Tex);
            }
            MeshHeight = Mathf.Max(1f, maxY - Mathf.Min(0f, minY));
            mesh.RecalculateBounds();

            foreach (var kv in anmData) Clips[kv.Key] = MakeClip(kv.Key, kv.Value);
            skn = null; texData.Clear(); anmData.Clear();
        }

        Matrix4x4 WorldOf(int i, Matrix4x4[] done)
        {
            var local = Matrix4x4.TRS(skl.Pos[i], skl.Rot[i], skl.Scale[i]);
            int p = skl.Parents[i];
            return p >= 0 && p < i ? done[p] * local : (p >= 0 ? WorldOf(p, done) * local : local);
        }

        string BonePath(int i)
        {
            var parts = new List<string>();
            for (int j = i; j >= 0; j = skl.Parents[j]) parts.Insert(0, skl.Names[j]);
            return "skeleton/" + string.Join("/", parts);
        }

        AnimationClip MakeClip(Role role, AnmData a)
        {
            var clip = new AnimationClip { name = role.ToString(), legacy = true, wrapMode = WrapMode.Loop };
            var byHash = new Dictionary<uint, AnmData.Track>();
            foreach (var t in a.Tracks) byHash[t.Hash] = t;
            for (int j = 0; j < skl.Names.Length; j++)
            {
                AnmData.Track tr = null;
                if (!byHash.TryGetValue(skl.Hashes[j], out tr)) byHash.TryGetValue(Conv.Elf(skl.Names[j]), out tr);
                var path = bonePaths[j];
                var rot = tr != null && tr.Rot.Count > 0 ? tr.Rot : new List<KeyValuePair<float, Quaternion>> { new KeyValuePair<float, Quaternion>(0, skl.Rot[j]) };
                var pos = tr != null && tr.Pos.Count > 0 ? tr.Pos : new List<KeyValuePair<float, Vector3>> { new KeyValuePair<float, Vector3>(0, skl.Pos[j]) };
                var scl = tr != null && tr.Scale.Count > 0 ? tr.Scale : new List<KeyValuePair<float, Vector3>> { new KeyValuePair<float, Vector3>(0, skl.Scale[j]) };
                SetQuat(clip, path, rot);
                SetVec(clip, path, "localPosition", pos);
                SetVec(clip, path, "localScale", scl);
            }
            return clip;
        }

        static void SetVec(AnimationClip c, string path, string prop, List<KeyValuePair<float, Vector3>> keys)
        {
            var x = new Keyframe[keys.Count]; var y = new Keyframe[keys.Count]; var z = new Keyframe[keys.Count];
            for (int i = 0; i < keys.Count; i++)
            {
                x[i] = new Keyframe(keys[i].Key, keys[i].Value.x); y[i] = new Keyframe(keys[i].Key, keys[i].Value.y); z[i] = new Keyframe(keys[i].Key, keys[i].Value.z);
            }
            c.SetCurve(path, typeof(Transform), prop + ".x", Linear(x));
            c.SetCurve(path, typeof(Transform), prop + ".y", Linear(y));
            c.SetCurve(path, typeof(Transform), prop + ".z", Linear(z));
        }

        static void SetQuat(AnimationClip c, string path, List<KeyValuePair<float, Quaternion>> keys)
        {
            var k = new Keyframe[4][];
            for (int a = 0; a < 4; a++) k[a] = new Keyframe[keys.Count];
            Quaternion prev = keys[0].Value;
            for (int i = 0; i < keys.Count; i++)
            {
                var q = keys[i].Value;
                if (Quaternion.Dot(prev, q) < 0) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                prev = q;
                k[0][i] = new Keyframe(keys[i].Key, q.x); k[1][i] = new Keyframe(keys[i].Key, q.y);
                k[2][i] = new Keyframe(keys[i].Key, q.z); k[3][i] = new Keyframe(keys[i].Key, q.w);
            }
            c.SetCurve(path, typeof(Transform), "localRotation.x", Linear(k[0]));
            c.SetCurve(path, typeof(Transform), "localRotation.y", Linear(k[1]));
            c.SetCurve(path, typeof(Transform), "localRotation.z", Linear(k[2]));
            c.SetCurve(path, typeof(Transform), "localRotation.w", Linear(k[3]));
        }

        static AnimationCurve Linear(Keyframe[] keys)
        {
            // keys can repeat a time in compressed files; keep the last value at each time
            var list = new List<Keyframe>();
            foreach (var key in keys.OrderBy(k => k.time))
            {
                if (list.Count > 0 && Mathf.Approximately(list[list.Count - 1].time, key.time)) list[list.Count - 1] = key;
                else list.Add(key);
            }
            var curve = new AnimationCurve(list.ToArray());
            for (int i = 0; i < curve.length; i++)
            {
                var kf = curve[i];
                float inT = i > 0 ? (kf.value - curve[i - 1].value) / Mathf.Max(1e-5f, kf.time - curve[i - 1].time) : 0;
                float outT = i < curve.length - 1 ? (curve[i + 1].value - kf.value) / Mathf.Max(1e-5f, curve[i + 1].time - kf.time) : 0;
                kf.inTangent = inT; kf.outTangent = outT;
                curve.MoveKey(i, kf);
            }
            return curve;
        }

        // A fresh copy of the champion for one fighter: bones, skinned mesh and its animations.
        public GameObject Instantiate(Material baseMaterial)
        {
            Build();
            var root = new GameObject("SlapRift_" + Def.Name);
            var skel = new GameObject("skeleton").transform;
            skel.SetParent(root.transform, false);
            var bones = new Transform[skl.Names.Length];
            for (int i = 0; i < bones.Length; i++) bones[i] = new GameObject(skl.Names[i]).transform;
            for (int i = 0; i < bones.Length; i++)
            {
                int p = skl.Parents[i];
                bones[i].SetParent(p >= 0 ? bones[p] : skel, false);
                bones[i].localPosition = skl.Pos[i]; bones[i].localRotation = skl.Rot[i]; bones[i].localScale = skl.Scale[i];
            }
            var body = new GameObject("body");
            body.transform.SetParent(root.transform, false);
            var smr = body.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = bones;
            smr.rootBone = skel;
            smr.updateWhenOffscreen = true;
            smr.materials = subTex.Select(t =>
            {
                // League textures are hand-painted with their lighting baked in, so they are drawn unlit
                var m = baseMaterial != null ? new Material(baseMaterial) : new Material(Shader.Find("Unlit/Texture") ?? Shader.Find("Standard"));
                m.mainTexture = textures[t];
                if (m.HasProperty("_Color")) m.color = Color.white;
                return m;
            }).ToArray();
            var anim = root.AddComponent<Animation>();
            anim.cullingType = AnimationCullingType.AlwaysAnimate;
            foreach (var kv in Clips) anim.AddClip(kv.Value, kv.Key.ToString());
            return root;
        }
    }
}
