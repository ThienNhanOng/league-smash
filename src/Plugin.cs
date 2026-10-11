using System;
using System.IO;
using System.Linq;
using System.Threading;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using SlapRift.Audio;
using SlapRift.Champion;
using SlapRift.Generated;
using SlapRift.League;
using UnityEngine;

namespace SlapRift
{
    [BepInPlugin("gg.melty.slaprift", "Slap Rift", "0.2.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static Plugin Instance;
        internal static volatile bool LeagueReady;
        internal static string Dir;
        // native helpers live outside plugins/ so BepInEx does not try to load them as plugins
        internal static string NativeDir => Path.Combine(Paths.BepInExRootPath, "SlapRift");

        void Awake()
        {
            Log = Logger; Instance = this;
            Dir = Path.GetDirectoryName(Info.Location);
            Dev.TestMode.Init();
            try { Zstd.Load(NativeDir); }
            catch (Exception e) { Log.LogError(e.Message); return; }
            if (!LeagueFiles.Find()) { Log.LogError(LeagueFiles.Problem); Dev.TestMode.Report("league-found", false, LeagueFiles.Problem); return; }
            Log.LogInfo("League of Legends found at " + LeagueFiles.Root);
            LeagueReady = true;
            Dev.TestMode.Report("league-found");
            foreach (var c in Sheets.Champions) ChampionAssets.Register(new ChampionAssets(c));
            new Thread(PrepareAll) { IsBackground = true, Name = "SlapRift League reader" }.Start();
            new Harmony("gg.melty.slaprift").PatchAll();
            Log.LogInfo("Slap Rift loaded: " + string.Join(", ", Sheets.Champions.Select(c => c.Name)));
        }

        // worker thread: icons and portraits first, then models and animations, then audio
        static void PrepareAll()
        {
            var t0 = DateTime.Now;
            foreach (var c in Sheets.Champions)
                try { ChampionAssets.Get(c.Id).LoadArt(); } catch (Exception e) { Log.LogError(c.Name + " art: " + e.Message); }
            foreach (var c in Sheets.Champions) ChampionAssets.Get(c.Id).Load();
            Log.LogInfo($"Champions read from League in {(DateTime.Now - t0).TotalSeconds:F1}s");
            var vgm = Path.Combine(NativeDir, "vgmstream", "vgmstream-cli.exe");
            foreach (var c in Sheets.Champions)
                try { AudioLibrary.Prepare(c, vgm); } catch (Exception e) { Log.LogError(c.Name + " audio: " + e.Message); }
            Log.LogInfo($"League audio ready: {AudioLibrary.Decoded} clips ({AudioLibrary.Failed} failed) in {(DateTime.Now - t0).TotalSeconds:F1}s");
        }

        void Update()
        {
            if (LeagueReady)
            {
                Game.RosterSlots.Poll();
                Dev.TestMode.Poll();
            }
            Dev.DevCommands.Poll();
        }
    }
}
