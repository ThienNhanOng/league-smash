using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using SlapRift.Audio;
using SlapRift.Champion;
using SlapRift.Generated;
using Smash;
using UnityEngine;

namespace SlapRift.Game
{
    internal static class Champions
    {
        public static ChampionDef ById(string id) => id == null ? null : Sheets.Champions.FirstOrDefault(c => c.Id == id);
        public static bool IsChampionSkin(string skin) => skin != null && skin.StartsWith("lol_");
        public static bool Enabled => !ModeSetup.ModeIsOnline && Plugin.LeagueReady;
    }

    // Hook:skin-meta
    [HarmonyPatch(typeof(LoadedCharacterSkins), nameof(LoadedCharacterSkins.EnsureCharacterMeta))]
    static class SkinMetaPatch
    {
        static readonly HashSet<string> done = new HashSet<string>();

        static void Prefix(string character)
        {
            if (done.Contains(character)) return;
            var champs = Sheets.Champions.Where(c => c.Base == character).ToList();
            if (champs.Count == 0) return;
            done.Add(character);
            if (!SmashResources.Instance.characters.TryGetMetaDict(character, out var meta) || meta == null) return;
            if (!(meta.TryGetValue("skins", out var o) && o is List<object> skins)) return;
            var def = skins.OfType<Dictionary<string, object>>().FirstOrDefault(d => (d.TryGetValue("id", out var id) ? id as string : null) == "default");
            if (def == null) return;
            foreach (var c in champs)
            {
                if (skins.OfType<Dictionary<string, object>>().Any(d => d.TryGetValue("id", out var id) && (id as string) == c.Id)) continue;
                skins.Add(new Dictionary<string, object>
                {
                    ["id"] = c.Id,
                    ["skinpath"] = def["skinpath"],
                    ["name"] = c.Name,
                    ["flair"] = "League of Legends",
                    ["portraits"] = def.TryGetValue("portraits", out var p) ? p : new List<object>(),
                    ["ulom"] = "slaprift_champion_slot_only",   // never in the base fighter's skin cycle
                    ["quotes"] = new List<object> { new Dictionary<string, object> { ["quote"] = c.Name + " claims the city." } },
                });
            }
        }
    }

    // Hook:skin-load
    // A champion skin uses the base fighter's default skin asset (meshes, swap textures).
    [HarmonyPatch]
    static class SkinLoadPatch
    {
        static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            var t = typeof(LoadedCharacterSkins);
            yield return AccessTools.Method(t, nameof(LoadedCharacterSkins.LoadCharacterSkin));
            yield return AccessTools.Method(t, nameof(LoadedCharacterSkins.UnloadCharacterSkin));
            yield return AccessTools.Method(t, nameof(LoadedCharacterSkins.CharacterSkinLoaded));
            yield return AccessTools.Method(t, nameof(LoadedCharacterSkins.SkinLoading));
            yield return AccessTools.Method(t, nameof(LoadedCharacterSkins.TryGetSkin));
        }

