using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;

namespace GodHandMod
{
    // 爱抚型神之手
    [StaticConstructorOnStartup]
    public class Designator_CaressGodHand : Designator
    {
        public override bool Visible => GodHandModMain.Settings.enableCaress;

        public Designator_CaressGodHand()
        {
            defaultLabel = "CaressGodHand_Label".Translate();
            // 使用简短描述作为工具提示
            defaultDesc = "CaressGodHand_Desc.Simple".Translate();

            // 加载图标
            icon = ContentFinder<Texture2D>.Get("UI/Designators/GodHandCaress", false);
            if (icon == null)
            {
                icon = ContentFinder<Texture2D>.Get("UI/Designators/Noimage", true);
            }

            soundDragSustain = SoundDefOf.Designate_DragStandard;
            soundDragChanged = SoundDefOf.Designate_DragStandard_Changed;
            useMouseIcon = true;
            soundSucceeded = SoundDefOf.Designate_Claim;
            hotKey = KeyBindingDefOf.Misc3;
        }

        public override void ProcessInput(Event ev)
        {
            // 检查是否第一次使用
            if (!GodHandModMain.Settings.hasSeenHelp_Caress)
            {
                GodHandModMain.Settings.hasSeenHelp_Caress = true;
                GodHandModMain.Settings.Write();

                Find.WindowStack.Add(new Dialog_GodHandInfo(
                    (Texture2D)icon,
                    "CaressGodHand_Label".Translate(),
                    "CaressGodHand_Desc".Translate(
                        GodHandModMain.Settings.caressMoodBonus,
                        GodHandModMain.Settings.caressMaxStages,
                        (GodHandModMain.Settings.caressPrisonerResistanceReduction * 100).ToString("F0"),
                        (GodHandModMain.Settings.caressRemoveLoyaltyChance * 100).ToString("F0"),
                        (GodHandModMain.Settings.caressEnemySurrenderChance * 100).ToString("F0")
                    )
                ));
                return;
            }

            base.ProcessInput(ev);
        }

        public override IEnumerable<FloatMenuOption> RightClickFloatMenuOptions
        {
            get
            {
                foreach (FloatMenuOption option in base.RightClickFloatMenuOptions)
                {
                    yield return option;
                }

                yield return new FloatMenuOption("GodHand.Help.View".Translate(), () =>
                {
                    Find.WindowStack.Add(new Dialog_GodHandInfo(
                        (Texture2D)icon,
                        "CaressGodHand_Label".Translate(),
                        "CaressGodHand_Desc".Translate(
                            GodHandModMain.Settings.caressMoodBonus,
                            GodHandModMain.Settings.caressMaxStages,
                            (GodHandModMain.Settings.caressPrisonerResistanceReduction * 100).ToString("F0"),
                            (GodHandModMain.Settings.caressRemoveLoyaltyChance * 100).ToString("F0"),
                            (GodHandModMain.Settings.caressEnemySurrenderChance * 100).ToString("F0")
                        )
                    ));
                });
            }
        }

        public override void SelectedUpdate()
        {
            // 高亮显示鼠标下的Pawn
            Pawn pawn = GetPawnUnderMouse();
            if (pawn != null)
            {
                GenDraw.DrawTargetHighlight(new LocalTargetInfo(pawn));
            }
        }

        public override void DrawMouseAttachments()
        {
            base.DrawMouseAttachments();

            Pawn pawn = GetPawnUnderMouse();
            if (pawn != null)
            {
                // 显示Pawn信息
                GenUI.DrawMouseAttachment(icon);
            }
        }

        public override AcceptanceReport CanDesignateCell(IntVec3 c)
        {
            if (!c.InBounds(base.Map))
                return false;

            if (c.Fogged(base.Map))
                return false;

            // 检查格子内是否有可抚摸的Pawn
            List<Thing> thingList = c.GetThingList(base.Map);
            foreach (Thing thing in thingList)
            {
                if (CanDesignateThing(thing).Accepted)
                    return true;
            }

            return "CaressGodHand_MustBePawn".Translate();
        }

        public override void DesignateSingleCell(IntVec3 c)
        {
            // 对格子内所有可抚摸的Pawn执行操作
            // 复制列表防修改
            List<Thing> thingList = c.GetThingList(base.Map);
            List<Thing> thingsToDesignate = new List<Thing>(thingList);

            foreach (Thing thing in thingsToDesignate)
            {
                if (CanDesignateThing(thing).Accepted)
                {
                    DesignateThing(thing);
                }
            }
        }

        public override AcceptanceReport CanDesignateThing(Thing t)
        {


            if (!(t is Pawn pawn))
            {

                return "CaressGodHand_MustBePawn".Translate();
            }

            if (pawn.Dead)
            {

                return "CaressGodHand_Dead".Translate();
            }


            return true;
        }

        public override void DesignateThing(Thing t)
        {


            if (t is Pawn pawn)
            {

                CaressGodHandController.CaressPawn(pawn);
            }
            else
            {
                Log.Warning($"[爱抚型神之手 Designator] t不是Pawn: {t?.GetType().Name}");
            }
        }

        // 获取鼠标下Pawn
        private Pawn GetPawnUnderMouse()
        {
            Map map = Find.CurrentMap;
            if (map == null)
                return null;

            IntVec3 cell = UI.MouseCell();
            if (!cell.InBounds(map))
                return null;

            // 查找该格子上的Pawn
            List<Thing> things = map.thingGrid.ThingsListAt(cell);
            foreach (Thing thing in things)
            {
                if (thing is Pawn pawn && !pawn.Dead && pawn.Spawned)
                {
                    return pawn;
                }
            }

            return null;
        }
    }
}
