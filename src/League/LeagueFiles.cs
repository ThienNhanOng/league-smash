using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace SlapRift.League
{
    // Hook:league-files
    // Finds the player's League of Legends install and opens champion WADs from it, read-only and in place.
    internal static class LeagueFiles
    {
        public static string Root { get; private set; }
        public static string Problem { get; private set; }
        static readonly Dictionary<string, Wad> wads = new Dictionary<string, Wad>(StringComparer.OrdinalIgnoreCase);

        public static string SettingsFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SlapRift", "settings.ini");

        public static bool Find()
        {
            foreach (var cand in Candidates())
            {
                if (string.IsNullOrEmpty(cand)) continue;
                var root = cand.Trim().Trim('"').Replace('/', Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar);
                // accept the install folder or its Game folder
                if (Directory.Exists(Path.Combine(root, "Game", "DATA", "FINAL", "Champions"))) { Root = root; return true; }
                if (Directory.Exists(Path.Combine(root, "DATA", "FINAL", "Champions"))) { Root = Path.GetDirectoryName(root); return true; }
            }
            Problem = "League of Legends was not found. Install it (it only needs to be installed, not running).";
            return false;
        }

        static IEnumerable<string> Candidates()
        {
            // 1. Melty writes the companion game's folder here before every Play
            if (File.Exists(SettingsFile))
                foreach (var line in File.ReadAllLines(SettingsFile))
                    if (line.StartsWith("league=", StringComparison.OrdinalIgnoreCase)) yield return line.Substring(7);
            yield return Environment.GetEnvironmentVariable("SLAPRIFT_LEAGUE");
            // 2. the Riot Client's own install record
            var meta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Riot Games", "Metadata", "league_of_legends.live", "league_of_legends.live.product_settings.yaml");
            if (File.Exists(meta))
            {
                var m = Regex.Match(File.ReadAllText(meta), "product_install_full_path:\\s*\"?([^\"\\r\\n]+)");
                if (m.Success) yield return m.Groups[1].Value;
            }
            // 3. the default install folders
            foreach (var d in DriveInfo.GetDrives())
                if (d.DriveType == DriveType.Fixed)
                {
                    yield return Path.Combine(d.Name, "Riot Games", "League of Legends");
                    yield return Path.Combine(d.Name, "Program Files", "Riot Games", "League of Legends");
                }
        }

        public static Wad Champion(string file)
        {
            lock (wads)
            {
                if (!wads.TryGetValue(file, out var w))
                {
                    w = new Wad(Path.Combine(Root, "Game", "DATA", "FINAL", "Champions", file));
                    wads[file] = w;
                }
                return w;
            }
        }
    }
}
