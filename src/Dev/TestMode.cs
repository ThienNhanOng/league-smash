using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SlapRift.Champion;
using SlapRift.Game;
using SlapRift.Generated;
using Smash;
using UnityEngine;

namespace SlapRift.Dev
{
    // Hook:test-mode
    // While Melty runs a test of this mashup (%LOCALAPPDATA%\Melty\test\<slug>.json exists), the game skips the
    // menus into a local four-champion CPU match and reports each check to the file Melty names.
    internal static class TestMode
    {
        public static readonly string[] Checks = { "league-found", "champion-spawned", "champion-model", "ability-cast", "ability-hit", "league-audio" };
        static string checksFile;
        static int seed;
        static bool started, matchStarted;
        static float startAt = -1f;
        static readonly HashSet<string> reported = new HashSet<string>();
        public static bool Active => checksFile != null;

        public static void Init()
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Melty", "test");
            if (!Directory.Exists(dir)) return;
            foreach (var f in Directory.GetFiles(dir, "*.json"))
            {
                var text = File.ReadAllText(f);
                var m = Regex.Match(text, "\"checks\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
                if (!m.Success) continue;
                checksFile = Regex.Unescape(m.Groups[1].Value);
                var s = Regex.Match(text, "\"seed\"\\s*:\\s*(-?\\d+)");
                if (s.Success) int.TryParse(s.Groups[1].Value, out seed);
                Plugin.Log.LogInfo("Melty test run: checks go to " + checksFile);
                break;
            }
        }

        public static void Report(string check, bool pass = true, string why = null)
        {
            if (checksFile == null || reported.Contains(check) || !Checks.Contains(check)) return;
            reported.Add(check);
            try { File.AppendAllText(checksFile, (pass ? "pass " : "fail ") + check + (pass ? "" : ": " + why) + "\n"); }
            catch (Exception e) { Plugin.Log.LogWarning("could not write test check: " + e.Message); }
        }

        public static void Poll()
        {
            if (checksFile == null && !forced) return;
            if (!started)
            {
                if (startAt < 0) startAt = Time.unscaledTime + 8f;
                if (Time.unscaledTime < startAt || !Sheets.Champions.All(c => ChampionAssets.Get(c.Id)?.Parsed == true)) return;
                started = true;
                StartMatch();
            }
            if (matchStarted && ChampionBody.Live.Any(b => b.HasModel)) Report("champion-model");
            if (matchStarted) Drive();
        }

        // CPUs favour some specials; in a test run each champion is also put into each of its four specials in turn
        static float nextCast; static int castTurn;
        static readonly string[] Slots = { "neutral", "side", "up", "down" };
        static void Drive()
        {
            if (Time.time < nextCast || !SmashGame.Instance.play) return;
            nextCast = Time.time + 1.6f;
            var live = ChampionBody.Live.Where(b => b.HasModel && b.Character != null && !SmashCharacter.deadMoves.Contains(b.Character.CurrentMoveId)).ToList();
            if (live.Count == 0) return;
            var b0 = live[castTurn % live.Count];
            var slot = Slots[(castTurn / live.Count) % 4];
            castTurn++;
            // let a test run see the third-cast knock-up (Steel Tempest's tornado) without three casts in a row
            var ab = b0.AbilityFor(slot);
            if (ab.Has(Effect.ThirdKnockup) && castTurn % 2 == 0) { b0.LastCastSlot = slot; b0.LastCastTime = Time.time; b0.Repeat = 2; }
            if (b0.Def.SpecialMoves.TryGetValue(slot, out var move)) b0.Character.CancelToMove(move);
        }

        static System.Collections.IEnumerator RestoreMode(ModeSettings.Mode keep)
        {
            yield return new WaitForSeconds(5f);
            ModeSettings.Instance.mode = keep;
            ModeSettings.Instance.SaveVersusSettings();
        }

        static bool forced;
        static string[] forcedPicks;
        public static void Force(string picks) { forced = true; started = false; startAt = Time.unscaledTime; forcedPicks = string.IsNullOrEmpty(picks) ? null : picks.Split(','); }

        // a local CPU stock match with four champions, the way the stage select starts one
        static void StartMatch()
        {
            var rnd = new System.Random(seed);
            var picks = forcedPicks != null ? forcedPicks.Select(id => Sheets.Champions.First(c => c.Id == id.Trim())).ToList() : Sheets.Champions.OrderBy(_ => rnd.Next()).Take(4).ToList();
            ModeSetup.SetMode(online: false, 2);
            TeamsIndex.SetupTeams(0, 1, 2, 3);
            for (int i = 0; i < Math.Max(4, SmashLoader.Instance.NumberOfPlayers); i++)
            {
                var p = SmashLoader.Instance.GetPlayer(i);
                p.Deactivate();
                p.active = i < 4;
                if (!p.active) continue;
                p.loadCharacter = picks[i].Base;
                p.skin = picks[i].Id;
                p.cpu = true;
                p.cpulevel = 7;
                p.input = ComputerController.GetCPUCtrl(i);
                p.suit = 0;
                p.compacttag = string.Empty;
            }
            var stages = SmashResources.Instance.stages.loadable;
            // Flatville: a regular battle stage (some registry entries, like thebox, are not playable)
            SmashLoader.Instance.stage.loadStage = stages.Any(s => s.id == "flatville") ? "flatville" : stages[0].id;
            TransitionScreen.RequestTransition(resume =>
            {
                GameManager.switchToState = GameManager.MacroState.Load;
                // a normal stock battle (training keeps resetting damage); the player's saved mode is put back afterwards
                var ms = ModeSettings.Instance; var keep = ms.mode;
                ms.mode = ModeSettings.Mode.Stock;
                SSStartLoader.LoadAndStartGame();
                Plugin.Log.LogInfo("test match mode " + ModeSetup.SelectedMode.GetType().Name + " (saved mode " + keep + ")");
                Plugin.Instance.StartCoroutine(RestoreMode(keep));
                resume(true);
            });
            matchStarted = true;
            Plugin.Log.LogInfo("Test match: " + string.Join(", ", picks.Select(p => p.Name)) + " on " + SmashLoader.Instance.stage.loadStage);
        }
    }
}
