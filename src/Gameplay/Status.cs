using System.Collections.Generic;
using SlapRift.Champion;
using SlapRift.Generated;
using Smash;
using UnityEngine;

namespace SlapRift.Gameplay
{
    // Hook:status-tick
    // League status effects on any fighter (champion or not): bleed and burn damage over time, slows, stuns,
    // delayed echo damage, and the champion's own speed buffs. Speed changes go through Slap City's own
    // SetBonusRunSpeed / SetBonusStateSpeed, so moves, physics and CPUs all respect them.
    public sealed class Status : MonoBehaviour
    {
        SmashCharacter sc;
        public int BleedStacks; float bleedUntil, bleedRate; ChampionBody bleedFrom;
        float burnUntil, burnRate;
        float slowUntil, stunUntil, speedUntil, speedMult = 1f;
        internal ChampionBody LastHitBy; internal float LastHitTime = -99f;
        readonly List<KeyValuePair<float, float>> echoes = new List<KeyValuePair<float, float>>();
        ChampionBody echoFrom; string echoSfx;
        bool bonusApplied, wasDead;

        public static Status Of(SmashCharacter sc)
        {
            if (sc == null) return null;
            var s = sc.GetComponent<Status>();
            if (s == null) { s = sc.gameObject.AddComponent<Status>(); s.sc = sc; }
            return s;
        }

        float Now => Time.time;
        public bool Stunned => Now < stunUntil;
        public bool Bleeding => Now < bleedUntil && BleedStacks > 0;

        public void Slow(float seconds) => slowUntil = Mathf.Max(slowUntil, Now + seconds);
        public void Stun(float seconds) => stunUntil = Mathf.Max(stunUntil, Now + seconds);
        public void SpeedUp(float mult, float seconds) { speedMult = mult; speedUntil = Now + seconds; }
        public void Burn(float percentPerSecond, float seconds) { burnRate = percentPerSecond; burnUntil = Now + seconds; }

        // returns the stack count after adding
        public int AddBleed(ChampionBody from, float ratePerStack)
        {
            if (!Bleeding) BleedStacks = 0;
            BleedStacks = Mathf.Min(5, BleedStacks + 1);
            bleedRate = ratePerStack; bleedFrom = from; bleedUntil = Now + 5f;
            return BleedStacks;
        }

        public void AddEcho(ChampionBody from, float amount, float delay, string sfx)
        {
            echoFrom = from; echoSfx = sfx;
            echoes.Add(new KeyValuePair<float, float>(Now + delay, amount));
        }

        public void Cleanse() { BleedStacks = 0; bleedUntil = burnUntil = slowUntil = stunUntil = 0f; }

        void Update()
        {
            if (sc == null) return;
            bool dead = SmashCharacter.deadMoves.Contains(sc.CurrentMoveId);
            if (dead && !wasDead)
            {
                // Get Excited!: the champion who hit this fighter in the last 3 s speeds up
                if (LastHitBy != null && Now - LastHitTime < 3f && LastHitBy.Def.Passive.Effect == Effect.Excited)
                {
                    Of(LastHitBy.Character)?.SpeedUp(LastHitBy.Def.Passive.Value, 4f);
                    LastHitBy.Proc(LastHitBy.Def.Passive.Sfx);
                    Plugin.Log.LogInfo(LastHitBy.Def.Name + " Get Excited!");
                }
                Cleanse(); echoes.Clear(); LastHitBy = null;
            }
            wasDead = dead;
            if (!SmashGame.Instance.play) return;

            float dt = Time.deltaTime;
            if (!dead)
            {
                float dot = (Bleeding ? BleedStacks * bleedRate : 0f) + (Now < burnUntil ? burnRate : 0f);
                if (dot > 0f) { sc.damage += dot * dt; sc.ClampDamage(); }
                for (int i = echoes.Count - 1; i >= 0; i--)
                    if (Now >= echoes[i].Key)
                    {
                        sc.damage += echoes[i].Value; sc.ClampDamage();
                        if (echoFrom != null) echoFrom.Proc(echoSfx);
                        Plugin.Log.LogInfo($"echo {echoes[i].Value:F1}% on {sc.name}");
                        echoes.RemoveAt(i);
                    }
            }
            if (!Bleeding) BleedStacks = 0;

            float run = (Now < slowUntil ? 0.6f : 1f) * (Now < speedUntil ? speedMult : 1f);
            float state = 1f;
            if (Stunned) { run = 0f; state = 0.08f; }
            bool active = run != 1f || state != 1f;
            if (active || bonusApplied)
            {
                sc.SetBonusRunSpeed(run);
                sc.SetBonusStateSpeed(state);
                bonusApplied = active;
            }
        }
    }
}
