using System.Collections.Generic;
using Verse;
using RimWorld;

namespace GodHandMod
{
    // 用于处理发电机清理逻辑的工具类
    public static class GeneratorCleanupUtility
    {
        public static void CleanupAllGenerators(bool silent = false)
        {
            if (Current.ProgramState != ProgramState.Playing)
            {
                if (!silent) Messages.Message("GodHand.Generator.OnlyCleanupWhilePlaying".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            int count = 0;
            foreach (Map map in Find.Maps)
            {
                List<Thing> allGenerators = map.listerThings.ThingsOfDef(GodHandDefOf.GodHand_GearboxGenerator);
                for (int i = allGenerators.Count - 1; i >= 0; i--)
                {
                    Thing gen = allGenerators[i];
                    // 拆除并返还资源
                    List<Thing> resources = gen.SmeltProducts(1.0f).ToSafeList();
                    IntVec3 pos = gen.Position;
                    gen.Destroy(DestroyMode.Deconstruct);
                    foreach (Thing res in resources)
                    {
                        GenPlace.TryPlaceThing(res, pos, map, ThingPlaceMode.Near);
                    }
                    count++;
                }
            }

            if (!silent)
            {
                if (count > 0)
                {
                    Messages.Message("GodHand.Generator.CleanupSuccess".Translate(count), MessageTypeDefOf.PositiveEvent);
                }
                else
                {
                    Messages.Message("GodHand.Generator.NoGeneratorsFound".Translate(), MessageTypeDefOf.RejectInput);
                }
            }
        }

        // 扩展方法简化列表转换
        private static List<T> ToSafeList<T>(this IEnumerable<T> source)
        {
            return new List<T>(source);
        }
    }
}
