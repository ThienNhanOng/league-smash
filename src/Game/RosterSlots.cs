using System.Collections.Generic;
using System.Linq;
using SlapRift.Champion;
using SlapRift.Generated;
using Smash;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SlapRift.Game
{
    // Hook:roster-slots
    // Adds a row of champion slots under the local character select grid, cloned from Slap City's own slots
    // and dressed with each champion's League icon and name.
    internal static class RosterSlots
    {
        static readonly HashSet<int> extended = new HashSet<int>();
        static float next;
        public static int Added;

        public static void Poll()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.5f;
            if (!Champions.Enabled || !Sheets.Champions.All(c => ChampionAssets.Get(c.Id)?.ArtReady == true)) return;
            foreach (var col in Object.FindObjectsOfType<CSSlotCollection>())
            {
                if (extended.Contains(col.GetInstanceID()) || col.transform.parent == null || col.transform.parent.name != "characterselect") continue;
                if (col.slots == null || col.slots.Length == 0) continue;
                extended.Add(col.GetInstanceID());
                Extend(col);
            }
        }

        static void Extend(CSSlotCollection col)
        {
            var orig = col.slots.Where(s => s != null).ToList();
            float topY = orig.Max(s => s.transform.localPosition.y), lowY = orig.Min(s => s.transform.localPosition.y);
            var top = orig.Where(s => Mathf.Approximately(s.transform.localPosition.y, topY)).OrderBy(s => s.transform.localPosition.x).ToList();
            float rowGap = topY - lowY > 1f ? topY - lowY : 185f;
            float scale = 0.7f;
            // nudge the existing grid up a little so three rows fit above the player boxes
            foreach (var s in orig) s.transform.localPosition += new Vector3(0f, rowGap * 0.25f, 0f);
            float y = lowY + rowGap * 0.25f - rowGap * 0.78f;
            float spacing = top.Count > 1 ? (top[1].transform.localPosition.x - top[0].transform.localPosition.x) : 200f;
            var list = col.slots.ToList();
            foreach (var c in Sheets.Champions.OrderBy(c => c.Slot))
            {
                var a = ChampionAssets.Get(c.Id);
                var go = Object.Instantiate(orig[0].gameObject, orig[0].transform.parent);
                go.name = c.Id;
                float x = (c.Slot - (Sheets.Champions.Length - 1) / 2f) * spacing;
                go.transform.localPosition = new Vector3(x, y, orig[0].transform.localPosition.z);
                go.transform.localScale = orig[0].transform.localScale * scale;
                var slot = go.GetComponent<CSCharacterSlot>();
                slot.character = c.Base + "#" + c.Id;
                foreach (var img in go.GetComponentsInChildren<RawImage>(true))
                {
                    if (img.gameObject.name != "Quad") continue;
                    img.texture = a.Icon;
                    img.uvRect = new Rect(0f, 1f, 1f, -1f);   // League art is stored top row first
                }
                foreach (var t in go.GetComponentsInChildren<TextMeshProUGUI>(true))
                {
                    var loc = t.GetComponent<LocReactText>();
                    if (loc != null) Object.Destroy(loc);
                    t.text = c.Name;
                }
                list.Add(slot);
                Added++;
            }
            col.slots = list.ToArray();
            Plugin.Log.LogInfo("Added " + Sheets.Champions.Length + " champion slots to the character select grid");
            Dev.TestMode.Report("roster-slots");
        }
    }
}
