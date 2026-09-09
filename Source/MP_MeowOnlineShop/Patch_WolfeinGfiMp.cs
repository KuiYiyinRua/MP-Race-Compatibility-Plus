using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    internal static class Patch_WolfeinGfiMp
    {
        private const string Ns = "JL_WolfeinExpand.";
        private static ISyncMethod removeBill;
        private static Type panelType;
        private static FieldInfo panelComp;
        private static FieldInfo thresholds;
        private static Type uiCompType;

        internal static void Apply(Harmony harmony)
        {
            if (!MP.enabled || !ModsConfig.IsActive("jl.wolfeingfiexpanded")) return;
            Register("CompWeaponSwitch", "SwitchWeapon");
            Register("CompWeaponFireModeSwitch", "SwitchFireMode", typeof(bool));
            Register("CompWolfeinMechCarrier", "TrySpawnPawns");
            Register("CompWingmanStorage", "DeployAllDrones");
            Register("CompWingmanStorage", "DeployDrone", typeof(int));
            Register("CompWingmanStorage", "TryAddDroneToSlot", typeof(Pawn), typeof(int));
            Register("Hediff_MobileArtificialMoonApparatus", "<GetGizmos>b__6_1");
            Register("HediffComp_TurretGun", "<CompGetGizmos>b__45_0");
            Register("HediffComp_TurretGun", "<CompGetGizmos>b__45_1", typeof(LocalTargetInfo));
            Register("CompExtraTex", "ApplyHologramTransform", typeof(float), typeof(float), typeof(float));
            Register("CompExtraTex", "<CompGetGizmosExtra>b__25_3", typeof(LocalTargetInfo));
            removeBill = MP.RegisterSyncMethod(typeof(Patch_WolfeinGfiMp), nameof(RemoveBill));
            foreach (string name in new[] { "CompChipSlots", "CompLoadOutSlots" })
            {
                Register(name, "AddInstallBill", typeof(int), typeof(ThingDef));
                Register(name, "AddUninstallBill", typeof(int));
                harmony.Patch(Required(name, "RemoveBill", AccessTools.TypeByName(Ns + "ChipBill")),
                    prefix: new HarmonyMethod(typeof(Patch_WolfeinGfiMp), nameof(RemoveBillPrefix)));
            }
            harmony.Patch(Required("CompLoadOutSlots", "PostExposeData"),
                transpiler: new HarmonyMethod(typeof(Patch_WolfeinGfiMp), nameof(LoadOutSaveLabels)));

            panelType = AccessTools.TypeByName(Ns + "Gizmo_WingmanSystemPanel");
            panelComp = AccessTools.Field(panelType, "hediffComp");
            MP.RegisterSyncWorker<object>(PanelWorker, panelType);
            MP.RegisterSyncDelegate(panelType, "<>c__DisplayClass8_0", "<ShowWorkModeMenu>b__0",
                new[] { "<>4__this", "mode" }, Type.EmptyTypes);

            Type wingman = AccessTools.TypeByName(Ns + "HediffComp_WingmanSystem");
            thresholds = AccessTools.Field(wingman, "_droneRechargeThresholds");
            uiCompType = AccessTools.TypeByName(Ns + "CompWolfeinUI");
            MP.RegisterSyncField(AccessTools.Field(uiCompType, "savedHairStyle"))
                .PostApply((target, value) => ((ThingComp)target).parent.Notify_ColorChanged());
            harmony.Patch(Required("Dialog_HairStyleSelector", "DoWindowContents", typeof(UnityEngine.Rect)),
                prefix: new HarmonyMethod(typeof(Patch_WolfeinGfiMp), nameof(WatchHair)),
                finalizer: new HarmonyMethod(typeof(Patch_WolfeinGfiMp), nameof(EndWatchHair)));
            Register("HediffComp_WingmanSystem", "set_droneRechargeThresholds", typeof(FloatRange));
            harmony.Patch(Required("HediffComp_WingmanSystem", "get_droneRechargeThresholds"),
                prefix: new HarmonyMethod(typeof(Patch_WolfeinGfiMp), nameof(GetThresholds)));
            harmony.Patch(Required("HediffComp_WingmanSystem", "CompExposeData"),
                postfix: new HarmonyMethod(typeof(Patch_WolfeinGfiMp), nameof(ExposeThresholds)));
            harmony.Patch(Required("WindowWithParticles", "DrawParticleEffects", typeof(UnityEngine.Rect)),
                prefix: new HarmonyMethod(typeof(Patch_WolfeinGfiMp), nameof(PushUiRand)),
                finalizer: new HarmonyMethod(typeof(Patch_WolfeinGfiMp), nameof(PopUiRand)));
            Log.Message("[MP-MeowOnlineShop] Wolfein GFI action registrations installed.");
        }

        private static MethodInfo Required(string type, string method, params Type[] args)
        {
            Type resolved = AccessTools.TypeByName(Ns + type);
            MethodInfo result = resolved == null ? null : AccessTools.DeclaredMethod(resolved, method, args);
            return result ?? throw new MissingMethodException(Ns + type, method);
        }

        private static void Register(string type, string method, params Type[] args)
        {
            MP.RegisterSyncMethod(Required(type, method, args), null);
        }

        private static void PanelWorker(SyncWorker sync, ref object panel)
        {
            HediffComp comp = sync.isWriting ? panelComp.GetValue(panel) as HediffComp : null;
            sync.Bind(ref comp);
            if (!sync.isWriting)
            {
                panel = Activator.CreateInstance(panelType);
                panelComp.SetValue(panel, comp);
            }
        }

        private static bool RemoveBillPrefix(ThingComp __instance, object __0)
        {
            if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand || !MP.InInterface) return true;
            if (__0 == null) return false;
            Type type = __0.GetType();
            removeBill.DoSync(null, __instance,
                (int)AccessTools.Field(type, "slotIndex").GetValue(__0),
                Convert.ToInt32(AccessTools.Field(type, "billType").GetValue(__0)),
                (ThingDef)AccessTools.Field(type, "chipDef").GetValue(__0));
            return false;
        }

        public static void RemoveBill(ThingComp comp, int slot, int kind, ThingDef def)
        {
            if (comp == null) return;
            IList bills = AccessTools.Field(comp.GetType(), "billStack").GetValue(comp) as IList;
            if (bills == null) return;
            foreach (object bill in bills)
            {
                Type type = bill.GetType();
                if ((int)AccessTools.Field(type, "slotIndex").GetValue(bill) != slot ||
                    Convert.ToInt32(AccessTools.Field(type, "billType").GetValue(bill)) != kind ||
                    AccessTools.Field(type, "chipDef").GetValue(bill) != def) continue;
                AccessTools.Method(comp.GetType(), "RemoveBill").Invoke(comp, new[] { bill });
                break;
            }
        }

        private static void ExposeThresholds(object __instance)
        {
            FloatRange range = (FloatRange)thresholds.GetValue(__instance);
            Scribe_Values.Look(ref range, "mpWolfeinDroneRechargeThresholds", default(FloatRange));
            thresholds.SetValue(__instance, range);
        }

        private static IEnumerable<CodeInstruction> LoadOutSaveLabels(IEnumerable<CodeInstruction> instructions)
        {
            int count = 0;
            foreach (var instruction in instructions)
            {
                yield return instruction;
                if (instruction.opcode != OpCodes.Ldstr ||
                    ((string)instruction.operand != "loadOutSlots" && (string)instruction.operand != "loadOutBillStack")) continue;
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Patch_WolfeinGfiMp), nameof(LoadOutLabel)));
                count++;
            }
            if (count != 2) throw new InvalidOperationException("Wolfein loadout save labels changed: " + count);
        }

        private static string LoadOutLabel(string label, ThingComp comp)
        {
            int ordinal = 0;
            foreach (var other in comp.parent.AllComps)
            {
                if (other == comp) break;
                if (other.GetType() == comp.GetType()) ordinal++;
            }
            if (ordinal == 0) return label;
            string unique = label + "_mpWolfein" + ordinal;
            // Older saves contain repeated sibling keys. Migrate the matching occurrence without copying slot 0.
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                var parent = Scribe.loader.curXmlParent;
                var legacy = parent?.SelectNodes(label);
                if (parent != null && parent.SelectSingleNode(unique) == null && legacy != null && legacy.Count > ordinal)
                {
                    var node = parent.OwnerDocument.CreateElement(unique);
                    node.InnerXml = legacy[ordinal].InnerXml;
                    parent.AppendChild(node);
                }
            }
            return unique;
        }

        private static bool GetThresholds(object __instance, ref FloatRange __result)
        {
            if (!MP.IsInMultiplayer) return true;
            __result = (FloatRange)thresholds.GetValue(__instance);
            // Merely opening the panel must not initialize a saved field on one peer.
            if (__result.max == 0f) __result = new FloatRange(24f, (float)AccessTools.Method(__instance.GetType(), "GetDroneMaxPowerHours").Invoke(__instance, null));
            return false;
        }

        private static void WatchHair(object __instance, out bool __state)
        {
            __state = false;
            if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand) return;
            Pawn pawn = AccessTools.Field(__instance.GetType(), "pawn").GetValue(__instance) as Pawn;
            ThingComp comp = pawn?.AllComps.Find(c => uiCompType.IsInstanceOfType(c));
            if (comp == null) return;
            MP.WatchBegin();
            __state = true;
            MP.Watch(comp, "savedHairStyle");
        }

        private static Exception EndWatchHair(Exception __exception, bool __state)
        {
            if (__state) MP.WatchEnd();
            return __exception;
        }

        private static void PushUiRand(out bool __state)
        {
            __state = MP.IsInMultiplayer;
            if (__state) Rand.PushState();
        }

        private static Exception PopUiRand(Exception __exception, bool __state)
        {
            if (__state) Rand.PopState();
            return __exception;
        }
    }
}
