using System.IO;
using System.Linq;
using System.Text;
using Smash;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SlapRift.Dev
{
    // Development-only inspection: F8 writes the character registry, live fighters and the open scenes to BepInEx/slaprift-dump.
    internal static class Dump
    {
        public static string Dir => System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "slaprift-dump");

        public static void All()
        {
            Directory.CreateDirectory(Dir);
            Characters();
            Fighters();
            Scenes();
            Plugin.Log.LogInfo("Dump written to " + Dir);
        }

        static void Characters()
        {
            var sb = new StringBuilder();
            var col = SmashResources.Instance.characters;
            foreach (var l in col.loadable)
            {
                sb.AppendLine("== " + l.id + " path=" + l.path);
                col.TryGetMeta(l.id, out var meta);
                sb.AppendLine(meta);
            }
            File.WriteAllText(System.IO.Path.Combine(Dir, "characters.txt"), sb.ToString());
        }

        // Loads each roster prefab without spawning it: its moveset text, animation clips and bones.
        public static void Prefabs(string only)
        {
            Directory.CreateDirectory(Dir);
            var col = SmashResources.Instance.characters;
            foreach (var l in col.loadable)
            {
                if (only.Length > 0 && l.id != only) continue;
                col.TryGetFullPath(l.id, out var path);
                var go = Resources.Load<GameObject>(path);
                if (go == null) { Plugin.Log.LogWarning("no prefab " + path); continue; }
                var sc = go.GetComponent<SmashCharacter>();
                var sb = new StringBuilder();
                sb.AppendLine("== " + l.id + " prefab=" + go.name);
                if (sc != null)
                {
                    if (sc.characterAnim != null)
                        foreach (AnimationState s in sc.characterAnim) sb.AppendLine("clip " + s.name + " len=" + s.length.ToString("F2"));
                    if (sc.defaultAnimations != null) sb.AppendLine("defaultAnims " + sc.defaultAnimations.Length);
                    foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                        sb.AppendLine("renderer " + TPath(r.transform, go.transform) + " " + r.GetType().Name + " shader=" + (r.sharedMaterial ? r.sharedMaterial.shader.name : "-") + " size=" + r.bounds.size);
                    Tree(sb, go.transform, 0, 12);
                    File.WriteAllText(System.IO.Path.Combine(Dir, "prefab-" + l.id + ".txt"), sb.ToString());
                    if (sc.movesetfile != null) File.WriteAllText(System.IO.Path.Combine(Dir, "moveset-" + l.id + ".json"), sc.movesetfile.text);
                }
            }
        }

        static void Fighters()
        {
            var sb = new StringBuilder();
            foreach (var sc in Object.FindObjectsOfType<SmashCharacter>())
            {
                sb.AppendLine("== " + sc.name + " move=" + sc.currentmoveid);
                if (sc.moveset != null)
                    sb.AppendLine("moves: " + string.Join(",", sc.moveset.GetMoveset().Select(m => m.id + ":" + m.move.GetType().Name)));
                if (sc.characterAnim != null)
                    foreach (AnimationState s in sc.characterAnim) sb.AppendLine("clip " + s.name + " len=" + s.length);
                foreach (var r in sc.GetComponentsInChildren<Renderer>(true))
                    sb.AppendLine("renderer " + TPath(r.transform, sc.transform) + " " + r.GetType().Name + " mat=" + (r.sharedMaterial ? r.sharedMaterial.shader.name : "-"));
                sb.AppendLine("bounds: " + string.Join(" ", sc.GetComponentsInChildren<Renderer>().Select(r => r.bounds.size.ToString())));
                Tree(sb, sc.transform, 0, 6);
            }
            File.WriteAllText(System.IO.Path.Combine(Dir, "fighters.txt"), sb.ToString());
        }

        static void Scenes()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                sb.AppendLine("### scene " + s.name);
                foreach (var go in s.GetRootGameObjects()) Tree(sb, go.transform, 0, 9);
            }
            File.WriteAllText(System.IO.Path.Combine(Dir, "scenes.txt"), sb.ToString());
        }

        static string TPath(Transform t, Transform root)
        {
            var p = t.name;
            while (t.parent != null && t != root) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }

        static void Tree(StringBuilder sb, Transform t, int depth, int max)
        {
            if (depth > max) return;
            var comps = string.Join(",", t.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name));
            sb.Append(' ', depth * 2).Append(t.name).Append(t.gameObject.activeInHierarchy ? "" : " (off)").Append(" [").Append(comps).Append("] ")
              .Append(t.localPosition.ToString("F2")).AppendLine();
            foreach (Transform c in t) Tree(sb, c, depth + 1, max);
        }
    }
}
