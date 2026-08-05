using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using UnityEngine;
using Verse.Sound;

namespace GodHandMod
{
    // 上帝帮手控制器
    public static class GodAssistantController
    {
        public static void Execute(Thing t, Map map, int mode)
        {
            if (t == null || map == null) return;

            switch (mode)
            {
                case 0: // 圣徒工匠
                    if (t is Building_WorkTable table) HandleWorkTable(table);
                    else Messages.Message("GodHand.Assistant.TargetMustBeWorkTable".Translate(), MessageTypeDefOf.RejectInput, false);
                    break;
                case 1: // 圣佑医师
                    if (t is Pawn patient) HandleMedicalPawn(patient);
                    else if (t is Building_Bed bed) HandleMedicalBed(bed);
                    else Messages.Message("GodHand.Assistant.TargetMustBePawnOrBed".Translate(), MessageTypeDefOf.RejectInput, false);
                    break;
            }
        }

        private static void HandleWorkTable(Building_WorkTable table)
        {
            var comp = table.Map.GetComponent<MapComponent_GodAssistant>();
            if (comp == null) return;

            bool isShiftPressed = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            // Shift强制关闭助手
            if (isShiftPressed)
            {
                if (comp.IsTableBusy(table))
                {
                    comp.RemoveTaskFor(table);
                    Messages.Message("GodHand.Assistant.Disabled".Translate(), table, MessageTypeDefOf.NeutralEvent, false);
                    SoundDefOf.Click.PlayOneShot(table);
                }
                return;
            }

            // 开启新任务或加速
            int totalActiveTasks = comp.GetCraftingTaskCount();
            int maxLimit = GodHandModMain.Settings.maxGodAssistantTasks;

            if (totalActiveTasks >= maxLimit)
            {
                Messages.Message("GodHand.Assistant.MaxLimitReached".Translate(maxLimit), table, MessageTypeDefOf.RejectInput, false);
                return;
            }

            comp.AddTask(new GodHandCraftingTask(table));

            int currentTableTasks = comp.GetTasksFor(table).Count();
            if (currentTableTasks > 1)
            {
                Messages.Message("GodHand.Assistant.ThreadAdded".Translate(currentTableTasks), table, MessageTypeDefOf.PositiveEvent, true);
            }
            else
            {
                Messages.Message("GodHand.Assistant.Enabled".Translate(), table, MessageTypeDefOf.PositiveEvent, false);
            }

            SoundDefOf.Designate_PlanAdd.PlayOneShot(table);
        }

        private static void HandleMedicalPawn(Pawn patient)
        {
            var comp = patient.Map.GetComponent<MapComponent_GodAssistant>();
            if (comp == null) return;

            bool isShiftPressed = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (isShiftPressed)
            {
                comp.RemoveTaskFor(patient);
                Messages.Message("GodHand.Assistant.Disabled".Translate(), patient, MessageTypeDefOf.NeutralEvent, false);
                return;
            }

            if (comp.HasMedicalTaskFor(patient))
            {
                Messages.Message("GodHand.Assistant.AlreadyActive".Translate(), patient, MessageTypeDefOf.RejectInput, false);
                return;
            }

            comp.AddTask(new GodHandMedicalTask_Pawn(patient));
            Messages.Message("GodHand.Assistant.Enabled".Translate(), patient, MessageTypeDefOf.PositiveEvent, false);
            SoundDefOf.Designate_PlanAdd.PlayOneShot(patient);
        }

        private static void HandleMedicalBed(Building_Bed bed)
        {
            var comp = bed.Map.GetComponent<MapComponent_GodAssistant>();
            if (comp == null) return;

            bool isShiftPressed = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (isShiftPressed)
            {
                comp.RemoveTaskFor(bed);
                Messages.Message("GodHand.Assistant.Disabled".Translate(), bed, MessageTypeDefOf.NeutralEvent, false);
                return;
            }

            if (comp.HasMedicalTaskFor(bed))
            {
                Messages.Message("GodHand.Assistant.AlreadyActive".Translate(), bed, MessageTypeDefOf.RejectInput, false);
                return;
            }

            comp.AddTask(new GodHandMedicalTask_Bed(bed));
            Messages.Message("GodHand.Assistant.Enabled".Translate(), bed, MessageTypeDefOf.PositiveEvent, false);
            SoundDefOf.Designate_PlanAdd.PlayOneShot(bed);
        }

