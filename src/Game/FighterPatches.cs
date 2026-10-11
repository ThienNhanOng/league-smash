using HarmonyLib;
using SlapRift.Champion;
using Smash;

namespace SlapRift.Game
{
    // Hook:apply-skin
    // A fighter spawned with a champion skin wears the base fighter's default look underneath and gets the
    // champion's League model on top (ChampionBody).
    [HarmonyPatch(typeof(SmashArticle), nameof(SmashArticle.ApplySkin))]
    static class ApplySkinPatch
    {
        static void Prefix(SmashArticle __instance, ref string skin, out string __state)
        {
            __state = Champions.IsChampionSkin(skin) ? skin : null;
            if (__state != null) skin = "default";
        }

        static void Postfix(SmashArticle __instance, string __state)
        {
            var sc = __instance as SmashCharacter;
            if (sc == null) return;
            var body = sc.GetComponent<ChampionBody>();
            var def = Champions.Enabled ? Champions.ById(__state) : null;
            if (def == null)
            {
                if (body != null) UnityEngine.Object.Destroy(body);
                return;
            }
            if (body != null && body.Def == def) return;
            if (body != null) UnityEngine.Object.Destroy(body);
            sc.gameObject.AddComponent<ChampionBody>().Init(def, sc);
            Plugin.Log.LogInfo("Spawned " + def.Name + " on " + sc.name);
            Dev.TestMode.Report("champion-spawned");
        }
    }
}