        static void Prefix(ref string skin)
        {
            if (Champions.IsChampionSkin(skin)) skin = "default";
        }
    }

    // Hook:select-slot
    // Champion slots carry "<base>#<champion skin>"; picking one selects the base fighter wearing that skin.
    [HarmonyPatch(typeof(CSPlayerBox), nameof(CSPlayerBox.SetCharacter))]
    static class SelectSlotPatch
    {
        static void Prefix(ref string newcharacter, out string __state)
        {
            __state = null;
            if (newcharacter != null && newcharacter.Contains("#"))
            {
                var parts = newcharacter.Split('#');
                newcharacter = parts[0];
                __state = parts[1];
            }
        }

        static void Postfix(CSPlayerBox __instance, string __state)
        {
            string want = __state ?? (Champions.IsChampionSkin(__instance.skin) ? "default" : null);
            if (want == null || __instance.skin == want || string.IsNullOrEmpty(__instance.character)) return;
            __instance.skin = want;
            __instance.suit = CSSuitEnforcer.ReportSuit(__instance.player, __instance.character, __instance.skin, __instance.suit);
            __instance.UpdMeta(true);
            __instance.PushPlayerSettings();
            if (__state != null) BoxPortraitPatch.PlayPick(__state);
        }
    }

    // Hook:box-portrait
    [HarmonyPatch(typeof(CSPlayerBox), nameof(CSPlayerBox.UpdMeta))]
    static class BoxPortraitPatch
    {
        static AudioSource ui;

        internal static void PlayPick(string champ)
        {
            var c = Champions.ById(champ);
            var clip = c != null ? AudioLibrary.Pick(c.Id, c.VoSelect) : null;
            if (clip == null) return;
            if (ui == null) { var go = new GameObject("SlapRiftUiVoice"); Object.DontDestroyOnLoad(go); ui = go.AddComponent<AudioSource>(); ui.spatialBlend = 0; }
            ui.Stop(); ui.clip = clip; ui.volume = Mathf.Clamp01(SystemSettings.SFXVol); ui.Play();
        }

        // shows the champion's loading-screen art (top part, the art is stored top row first) or resets the crop
        internal static bool ApplyArt(MenuTextureSwitcher sw, string skin, bool hud = false)
        {
            if (sw == null) return false;
            var c = Champions.IsChampionSkin(skin) ? ChampionAssets.Get(skin) : null;
            var tex = c != null && c.ArtReady ? c.Portrait : null;
            // the HUD frame is wide, so it shows just the face; the select box shows head and shoulders
            float top = 0.04f, h = hud ? 0.3f : 0.58f;
            if (sw.rawimage != null) sw.rawimage.uvRect = tex != null ? new Rect(0f, top + h, 1f, -h) : new Rect(0f, 0f, 1f, 1f);
            if (tex == null) return false;
            sw.SetTexture(tex);
            if (sw.target != null)
            {
                var block = new MaterialPropertyBlock();
                sw.target.GetPropertyBlock(block);
                block.SetVector("_MainTex_ST", new Vector4(1f, -h, 0f, top + h));
                sw.target.SetPropertyBlock(block);
            }
            return true;
        }

        internal static bool Apply(CSPlayerBox box)
        {
            if (!ApplyArt(box.portraitSwitcher, box.skin)) return false;
            box.portraitObject.SetActive(box.playerActive);
            return true;
        }

        static void Postfix(CSPlayerBox __instance) => Apply(__instance);
    }

    // the in-match HUD (and arcade screens) portrait: champion art instead of the base fighter's
    [HarmonyPatch(typeof(ArcadeInterstitialPortrait), nameof(ArcadeInterstitialPortrait.SetPortrait))]
    static class HudPortraitPatch
    {
        static void Postfix(ArcadeInterstitialPortrait __instance, string skin)
        {
            if (BoxPortraitPatch.ApplyArt(__instance.switcher, skin, true)) { __instance.show.SetActive(true); __instance.loaded = true; }
        }
    }

    [HarmonyPatch(typeof(ArcadeInterstitialPortrait), nameof(ArcadeInterstitialPortrait.LoadedPortrait))]
    static class HudPortraitLoadedPatch
    {
        static bool Prefix(ArcadeInterstitialPortrait __instance)
        {
            if (!BoxPortraitPatch.ApplyArt(__instance.switcher, __instance.oldSkin, true)) return true;
            __instance.show.SetActive(true); __instance.loaded = true;
            return false;
        }
    }

    // champion names and victory lines are plain text, not keys in Slap City's string tables
    [HarmonyPatch(typeof(Smash.LocalizationSystem), nameof(Smash.LocalizationSystem.GetText))]
    static class ChampionTextPatch
    {
        static readonly HashSet<string> plain = new HashSet<string>(Sheets.Champions.SelectMany(c => new[] { c.Name, c.Name + " claims the city." }));

        static bool Prefix(string key, ref string __result)
        {
            if (key == null || !plain.Contains(key)) return true;
            __result = key;
            return false;
        }
    }

    // the base fighter's portrait finishes loading after UpdMeta; keep the champion art for champion skins
    [HarmonyPatch(typeof(CSPlayerBox), nameof(CSPlayerBox.LoadedPortrait))]
    static class BoxPortraitLoadedPatch
    {
        static bool Prefix(CSPlayerBox __instance) => !BoxPortraitPatch.Apply(__instance);
    }
}
