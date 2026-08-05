using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;

namespace GodHandMod
{
    // 脑瓜崩工具逻辑
    [StaticConstructorOnStartup]
    public class Designator_PokeGodHand : Designator
    {
        public override bool Visible => GodHandModMain.Settings.enablePoke;

        // 加载专用图标
        private static readonly Texture2D Icon = ContentFinder<Texture2D>.Get("UI/Designators/GodPoke", false)
                                                ?? ContentFinder<Texture2D>.Get("UI/Designators/GodHand");

        private PokeGodHandController controller;

        public Designator_PokeGodHand()
        {
            defaultLabel = "GodHand.Poke.Label".Translate();
            // 简短描述
            defaultDesc = "GodHand.Poke.Desc.Simple".Translate();
            icon = Icon ?? BaseContent.BadTex;
            useMouseIcon = true;
            soundDragSustain = null;
            soundDragChanged = null;
            soundSucceeded = null;

            controller = new PokeGodHandController();
        }

        public override void ProcessInput(Event ev)
        {
            // 检查是否第一次使用
            if (!GodHandModMain.Settings.hasSeenHelp_Poke)
            {
                GodHandModMain.Settings.hasSeenHelp_Poke = true;
                GodHandModMain.Settings.Write();

                Find.WindowStack.Add(new Dialog_GodHandInfo(
                    Icon,
                    "GodHand.Poke.Label".Translate(),
                    "GodHand.Poke.Desc".Translate()
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
                        Icon,
                        "GodHand.Poke.Label".Translate(),
                        "GodHand.Poke.Desc".Translate()
                    ));
                });
            }
        }

        public override AcceptanceReport CanDesignateCell(IntVec3 c)
        {
            // 总是允许选择
            return true;
        }

        public override void DesignateSingleCell(IntVec3 c)
        {
            // 禁用默认点击逻辑
        }

        public override void SelectedUpdate()
        {
            controller.Update();
            HandleInput();

            if (controller.IsAiming)
            {
                // 绘制瞄准线
                controller.DrawAimLine();
            }
        }

        public override void DrawMouseAttachments()
        {
            base.DrawMouseAttachments();

            if (!controller.IsAiming)
            {
                // 高亮鼠标下的Pawn
                IntVec3 mouseCell = UI.MouseCell();
                if (mouseCell.InBounds(Map))
                {
                    Pawn pawn = mouseCell.GetFirstPawn(Map);
                    if (pawn != null)
                    {
                        GenDraw.DrawTargetHighlight(pawn);
                        GenMapUI.DrawThingLabel(pawn, pawn.LabelShortCap, Color.red);
                    }
                    else
                    {
                        // 尝试高亮尸体
                        List<Thing> things = mouseCell.GetThingList(Map);
                        foreach (Thing t in things)
                        {
                            if (t is Corpse corpse)
                            {
                                GenDraw.DrawTargetHighlight(corpse);
                                GenMapUI.DrawThingLabel(corpse, corpse.LabelShortCap, Color.yellow);
                                break;
                            }
                        }
                    }
                }
            }
        }

        // 禁用原版的高亮框
        public override void RenderHighlight(List<IntVec3> dragCells) { }

        private void HandleInput()
        {
            Map map = Find.CurrentMap;
            if (map == null) return;

            IntVec3 mouseCell = UI.MouseCell();
            if (!mouseCell.InBounds(map)) return;

            // 尝试开始互动
            if (Input.GetMouseButtonDown(0))
            {
                Pawn pawn = mouseCell.GetFirstPawn(map);
                if (pawn != null)
                {
                    controller.StartInteraction(map, pawn, mouseCell);
                    Event.current?.Use();
                }
                else
                {
                    // 尝试找尸体
                    List<Thing> things = mouseCell.GetThingList(map);
                    foreach (Thing t in things)
                    {
                        if (t is Corpse corpse)
                        {
                            controller.StartInteraction(map, corpse, mouseCell);
                            Event.current?.Use();
                            break;
                        }
                    }
                }
            }

            // 松开执行操作
            if (Input.GetMouseButtonUp(0))
            {
                if (controller.IsInteractng)
                {
                    controller.EndInteraction(map, mouseCell);
                    Event.current?.Use();
                }
            }
        }
    }
}
