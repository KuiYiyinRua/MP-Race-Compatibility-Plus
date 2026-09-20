using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>联机下为通讯台添加喵喵电商 Gizmo，通过 Comp 注入（比 Harmony Postfix 更可靠）。</summary>
    public class CompMeowCommsConsole : ThingComp
    {
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (!CompatibilityPatchCategories.IsEnabled("meow")) yield break;
            CommsConsoleDebug.Log("CompMeowCommsConsole.CompGetGizmosExtra ENTER");
            if (!MP.enabled || !MP.IsInMultiplayer)
            {
                CommsConsoleDebug.Log("CompMeowCommsConsole: skip MP.enabled/IsInMultiplayer=false");
                yield break;
            }
            var map = parent?.Map;
            if (map == null)
            {
                CommsConsoleDebug.Log("CompMeowCommsConsole: skip map=null");
                yield break;
            }
            var pawn = map.mapPawns.FreeColonists.FirstOrFallback();
            // 无殖民者时也显示按钮，点击时 OpenMeowCommsShopLocal 会用 Find.CurrentMap 找谈判者

            var defType = AccessTools.TypeByName("Meow.CommsShopDef");
            if (defType == null)
            {
                CommsConsoleDebug.Log("CompMeowCommsConsole: skip CommsShopDef type null");
                yield break;
            }
            var dbType = typeof(DefDatabase<>).MakeGenericType(defType);
            var allDefs = AccessTools.Method(dbType, "AllDefs");
            if (allDefs == null) yield break;
            var defs = allDefs.Invoke(null, null) as System.Collections.IEnumerable;
            if (defs == null) yield break;

            var yielded = 0;
            foreach (var def in defs)
            {
                if (def == null) continue;
                var defName = (def as Def)?.defName ?? AccessTools.Property(def.GetType(), "defName")?.GetValue(def) as string;
                if (string.IsNullOrEmpty(defName)) continue;
                yielded++;
                var label = AccessTools.Property(def.GetType(), "label")?.GetValue(def) as string ?? defName;
                var getCallLabel = AccessTools.Method(AccessTools.TypeByName("Meow.CommsShop"), "GetCallLabel");
                string displayLabel = (def as Def)?.LabelCap ?? label;
                if (getCallLabel != null)
                {
                    var commShopType = AccessTools.TypeByName("Meow.CommsShop");
                    var ctor = AccessTools.Constructor(commShopType, new[] { typeof(string), defType });
                    if (ctor != null)
                    {
                        var temp = ctor.Invoke(new object[] { label, def });
                        displayLabel = getCallLabel.Invoke(temp, null) as string ?? displayLabel;
                    }
                }
                var texture = AccessTools.Property(def.GetType(), "Texture")?.GetValue(def) as Texture2D;
                var p = pawn;
                var d = defName;
                yield return new Command_Action
                {
                    defaultLabel = displayLabel,
                    defaultDesc = "Meow.ExpressDesc".Translate(displayLabel.CapitalizeFirst()),
                    icon = texture,
                    action = () =>
                    {
                        CommsConsoleDebug.Log($"CompMeowCommsConsole action clicked: pawn={p?.LabelShort ?? "null"} defName={d}");
                        LongEventHandler.ExecuteWhenFinished(() => Patch_CommsConsole.OpenMeowCommsShopLocal(p, d));
                    }
                };
            }
            CommsConsoleDebug.Log($"CompMeowCommsConsole: yielded {yielded} gizmos");
        }
    }

    public class CompProperties_MeowCommsConsole : CompProperties
    {
        public CompProperties_MeowCommsConsole() => compClass = typeof(CompMeowCommsConsole);
    }
}