        private const float SpatialSearchRadius = 40f; // 空间搜寻半径阈值
        private const int MaxCandidates = 150;        // 最大候选者数量限制

        // 锁定配料工具 - 优化性能
        public static bool TryFindIngredients(Bill_Production bill, Building_WorkTable table, out List<Pair<Thing, IntVec3>> foundIngredients)
        {
            foundIngredients = new List<Pair<Thing, IntVec3>>();
            if (bill.recipe.ingredients.NullOrEmpty()) return true;

            Map map = table.Map;
            float radius = bill.ingredientSearchRadius;
            float radiusSq = radius * radius;
            IntVec3 root = table.Position;

            List<Thing> candidates = new List<Thing>();

            // 空间搜寻
            if (radius < SpatialSearchRadius)
            {
                int r = Mathf.CeilToInt(radius);
                foreach (IntVec3 cell in GenRadial.RadialCellsAround(root, r, true))
                {
                    if (!cell.InBounds(map)) continue;
                    var thingList = cell.GetThingList(map);
                    for (int i = 0; i < thingList.Count; i++)
                    {
                        Thing t = thingList[i];
                        if (t.Spawned && !t.IsForbidden(Faction.OfPlayer) && IsValidIngredient(t, bill))
                        {
                            candidates.Add(t);
                            if (candidates.Count >= MaxCandidates) break;
                        }
                    }
                    if (candidates.Count >= MaxCandidates) break;
                }
            }
            // 全局搜寻
            else
            {
                // 允许搜索全类型材料
                var allThings = map.listerThings.AllThings;
                for (int i = 0; i < allThings.Count; i++)
                {
                    Thing t = allThings[i];
                    if (t.Spawned && !t.IsForbidden(Faction.OfPlayer) &&
                        (t.Position - root).LengthHorizontalSquared < radiusSq &&
                        IsValidIngredient(t, bill))
                    {
                        candidates.Add(t);
                        if (candidates.Count >= MaxCandidates) break;
                    }
                }
            }

            if (candidates.Count == 0) return false;

            // 排序仅限候选池
            candidates = candidates.OrderBy(t => t.Position.DistanceToSquared(root)).ToList();

            // 记录分离位置
            var chosen = new List<Pair<ThingCount, IntVec3>>();
            if (!TrySelectIngredientsWithPos(bill, candidates, chosen)) return false;

            foreach (var cp in chosen)
            {
                ThingCount tc = cp.First;
                IntVec3 pos = cp.Second;
                Thing splitThing = tc.Thing.SplitOff(tc.Count);
                foundIngredients.Add(new Pair<Thing, IntVec3>(splitThing, pos));
            }
            return true;
        }

        private static bool TrySelectIngredientsWithPos(Bill_Production bill, List<Thing> candidates, List<Pair<ThingCount, IntVec3>> chosen)
        {
            var counts = new List<Pair<ThingCount, IntVec3>>();
            foreach (var ic in bill.recipe.ingredients)
            {
                float needed = ic.GetBaseCount();
                foreach (var t in candidates)
                {
                    if (needed <= 0.001f) break;
                    if (!ic.filter.Allows(t)) continue;

                    int alreadyUsed = counts.Where(c => c.First.Thing == t).Sum(c => c.First.Count);
                    int available = t.stackCount - alreadyUsed;
                    if (available <= 0) continue;

                    float valuePerUnit = bill.recipe.IngredientValueGetter.ValuePerUnitOf(t.def);
                    int take = Mathf.CeilToInt(needed / valuePerUnit);
                    take = Mathf.Min(take, available);

                    if (take > 0)
                    {
                        counts.Add(new Pair<ThingCount, IntVec3>(new ThingCount(t, take), t.Position));
                        needed -= take * valuePerUnit;
                    }
                }
                if (needed > 0.001f) return false;
            }
            chosen.AddRange(counts);
            return true;
        }

        private static bool IsValidIngredient(Thing t, Bill bill)
        {
            if (!bill.recipe.fixedIngredientFilter.Allows(t)) return false;
            if (bill.ingredientFilter != null && !bill.ingredientFilter.Allows(t)) return false;
            return true;
        }

