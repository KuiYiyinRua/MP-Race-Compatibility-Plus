using System.Collections.Generic;
using Verse;
using RimWorld;

namespace GodHandMod
{
    // 永动机清理工具
    public static class EndRodCleanupUtility
    {
        public static void CleanupAllEndRods(bool silent = false)
        {
            if (Current.ProgramState != ProgramState.Playing) return;

            int count = 0;
            ThingDef endRodDef = GodHandDefOf.GodHand_PawnFlyer;
            // 现场获取定义
            ThingDef targetDef = DefDatabase<ThingDef>.GetNamedSilentFail("GodHand_EndRodGenerator");
            if (targetDef == null) return;

            foreach (Map map in Find.Maps)
            {
                List<Thing> allTools = map.listerThings.ThingsOfDef(targetDef);
                for (int i = allTools.Count - 1; i >= 0; i--)
                {
                    Thing rod = allTools[i];
                    // 获取返还材料
                    List<Thing> resources = rod.SmeltProducts(1.0f).ToSafeList();
                    IntVec3 pos = rod.Position;
                    rod.Destroy(DestroyMode.Deconstruct);
                    foreach (Thing res in resources)
                    {
                        GenPlace.TryPlaceThing(res, pos, map, ThingPlaceMode.Near);
                    }
                    count++;
                }
            }

            if (!silent && count > 0)
            {
                Messages.Message("已自动拆除 " + count + " 个异世界第一类永动机并全额返还材料。", MessageTypeDefOf.PositiveEvent);
            }
        }

        private static List<T> ToSafeList<T>(this IEnumerable<T> source)
        {
            return new List<T>(source);
        }
    }
}
