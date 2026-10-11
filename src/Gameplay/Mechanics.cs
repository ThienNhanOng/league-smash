using System.Collections.Generic;
using SlapRift.Champion;
using SlapRift.Generated;
using Smash;
using UnityEngine;

namespace SlapRift.Gameplay
{
    // Hook:mechanics
    // The League rules, one case per row of sheets/effects.json. Cast effects run when a special starts;
    // hit effects change a hit's properties before Slap City applies it (Before) and act after it (After).
    internal static class Mechanics
    {
        // what a hit does once Slap City has taken it
        internal sealed class HitResult
        {
            public ChampionBody Attacker, Victim; public SmashCharacter Target; public AbilityDef Ability;
            public float Heal, Slow, Stun, EchoAmount; public bool AddBleed, BurnTarget, Vital, EmpowerUsed, Proc, Challenge;
        }

        public static void OnCast(ChampionBody b, AbilityDef a)
        {
            var sc = b.Character;
            float now = Time.time;
            // repeat counter for Steel Tempest / Mortal Steel and Sweeping Blade
            bool sameAgain = b.LastCastSlot == a.Slot && now - b.LastCastTime < (a.Has(Effect.Ramp) ? 3f : 4f);
            b.Repeat = sameAgain ? b.Repeat + 1 : 1;
            b.LastCastSlot = a.Slot;
            b.ThirdCast = a.Has(Effect.ThirdKnockup) && b.Repeat % 3 == 0;
            if (b.ThirdCast) b.Proc(a.SfxProc);
            foreach (var u in a.Effects)
                switch (u.Effect)
                {
                    case Effect.Dash: sc.slideX += sc.forwardDir * 9f * u.Value; break;
                    case Effect.HealCast: sc.damage = Mathf.Max(0f, sc.damage - u.Value); break;
                    case Effect.Cleanse: Status.Of(sc).Cleanse(); break;
                    case Effect.Shield: b.ShieldUntil = now + 1.5f; b.ShieldBlock = u.Value / 100f; break;
                    case Effect.Parry: b.ParryUntil = now + u.Value; b.ParryAbility = a; break;
                    case Effect.Windwall: b.WindwallUntil = now + u.Value; b.WindwallAbility = a; break;
                    case Effect.Empower: b.EmpowerHits = (int)u.Value; break;
                    case Effect.Challenge: b.ChallengeUntil = now + 6f; b.ChallengeMult = u.Value; break;
                }
        }

