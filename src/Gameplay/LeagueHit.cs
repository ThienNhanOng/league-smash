using System.Collections.Generic;
using SlapRift.Champion;
using Smash;
using UnityEngine;

namespace SlapRift.Gameplay
{
    // Hook:league-hit
    // A hit from something that is not a Slap City attack (a tornado, an exploding barrel): it is handed to the target as
    // the same hurt message Slap City's combat resolver sends, so knockback, hitstun, DI, sounds and the HUD all behave as usual.
    internal static class LeagueHit
    {
        public static bool Strike(ChampionBody from, SmashCharacter target, Vector2 source, float dmg, float basekb, float kbgain, float angle)
        {
            if (from == null || target == null || target == from.Character) return false;
            var tg = target.GUA; var ag = from.Character.GUA;
            if (tg == null || ag == null || SmashCharacter.deadMoves.Contains(target.CurrentMoveId)) return false;
            if (ag.AttackTeam >= 0 && ag.AttackTeam == tg.DefendTeam) return false;
            var hrt = target.hurtset != null && target.hurtset.HurtBoxCount > 0 ? target.hurtset.GetHurtBox(0) : null;
            if (hrt == null) return false;
            float dir = Mathf.Sign(tg.GetPosition().x - source.x);
            if (dir == 0f) dir = from.Character.forwardDir;
            var prop = new AtkProp { type = AtkProp.AtkType.Attack, dmg = dmg, basekb = basekb, kbgain = kbgain, angle = angle, stun = 1f, hitlag = 1f, reversible = false };
            var hit = new CombatFate.HitInWorld
            {
                attackerTeam = ag.AttackTeam, attackerPlayerIndex = ag.PlayerIndex, hurtTeam = tg.DefendTeam, hurtPlayerIndex = tg.PlayerIndex,
                hrtType = hrt.type, hrtArmor = hrt.TotalArmor, hrtPush = hrt.shieldpush,
                hitPosition = tg.GetCenter(), vFromAtkToHrt = tg.GetPosition() - source, atkDirection = dir,
                atkPosition = source, atkCenter = source, atkVelocity = Vector2.zero,
                hrtPosition = tg.GetPosition(), hrtCenter = tg.GetCenter(), hrtGrounded = tg.Grounded, hrtDamage = tg.Damage, hrtWeight = tg.Weight,
                hrtShieldHealth = target.ShieldStrength, atkIsProjectile = true, atkLevelOfProjectile = 1, hrt = hrt,
            };
            hit.prop.CopyFrom(prop);
            var st = Status.Of(target);
            st.LastHitBy = from; st.LastHitTime = Time.time;
            tg.Message(new Dictionary<string, object>
            {
                ["CmbRes"] = CombatFate.EndResult.Launched,
                ["listofhurts"] = new List<object> { hit },
            });
            Dev.TestMode.Report("ability-hit");
            return true;
        }
    }
}
