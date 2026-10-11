using System.Collections.Generic;
using System.Linq;
using SlapRift.Champion;
using SlapRift.Generated;
using SlapRift.League;
using Smash;
using UnityEngine;

namespace SlapRift.Gameplay
{
    // Hook:league-objects
    // What abilities put on the stage (sheets/objects.json): Yasuo's tornado and Wind Wall, Gangplank's barrels and
    // Yone's spirit tether. Meshes, models and textures are League's own, read from the player's install on first use.
    internal static class LeagueObjects
    {
        static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();

        internal static Texture2D Tex(ChampionDef c, string path)
        {
            if (!textures.TryGetValue(path, out var t))
                textures[path] = t = ChampionAssets.MakeTex(TexData.Read(LeagueFiles.Champion(c.Wad).Read(path)), path);
            return t;
        }

        // League's effect textures are grey wisps that its shaders colour in; for Slap City's bright stages they are
        // redrawn once as pure white with the original alpha, and the vertex tint gives the colour
        static readonly Dictionary<string, Texture2D> bright = new Dictionary<string, Texture2D>();
        internal static Texture2D Bright(ChampionDef c, string path, float alphaGain)
        {
            if (bright.TryGetValue(path, out var b)) return b;
            var src = Tex(c, path);
            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var prev = RenderTexture.active;
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            var t = new Texture2D(src.width, src.height, TextureFormat.RGBA32, true, true) { name = path + "_bright", wrapMode = TextureWrapMode.Repeat };
            t.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var px = t.GetPixels32();
            for (int i = 0; i < px.Length; i++)
            {
                // brightness and alpha both carry the wisp's shape in these textures
                float lum = (px[i].r + px[i].g + px[i].b) / (3f * 255f);
                float a = Mathf.Clamp01(px[i].a / 255f * Mathf.Lerp(0.4f, 1.2f, lum) * alphaGain);
                px[i] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            t.SetPixels32(px);
            t.Apply(true, true);
            bright[path] = t;
            return t;
        }

        internal static Mesh Scb(ChampionDef c, string path)
        {
            if (!meshes.TryGetValue(path, out var m))
                meshes[path] = m = League.Scb.Read(LeagueFiles.Champion(c.Wad).Read(path), path);
            return m;
        }

        // alpha-blended with a vertex-colour tint (Sprites/Default ships with every Unity build): additive light vanishes
        // against Slap City's bright skies, so League's white wind and blue flame are blended in instead.
        // Meshes get Tint(), lines get startColor/endColor.
        internal static Material Glow(Texture2D tex) => new Material(Shader.Find("Sprites/Default")) { mainTexture = tex };

        internal static void Tint(Mesh m, Color c)
        {
            var cols = new Color[m.vertexCount];
            for (int i = 0; i < cols.Length; i++) cols[i] = c;
            m.colors = cols;
        }

        internal static Vector3 Pos(SmashCharacter sc) => sc.characterPos != null ? sc.characterPos.position : sc.transform.position;

        internal static IEnumerable<SmashCharacter> Fighters()
        {
            for (int i = 0; i < SmashLoader.Instance.NumberOfPlayers; i++)
            {
                var p = SmashLoader.Instance.GetPlayer(i);
                if (p.active && p.character != null && p.character.gameObject.activeInHierarchy) yield return p.character;
            }
        }

        public static void OnCast(ChampionBody b, AbilityDef a)
        {
            foreach (var o in Sheets.Objects)
            {
                if (o.Champion != b.Def.Id || o.Slot != a.Slot || (o.Third && !b.ThirdCast)) continue;
                try { Spawn(b, o); }
                catch (System.Exception e) { Plugin.Log.LogError("could not spawn " + o.Id + ": " + e.Message); }
            }
        }

        // development: put one object in front of its champion and keep it there for a close look
        public static void DebugSpawn(string id)
        {
            var o = Sheets.Objects.FirstOrDefault(x => x.Id == id);
            var b = o != null ? ChampionBody.Live.FirstOrDefault(x => x.Def.Id == o.Champion && x.HasModel) : null;
            if (b == null) { Plugin.Log.LogWarning("spawn: no " + id + " or its champion is not in the match"); return; }
            var still = (ObjectDef)typeof(ObjectDef).GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(o, null);
            still.Life = 12f; still.Speed = 0f;
            Spawn(b, still);
        }

        // development: a wall placed on a live enemy projectile must remove it on its next update
        public static System.Collections.IEnumerator WallCheck()
        {
            var o = Sheets.Objects.First(x => x.Kind == "wall");
            var b = ChampionBody.Live.FirstOrDefault(x => x.Def.Id == o.Champion && x.HasModel);
            if (b == null) { Plugin.Log.LogWarning("wallcheck needs " + o.Champion); yield break; }
            float until = Time.time + 30f;
            while (Time.time < until)
            {
                var art = Object.FindObjectsOfType<SmashArticle>().FirstOrDefault(a => !(a is SmashCharacter) && a.GUA != null && a.GUA.IsProjectile && a.GUA.AttackTeam != b.Character.GUA.AttackTeam);
                if (art != null)
                {
                    var name = art.name;
                    var go = new GameObject("SlapRift_wallcheck");
                    var w = go.AddComponent<WindWall>();
                    w.Init(b, o);
                    var q = art.GUA.GetCenter();
                    go.transform.position = new Vector3(q.x, q.y - 1f, go.transform.position.z);
                    yield return null; yield return null;
                    bool gone = art == null || !art.gameObject.activeInHierarchy || !art.GUA.IsProjectile;
                    Plugin.Log.LogInfo((gone ? "PASS" : "FAIL") + " Wind Wall removed " + name);
                    Object.Destroy(go);
                    yield break;
                }
                yield return null;
            }
            Plugin.Log.LogInfo("FAIL wallcheck: no enemy projectile appeared in 30 s");
        }

        static void Spawn(ChampionBody b, ObjectDef o)
        {
            var go = new GameObject("SlapRift_" + o.Id);
            var stage = SmashLoader.Instance.stage.stage;
            if (stage != null) go.transform.SetParent(stage.transform.parent, true);
            LeagueObject obj;
            switch (o.Kind)
            {
                case "tornado": obj = go.AddComponent<Tornado>(); break;
                case "wall": obj = go.AddComponent<WindWall>(); break;
                case "barrel": obj = go.AddComponent<Barrel>(); break;
                default: obj = go.AddComponent<Spirit>(); break;
            }
            obj.Init(b, o);
            if (o.SfxSpawn != "none") b.Proc(o.SfxSpawn);
            var cam = SmashCameraSystem.Instance != null ? SmashCameraSystem.Instance.ActiveOverlayCamera() : null;
            var vp = cam != null ? cam.WorldToViewportPoint(go.transform.position) : Vector3.zero;
            Plugin.Log.LogInfo($"{b.Def.Name} spawned {o.Id} at viewport {vp.x:F2},{vp.y:F2}");
        }
    }

