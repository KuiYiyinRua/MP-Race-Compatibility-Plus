using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace GodHandMod
{
    // 模块安装驱动类
    public class JobDriver_InstallTurretModule : JobDriver
    {
        private const TargetIndex ModuleIndex = TargetIndex.A;

        private Thing Module => job.GetTarget(ModuleIndex).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Module, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // 确保模块有效
            this.FailOnDestroyedOrNull(ModuleIndex);
            this.FailOnForbidden(ModuleIndex);

            // 前往模块位置
            yield return Toils_Goto.GotoThing(ModuleIndex, PathEndMode.Touch);

            // 安装模块
            Toil installToil = new Toil();
            installToil.initAction = delegate
            {
                Thing module = Module;
                if (module == null || module.Destroyed)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                // 找到炮塔头
                GodHandTurretHead turretHead = pawn.apparel?.WornApparel?.OfType<GodHandTurretHead>().FirstOrDefault();
                if (turretHead == null)
                {
                    Messages.Message("GodHand.TurretModule.NoTurretHead".Translate(pawn.LabelShort), MessageTypeDefOf.RejectInput);
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                // 找到可以安装的槽位
                TurretSlot targetSlot = null;
                int moduleCase = GetModuleCase(module);

                foreach (var slot in turretHead.TurretSlots)
                {
                    if (slot.hasModuleSystem && slot.SlotIsAvailable(moduleCase, module.def))
                    {
                        targetSlot = slot;
                        break;
                    }
                }

                if (targetSlot == null)
                {
                    Messages.Message("GodHand.TurretModule.NoAvailableSlot".Translate(module.LabelCap), MessageTypeDefOf.RejectInput);
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                // 安装模块
                if (targetSlot.AddModule(module))
                {
                    module.DeSpawn();
                    Messages.Message("GodHand.TurretModule.Installed".Translate(pawn.LabelShort, module.LabelCap), MessageTypeDefOf.PositiveEvent);
                }
                else
                {
                    Messages.Message("GodHand.TurretModule.SlotFull".Translate(module.LabelCap), MessageTypeDefOf.RejectInput);
                    EndJobWith(JobCondition.Incompletable);
                }
            };
            installToil.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return installToil;
        }

        // 缓存反射结果
        private static readonly Dictionary<Type, System.Reflection.PropertyInfo> propCache = new Dictionary<Type, System.Reflection.PropertyInfo>();
        private static readonly Dictionary<Type, System.Reflection.FieldInfo> fieldCache = new Dictionary<Type, System.Reflection.FieldInfo>();

        private int GetModuleCase(Thing module)
        {
            if (module == null) return 0;
            Type type = module.GetType();
            Type defType = module.def.GetType();

            try
            {
                if (!propCache.TryGetValue(type, out var prop))
                {
                    prop = type.GetProperty("moduleCase", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    propCache[type] = prop;
                }
                if (prop != null) return (int)prop.GetValue(module);

                if (!fieldCache.TryGetValue(defType, out var field))
                {
                    field = defType.GetField("moduleCase", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    fieldCache[defType] = field;
                }
                if (field != null) return (int)field.GetValue(module.def);
            }
            catch { }
            return 0;
        }
    }
}
