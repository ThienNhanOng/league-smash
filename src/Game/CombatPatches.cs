using HarmonyLib;
using SlapRift.Champion;
using SlapRift.Gameplay;
using Smash;
using UnityEngine;

namespace SlapRift.Game
{
    // Hook:hit-effects
    // Every hit Slap City registers on someone passes through AddHurt. A champion's hits get its passive, its buffs
    // and, during a special (or from a projectile it just cast), the ability's League mechanics (Gameplay/Mechanics).
    // The hitbox's properties are copied into the hit inside AddHurt, so they are changed just for this call.
    [HarmonyPatch(typeof(ResolveCombatBasic), "AddHurt")]
    static class HitEffectsPatch
    {
        internal struct Saved { public AtkProp Prop; public float Dmg, DmgMult, KbMult, Angle, MinStun; public Mechanics.HitResult Result; }

        static SmashCharacter CharacterOf(GameUpdateAgent a)
        {
            if (a == null) return null;
            if (a.IsPlayerCharacter) return a.Character;
            if (a.IsProjectile)
            {
                int i = a.PlayerIndex;
                if (i >= 0 && i < SmashLoader.Instance.NumberOfPlayers) return SmashLoader.Instance.GetPlayer(i).character;
            }
            return null;
        }

        // Hook:parry-block (returning false skips the hit entirely)
        static bool Prefix(HitBox hb, GameUpdateAgent attacker, GameUpdateAgent hurt, out Saved __state)
        {
            __state = default;
            if (!Champions.Enabled || hb == null || hb.prop == null) return true;
            var p = hb.prop;
            if (Dev.DevCommands.LogProps > 0) { Dev.DevCommands.LogProps--; Plugin.Log.LogInfo($"hitprop {(attacker != null && attacker.IsPlayerCharacter ? attacker.Character.name : "proj")} dmg {p.dmg} basekb {p.basekb} kbgain {p.kbgain} setkb {p.setkb} angle {p.angle} stun {p.stun} hitlag {p.hitlag} type {p.type}"); }
            __state = new Saved { Prop = p, Dmg = p.dmg, DmgMult = p.dmgmult, KbMult = p.kbmult, Angle = p.angle, MinStun = p.minstunframes };

            var atkSc = CharacterOf(attacker);
            var atk = atkSc != null ? atkSc.GetComponent<ChampionBody>() : null;
            var target = hurt != null && hurt.IsPlayerCharacter ? hurt.Character : null;
            var vic = target != null ? target.GetComponent<ChampionBody>() : null;
            if (atk == null && vic == null) return true;

            Generated.AbilityDef ab = null;
            if (atk != null)
            {
                ab = atk.Active;
                if (ab == null && attacker.IsProjectile && Time.time - atk.LastCastTime < 2.5f) ab = atk.LastCast;
            }
            var r = new Mechanics.HitResult { Victim = vic };
            if (!Mechanics.Before(p, attacker, hurt, atk, ab, vic, target, r))
            {
                Restore(__state);
                __state = default;
                return false;
            }
            __state.Result = r;
            return true;
        }

        static void Restore(Saved s)
        {
            var p = s.Prop;
            p.dmg = s.Dmg; p.dmgmult = s.DmgMult; p.kbmult = s.KbMult; p.angle = s.Angle; p.minstunframes = s.MinStun;
        }

        // Hook:hit-after
        static void Postfix(Saved __state)
        {
            if (__state.Prop == null) return;
            Restore(__state);
            if (__state.Result != null) Mechanics.After(__state.Result);
        }
    }
}