    internal abstract class LeagueObject : MonoBehaviour
    {
        protected ChampionBody owner; protected ObjectDef def; protected float born; protected float dir;
        protected SmashCharacter Owner => owner != null ? owner.Character : null;

        public virtual void Init(ChampionBody b, ObjectDef o)
        {
            owner = b; def = o; born = Time.time; dir = b.Character.forwardDir;
            var p = LeagueObjects.Pos(b.Character);
            transform.position = p + new Vector3(dir * o.Forward, 0f, 0f);
            Build();
        }

        protected abstract void Build();
        protected virtual void Tick() { }
        protected virtual void End() => Destroy(gameObject);

        void Update()
        {
            if (owner == null || Owner == null || !Owner.gameObject.activeInHierarchy) { Destroy(gameObject); return; }
            if (!SmashGame.Instance.play) return;
            if (Time.time - born > def.Life) { End(); return; }
            Tick();
        }
    }

    // Steel Tempest's third cast: a whirlwind that rolls forward and launches everyone it touches
    internal sealed class Tornado : LeagueObject
    {
        readonly HashSet<SmashCharacter> hit = new HashSet<SmashCharacter>();
        readonly List<Transform> rings = new List<Transform>();
        const int Rings = 7;

        // League's wind ring stacked into a funnel: narrow at the ground, wide at the top, each ring spinning
        protected override void Build()
        {
            var c = owner.Def;
            var mesh = LeagueObjects.Scb(c, def.Mesh);
            LeagueObjects.Tint(mesh, new Color(0.88f, 0.96f, 1f, 0.9f));
            var mat = LeagueObjects.Glow(LeagueObjects.Bright(c, def.Tex, 1.3f));
            float meshW = Mathf.Max(0.01f, mesh.bounds.size.x);
            for (int i = 0; i < Rings; i++)
            {
                float t = i / (float)(Rings - 1);
                var r = new GameObject("ring" + i).transform;
                r.SetParent(transform, false);
                r.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                r.gameObject.AddComponent<MeshRenderer>().sharedMaterial = mat;
                float diameter = def.Size * Mathf.Lerp(0.22f, 0.75f, t);
                float s = diameter / meshW;
                r.localScale = new Vector3(s, s * 0.6f, s);
                r.localPosition = new Vector3(0f, def.Size * t * 0.95f, 0f);
                r.localRotation = Quaternion.Euler(0f, i * 47f, 0f);
                rings.Add(r);
            }
        }

