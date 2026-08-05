using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace GodHandMod
{
    [StaticConstructorOnStartup]
    public class Building_EndRodGenerator : Building
    {
        // 全局渲染数据 (静态)
        private static ModelDef modelDef;
        private static bool modelInitialized = false;

        // 运行时状态
        private float pistonExtendProgress = 0f;
        private bool extending = true;
        private int waitTime = 0; // 等待计数器

        private const float EXTEND_SPEED = 0.4f; // 缩短推拉时间
        private const int WAIT_TICKS = 10; // 伸缩后的停顿周期

        // Meme 开关状态
        private bool isObserverRemoved = false;
        private int pickaxeAnimTicks = 0; // 镐子挥动动画倒计时

        // 纹理资源
        private static Material observerItemMat;
        private static Material pickaxeMat;

        public float PistonExtendProgress => pistonExtendProgress;
        public bool IsObserverRemoved => isObserverRemoved;

        public Vector3 GetPistonTipPosition()
        {
            float boatLocalZ = 4.3f + this.pistonExtendProgress * 0.109375f;
            Vector3 center = this.DrawPos;
            Vector3 boatWorldOffset = this.Rotation.AsQuat * new Vector3(0, 0, boatLocalZ - 2.5f);
            return center + boatWorldOffset;
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            InitializeModel();
        }

        private static void InitializeModel()
        {
            if (modelInitialized) return;
            modelDef = DefDatabase<ModelDef>.GetNamedSilentFail("GodHand_EndRodGenerator");
            observerItemMat = MaterialPool.MatFrom("Meme/MC_Observer", ShaderDatabase.Cutout, Color.white);
            pickaxeMat = MaterialPool.MatFrom("Meme/DiamondPickaxe", ShaderDatabase.Cutout, Color.white);
            modelInitialized = true;
        }

        protected override void Tick()
        {
            base.Tick();

            // 等待间隔逻辑
            if (waitTime > 0)
            {
                waitTime--;
                return;
            }

            // 动画倒计时
            if (pickaxeAnimTicks > 0) pickaxeAnimTicks--;

            // 探测器丢失停机
            if (isObserverRemoved) return;

            // 高频往复
            if (extending)
            {
                if (pistonExtendProgress == 0f) GodHandDefOf.GodHand_PistonOut.PlayOneShot(new TargetInfo(Position, Map));
                pistonExtendProgress += EXTEND_SPEED;
                if (pistonExtendProgress >= 1f)
                {
                    pistonExtendProgress = 1f;
                    extending = false;
                }
            }
            else
            {
                if (pistonExtendProgress == 1f) GodHandDefOf.GodHand_PistonIn.PlayOneShot(new TargetInfo(Position, Map));
                pistonExtendProgress -= EXTEND_SPEED;
                if (pistonExtendProgress <= 0f)
                {
                    pistonExtendProgress = 0f;
                    extending = true;
                    waitTime = WAIT_TICKS; // 完成一次循环后进入等待
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref pistonExtendProgress, "pistonExtendProgress", 0f);
            Scribe_Values.Look(ref extending, "extending", true);
            Scribe_Values.Look(ref waitTime, "waitTime", 0);
            Scribe_Values.Look(ref isObserverRemoved, "isObserverRemoved", false);
        }

        private static readonly Material BlackOutlineMat = SolidColorMaterials.SimpleSolidColorMaterial(Color.black);

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            InitializeModel();
            if (modelDef == null) return;
            // 调用统一渲染入口，不进行额外下沉，因为控制器内部已处理
            GodHandGeneratorController.RenderEndRodMachine(drawLoc, this.Rotation, Time.realtimeSinceStartup, this);
        }

        public override void Print(SectionLayer layer)
        {
            // 对于实时渲染建筑，只在特定条件下打印静态部分，或者完全不打印以避免闪烁
            // 该建筑所有部件均带动态效果，故不参与静态打印
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var g in base.GetGizmos()) yield return g;

            // Meme 开关
            yield return new Command_Toggle
            {
                defaultLabel = isObserverRemoved ? "GodHand.EndRod.InstallObserver".Translate() : "GodHand.EndRod.RemoveObserver".Translate(),
                defaultDesc = "GodHand.EndRod.ObserverDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get(isObserverRemoved ? "Meme/MC_Observer" : "Meme/DiamondPickaxe"),
                isActive = () => true,
                toggleAction = () =>
                {
                    isObserverRemoved = !isObserverRemoved;
                    if (isObserverRemoved)
                    {
                        // 播放挖掘动画和音效
                        pickaxeAnimTicks = 40;
                        SoundDef.Named("Mining").PlayOneShot(new TargetInfo(Position, Map));
                        for (int i = 0; i < 5; i++)
                            FleckMaker.ThrowDustPuff(Position, Map, 1f);
                    }
                    else
                    {
                        // 安装音效
                        SoundDefOf.Building_Complete.PlayOneShot(new TargetInfo(Position, Map));
                    }
                }
            };

            if (Prefs.DevMode)
            {
                yield return new Command_Action
                {
                    defaultLabel = "GodHand.EndRod.DebugPose".Translate(),
                    defaultDesc = "GodHand.EndRod.DebugPoseDesc".Translate(),
                    icon = null, // 暂无图标
                    action = () =>
                    {
                        Find.WindowStack.Add(new Window_PoseDebugger());
                    }
                };
            }
        }
    }
}
