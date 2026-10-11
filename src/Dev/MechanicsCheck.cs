using System.Collections;
using System.Linq;
using SlapRift.Champion;
using SlapRift.Generated;
using SlapRift.Gameplay;
using Smash;
using UnityEngine;

namespace SlapRift.Dev
{
    // Development-only ("mechcheck" command): in a live match, runs League rules that need lucky timing from CPUs
    // (5-hit bleed, parry, echo, third-cast knock-up) through Mechanics with the real fighters and logs PASS/FAIL.
    internal static class MechanicsCheck
    {
        public static void Run() => Plugin.Instance.StartCoroutine(Go());

        public static void DamageWrite() => Plugin.Instance.StartCoroutine(DamageWriteGo());

        static IEnumerator DamageWriteGo()
        {
            Plugin.Log.LogInfo("mode " + (ModeSetup.SelectedMode != null ? ModeSetup.SelectedMode.GetType().Name : "-") + " settings " + ModeSettings.Instance.mode);
            foreach (var b in ChampionBody.Live)
                Plugin.Log.LogInfo($"{b.Def.Name} move={b.Character.CurrentMoveId} damage={b.Character.damage:F2} play={SmashGame.Instance.play} timeScale={Time.timeScale}");
            foreach (var b in ChampionBody.Live) b.Character.damage += 20f;
            yield return null;
            foreach (var b in ChampionBody.Live) Plugin.Log.LogInfo($"{b.Def.Name} next frame damage={b.Character.damage:F2}");
            yield return new WaitForSeconds(1f);
            foreach (var b in ChampionBody.Live) Plugin.Log.LogInfo($"{b.Def.Name} 1s later damage={b.Character.damage:F2} bleed={Status.Of(b.Character).BleedStacks}");
        }

        static ChampionBody Body(string id) => ChampionBody.Live.FirstOrDefault(b => b.Def.Id == id && b.Character != null);

        static void Log(bool ok, string what) => Plugin.Log.LogInfo((ok ? "PASS " : "FAIL ") + what);

        static bool Hit(ChampionBody atk, AbilityDef ab, ChampionBody vic, out AtkProp p)
        {
            p = new AtkProp { dmg = 10f, angle = 45f };
            var r = new Mechanics.HitResult { Victim = vic };
            var keep = atk.Active; atk.Active = ab;
            bool landed = Mechanics.Before(p, atk.Character.GUA, vic.Character.GUA, atk, ab, vic, vic.Character, r);
            atk.Active = keep;
            if (landed) Mechanics.After(r);
            return landed;
        }