        protected override void Tick()
        {
            transform.position += new Vector3(dir * def.Speed * Time.deltaTime, 0f, 0f);
            for (int i = 0; i < rings.Count; i++)
            {
                rings[i].Rotate(0f, (700f + i * 90f) * Time.deltaTime, 0f, Space.Self);
                // the funnel sways a little as it travels
                var lp = rings[i].localPosition;
                rings[i].localPosition = new Vector3(Mathf.Sin(Time.time * 9f + i) * 0.08f * i, lp.y, lp.z);
            }
            Vector2 c = (Vector2)transform.position + new Vector2(0f, def.Size * 0.4f);
            foreach (var f in LeagueObjects.Fighters())
            {
                if (f == Owner || hit.Contains(f)) continue;
                if (Vector2.Distance(c, f.GUA.GetCenter()) > def.Radius + 0.6f) continue;
                if (LeagueHit.Strike(owner, f, (Vector2)transform.position - new Vector2(dir, 0f), def.Dmg, def.BaseKb, def.KbGain, def.Angle))
                {
                    hit.Add(f);
                    if (def.SfxHit != "none") owner.Proc(def.SfxHit);
                    Plugin.Log.LogInfo($"tornado launched {f.name}");
                }
            }
        }
    }

    // Wind Wall: a curtain of wind in front of Yasuo that deletes enemy projectiles flying into it
    internal sealed class WindWall : LeagueObject
    {
        Material mat; Mesh wallMesh;

        protected override void Build()
        {
            var c = owner.Def;
            float w = def.Radius * 2f, h = def.Size;
            var mesh = wallMesh = new Mesh
            {
                vertices = new[] { new Vector3(-w / 2, 0, 0), new Vector3(w / 2, 0, 0), new Vector3(-w / 2, h, 0), new Vector3(w / 2, h, 0) },
                uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) },
                triangles = new[] { 0, 2, 1, 1, 2, 3, 0, 1, 2, 1, 3, 2 },
            };
            // two crossed curtains so the wall reads from the side-on camera
            for (int i = 0; i < 2; i++)
            {
                var g = new GameObject("windwall" + i);
                g.transform.SetParent(transform, false);
                g.transform.localRotation = Quaternion.Euler(0f, i == 0 ? 0f : 90f, 0f);
                g.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = g.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat ?? (mat = LeagueObjects.Glow(LeagueObjects.Bright(c, def.Tex, 1.1f)));
            }
        }

