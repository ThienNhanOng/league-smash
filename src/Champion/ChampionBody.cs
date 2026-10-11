using System.Collections.Generic;
using SlapRift.Audio;
using SlapRift.Generated;
using Smash;
using UnityEngine;

namespace SlapRift.Champion
{
    // Sits on a Slap City fighter that was picked as a League champion. The fighter keeps its own moveset,
    // physics and hitboxes; this draws the champion's League model over it and plays League animations,
    // sounds and voice lines that follow what the fighter is doing.
    public sealed class ChampionBody : MonoBehaviour
    {
        internal ChampionDef Def;
        ChampionAssets assets;
        SmashCharacter sc;
        GameObject model;
        Animation anim;
        SkinnedMeshRenderer smr;
        AudioSource voice, sfx;
        readonly List<Renderer> hidden = new List<Renderer>();
        readonly Dictionary<string, ClipRule> ruleCache = new Dictionary<string, ClipRule>();

        string lastMove = "", playing = "";
        Role lastRole = Role.Idle;
        internal AbilityDef Active;          // the special move in progress
        internal AbilityDef LastCast;        // for projectiles that land after the move ended
        internal float LastCastTime = -99f;
        internal float ShieldUntil = -1f, ShieldBlock;
        // League mechanics state (Gameplay/Mechanics)
        internal string LastCastSlot; internal int Repeat; internal bool ThirdCast;
        internal float ParryUntil = -1f, WindwallUntil = -1f, ChallengeUntil = -1f, ChallengeMult = 1f, MightUntil = -1f, LastHitTime = -99f;
        internal AbilityDef ParryAbility, WindwallAbility;
        internal int EmpowerHits, HitCount;
        internal readonly Dictionary<int, int> VitalCount = new Dictionary<int, int>();
        internal float Flow;
        Vector3 lastPos;
        internal SmashCharacter Character => sc;
        public static readonly List<ChampionBody> Live = new List<ChampionBody>();
        public int Casts, HitsLanded;

        internal void Init(ChampionDef def, SmashCharacter character)
        {
            Def = def; sc = character;
            assets = ChampionAssets.Get(def.Id);
            voice = gameObject.AddComponent<AudioSource>(); voice.spatialBlend = 0; voice.playOnAwake = false;
            sfx = gameObject.AddComponent<AudioSource>(); sfx.spatialBlend = 0; sfx.playOnAwake = false;
            if (!Live.Contains(this)) Live.Add(this);
        }

        void OnDestroy() { Live.Remove(this); if (model != null) Destroy(model); }

        void OnDisable() { if (model != null) model.SetActive(false); }

        void OnEnable() { if (model != null) model.SetActive(true); }

        public bool HasModel => model != null;
        internal Transform ModelTransform => model != null ? model.transform : null;

