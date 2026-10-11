using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using SlapRift.Generated;
using SlapRift.League;
using UnityEngine;

namespace SlapRift.Audio
{
    // Hook:audio-cache
    // Each champion's voice lines and ability sounds come from the player's League install: the Wwise media is
    // decoded once with the bundled vgmstream into %LOCALAPPDATA%\SlapRift\cache and loaded as AudioClips.
    internal static class AudioLibrary
    {
        sealed class Pcm { public int Channels, Rate; public float[] Samples; }

        static readonly Dictionary<string, Dictionary<string, List<uint>>> events = new Dictionary<string, Dictionary<string, List<uint>>>();
        static readonly Dictionary<string, Pcm> pcm = new Dictionary<string, Pcm>();
        static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        static readonly System.Random rng = new System.Random();
        public static int Decoded, Failed;
        public static string CacheDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SlapRift", "cache");

        public static IEnumerable<string> EventsOf(ChampionDef c)
        {
            yield return c.VoSelect; yield return c.VoKO; yield return c.VoTaunt;
            foreach (var a in new[] { c.Neutral, c.Side, c.Up, c.Down }) { yield return a.Sfx; yield return a.Vo; if (a.SfxProc != "none") yield return a.SfxProc; }
            if (c.Passive.Sfx != "none") yield return c.Passive.Sfx;
        }

        // worker thread
        public static void Prepare(ChampionDef c, string vgmstream)
        {
            var wad = LeagueFiles.Champion(c.Wad);
            Wad vo = null;
            try { vo = LeagueFiles.Champion(c.VoWad); } catch (Exception e) { Plugin.Log.LogWarning(c.Name + " voice WAD: " + e.Message); }
            var sfxHirc = new Wwise.Hirc(wad.Read(c.SfxEvents));
            byte[] voEv = null;
            if (vo != null) vo.TryRead(c.VoEvents, out voEv);
            if (voEv == null) wad.TryRead(c.VoEvents, out voEv);
            var voHirc = voEv != null ? new Wwise.Hirc(voEv) : null;
            var media = Wwise.BankMedia(wad.Read(c.SfxBank));
            if (vo != null && vo.TryRead(c.VoAudio, out var wpk)) foreach (var kv in Wwise.WpkMedia(wpk)) media[kv.Key] = kv.Value;

            var map = new Dictionary<string, List<uint>>();
            foreach (var ev in EventsOf(c).Distinct())
            {
                var h = ev.StartsWith("Play_vo_") ? voHirc : sfxHirc;
                map[ev] = h != null ? h.EventWems(ev).Where(media.ContainsKey).Distinct().ToList() : new List<uint>();
            }
            lock (events) events[c.Id] = map;

            var dir = Path.Combine(CacheDir, c.Id);
            Directory.CreateDirectory(dir);
            foreach (var id in map.Values.SelectMany(x => x).Distinct())
            {
                var wav = Path.Combine(dir, id + ".wav");
                if (!File.Exists(wav))
                {
                    var wem = Path.Combine(dir, id + ".wem");
                    var seg = media[id];
                    using (var f = File.Create(wem)) f.Write(seg.Array, seg.Offset, seg.Count);
                    Decode(vgmstream, wem, wav);
                    try { File.Delete(wem); } catch { }
                }
                var p = File.Exists(wav) ? ReadWav(wav) : null;
                lock (pcm) { if (p != null) { pcm[c.Id + "/" + id] = p; Decoded++; } else Failed++; }
            }
        }

        static void Decode(string exe, string wem, string wav)
        {
            var psi = new ProcessStartInfo(exe, "-o \"" + wav + "\" \"" + wem + "\"")
            { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(exe) };
            using (var p = Process.Start(psi))
            {
                p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd();
                if (!p.WaitForExit(20000)) { try { p.Kill(); } catch { } }
            }
        }

        static Pcm ReadWav(string path)
        {
            var d = File.ReadAllBytes(path);
            int p = 12, ch = 1, rate = 44100, bits = 16;
            while (p + 8 <= d.Length)
            {
                string tag = System.Text.Encoding.ASCII.GetString(d, p, 4); int sz = BitConverter.ToInt32(d, p + 4);
                if (tag == "fmt ") { ch = BitConverter.ToInt16(d, p + 10); rate = BitConverter.ToInt32(d, p + 12); bits = BitConverter.ToInt16(d, p + 22); }
                else if (tag == "data" && bits == 16)
                {
                    int n = Math.Min(sz, d.Length - p - 8) / 2;
                    var s = new float[n];
                    for (int i = 0; i < n; i++) s[i] = BitConverter.ToInt16(d, p + 8 + i * 2) / 32768f;
                    return new Pcm { Channels = ch, Rate = rate, Samples = s };
                }
                p += 8 + sz + (sz & 1);
            }
            return null;
        }

        static AudioClip Clip(string champ, uint id)
        {
            var key = champ + "/" + id;
            if (clips.TryGetValue(key, out var c)) return c;
            Pcm p; lock (pcm) pcm.TryGetValue(key, out p);
            if (p == null) return null;
            c = AudioClip.Create(key, p.Samples.Length / p.Channels, p.Channels, p.Rate, false);
            c.SetData(p.Samples, 0);
            clips[key] = c;
            return c;
        }

        public static bool Has(string champ, string ev)
        {
            lock (events) return events.TryGetValue(champ, out var m) && m.TryGetValue(ev, out var l) && l.Count > 0;
        }

        // main thread
        public static AudioClip Pick(string champ, string ev)
        {
            List<uint> ids;
            lock (events) { if (!events.TryGetValue(champ, out var m) || !m.TryGetValue(ev, out ids) || ids.Count == 0) return null; }
            for (int tries = 0; tries < ids.Count; tries++)
            {
                var c = Clip(champ, ids[rng.Next(ids.Count)]);
                if (c != null) return c;
            }
            return null;
        }
    }
}