        protected override void Tick()
        {
            mat.mainTextureOffset = new Vector2(0f, -Time.time * 1.2f);
            float fade = Mathf.Clamp01((def.Life - (Time.time - born)) * 2f);
            LeagueObjects.Tint(wallMesh, new Color(0.7f, 0.9f, 1f, fade));
            var p = transform.position;
            // any enemy projectile article (pills, boomerangs, bombs, mines...) entering the wall is removed
            foreach (var art in Object.FindObjectsOfType<SmashArticle>())
            {
                if (art is SmashCharacter || art.GUA == null || !art.GUA.IsProjectile) continue;
                if (art.GUA.AttackTeam >= 0 && art.GUA.AttackTeam == Owner.GUA.AttackTeam) continue;
                var q = art.GUA.GetCenter();
                if (Mathf.Abs(q.x - p.x) > def.Radius + 0.4f || q.y < p.y - 0.5f || q.y > p.y + def.Size) continue;
                if (art is SmashProjectile sp) sp.Deactivate(); else art.Stop();
                if (def.SfxHit != "none") owner.Proc(def.SfxHit);
                Plugin.Log.LogInfo("Wind Wall destroyed " + art.name);
                Dev.TestMode.Report("ability-hit");
            }
        }
    }

    // Powder Keg: Gangplank's barrel. It sits where he put it; when he attacks next to it, it blows up,
    // hitting everyone around it and setting off his other barrels nearby.
    internal sealed class Barrel : LeagueObject
    {
        static readonly List<Barrel> all = new List<Barrel>();
        Animation anim; bool exploding; float explodeAt;

        static ChampionAssets Model(ChampionDef gp, ObjectDef o)
        {
            var id = "lol_" + o.Model;
            var a = ChampionAssets.Get(id);
            if (a != null) return a;
            var mb = "assets/characters/" + o.Model + "/skins/base/";
            var d = new ChampionDef
            {
                Id = id, Name = o.Model, Wad = gp.Wad, Skn = mb + o.Model + ".skn", Skl = mb + o.Model + ".skl", Tex = o.Tex,
                TexBySubmesh = new Dictionary<string, string>(), HideSubmeshes = new string[0],
                Anims = new Dictionary<Role, string> { { Role.Idle, o.Model + "_idle1" }, { Role.Taunt, o.Model + "_spawn" }, { Role.Death, o.Model + "_death" } },
            };
            a = new ChampionAssets(d);
            a.Load();
            ChampionAssets.Register(a);
            return a;
        }

        protected override void Build()
        {
            var a = Model(owner.Def, def);
            var m = a.Instantiate(null);
            m.transform.SetParent(transform, false);
            m.transform.localScale = Vector3.one * (def.Size / a.MeshHeight);
            m.transform.localRotation = Quaternion.Euler(0f, 160f, 0f);
            anim = m.GetComponent<Animation>();
            if (anim["Taunt"] != null) { anim["Taunt"].wrapMode = WrapMode.Once; anim.Play("Taunt"); anim.CrossFadeQueued("Idle"); }
            if (anim["Idle"] != null) anim["Idle"].wrapMode = WrapMode.Loop;
            // keep three at most, like the oldest keg decaying
            all.RemoveAll(x => x == null);
            var mine = all.Where(x => x.owner == owner).ToList();
            if (mine.Count >= 3) Destroy(mine[0].gameObject);
            all.Add(this);
        }

        void OnDestroy() => all.Remove(this);

        protected override void Tick()
        {
            if (exploding) { if (Time.time >= explodeAt) Explode(); return; }
            var o = Owner;
            Vector3 op = LeagueObjects.Pos(o), bp = transform.position;
            bool facing = Mathf.Sign(bp.x - op.x) == o.forwardDir || Mathf.Abs(bp.x - op.x) < 0.6f;
            if (o.attack != null && o.attack.HitBoxCount > 0 && facing && Vector2.Distance(op, bp) < 2.8f) Ignite(0f);
        }

        void Ignite(float delay)
        {
            if (exploding) return;
            exploding = true; explodeAt = Time.time + delay;
        }

        void Explode()
        {
            if (def.SfxHit != "none") owner.Proc(def.SfxHit);
            int n = 0;
            foreach (var f in LeagueObjects.Fighters())
            {
                if (f == Owner || Vector2.Distance(transform.position, f.GUA.GetCenter()) > def.Radius) continue;
                if (LeagueHit.Strike(owner, f, transform.position, def.Dmg, def.BaseKb, def.KbGain, def.Angle))
                {
                    n++;
                    if (def.Slow > 0f) Status.Of(f).Slow(def.Slow);
                }
            }
            foreach (var other in all.ToList())
                if (other != null && other != this && other.owner == owner && Vector2.Distance(other.transform.position, transform.position) < def.Radius * 1.5f)
                    other.Ignite(0.15f);
            Plugin.Log.LogInfo($"barrel exploded, hit {n}");
            if (anim["Death"] != null) { anim["Death"].wrapMode = WrapMode.ClampForever; anim.Play("Death"); }
            enabled = false;
            Destroy(gameObject, 0.5f);
        }

        protected override void End()
        {
            if (anim != null && anim["Death"] != null) { anim["Death"].wrapMode = WrapMode.ClampForever; anim.Play("Death"); }
            enabled = false;
            Destroy(gameObject, 0.6f);
        }
    }

    // Soul Unbound: Yone's body stays where he cast E, joined to his spirit by a tether; when it ends he snaps back
    internal sealed class Spirit : LeagueObject
    {
        GameObject body; LineRenderer tether; Vector2 origin; Vector3 originWorld;

        protected override void Build()
        {
            var o = Owner;
            originWorld = LeagueObjects.Pos(o);
            transform.position = originWorld;
            origin = o.GUA.GetPosition();
            var live = owner.ModelTransform;
            var a = ChampionAssets.Get(owner.Def.Id);
            if (a != null && live != null)
            {
                body = a.Instantiate(null);
                body.transform.SetParent(transform, false);
                body.transform.rotation = live.rotation;
                body.transform.localScale = live.lossyScale;
                var an = body.GetComponent<Animation>();
                if (an["Idle"] != null) { an["Idle"].wrapMode = WrapMode.Loop; an.Play("Idle"); }
            }
            tether = gameObject.AddComponent<LineRenderer>();
            tether.positionCount = 2;
            tether.startWidth = tether.endWidth = def.Size;
            tether.sortingOrder = 10;
            tether.textureMode = LineTextureMode.Stretch;
            tether.material = LeagueObjects.Glow(LeagueObjects.Bright(owner.Def, def.Tex, 1.5f));
            tether.startColor = tether.endColor = new Color(0.55f, 0.85f, 1f, 1f);
            Tick();
        }

        protected override void Tick()
        {
            var up = new Vector3(0f, 1.6f, 0f);
            tether.SetPosition(0, originWorld + up);
            tether.SetPosition(1, LeagueObjects.Pos(Owner) + up);
            tether.material.mainTextureOffset = new Vector2(-Time.time * 2f, 0f);
        }

        protected override void End()
        {
            var o = Owner;
            if (!SmashCharacter.deadMoves.Contains(o.CurrentMoveId))
            {
                o._teleport_to = origin;
                o._teleport = true;
                if (def.SfxHit != "none") owner.Proc(def.SfxHit);
                Plugin.Log.LogInfo(owner.Def.Name + " snapped back to the body");
            }
            Destroy(gameObject);
        }
    }
}