        static IEnumerator Go()
        {
            var darius = Body("lol_darius"); var fiora = Body("lol_fiora"); var yone = Body("lol_yone"); var jinx = Body("lol_jinx");
            var gp = Body("lol_gangplank"); var yasuo = Body("lol_yasuo");
            if (gp != null && yasuo != null && darius != null && jinx != null)
            {
                // Gangplank: Remove Scurvy cleanses and heals; Trial by Fire empowers the first hit after 5 s
                Hit(darius, null, gp, out _);
                gp.Character.damage = 40f;
                Mechanics.OnCast(gp, gp.Def.Up);
                Log(!Status.Of(gp.Character).Bleeding && gp.Character.damage <= 32.01f, $"Remove Scurvy cleansed the bleed and healed to {gp.Character.damage:F0}%");
                gp.LastHitTime = Time.time - 10f;
                Hit(gp, null, jinx, out var pt);
                Log(pt.dmgmult >= 1.5f, $"Trial by Fire first hit x{pt.dmgmult:F2}");
                // Yasuo: Flow halves the next hit; Sweeping Blade ramps; third Steel Tempest knocks up
                yasuo.Flow = 1f;
                Hit(darius, null, yasuo, out var pf2);
                Log(pf2.dmgmult <= 0.51f && yasuo.Flow == 0f, $"Flow shield: hit taken x{pf2.dmgmult:F2}");
                yasuo.LastCastTime = -99f;
                for (int i = 0; i < 3; i++) { Mechanics.OnCast(yasuo, yasuo.Def.Side); yasuo.LastCastTime = Time.time; }
                Hit(yasuo, yasuo.Def.Side, jinx, out var pr2);
                Log(pr2.dmgmult >= 1.49f, $"Sweeping Blade third repeat x{pr2.dmgmult:F2}");
                yasuo.LastCastTime = -99f;
                for (int i = 0; i < 3; i++) { Mechanics.OnCast(yasuo, yasuo.Def.Neutral); yasuo.LastCastTime = Time.time; }
                Hit(yasuo, yasuo.Def.Neutral, jinx, out var pq);
                Log(yasuo.ThirdCast && Mathf.Approximately(pq.angle, 90f), "third Steel Tempest launches straight up");
                yield break;
            }
            if (darius == null || fiora == null || yone == null || jinx == null) { Plugin.Log.LogWarning("mechcheck needs Darius, Fiora, Yone and Jinx (or Gangplank, Yasuo, Darius and Jinx) in the match"); yield break; }

            // Darius: five hits stack Hemorrhage to 5 and grant Noxian Might; Guillotine then hits harder per stack
            for (int i = 0; i < 5; i++) Hit(darius, null, jinx, out _);
            var st = Status.Of(jinx.Character);
            Log(st.BleedStacks == 5, $"Hemorrhage stacks {st.BleedStacks}/5");
            Log(Time.time < darius.MightUntil, "Noxian Might active after 5 stacks");
            float before = jinx.Character.damage;
            yield return new WaitForSeconds(1f);
            Log(jinx.Character.damage > before + 1.5f, $"bleed damage over time {before:F2}% -> {jinx.Character.damage:F2}% (stacks {st.BleedStacks}, move {jinx.Character.CurrentMoveId})");
            Hit(darius, darius.Def.Down, jinx, out var pr);
            Log(pr.dmgmult > 1.9f, $"Noxian Guillotine on 5 stacks: damage x{pr.dmgmult:F2} (might x1.3, R x1.1, +20%/stack)");

            // Fiora: Riposte blocks the next hit and stuns the attacker
            Mechanics.OnCast(fiora, fiora.Def.Down);
            float fioraDmg = fiora.Character.damage;
            bool landed = Hit(darius, null, fiora, out _);
            Log(!landed && fiora.Character.damage == fioraDmg, "Riposte blocked Darius's hit");
            Log(Status.Of(darius.Character).Stunned, "Riposte stunned Darius");
            Mechanics.OnCast(fiora, fiora.Def.Up);
            Hit(fiora, null, jinx, out var pf);
            Log(pf.dmgmult >= 1.3f, $"Grand Challenge damage x{pf.dmgmult:F2}");

            // Yone: third Mortal Steel knocks up; Soul Unbound echoes damage 3 s later
            yone.LastCastTime = -99f;
            for (int i = 0; i < 3; i++) { Mechanics.OnCast(yone, yone.Def.Neutral); yone.LastCastTime = Time.time; yone.LastCastSlot = "neutral"; }
            Hit(yone, yone.Def.Neutral, jinx, out var pk);
            Log(yone.ThirdCast && Mathf.Approximately(pk.angle, 90f), $"third Mortal Steel launches straight up (angle {pk.angle}, kb x{pk.kbmult:F2})");
            Hit(yone, yone.Def.Side, jinx, out _);
            float pre = jinx.Character.damage;
            yield return new WaitForSeconds(3.3f);
            Log(jinx.Character.damage > pre + 1f, $"Soul Unbound echo {pre:F1}% -> {jinx.Character.damage:F1}%");

            // Jinx: Super Mega Death Rocket grows with the target's damage
            darius.Character.damage = 150f;
            Hit(jinx, jinx.Def.Down, darius, out var pj);
            Log(pj.dmgmult >= 1.9f, $"Death Rocket at 150%: damage x{pj.dmgmult:F2}");
            Status.Of(darius.Character).Cleanse();
        }
    }
}