        // false = Slap City never registers this hit (parried, or a projectile into Wind Wall)
        public static bool Before(AtkProp p, GameUpdateAgent attacker, GameUpdateAgent hurt, ChampionBody atk, AbilityDef ab, ChampionBody vic, SmashCharacter target, HitResult r)
        {
            float now = Time.time;
            if (vic != null && vic != atk)
            {
                if (now < vic.ParryUntil)
                {
                    var pa = vic.ParryAbility;
                    vic.ParryUntil = 0f;
                    vic.Proc(pa.SfxProc);
                    var foe = attacker != null && attacker.IsPlayerCharacter ? attacker.Character : (atk != null ? atk.Character : null);
                    if (foe != null)
                    {
                        Status.Of(foe).Stun(pa.Get(Effect.Stun));
                        foe.damage += 6f; foe.ClampDamage();
                    }
                    Plugin.Log.LogInfo(vic.Def.Name + " " + pa.Name + " parried " + (foe != null ? foe.name : "a hit"));
                    Dev.TestMode.Report("ability-hit");
                    return false;
                }
                if (now < vic.WindwallUntil && attacker != null && attacker.IsProjectile)
                {
                    vic.Proc(vic.WindwallAbility.SfxProc);
                    Plugin.Log.LogInfo(vic.Def.Name + " Wind Wall blocked a projectile");
                    return false;
                }
            }

            if (atk != null && target != null && atk.Character != target)
            {
                r.Attacker = atk; r.Target = target;
                var st = Status.Of(target);
                // passive
                var pas = atk.Def.Passive;
                switch (pas.Effect)
                {
                    case Effect.Hemorrhage: r.AddBleed = true; break;
                    case Effect.TrialByFire:
                        if (now - atk.LastHitTime > 5f) { p.dmgmult *= pas.Value; r.BurnTarget = true; }
                        break;
                    case Effect.Hunter: if (atk.HitCount % 2 == 1) p.dmgmult *= pas.Value; break;
                    case Effect.Vitals:
                    {
                        int id = target.GetInstanceID();
                        atk.VitalCount.TryGetValue(id, out var n);
                        atk.VitalCount[id] = ++n;
                        if (n % 4 == 0) { p.dmgmult *= 1.4f; r.Vital = true; }
                        break;
                    }
                }
                // buffs
                if (now < atk.MightUntil) p.dmgmult *= 1.3f;
                if (now < atk.ChallengeUntil) { p.dmgmult *= atk.ChallengeMult; r.Challenge = true; }
                if (atk.EmpowerHits > 0) { p.dmgmult *= 1.4f; r.EmpowerUsed = true; }

                if (ab != null)
                {
                    r.Ability = ab;
                    p.dmgmult *= ab.Dmg;
                    p.kbmult *= ab.Kb;
                    foreach (var u in ab.Effects)
                        switch (u.Effect)
                        {
                            case Effect.OuterEdge:
                                if (attacker != null && hurt != null && Vector2.Distance(attacker.GetPosition(), hurt.GetPosition()) > u.Value)
                                { p.dmgmult *= 1.5f; r.Heal += 4f; r.Proc = true; }
                                break;
                            case Effect.Pull:
                                if (p.angle <= 360f) p.angle = 180f - p.angle;
                                p.kbmult *= Mathf.Lerp(1f, 0.7f, Mathf.Clamp01(u.Value));
                                r.Proc = true;
                                break;
                            case Effect.Slow: r.Slow = Mathf.Max(r.Slow, u.Value); break;
                            case Effect.Stun: if (!ab.Has(Effect.Parry)) r.Stun = Mathf.Max(r.Stun, u.Value); break;
                            case Effect.AirborneStun: if (!hurt.Grounded) r.Stun = Mathf.Max(r.Stun, u.Value); break;
                            case Effect.Execute: if (hurt.Damage >= u.Value) { p.kbmult *= 2f; r.Proc = true; } break;
                            case Effect.BleedExecute: if (st.Bleeding) { p.dmgmult *= 1f + u.Value * st.BleedStacks; r.Proc = true; } break;
                            case Effect.Airborne: if (!hurt.Grounded) p.dmgmult *= u.Value; break;
                            case Effect.Crit: if (Random.value < u.Value) { p.dmgmult *= 1.6f; r.Proc = true; } break;
                            case Effect.ThirdKnockup: if (atk.ThirdCast) { p.angle = 90f; p.kbmult *= u.Value; } break;
                            case Effect.Ramp: p.dmgmult *= 1f + u.Value * Mathf.Min(4, atk.Repeat - 1); break;
                            case Effect.MissingHealth: p.dmgmult *= 1f + hurt.Damage / u.Value; break;
                            case Effect.Echo: r.EchoAmount = p.dmgtot * u.Value / 100f; break;
                        }
                }
            }

            // what the victim takes is cut by its own shields
            if (vic != null && vic != atk)
            {
                if (now < vic.ShieldUntil) p.dmgmult *= 1f - vic.ShieldBlock;
                if (vic.Def.Passive.Effect == Effect.Flow && vic.Flow >= 1f)
                {
                    p.dmgmult *= 0.5f; vic.Flow = 0f;
                    Plugin.Log.LogInfo(vic.Def.Name + " Flow shield absorbed half a hit");
                }
            }
            return true;
        }

        public static void After(HitResult r)
        {
            var atk = r.Attacker;
            if (atk == null) return;
            var sc = atk.Character;
            var st = Status.Of(r.Target);
            st.LastHitBy = atk; st.LastHitTime = Time.time;
            atk.LastHitTime = Time.time;
            atk.HitCount++;
            var pas = atk.Def.Passive;
            if (r.AddBleed && st.AddBleed(atk, pas.Value) == 5 && Time.time >= atk.MightUntil)
            {
                atk.MightUntil = Time.time + 5f;
                atk.Proc(pas.Sfx);
                Plugin.Log.LogInfo(atk.Def.Name + " Noxian Might (5 bleed stacks)");
            }
            if (r.BurnTarget)
            {
                st.Burn(1f, 3f);
                Status.Of(sc).SpeedUp(1.3f, 2f);
                atk.Proc(pas.Sfx);
                Plugin.Log.LogInfo(atk.Def.Name + " Trial by Fire");
            }
            if (r.Vital) { r.Heal += pas.Value; atk.Proc(pas.Sfx); Plugin.Log.LogInfo(atk.Def.Name + " struck a vital"); }
            if (r.Challenge) r.Heal += 3f;
            if (r.EmpowerUsed) atk.EmpowerHits--;
            if (r.Slow > 0f) st.Slow(r.Slow);
            if (r.Stun > 0f) st.Stun(r.Stun);
            if (r.EchoAmount > 0f) st.AddEcho(atk, r.EchoAmount, 3f, r.Ability.SfxProc);
            if (r.Heal > 0f) sc.damage = Mathf.Max(0f, sc.damage - r.Heal);
            if (r.Ability != null)
            {
                if (r.Proc || (r.Challenge && r.Ability.Has(Effect.Challenge))) atk.Proc(r.Ability.SfxProc);
                if (atk.HitsLanded++ < 60) Plugin.Log.LogInfo($"{atk.Def.Name} {r.Ability.Key} {r.Ability.Name} hit{(r.Stun > 0 ? " stun" : "")}{(r.Slow > 0 ? " slow" : "")}{(r.Proc ? " proc" : "")}");
                Dev.TestMode.Report("ability-hit");
            }
        }
    }
}