        // 生成神级产物
        public static List<Thing> MakeGodProducts(RecipeDef recipe, Pawn worker, List<Thing> ingredients, Building_WorkTable table)
        {
            List<Thing> finalProducts = new List<Thing>();

            if (recipe == null) return finalProducts;

            // 同步派系信仰保证风格
            if (worker != null && ModsConfig.IdeologyActive && Faction.OfPlayer?.ideos?.PrimaryIdeo != null)
            {
                if (worker.ideo != null && worker.Ideo != Faction.OfPlayer.ideos.PrimaryIdeo)
                {
                    worker.ideo.SetIdeo(Faction.OfPlayer.ideos.PrimaryIdeo);
                }
            }

            Thing dominant = null;
            if (!ingredients.NullOrEmpty())
                dominant = ingredients.FirstOrDefault(i => i.def.IsStuff) ?? ingredients[0];

            // 临时将虚拟工作者设置为实体坐标与所属地图，防止屠宰落血、垃圾报错
            var mapIndexField = HarmonyLib.AccessTools.Field(typeof(Thing), "mapIndexOrState");
            if (worker != null && table != null && mapIndexField != null)
            {
                worker.Position = table.Position;
                mapIndexField.SetValue(worker, (sbyte)table.Map.Index);
            }

            try
            {
                IEnumerable<Thing> generated = GenRecipe.MakeRecipeProducts(recipe, worker, ingredients, dominant, table);

                foreach (var product in generated)
                {
                    if (product == null) continue;

                    var compQ = product.TryGetComp<CompQuality>();
                    if (compQ != null)
                    {
                        compQ.SetQuality(QualityCategory.Legendary, ArtGenerationContext.Colony);
                    }

                    var compArt = product.TryGetComp<CompArt>();
                    if (compArt != null)
                    {
                        compArt.Title = GetRandomPoeticTitle();
                        Find.World.GetComponent<WorldComponent_GodHandData>()?.RegisterDescription(product.thingIDNumber, GetRandomPoeticDescription());
                    }

                    finalProducts.Add(product);
                }
            }
            finally
            {
                // 用完即焚：清理伪造的地图与坐标索引防脏乱抛错甚至坏档
                if (worker != null && mapIndexField != null)
                {
                    mapIndexField.SetValue(worker, (sbyte)-1);
                    worker.Position = IntVec3.Invalid;
                }
            }

            return finalProducts;
        }

        private static readonly List<string> PoeticTitles = new List<string>
        {
            "当白昼倾坠之时", "风声雨声十四行诗", "群星归位之刻", "时间是静止的河流",
            "虚空中的低语", "永恒的刹那", "破碎的维度之镜", "众神的沉默",
            "不可名状的几何", "创世的余烬", "最后的挽歌", "无尽的螺旋",
            "静谧之蓝", "燃烧的冰原", "被遗忘的誓言"
        };

        private static readonly List<string> PoeticDescriptions = new List<string>
        {
            "这件作品凝固了时间一瞬 仿佛白昼在无尽黑暗中倾坠 引发关于终结与新生的沉思",
            "雕刻中蕴含着风声雨声韵律 宛如一首无声十四行诗 诉说着大自然的悲欢离合",
            "画面描绘了群星归位宏大景象 星辰轨迹交织成神秘符文 预示着古老神力苏醒",
            "在作品深处 时间仿佛变成静止河流 所有过往未来都凝结在这一刻宁静中",
            "细微纹理仿佛是虚空中低语 只有最敏锐心灵能倾听到来自维度的呼唤",
            "这件物品捕捉了永恒刹那 将瞬间辉煌永久地封存在了物质形态中",
            "错综复杂线条构成破碎维度之镜 映照出观者内心深处最隐秘渴望恐惧",
            "作品散发庄严氛围 如同众神沉默 威严而不可侵犯 让人心生敬畏",
            "违反常理几何结构构成不可名状形式 挑战凡人认知边界 暗示高维度存在",
            "表面闪烁微光 如同创世之初余烬 温暖而古老 承载着宇宙诞生记忆"
        };

        private static string GetRandomPoeticTitle() => PoeticTitles.RandomElement();
        private static string GetRandomPoeticDescription() => PoeticDescriptions.RandomElement();
    }
}
