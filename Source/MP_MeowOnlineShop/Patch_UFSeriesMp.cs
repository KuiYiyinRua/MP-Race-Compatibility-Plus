// Adapted from HKXluo & GPT5.4 (Workshop uploader MAO_LIULI), UF series MP patch.
// https://steamcommunity.com/sharedfiles/filedetails/?id=3737883119
// https://git.liulikeji.cn/xingluo/uf-multiplayer-compat-pack
// Source commit: dbd0d3509fa19dc8da0e6c9f6bec889dce8a2789.
// About.xml explicitly permits viewing, learning and secondary modification;
// upstream has no separate license. Do not relicense this adapted file as MIT.
// Local changes: MP-only UI, preserve original command objects/disabled metadata,
// skip when standalone UF patch is loaded; defer remote map generation, order-dialog
// effects and orbital laser pending full transaction validation. RNG handled separately.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace MP_MeowOnlineShop
{
    internal static class Patch_UFSeriesMp
    {
        private const string TopTurretTypeName = "SRA.HediffComp_TopTurret";
        private const string HolographicTypeName = "SRA.CompHolographic";
        private const string UFLIHolographicTypeName = "UFLIFB.CompHolographic";
        private const string ClearTimedConditionsTypeName = "SRA.CompClearTimedGameConditions";
        private const string TurretTypeName = "SRA.Building_TurretGunHasSpeed";
        private const string PulseElectrodeTypeName = "SRA.CompPulseElectrode";
        private const string LaserADSTypeName = "SRA.CompLaserADS";
        private const string WeaponSwitcherTypeName = "SRA.HediffComp_WeaponSwitcher";
        private const string AbsoluteTerrorFieldTypeName = "ATFieldGenerator.Comp_AbsoluteTerrorField";
        private const string TurbojetFlightTypeName = "TurbojetBackpack.CompTurbojetFlight";
        private const string TurbojetInterceptorTypeName = "TurbojetBackpack.CompInterceptorSystem";
        private const string TurbojetJumpVerbTypeName = "TurbojetBackpack.Verb_TurbojetJump";
        private const string TurbojetDashVerbTypeName = "TurbojetBackpack.Verb_TurbojetDash";

        private static readonly Type TopTurretType = AccessTools.TypeByName(TopTurretTypeName);
        private static readonly Type HolographicType = AccessTools.TypeByName(HolographicTypeName);
        private static readonly Type UFLIHolographicType = AccessTools.TypeByName(UFLIHolographicTypeName);
        private static readonly Type ClearTimedConditionsType = AccessTools.TypeByName(ClearTimedConditionsTypeName);
        private static readonly Type TurretType = AccessTools.TypeByName(TurretTypeName);
        private static readonly Type PulseElectrodeType = AccessTools.TypeByName(PulseElectrodeTypeName);
        private static readonly Type LaserADSType = AccessTools.TypeByName(LaserADSTypeName);
        private static readonly Type WeaponSwitcherType = AccessTools.TypeByName(WeaponSwitcherTypeName);
        private static readonly Type AbsoluteTerrorFieldType = AccessTools.TypeByName(AbsoluteTerrorFieldTypeName);
        private static readonly Type TurbojetFlightType = AccessTools.TypeByName(TurbojetFlightTypeName);
        private static readonly Type TurbojetInterceptorType = AccessTools.TypeByName(TurbojetInterceptorTypeName);
        private static readonly Type TurbojetJumpVerbType = AccessTools.TypeByName(TurbojetJumpVerbTypeName);
        private static readonly Type TurbojetDashVerbType = AccessTools.TypeByName(TurbojetDashVerbTypeName);
        private static bool applied;

        internal static void Apply(Harmony harmony)
        {
            if (!MP.enabled || harmony == null || applied || ModsConfig.IsActive("hkxluo.ufseries.multiplayer.compat"))
                return;

            applied = true;

            RegisterOwnSyncMethods();
            RegisterDirectSyncMethods();
            PatchLambdaBackedGizmos(harmony);
            Log.Message("[MP-MeowOnlineShop] UF series UI compatibility registered (monitor/order/laser deferred).");
        }

        private static void RegisterOwnSyncMethods()
        {
            MP.RegisterSyncMethod(typeof(Patch_UFSeriesMp), nameof(SyncToggleTopTurretFireAtWill));
            MP.RegisterSyncMethod(typeof(Patch_UFSeriesMp), nameof(SyncToggleTurretHoldFire));
            MP.RegisterSyncMethod(typeof(Patch_UFSeriesMp), nameof(SyncToggleHolographicAutoplay));
            MP.RegisterSyncMethod(typeof(Patch_UFSeriesMp), nameof(SyncToggleOptionalHolographicAutoplay));
            MP.RegisterSyncMethod(typeof(Patch_UFSeriesMp), nameof(SyncStartOptionalHolographicTransition));
            MP.RegisterSyncMethod(typeof(Patch_UFSeriesMp), nameof(SyncTogglePulseElectrodeArmed));
            MP.RegisterSyncMethod(typeof(Patch_UFSeriesMp), nameof(SyncSetPulseElectrodeForcedTarget));
            MP.RegisterSyncMethod(typeof(Patch_UFSeriesMp), nameof(SyncResetPulseElectrodeTarget));
            MP.RegisterSyncMethod(typeof(Patch_UFSeriesMp), nameof(SyncCycleLaserADSMode));
            MP.RegisterSyncMethod(typeof(Patch_UFSeriesMp), nameof(SyncAdjustLaserADSMinInterceptDamage));
            MP.RegisterSyncMethod(typeof(Patch_UFSeriesMp), nameof(SyncSetLaserADSForcedTarget));
            MP.RegisterSyncMethod(typeof(Patch_UFSeriesMp), nameof(SyncResetLaserADSTarget));
            MP.RegisterSyncMethod(typeof(Patch_UFSeriesMp), nameof(SyncAdjustATFieldRadius));
            MP.RegisterSyncMethod(typeof(Patch_UFSeriesMp), nameof(SyncSwitchWeapon));
            MP.RegisterSyncMethod(typeof(Patch_UFSeriesMp), nameof(SyncToggleATFieldReflectMode));
            MP.RegisterSyncMethod(typeof(Patch_UFSeriesMp), nameof(SyncToggleATFieldSuppressExplosions));
            MP.RegisterSyncMethod(typeof(Patch_UFSeriesMp), nameof(SyncToggleATFieldRedirectSkyfallers));
            MP.RegisterSyncMethod(typeof(Patch_UFSeriesMp), nameof(SyncToggleATFieldAntiTeleport));
            MP.RegisterSyncMethod(typeof(Patch_UFSeriesMp), nameof(SyncCycleTurbojetFlightMode));
            MP.RegisterSyncMethod(typeof(Patch_UFSeriesMp), nameof(SyncToggleTurbojetCombatMode));
            MP.RegisterSyncMethod(typeof(Patch_UFSeriesMp), nameof(SyncToggleTurbojetInterceptor));
        }

        private static void RegisterDirectSyncMethods()
        {
            TryRegisterSyncMethod(TurretType, "ExtractShell");
            TryRegisterSyncMethod(TurretType, "ResetForcedTarget");
            TryRegisterSyncMethod(TurretType, "OrderAttack");
            TryRegisterSyncMethod(ClearTimedConditionsType, "ClearTimedConditions");
            TryRegisterSyncMethod(TurbojetJumpVerbType, "OrderJump");
            TryRegisterSyncMethod(TurbojetDashVerbType, "CastJump");
        }

        private static void PatchLambdaBackedGizmos(Harmony harmony)
        {
            TryPatchPostfix(harmony, TopTurretType, "CompGetGizmos", nameof(TopTurretGizmosPostfix));
            TryPatchPostfix(harmony, TurretType, "GetGizmos", nameof(BuildingTurretGizmosPostfix));
            if (Patch_UFHologramClockMp.Apply(harmony, HolographicType))
                TryPatchPostfix(harmony, HolographicType, "CompGetGizmosExtra", nameof(HolographicGizmosPostfix));
            if (Patch_UFHologramClockMp.Apply(harmony, UFLIHolographicType))
                TryPatchPostfix(harmony, UFLIHolographicType, "CompGetGizmosExtra", nameof(UFLIHolographicGizmosPostfix));
            TryPatchPostfix(harmony, PulseElectrodeType, "CompGetGizmosExtra", nameof(PulseElectrodeGizmosPostfix));
            TryPatchPostfix(harmony, LaserADSType, "CompGetGizmosExtra", nameof(LaserADSGizmosPostfix));
            TryPatchPostfix(harmony, AbsoluteTerrorFieldType, "CompGetGizmosExtra", nameof(ATFieldGizmosPostfix));
            TryPatchPostfix(harmony, WeaponSwitcherType, "CompGetGizmos", nameof(WeaponSwitcherGizmosPostfix));
            TryPatchPostfix(harmony, TurbojetFlightType, "CompGetWornGizmosExtra", nameof(TurbojetFlightGizmosPostfix));
            TryPatchPostfix(harmony, TurbojetInterceptorType, "CompGetWornGizmosExtra", nameof(TurbojetInterceptorGizmosPostfix));
        }

        private static void TryRegisterSyncMethod(Type type, string methodName)
        {
            if (type == null)
                return;

            var method = AccessTools.Method(type, methodName);
            if (method is MethodInfo info)
                MP.RegisterSyncMethod(info, null);
        }

        private static void TryPatchPostfix(Harmony harmony, Type type, string methodName, string postfixName)
        {
            if (type == null)
                return;

            var target = AccessTools.Method(type, methodName);
            if (target == null)
                return;

            var postfix = AccessTools.Method(typeof(Patch_UFSeriesMp), postfixName);
            harmony.Patch(target, postfix: new HarmonyMethod(postfix));
        }

        public static void TopTurretGizmosPostfix(HediffComp __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!MP.IsInMultiplayer) return;
            if (__instance?.GetType().FullName != TopTurretTypeName || __result == null)
                return;

            var list = __result.ToList();
            for (int i = 0; i < list.Count; i++)
            {
                var toggle = list[i] as Command_Toggle;
                if (toggle == null || toggle.toggleAction?.Target != __instance)
                    continue;

                toggle.toggleAction = () => SyncToggleTopTurretFireAtWill(__instance.Pawn);
                var replacement = toggle;
                list[i] = replacement;
            }

            __result = list;
        }

        public static void BuildingTurretGizmosPostfix(Building __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!MP.IsInMultiplayer) return;
            if (__instance?.GetType().FullName != TurretTypeName || __result == null)
                return;

            var list = __result.ToList();
            string holdFireLabel = "CommandHoldFire".Translate();

            for (int i = 0; i < list.Count; i++)
            {
                var toggle = list[i] as Command_Toggle;
                if (toggle == null || toggle.defaultLabel != holdFireLabel)
                    continue;

                toggle.toggleAction = () => SyncToggleTurretHoldFire(__instance);
                var replacement = toggle;
                list[i] = replacement;
            }

            __result = list;
        }

        public static void HolographicGizmosPostfix(ThingComp __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!MP.IsInMultiplayer) return;
            if (__instance?.GetType().FullName != HolographicTypeName || __result == null)
                return;

            ReplaceHolographicGizmos(__instance, ref __result, HolographicTypeName);
        }

        public static void UFLIHolographicGizmosPostfix(ThingComp __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!MP.IsInMultiplayer) return;
            if (__instance?.GetType().FullName != UFLIHolographicTypeName || __result == null)
                return;

            ReplaceHolographicGizmos(__instance, ref __result, UFLIHolographicTypeName);
        }

        private static void ReplaceHolographicGizmos(ThingComp comp, ref IEnumerable<Gizmo> gizmos, string typeName)
        {
            var list = gizmos.ToList();
            var parent = comp.parent as ThingWithComps;
            if (parent == null)
                return;

            var powerComp = parent.GetComp<CompPowerTrader>();
            string noPowerReason = TranslateHolographicProp(comp, "disableReasonNoPower");
            string changingReason = TranslateHolographicProp(comp, "disableReasonChanging");
            string nextLabel = TranslateHolographicProp(comp, "labelNext");
            string autoplayLabel = TranslateHolographicProp(comp, "labelAutoplay");
            bool isChanging = GetThingCompInt(parent, typeName, "transitionTicks") > 0;

            for (int i = 0; i < list.Count; i++)
            {
                var action = list[i] as Command_Action;
                if (action != null && !nextLabel.NullOrEmpty() && action.defaultLabel == nextLabel)
                {
                    action.action = () => SyncStartOptionalHolographicTransition(parent, typeName);
                var replacementAction = action;

                    if (powerComp != null && !powerComp.PowerOn)
                        replacementAction.Disable(noPowerReason);
                    else if (isChanging)
                        replacementAction.Disable(changingReason);

                    list[i] = replacementAction;
                    continue;
                }

                var toggle = list[i] as Command_Toggle;
                if (toggle == null || autoplayLabel.NullOrEmpty() || toggle.defaultLabel != autoplayLabel)
                    continue;

                toggle.isActive = () => GetThingCompBool(parent, typeName, "isAutoplaying");
                toggle.toggleAction = () => SyncToggleOptionalHolographicAutoplay(parent, typeName);
                var replacement = toggle;

                if (powerComp != null && !powerComp.PowerOn)
                    replacement.Disable(noPowerReason);
                else if (isChanging)
                    replacement.Disable(changingReason);

                list[i] = replacement;
            }

            gizmos = list;
        }

        public static void PulseElectrodeGizmosPostfix(ThingComp __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!MP.IsInMultiplayer) return;
            if (__instance?.GetType().FullName != PulseElectrodeTypeName || __result == null)
                return;

            var original = __result.ToList();
            if (!original.Any(g => g?.GetType().FullName == "SRA.Gizmo_PulseElectrodeController"))
                return;
            var list = original.Where(g => g == null || g.GetType().FullName != "SRA.Gizmo_PulseElectrodeController").ToList();
            var parent = __instance.parent as ThingWithComps;
            if (parent == null)
            {
                __result = list;
                return;
            }

            list.Add(CreatePulseElectrodeArmedToggle(parent));

            if (IsPulseElectrodeArmed(parent))
            {
                list.Add(CreatePulseElectrodeTargetCommand(parent));

                if (HasPulseElectrodeForcedTarget(parent))
                    list.Add(CreatePulseElectrodeCancelCommand(parent));
            }

            __result = list;
        }

        public static void ATFieldGizmosPostfix(ThingComp __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!MP.IsInMultiplayer) return;
            if (__instance?.GetType().FullName != AbsoluteTerrorFieldTypeName || __result == null)
                return;

            var parent = __instance.parent as ThingWithComps;
            if (parent == null)
                return;

            string reflectLabel = "ATField_ReflectMode_Label".Translate().ToString();
            string suppressLabel = "ATField_SuppressExplosions_Label".Translate().ToString();
            string skyfallLabel = "ATField_RedirectSkyfall_Label".Translate().ToString();
            string teleportLabel = "ATField_AntiTeleport_Label".Translate().ToString();
            var original = __result.ToList();
            if (!original.Any(g => g?.GetType().FullName == "ATFieldGenerator.Gizmo_RadiusSlider"))
                return;
            var list = original.Where(g => g == null || g.GetType().FullName != "ATFieldGenerator.Gizmo_RadiusSlider").ToList();

            for (int i = 0; i < list.Count; i++)
            {
                var toggle = list[i] as Command_Toggle;
                if (toggle == null)
                    continue;

                Action syncAction = null;
                Func<bool> isActive = toggle.isActive;

                if (toggle.defaultLabel == reflectLabel)
                {
                    syncAction = () => SyncToggleATFieldReflectMode(parent);
                    isActive = () => GetATFieldBool(parent, "reflectMode");
                }
                else if (toggle.defaultLabel == suppressLabel)
                {
                    syncAction = () => SyncToggleATFieldSuppressExplosions(parent);
                    isActive = () => GetATFieldBool(parent, "suppressExplosions");
                }
                else if (toggle.defaultLabel == skyfallLabel)
                {
                    syncAction = () => SyncToggleATFieldRedirectSkyfallers(parent);
                    isActive = () => GetATFieldBool(parent, "redirectSkyfallers");
                }
                else if (toggle.defaultLabel == teleportLabel)
                {
                    syncAction = () => SyncToggleATFieldAntiTeleport(parent);
                    isActive = () => GetATFieldBool(parent, "antiTeleport");
                }

                if (syncAction == null)
                    continue;

                toggle.isActive = isActive;
                toggle.toggleAction = syncAction;
            }

            list.Add(CreateATFieldRadiusCommand(parent, -5f));
            list.Add(CreateATFieldRadiusCommand(parent, 5f));

            __result = list;
        }

        public static void LaserADSGizmosPostfix(ThingComp __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!MP.IsInMultiplayer) return;
            if (__instance?.GetType().FullName != LaserADSTypeName || __result == null)
                return;

            var parent = __instance.parent as ThingWithComps;
            if (parent == null)
                return;

            var original = __result.ToList();
            if (!original.Any(g => g?.GetType().FullName == "SRA.Gizmo_LaserController"))
                return;
            var list = original.Where(g => g == null || g.GetType().FullName != "SRA.Gizmo_LaserController").ToList();
            list.Add(CreateLaserADSModeCommand(parent));

            if (GetLaserADSMode(parent) == 1)
            {
                list.Add(CreateLaserADSMinDamageCommand(parent, -1));
                list.Add(CreateLaserADSMinDamageCommand(parent, 1));
            }
            else if (GetLaserADSMode(parent) == 2)
            {
                list.Add(CreateLaserADSTargetCommand(parent));
                if (HasLaserADSForcedTarget(parent))
                    list.Add(CreateLaserADSCancelCommand(parent));
            }

            __result = list;
        }

        public static void WeaponSwitcherGizmosPostfix(HediffComp __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!MP.IsInMultiplayer) return;
            if (__instance?.GetType().FullName != WeaponSwitcherTypeName || __result == null)
                return;

            var list = __result.ToList();
            for (int i = 0; i < list.Count; i++)
            {
                var action = list[i] as Command_Action;
                if (action == null || action.defaultLabel != "SRAWeaponSwitcherLabel".Translate().ToString())
                    continue;

                var pawn = __instance.Pawn;
                action.action = () => OpenSyncedWeaponMenu(pawn);
            }

            __result = list;
        }

        public static void TurbojetFlightGizmosPostfix(ThingComp __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!MP.IsInMultiplayer) return;
            if (__instance?.GetType().FullName != TurbojetFlightTypeName || __result == null)
                return;

            var parent = __instance.parent as ThingWithComps;
            if (parent == null)
                return;

            string combatLabel = "Turbojet_CombatMode_Label".Translate().ToString();
            string offLabel = "Turbojet_Mode_Off_Label".Translate().ToString();
            string hoverMoveLabel = "Turbojet_Mode_HoverMove_Label".Translate().ToString();
            string hoverAlwaysLabel = "Turbojet_Mode_HoverAlways_Label".Translate().ToString();
            var list = __result.ToList();

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] is Command_Toggle toggle && toggle.defaultLabel == combatLabel)
                {
                    toggle.isActive = () => IsTurbojetCombatMode(parent);
                toggle.toggleAction = () => SyncToggleTurbojetCombatMode(parent);
                    continue;
                }

                if (list[i] is Command_Action action && IsTurbojetFlightModeLabel(action.defaultLabel, offLabel, hoverMoveLabel, hoverAlwaysLabel))
                {
                    action.action = () => SyncCycleTurbojetFlightMode(parent);
                }
            }

            __result = list;
        }

        public static void TurbojetInterceptorGizmosPostfix(ThingComp __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!MP.IsInMultiplayer) return;
            if (__instance?.GetType().FullName != TurbojetInterceptorTypeName || __result == null)
                return;

            var parent = __instance.parent as ThingWithComps;
            if (parent == null)
                return;

            string adsLabel = "Turbojet_ADS_Label".Translate().ToString();
            var list = __result.ToList();

            for (int i = 0; i < list.Count; i++)
            {
                var toggle = list[i] as Command_Toggle;
                if (toggle == null || toggle.defaultLabel != adsLabel)
                    continue;

                toggle.isActive = () => IsTurbojetInterceptorActive(parent);
                toggle.toggleAction = () => SyncToggleTurbojetInterceptor(parent);
            }

            __result = list;
        }

        public static void SyncToggleTopTurretFireAtWill(Pawn pawn)
        {
            var comp = FindTopTurretComp(pawn);
            if (comp == null)
                return;

            var field = AccessTools.Field(comp.GetType(), "fireAtWill");
            if (field == null)
                return;

            bool current = (bool)field.GetValue(comp);
            field.SetValue(comp, !current);
        }

        public static void SyncToggleTurretHoldFire(Building building)
        {
            if (building == null || building.GetType().FullName != TurretTypeName)
                return;

            var holdFireField = AccessTools.Field(building.GetType(), "holdFire");
            if (holdFireField == null)
                return;

            bool holdFire = (bool)holdFireField.GetValue(building);
            holdFire = !holdFire;
            holdFireField.SetValue(building, holdFire);

            if (holdFire)
            {
                var resetForcedTarget = AccessTools.Method(building.GetType(), "ResetForcedTarget");
                resetForcedTarget?.Invoke(building, Array.Empty<object>());
            }
        }

        public static void SyncToggleHolographicAutoplay(ThingWithComps thing)
        {
            SyncToggleOptionalHolographicAutoplay(thing, HolographicTypeName);
        }

        public static void SyncToggleOptionalHolographicAutoplay(ThingWithComps thing, string typeName)
        {
            var comp = FindThingCompByTypeName(thing, typeName);
            if (comp == null)
                return;

            var autoplayField = AccessTools.Field(comp.GetType(), "isAutoplaying");
            var timerField = AccessTools.Field(comp.GetType(), "autoplayTimerTicks");
            var props = AccessTools.Property(comp.GetType(), "Props")?.GetValue(comp);
            if (autoplayField == null || timerField == null || props == null)
                return;

            bool current = (bool)autoplayField.GetValue(comp);
            autoplayField.SetValue(comp, !current);

            int interval = (int)(AccessTools.Field(props.GetType(), "autoplayIntervalTicks")?.GetValue(props) ?? 0);
            timerField.SetValue(comp, interval);
        }

        public static void SyncStartOptionalHolographicTransition(ThingWithComps thing, string typeName)
        {
            var comp = FindThingCompByTypeName(thing, typeName);
            if (comp == null)
                return;

            AccessTools.Method(comp.GetType(), "StartTransition")?.Invoke(comp, Array.Empty<object>());
        }

        public static void SyncTogglePulseElectrodeArmed(ThingWithComps thing)
        {
            var comp = FindThingCompByTypeName(thing, PulseElectrodeTypeName);
            if (comp == null)
                return;

            var armedField = AccessTools.Field(comp.GetType(), "isArmed");
            if (armedField == null)
                return;

            bool current = (bool)armedField.GetValue(comp);
            armedField.SetValue(comp, !current);

            AccessTools.Method(comp.GetType(), "ResetTarget")?.Invoke(comp, Array.Empty<object>());
        }

        public static void SyncSetPulseElectrodeForcedTarget(ThingWithComps thing, LocalTargetInfo target)
        {
            var comp = FindThingCompByTypeName(thing, PulseElectrodeTypeName);
            if (comp == null)
                return;

            AccessTools.Method(comp.GetType(), "SetForcedTarget")?.Invoke(comp, new object[] { target });
        }

        public static void SyncResetPulseElectrodeTarget(ThingWithComps thing)
        {
            var comp = FindThingCompByTypeName(thing, PulseElectrodeTypeName);
            if (comp == null)
                return;

            AccessTools.Method(comp.GetType(), "ResetTarget")?.Invoke(comp, Array.Empty<object>());
        }

        public static void SyncCycleLaserADSMode(ThingWithComps thing)
        {
            var comp = FindThingCompByTypeName(thing, LaserADSTypeName);
            var modeField = comp != null ? AccessTools.Field(comp.GetType(), "currentMode") : null;
            if (modeField == null)
                return;

            int current = Convert.ToInt32(modeField.GetValue(comp));
            var enumType = modeField.FieldType;
            modeField.SetValue(comp, Enum.ToObject(enumType, (current + 1) % 3));
            AccessTools.Method(comp.GetType(), "ResetTarget")?.Invoke(comp, Array.Empty<object>());
        }

        public static void SyncAdjustLaserADSMinInterceptDamage(ThingWithComps thing, int direction)
        {
            var comp = FindThingCompByTypeName(thing, LaserADSTypeName);
            if (comp == null)
                return;

            var minDamageField = AccessTools.Field(comp.GetType(), "minInterceptDamage");
            var props = AccessTools.Property(comp.GetType(), "Props")?.GetValue(comp);
            var stepField = props != null ? AccessTools.Field(props.GetType(), "minDamageStep") : null;
            if (minDamageField == null || stepField == null)
                return;

            int current = (int)minDamageField.GetValue(comp);
            int step = (int)stepField.GetValue(props);
            minDamageField.SetValue(comp, Mathf.Clamp(current + step * direction, 0, 3000));
        }

        public static void SyncSetLaserADSForcedTarget(ThingWithComps thing, LocalTargetInfo target)
        {
            var comp = FindThingCompByTypeName(thing, LaserADSTypeName);
            AccessTools.Method(comp?.GetType(), "SetForcedTarget")?.Invoke(comp, new object[] { target });
        }

        public static void SyncResetLaserADSTarget(ThingWithComps thing)
        {
            var comp = FindThingCompByTypeName(thing, LaserADSTypeName);
            AccessTools.Method(comp?.GetType(), "ResetTarget")?.Invoke(comp, Array.Empty<object>());
        }

        public static void SyncAdjustATFieldRadius(ThingWithComps thing, float delta)
        {
            var comp = FindThingCompByTypeName(thing, AbsoluteTerrorFieldTypeName);
            if (comp == null)
                return;

            var radiusField = AccessTools.Field(comp.GetType(), "radius");
            var props = AccessTools.Property(comp.GetType(), "Props")?.GetValue(comp);
            if (radiusField == null || props == null)
                return;

            float current = Convert.ToSingle(radiusField.GetValue(comp));
            float min = Convert.ToSingle(AccessTools.Field(props.GetType(), "radiusMin")?.GetValue(props) ?? current);
            float max = Convert.ToSingle(AccessTools.Field(props.GetType(), "radiusMax")?.GetValue(props) ?? current);
            radiusField.SetValue(comp, Mathf.Clamp(current + delta, min, max));
        }

        public static void SyncSwitchWeapon(Pawn pawn, string weaponDefName)
        {
            var comp = FindWeaponSwitcherComp(pawn);
            if (comp == null || weaponDefName.NullOrEmpty())
                return;

            var weaponDef = DefDatabase<ThingDef>.GetNamedSilentFail(weaponDefName);
            if (weaponDef == null)
                return;

            AccessTools.Method(comp.GetType(), "SwitchToWeapon")?.Invoke(comp, new object[] { weaponDef });
        }

        public static void SyncToggleATFieldReflectMode(ThingWithComps thing)
        {
            ToggleATFieldBool(thing, "reflectMode");
        }

        public static void SyncToggleATFieldSuppressExplosions(ThingWithComps thing)
        {
            ToggleATFieldBool(thing, "suppressExplosions");
        }

        public static void SyncToggleATFieldRedirectSkyfallers(ThingWithComps thing)
        {
            ToggleATFieldBool(thing, "redirectSkyfallers");
        }

        public static void SyncToggleATFieldAntiTeleport(ThingWithComps thing)
        {
            ToggleATFieldBool(thing, "antiTeleport");
        }

        public static void SyncCycleTurbojetFlightMode(ThingWithComps thing)
        {
            var comp = FindThingCompByTypeName(thing, TurbojetFlightTypeName);
            var modeField = comp != null ? AccessTools.Field(comp.GetType(), "currentMode") : null;
            if (modeField == null)
                return;

            int current = Convert.ToInt32(modeField.GetValue(comp));
            var enumType = modeField.FieldType;
            modeField.SetValue(comp, Enum.ToObject(enumType, (current + 1) % 3));
        }

        public static void SyncToggleTurbojetCombatMode(ThingWithComps thing)
        {
            ToggleThingCompBool(thing, TurbojetFlightTypeName, "isCombatMode");
        }

        public static void SyncToggleTurbojetInterceptor(ThingWithComps thing)
        {
            ToggleThingCompBool(thing, TurbojetInterceptorTypeName, "isActive");
        }

        private static HediffComp FindTopTurretComp(Pawn pawn)
        {
            if (pawn?.health?.hediffSet?.hediffs == null)
                return null;

            foreach (var hediff in pawn.health.hediffSet.hediffs)
            {
                if (hediff == null)
                    continue;

                var compsField = AccessTools.Field(hediff.GetType(), "comps");
                var comps = compsField != null ? compsField.GetValue(hediff) as IEnumerable<HediffComp> : null;
                if (comps == null)
                    continue;

                foreach (var comp in comps)
                {
                    if (comp?.GetType().FullName == TopTurretTypeName)
                        return comp;
                }
            }

            return null;
        }

        private static ThingComp FindThingCompByTypeName(ThingWithComps thing, string typeName)
        {
            if (thing?.AllComps == null)
                return null;

            for (int i = 0; i < thing.AllComps.Count; i++)
            {
                var comp = thing.AllComps[i];
                if (comp?.GetType().FullName == typeName)
                    return comp;
            }

            return null;
        }

        private static HediffComp FindWeaponSwitcherComp(Pawn pawn)
        {
            if (pawn?.health?.hediffSet?.hediffs == null)
                return null;

            foreach (var hediff in pawn.health.hediffSet.hediffs)
            {
                if (hediff == null)
                    continue;

                var compsField = AccessTools.Field(hediff.GetType(), "comps");
                var comps = compsField != null ? compsField.GetValue(hediff) as IEnumerable<HediffComp> : null;
                if (comps == null)
                    continue;

                foreach (var comp in comps)
                {
                    if (comp?.GetType().FullName == WeaponSwitcherTypeName)
                        return comp;
                }
            }

            return null;
        }

        private static bool GetThingCompBool(ThingWithComps thing, string typeName, string fieldName)
        {
            var comp = FindThingCompByTypeName(thing, typeName);
            var field = comp != null ? AccessTools.Field(comp.GetType(), fieldName) : null;
            return field != null && (bool)field.GetValue(comp);
        }

        private static int GetThingCompInt(ThingWithComps thing, string typeName, string fieldName)
        {
            var comp = FindThingCompByTypeName(thing, typeName);
            var field = comp != null ? AccessTools.Field(comp.GetType(), fieldName) : null;
            return field != null ? Convert.ToInt32(field.GetValue(comp)) : 0;
        }

        private static void ToggleATFieldBool(ThingWithComps thing, string fieldName)
        {
            var comp = FindThingCompByTypeName(thing, AbsoluteTerrorFieldTypeName);
            var field = comp != null ? AccessTools.Field(comp.GetType(), fieldName) : null;
            if (field == null)
                return;

            bool current = (bool)field.GetValue(comp);
            field.SetValue(comp, !current);
        }

        private static void ToggleThingCompBool(ThingWithComps thing, string typeName, string fieldName)
        {
            var comp = FindThingCompByTypeName(thing, typeName);
            var field = comp != null ? AccessTools.Field(comp.GetType(), fieldName) : null;
            if (field == null)
                return;

            bool current = (bool)field.GetValue(comp);
            field.SetValue(comp, !current);
        }

        private static bool GetATFieldBool(ThingWithComps thing, string fieldName)
        {
            var comp = FindThingCompByTypeName(thing, AbsoluteTerrorFieldTypeName);
            var field = comp != null ? AccessTools.Field(comp.GetType(), fieldName) : null;
            return field != null && (bool)field.GetValue(comp);
        }

        private static float GetATFieldRadius(ThingWithComps thing)
        {
            var comp = FindThingCompByTypeName(thing, AbsoluteTerrorFieldTypeName);
            var field = comp != null ? AccessTools.Field(comp.GetType(), "radius") : null;
            return field != null ? Convert.ToSingle(field.GetValue(comp)) : 0f;
        }

        private static int GetLaserADSMode(ThingWithComps thing)
        {
            var comp = FindThingCompByTypeName(thing, LaserADSTypeName);
            var field = comp != null ? AccessTools.Field(comp.GetType(), "currentMode") : null;
            return field != null ? Convert.ToInt32(field.GetValue(comp)) : 0;
        }

        private static int GetLaserADSMinInterceptDamage(ThingWithComps thing)
        {
            var comp = FindThingCompByTypeName(thing, LaserADSTypeName);
            var field = comp != null ? AccessTools.Field(comp.GetType(), "minInterceptDamage") : null;
            return field != null ? (int)field.GetValue(comp) : 0;
        }

        private static bool HasLaserADSForcedTarget(ThingWithComps thing)
        {
            var comp = FindThingCompByTypeName(thing, LaserADSTypeName);
            var field = comp != null ? AccessTools.Field(comp.GetType(), "forcedTarget") : null;
            return field != null && ((LocalTargetInfo)field.GetValue(comp)).IsValid;
        }

        private static bool IsTurbojetCombatMode(ThingWithComps thing)
        {
            var comp = FindThingCompByTypeName(thing, TurbojetFlightTypeName);
            var field = comp != null ? AccessTools.Field(comp.GetType(), "isCombatMode") : null;
            return field != null && (bool)field.GetValue(comp);
        }

        private static bool IsTurbojetInterceptorActive(ThingWithComps thing)
        {
            var comp = FindThingCompByTypeName(thing, TurbojetInterceptorTypeName);
            var field = comp != null ? AccessTools.Field(comp.GetType(), "isActive") : null;
            return field != null && (bool)field.GetValue(comp);
        }

        private static bool IsTurbojetFlightModeLabel(string label, string offLabel, string hoverMoveLabel, string hoverAlwaysLabel)
        {
            return label == offLabel || label == hoverMoveLabel || label == hoverAlwaysLabel;
        }

        private static Command_Toggle CreatePulseElectrodeArmedToggle(ThingWithComps thing)
        {
            return new Command_Toggle
            {
                defaultLabel = "KT_Pulse_ModeOn_Tip".Translate().ToString(),
                defaultDesc = "KT_Pulse_ModeOff_Tip".Translate().ToString(),
                icon = GetPulseElectrodeIcon(thing, IsPulseElectrodeArmed(thing) ? "uiIconPath_ModeOn" : "uiIconPath_ModeOff"),
                isActive = () => IsPulseElectrodeArmed(thing),
                toggleAction = () => SyncTogglePulseElectrodeArmed(thing)
            };
        }

        private static Command_Action CreateATFieldRadiusCommand(ThingWithComps thing, float delta)
        {
            string label = delta < 0 ? "AT Radius -5" : "AT Radius +5";
            return new Command_Action
            {
                defaultLabel = label,
                defaultDesc = "ATField_Radius_Label".Translate(GetATFieldRadius(thing).ToString("F0")).ToString(),
                action = () => SyncAdjustATFieldRadius(thing, delta)
            };
        }

        private static Command_Action CreateLaserADSModeCommand(ThingWithComps thing)
        {
            return new Command_Action
            {
                defaultLabel = "KTLaserADS_ToggleModeDesc".Translate(GetLaserADSModeLabel(thing)).ToString(),
                defaultDesc = "KTLaserADS_ToggleModeDesc".Translate(GetLaserADSModeLabel(thing)).ToString(),
                icon = GetThingCompIcon(thing, LaserADSTypeName, GetLaserADSModeIconField(thing)),
                action = () => SyncCycleLaserADSMode(thing)
            };
        }

        private static Command_Action CreateLaserADSMinDamageCommand(ThingWithComps thing, int direction)
        {
            string sign = direction < 0 ? "-" : "+";
            return new Command_Action
            {
                defaultLabel = $"ADS {sign}{GetLaserADSMinDamageStep(thing)}",
                defaultDesc = "KTLaserADS_MinInterceptDamageDesc".Translate().ToString() + "\n" + GetLaserADSMinInterceptDamage(thing),
                action = () => SyncAdjustLaserADSMinInterceptDamage(thing, direction)
            };
        }

        private static Command_Action CreateLaserADSTargetCommand(ThingWithComps thing)
        {
            return new Command_Action
            {
                defaultLabel = "KTLaserADS_ManualTargeting".Translate().ToString(),
                defaultDesc = "KTLaserADS_ManualTargetingDesc".Translate().ToString(),
                icon = GetThingCompIcon(thing, LaserADSTypeName, "uiIconPath_ManualAim"),
                action = delegate
                {
                    float range = GetLaserADSGroundRange(thing);
                    PlaySoundAtThing(SoundDefOf.Tick_Tiny, thing);
                    Find.Targeter.BeginTargeting(new TargetingParameters
                    {
                        canTargetPawns = true,
                        canTargetBuildings = true,
                        canTargetItems = true,
                        mapObjectTargetsMustBeAutoAttackable = false,
                        validator = delegate(TargetInfo targ)
                        {
                            if (!targ.HasThing || thing.Map == null)
                                return false;
                            if (targ.Thing.Position.Roofed(thing.Map))
                                return false;
                            return targ.Thing.Position.DistanceToSquared(thing.Position) <= range * range;
                        }
                    }, delegate(LocalTargetInfo targ)
                    {
                        SyncSetLaserADSForcedTarget(thing, targ);
                    });
                }
            };
        }

        private static Command_Action CreateLaserADSCancelCommand(ThingWithComps thing)
        {
            return new Command_Action
            {
                defaultLabel = "KTLaserADS_CancelTargeting".Translate().ToString(),
                defaultDesc = "KTLaserADS_ManualTargetingDesc".Translate().ToString(),
                icon = GetThingCompIcon(thing, LaserADSTypeName, "uiIconPath_CancelAim"),
                action = delegate
                {
                    SyncResetLaserADSTarget(thing);
                    PlaySoundAtThing(SoundDefOf.Click, thing);
                }
            };
        }

        private static Command_Action CreatePulseElectrodeTargetCommand(ThingWithComps thing)
        {
            return new Command_Action
            {
                defaultLabel = "KT_Pulse_ManualAim_Tip".Translate().ToString(),
                defaultDesc = "KT_Pulse_ManualAim_Tip".Translate().ToString(),
                icon = GetPulseElectrodeIcon(thing, "uiIconPath_ForcedTarget"),
                action = delegate
                {
                    var comp = FindThingCompByTypeName(thing, PulseElectrodeTypeName);
                    if (comp == null)
                        return;

                    float range = GetPulseElectrodeRange(comp);
                    PlaySoundAtThing(SoundDefOf.Tick_Tiny, thing);
                    Find.Targeter.BeginTargeting(new TargetingParameters
                    {
                        canTargetPawns = true,
                        canTargetBuildings = true,
                        canTargetLocations = true,
                        validator = delegate(TargetInfo t)
                        {
                            return t.Cell.DistanceToSquared(thing.Position) <= range * range;
                        }
                    }, delegate(LocalTargetInfo t)
                    {
                        SyncSetPulseElectrodeForcedTarget(thing, t);
                    });
                }
            };
        }

        private static Command_Action CreatePulseElectrodeCancelCommand(ThingWithComps thing)
        {
            return new Command_Action
            {
                defaultLabel = "KT_Pulse_CancelTarget_Tip".Translate().ToString(),
                defaultDesc = "KT_Pulse_CancelTarget_Tip".Translate().ToString(),
                icon = GetPulseElectrodeIcon(thing, "uiIconPath_CancelTarget"),
                action = delegate
                {
                    SyncResetPulseElectrodeTarget(thing);
                    PlaySoundAtThing(SoundDefOf.Click, thing);
                }
            };
        }

        private static void PlaySoundAtThing(SoundDef sound, Thing thing)
        {
            if (sound == null || thing?.Map == null)
                return;

            sound.PlayOneShot(new TargetInfo(thing.Position, thing.Map, false));
        }

        private static Texture2D GetPulseElectrodeIcon(ThingWithComps thing, string fieldName)
        {
            return GetThingCompIcon(thing, PulseElectrodeTypeName, fieldName);
        }

        private static Texture2D GetThingCompIcon(ThingWithComps thing, string typeName, string fieldName)
        {
            var comp = FindThingCompByTypeName(thing, typeName);
            if (comp == null)
                return BaseContent.BadTex;

            var props = AccessTools.Property(comp.GetType(), "Props")?.GetValue(comp);
            if (props == null)
                return BaseContent.BadTex;

            string path = AccessTools.Field(props.GetType(), fieldName)?.GetValue(props) as string;
            if (path.NullOrEmpty())
                return BaseContent.BadTex;

            return ContentFinder<Texture2D>.Get(path, false) ?? BaseContent.BadTex;
        }

        private static float GetLaserADSGroundRange(ThingWithComps thing)
        {
            var comp = FindThingCompByTypeName(thing, LaserADSTypeName);
            var props = AccessTools.Property(comp?.GetType(), "Props")?.GetValue(comp);
            return props != null ? Convert.ToSingle(AccessTools.Field(props.GetType(), "groundRange")?.GetValue(props) ?? 0f) : 0f;
        }

        private static int GetLaserADSMinDamageStep(ThingWithComps thing)
        {
            var comp = FindThingCompByTypeName(thing, LaserADSTypeName);
            var props = AccessTools.Property(comp?.GetType(), "Props")?.GetValue(comp);
            return props != null ? (int)(AccessTools.Field(props.GetType(), "minDamageStep")?.GetValue(props) ?? 0) : 0;
        }

        private static string GetLaserADSModeLabel(ThingWithComps thing)
        {
            switch (GetLaserADSMode(thing))
            {
                case 1: return "KTLaserADS_ModeAntiAir".Translate().ToString();
                case 2: return "KTLaserADS_ModeAntiGround".Translate().ToString();
                default: return "KTLaserADS_ModeOff".Translate().ToString();
            }
        }

        private static string GetLaserADSModeIconField(ThingWithComps thing)
        {
            switch (GetLaserADSMode(thing))
            {
                case 1: return "uiIconPath_ModeAir";
                case 2: return "uiIconPath_ModeGround";
                default: return "uiIconPath_ModeOff";
            }
        }

        private static void OpenSyncedWeaponMenu(Pawn pawn)
        {
            var comp = FindWeaponSwitcherComp(pawn);
            if (comp == null)
                return;

            var linkedWeapons = AccessTools.Property(comp.GetType(), "LinkedWeapons")?.GetValue(comp) as IEnumerable<ThingDef>;
            if (linkedWeapons == null)
                return;

            var held = GetHeldWeaponDefs(pawn, linkedWeapons).ToHashSet();
            var options = new List<FloatMenuOption>();
            foreach (var weaponDef in linkedWeapons)
            {
                if (weaponDef == null || held.Contains(weaponDef))
                    continue;

                string defName = weaponDef.defName;
                options.Add(new FloatMenuOption(weaponDef.LabelCap, () => SyncSwitchWeapon(pawn, defName), shownItemForIcon: weaponDef));
            }

            if (options.Count > 0)
                Find.WindowStack.Add(new FloatMenu(options));
        }

        private static IEnumerable<ThingDef> GetHeldWeaponDefs(Pawn pawn, IEnumerable<ThingDef> linkedWeapons)
        {
            var linked = linkedWeapons.ToHashSet();
            if (pawn?.equipment?.Primary != null && linked.Contains(pawn.equipment.Primary.def))
                yield return pawn.equipment.Primary.def;

            if (pawn?.inventory?.innerContainer == null)
                yield break;

            foreach (var thing in pawn.inventory.innerContainer)
            {
                if (thing?.def != null && thing.def.IsWeapon && linked.Contains(thing.def))
                    yield return thing.def;
            }
        }

        private static bool IsPulseElectrodeArmed(ThingWithComps thing)
        {
            var comp = FindThingCompByTypeName(thing, PulseElectrodeTypeName);
            var field = comp != null ? AccessTools.Field(comp.GetType(), "isArmed") : null;
            return field != null && (bool)field.GetValue(comp);
        }

        private static bool HasPulseElectrodeForcedTarget(ThingWithComps thing)
        {
            var comp = FindThingCompByTypeName(thing, PulseElectrodeTypeName);
            var field = comp != null ? AccessTools.Field(comp.GetType(), "forcedTarget") : null;
            return field != null && ((LocalTargetInfo)field.GetValue(comp)).IsValid;
        }

        private static float GetPulseElectrodeRange(ThingComp comp)
        {
            var props = AccessTools.Property(comp.GetType(), "Props")?.GetValue(comp);
            if (props == null)
                return 0f;

            object range = AccessTools.Field(props.GetType(), "range")?.GetValue(props);
            return range is float value ? value : 0f;
        }

        private static string TranslateHolographicProp(object comp, string fieldName)
        {
            var props = AccessTools.Property(comp.GetType(), "Props")?.GetValue(comp);
            if (props == null)
                return string.Empty;

            string key = AccessTools.Field(props.GetType(), fieldName)?.GetValue(props) as string;
            if (key.NullOrEmpty())
                return string.Empty;

            return key.Translate().ToString();
        }

    }
}