        bool TryCreateModel()
        {
            if (model != null) return true;
            if (assets == null || !assets.Parsed || sc.mainMesh == null) return false;
            var parent = sc.mainMesh.transform.parent;
            model = assets.Instantiate(null);
            model.transform.SetParent(parent, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(0f, Def.Yaw, 0f);
            float target = Def.Height > 0 ? Def.Height : 4f;
            float s = target / assets.MeshHeight / Mathf.Max(1e-4f, parent.lossyScale.y);
            model.transform.localScale = Vector3.one * s;
            anim = model.GetComponent<Animation>();
            smr = model.GetComponentInChildren<SkinnedMeshRenderer>();
            model.layer = sc.mainMesh.gameObject.layer;
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = model.layer;

            // hide the fighter's own body; its effects (trails, sparks, shields) stay
            foreach (var r in sc.GetComponentsInChildren<Renderer>(true))
                if (r is SkinnedMeshRenderer && r.transform.IsChildOf(parent) && !r.transform.IsChildOf(model.transform)
                    && r.sharedMaterial != null && r.sharedMaterial.shader.name.Contains("Toon"))
                { r.forceRenderingOff = true; hidden.Add(r); }
            Plugin.Log.LogInfo($"{Def.Name} model on {sc.name}: height {target:F1}, scale {s:F4}, clips {assets.Clips.Count}");
            return true;
        }

        // Slap City samples its clips itself (SmashAnimator) and disables them straight after, so read its play list:
        // the clip with the most weight right now, and how far into it the fighter is
        struct SlapClip { public string Name; public float Time, Length; }

        SlapClip CurrentSlapClip()
        {
            SmashAnimator.AnimPlaying best = null;
            float w = 0f;
            var list = sc.smashAnim != null ? sc.smashAnim.animsList : null;
            if (list != null)
                foreach (var a in list)
                    if (a.AnimState != null && a.currentWeight > w) { w = a.currentWeight; best = a; }
            if (best == null) return new SlapClip { Name = "idle" };
            return new SlapClip { Name = best.AnimState.name, Time = best.atTime, Length = best.AnimState.length };
        }

        ClipRule Rule(string clip)
        {
            if (ruleCache.TryGetValue(clip, out var r)) return r;
            foreach (var rule in Sheets.Clips)
            {
                var p = rule.Pattern;
                bool m = p == "*" || (p.StartsWith("*") ? clip.EndsWith(p.Substring(1)) : p.EndsWith("*") ? clip.StartsWith(p.Substring(0, p.Length - 1)) : p == clip);
                if (m) { r = rule; break; }
            }
            ruleCache[clip] = r;
            return r;
        }

        internal AbilityDef AbilityFor(string slot)
        {
            switch (slot) { case "neutral": return Def.Neutral; case "side": return Def.Side; case "up": return Def.Up; default: return Def.Down; }
        }

        // Hook:anim-driver
        void LateUpdate()
        {
            if (sc == null || !TryCreateModel()) return;
            foreach (var r in hidden) if (r != null) r.forceRenderingOff = true;
            // face the way the fighter faces, turned a little toward the camera like Slap City's own cast
            model.transform.rotation = Quaternion.LookRotation(new Vector3(sc.forwardDir, 0f, -0.45f), Vector3.up) * Quaternion.Euler(0f, Def.Yaw, 0f);
            smr.enabled = hidden.Count == 0 || hidden[0] == null || hidden[0].enabled;

            var st = CurrentSlapClip();
            string clip = st.Name;
            var rule = Rule(clip);
            AbilityDef ability = rule.Special != null ? AbilityFor(rule.Special) : null;
            Role role = ability != null ? ability.Role : rule.Role;

            // Hook:special-cast
            string move = sc.currentmoveid ?? "";
            if (move != lastMove)
            {
                lastMove = move;
                Active = ability;
                if (ability != null) Cast(ability);
            }
            if (ability == null) Active = null;

            // Way of the Wanderer: Flow fills while moving
            var pos = sc.characterPos != null ? sc.characterPos.position : sc.transform.position;
            if (Def.Passive.Effect == Effect.Flow && Flow < 1f)
            {
                Flow = Mathf.Min(1f, Flow + Mathf.Abs(pos.x - lastPos.x) / Def.Passive.Value);
                if (Flow >= 1f) Plugin.Log.LogInfo(Def.Name + " Flow full");
            }
            lastPos = pos;

            // Hook:ko-voice
            if (role == Role.Death && lastRole != Role.Death) Say(Def.VoKO);
            if (role == Role.Taunt && lastRole != Role.Taunt) Say(Def.VoTaunt);
            lastRole = role;

            string name = role.ToString();
            var state = anim[name];
            if (state == null) { name = Role.Idle.ToString(); state = anim[name]; if (state == null) return; }
            if (name != playing)
            {
                state.wrapMode = rule.Loop ? WrapMode.Loop : WrapMode.ClampForever;
                anim.CrossFade(name, 0.08f);
                playing = name;
            }
            if (rule.Sync && st.Length > 0)
            {
                state.speed = 0f;
                state.normalizedTime = Mathf.Clamp01(st.Time / st.Length);
            }
            else state.speed = rule.Speed;
        }

        void Cast(AbilityDef a)
        {
            Casts++;
            Gameplay.Mechanics.OnCast(this, a);
            Gameplay.LeagueObjects.OnCast(this, a);
            LastCast = a; LastCastTime = Time.time;
            Play(sfx, a.Sfx, 0.9f);
            Say(a.Vo);
            Plugin.Log.LogInfo($"{Def.Name} cast {a.Key} {a.Name}" + (ThirdCast ? " (third: knock-up)" : Repeat > 1 ? $" x{Repeat}" : ""));
            Dev.TestMode.Report("ability-cast");
        }

        AudioSource proc;
        internal void Proc(string ev)
        {
            if (string.IsNullOrEmpty(ev) || ev == "none") return;
            if (proc == null) { proc = gameObject.AddComponent<AudioSource>(); proc.spatialBlend = 0; proc.playOnAwake = false; }
            Play(proc, ev, 0.9f);
        }

        internal void Say(string ev) => Play(voice, ev, 1f);

        void Play(AudioSource src, string ev, float vol)
        {
            var clip = AudioLibrary.Pick(Def.Id, ev);
            if (clip == null) return;
            src.Stop();
            src.clip = clip;
            src.volume = vol * Mathf.Clamp01(SystemSettings.SFXVol <= 0 ? 0 : SystemSettings.SFXVol);
            src.Play();
            Dev.TestMode.Report("league-audio");
        }
    }
}
