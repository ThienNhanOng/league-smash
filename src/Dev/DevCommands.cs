using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SlapRift.Dev
{
    // Development-only: lines written to BepInEx/slaprift-cmd.txt run once (dump, debugloader, shot <file>).
    internal static class DevCommands
    {
        static float next;
        public static int LogProps;
        static string CmdFile => Path.Combine(BepInEx.Paths.BepInExRootPath, "slaprift-cmd.txt");

        public static void Poll()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.5f;
            if (!File.Exists(CmdFile)) return;
            string[] lines;
            try { lines = File.ReadAllLines(CmdFile); File.Delete(CmdFile); } catch { return; }
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                try { Run(line); }
                catch (Exception e) { Plugin.Log.LogError("cmd " + line + ": " + e); }
            }
        }

        static void Run(string line)
        {
            var parts = line.Split(new[] { ' ' }, 2);
            var arg = parts.Length > 1 ? parts[1] : "";
            switch (parts[0])
            {
                case "dump": Dump.All(); break;
                case "prefabs": Dump.Prefabs(arg); break;
                case "testmatch": TestMode.Force(arg); break;
                case "mechcheck": MechanicsCheck.Run(); break;
                case "dmgtest": MechanicsCheck.DamageWrite(); break;
                case "hitprops": LogProps = 25; break;
                case "spawn": Gameplay.LeagueObjects.DebugSpawn(arg); break;
                case "wallcheck": Plugin.Instance.StartCoroutine(Gameplay.LeagueObjects.WallCheck()); break;
                case "objshader":
                {
                    var sh = Shader.Find(arg);
                    Plugin.Log.LogInfo("objshader " + arg + " found=" + (sh != null) + " supported=" + (sh != null && sh.isSupported));
                    if (sh == null) break;
                    foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>())
                        if (r.GetComponentInParent<Gameplay.LeagueObject>() != null) { r.material.shader = sh; Plugin.Log.LogInfo("  " + r.name + " layer " + r.gameObject.layer + " active " + r.gameObject.activeInHierarchy + " bounds " + r.bounds); }
                    break;
                }
                case "freeze": Smash.SmashGame.Instance.play = arg != "1" ? true : false; break;
                case "shader":
                {
                    var sh = Shader.Find(arg);
                    if (sh == null) { Plugin.Log.LogWarning("no shader " + arg); break; }
                    foreach (var b in Champion.ChampionBody.Live)
                        foreach (var r in b.GetComponentsInChildren<SkinnedMeshRenderer>())
                            if (r.name == "body") foreach (var m in r.materials) { var t = m.mainTexture; m.shader = sh; m.mainTexture = t; if (m.HasProperty("_Color")) m.color = Color.white; }
                    break;
                }
                case "yaw":
                    foreach (var b in Champion.ChampionBody.Live)
                    {
                        var m = b.transform.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.StartsWith("SlapRift_"));
                        if (m != null) m.localRotation = Quaternion.Euler(0f, float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture), 0f);
                    }
                    break;
                case "materials":
                    foreach (var sc in UnityEngine.Object.FindObjectsOfType<Smash.SmashCharacter>())
                    {
                        var m = sc.mainMesh != null ? sc.mainMesh.sharedMaterial : null;
                        if (m == null) continue;
                        Plugin.Log.LogInfo(sc.name + " shader " + m.shader.name + " textures: " + string.Join(", ", m.GetTexturePropertyNames().Select(n => n + "=" + (m.GetTexture(n) ? m.GetTexture(n).name + " " + m.GetTexture(n).width + "x" + m.GetTexture(n).height : "-"))));
                        Plugin.Log.LogInfo("  keywords: " + string.Join(" ", m.shaderKeywords));
                    }
                    var shaders = Resources.FindObjectsOfTypeAll<Shader>().Select(s => s.name).Distinct().OrderBy(s => s);
                    Plugin.Log.LogInfo("shaders: " + string.Join(" | ", shaders));
                    break;
                case "debugloader": new GameObject("SlapRiftDebugLoader").AddComponent<Smash.DebugLoader>(); break;
                case "shot": ScreenCapture.CaptureScreenshot(arg); break;
                default: Plugin.Log.LogWarning("unknown cmd " + line); break;
            }
            Plugin.Log.LogInfo("cmd done: " + line);
        }
    }
}
