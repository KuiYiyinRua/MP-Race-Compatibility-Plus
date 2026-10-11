using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.RaceTrioCompatibility
{
    // Targets are verified against ASEL SHA256 1B840E5C...AEC3353.
    // Keep stored weapons out of generic Thing serialization: this mod stores
    // them in a plain List without an IThingHolder ownership chain.
    internal static class MonolynActions
    {
        static Type conduitType, storageType, weaponOuterType, weaponInnerType;
        static FieldInfo customLabel, connectedConduit, connectedThingId, weapons;
        static FieldInfo weaponOuterOwner, weaponOuterThing, weaponInnerOuter, weaponInnerThing;
        static FieldInfo nameDialogComp, phoneConfusion, phoneActiveTick;
        static MethodInfo ejectNative;
        static ISyncMethod eject;
        static FieldInfo storedCorpse, baptismPawn;

        internal static void Apply(Harmony harmony)
        {
            conduitType = RequiredType("ASEL.CompLightConduit");
            customLabel = RequiredField(conduitType, "customLabel");
            connectedConduit = RequiredField(conduitType, "connectedConduit");
            connectedThingId = RequiredField(conduitType, "connectedThingID");
            storageType = RequiredType("ASEL.CompWeaponCodexStorage");
            weapons = RequiredField(storageType, "weapons");
            weaponOuterType = RequiredType("ASEL.CompWeaponCodexStorage+<>c__DisplayClass4_0");
            weaponInnerType = RequiredType("ASEL.CompWeaponCodexStorage+<>c__DisplayClass4_1");
            weaponOuterOwner = RequiredField(weaponOuterType, "<>4__this");
            weaponOuterThing = RequiredField(weaponOuterType, "thing");
            weaponInnerOuter = RequiredField(weaponInnerType, "CS$<>8__locals1");
            weaponInnerThing = RequiredField(weaponInnerType, "twc");
            ejectNative = RequiredMethod(weaponInnerType, "<CompGetGizmosExtra>b__1");
            var nameDialog = RequiredType("ASEL.Dialog_NameConduit");
            nameDialogComp = RequiredField(nameDialog, "comp");
            var phone = RequiredType("ASEL.CompPhoneBooth");
            phoneConfusion = RequiredField(phone, "confusion");
            phoneActiveTick = RequiredField(phone, "activeTick");
            storedCorpse = RequiredField(RequiredType("ASEL.CompCorpseStorage"), "StoredCorpse");
            baptismPawn = RequiredField(RequiredType("ASEL.Building_BaptismalFont"), "selectedPawn");

            Register("ASEL.CompComputationPit", "<CompGetGizmosExtra>b__9_0");
            Register("ASEL.CompComputationPit", "<CompGetGizmosExtra>b__9_1");
            Register("ASEL.CompCorpseStorage", "<CompGetGizmosExtra>b__7_0");
            Register("ASEL.Building_BaptismalFont", "<GetGizmos>b__66_2");
            Register("ASEL.CompPhoneBooth", "<CompGetGizmosExtra>b__48_1");
            harmony.Patch(RequiredMethod(storedCorpse.DeclaringType, "<CompGetGizmosExtra>b__7_0"),
                prefix: new HarmonyMethod(typeof(MonolynActions), nameof(CorpseReleasePrefix)),
                transpiler: new HarmonyMethod(typeof(MonolynActions), nameof(CorpsePlacement)));
            harmony.Patch(RequiredMethod(RequiredType("ASEL.Building_BaptismalFont"), "<GetGizmos>b__66_2"),
                prefix: new HarmonyMethod(typeof(MonolynActions), nameof(BaptismCancelPrefix)));

            // Native SelectPawn (including its forced confirmation) ultimately
            // reaches MP's registered Building_Enterable.SelectPawn. Do not
            // register the override or its confirmation a second time.
            eject = MP.RegisterSyncMethod(typeof(MonolynActions), nameof(EjectWeapon));
            MP.RegisterSyncField(customLabel);
            harmony.Patch(ejectNative, prefix: new HarmonyMethod(typeof(MonolynActions), nameof(EjectPrefix)));
            harmony.Patch(RequiredMethod(nameDialog, "DoWindowContents", typeof(UnityEngine.Rect)),
                prefix: new HarmonyMethod(typeof(MonolynActions), nameof(NamePrefix)),
                finalizer: new HarmonyMethod(typeof(MonolynActions), nameof(NameFinalizer)));
            harmony.Patch(RequiredMethod(conduitType, "PostExposeData"),
                prefix: new HarmonyMethod(typeof(MonolynActions), nameof(ConduitSavePrefix)));
            harmony.Patch(RequiredMethod(phone, "PostExposeData"),
                postfix: new HarmonyMethod(typeof(MonolynActions), nameof(PhoneSavePostfix)));
            Log.Message("[MonolynActions] resolved=5 callbacks, weapon ejection, rename, conduit save, phone save; missing=0");
        }

        static void Register(string typeName, string methodName)
        {
            MP.RegisterSyncMethod(RequiredMethod(RequiredType(typeName), methodName), null);
        }

        // Two peers may queue the same action before the first command replays.
        static bool CorpseReleasePrefix(object __instance) => !MP.IsInMultiplayer ||
            storedCorpse.GetValue(__instance) != null && ((ThingComp)__instance).parent.MapHeld != null;
        static Map CorpseMap(Thing parent) => MP.IsInMultiplayer ? parent.MapHeld : parent.Map;
        static IntVec3 CorpsePosition(Thing parent) => MP.IsInMultiplayer ? parent.PositionHeld : parent.Position;
        static IEnumerable<CodeInstruction> CorpsePlacement(IEnumerable<CodeInstruction> instructions)
        {
            int maps = 0, positions = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.operand is MethodInfo getter && getter.DeclaringType == typeof(Thing))
                {
                    // This component lives on a lantern, including worn apparel.
                    // The native callback's parent.Map/Position only describe a
                    // ground item. Keep placement at the actual held game owner.
                    if (getter.Name == "get_Map")
                    {
                        instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(MonolynActions), nameof(CorpseMap)); maps++;
                    }
                    else if (getter.Name == "get_Position")
                    {
                        instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(MonolynActions), nameof(CorpsePosition)); positions++;
                    }
                }
                yield return instruction;
            }
            if (maps != 1 || positions != 1) throw new InvalidOperationException("Native corpse placement shape changed: " + maps + "/" + positions);
        }
        static bool BaptismCancelPrefix(object __instance) => !MP.IsInMultiplayer || baptismPawn.GetValue(__instance) != null;

        static bool EjectPrefix(object __instance)
        {
            if (!MP.IsInMultiplayer || !MP.InInterface || MP.IsExecutingSyncCommand) return true;
            var outer = weaponInnerOuter.GetValue(__instance);
            var owner = weaponOuterOwner.GetValue(outer) as ThingComp;
            var weapon = weaponInnerThing.GetValue(__instance) as Thing;
            if (owner != null && weapon != null) eject.DoSync(null, owner, weapon.thingIDNumber);
            return false;
        }

        static void EjectWeapon(ThingComp owner, int weaponId)
        {
            if (owner?.parent == null || owner.parent.Destroyed || owner.parent.Map == null || !storageType.IsInstanceOfType(owner)) return;
            var list = weapons.GetValue(owner) as IList;
            if (list == null) return;
            ThingWithComps weapon = null;
            foreach (var item in list)
                if (item is ThingWithComps candidate && candidate.thingIDNumber == weaponId) { weapon = candidate; break; }
            if (weapon == null || weapon.Destroyed || !weapon.def.IsWeapon) return;
            var outer = Activator.CreateInstance(weaponOuterType, true);
            var inner = Activator.CreateInstance(weaponInnerType, true);
            weaponOuterOwner.SetValue(outer, owner);
            weaponOuterThing.SetValue(outer, weapon);
            weaponInnerOuter.SetValue(inner, outer);
            weaponInnerThing.SetValue(inner, weapon);
            ejectNative.Invoke(inner, null);
        }

        static void NamePrefix(object __instance, out bool __state)
        {
            __state = false;
            if (!MP.IsInMultiplayer || !MP.InInterface) return;
            var comp = nameDialogComp.GetValue(__instance);
            if (comp == null) return;
            MP.WatchBegin();
            __state = true;
            MP.Watch(conduitType, "customLabel", comp);
        }

        static Exception NameFinalizer(Exception __exception, bool __state)
        {
            if (__state) MP.WatchEnd();
            return __exception;
        }

        static void ConduitSavePrefix(object __instance)
        {
            // Native code assigns connectedThingID AFTER Scribe_Values.Look;
            // snapshots otherwise contain the preceding connection (or null).
            if (Scribe.mode != LoadSaveMode.Saving) return;
            var target = connectedConduit.GetValue(__instance) as ThingComp;
            connectedThingId.SetValue(__instance, target?.parent != null && !target.parent.Destroyed ? target.parent.ThingID : null);
        }

        static void PhoneSavePostfix(object __instance)
        {
            bool confusion = (bool)phoneConfusion.GetValue(__instance);
            int activeTick = (int)phoneActiveTick.GetValue(__instance);
            Scribe_Values.Look(ref confusion, "meowPhoneConfusion", true);
            Scribe_Values.Look(ref activeTick, "meowPhoneActiveTick", 0);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                phoneConfusion.SetValue(__instance, confusion);
                phoneActiveTick.SetValue(__instance, activeTick);
            }
        }

        static Type RequiredType(string name) => AccessTools.TypeByName(name) ?? throw new TypeLoadException(name);
        static FieldInfo RequiredField(Type type, string name) => AccessTools.Field(type, name) ?? throw new MissingFieldException(type.FullName, name);
        static MethodInfo RequiredMethod(Type type, string name, params Type[] args) => AccessTools.DeclaredMethod(type, name, args) ?? throw new MissingMethodException(type.FullName, name);
    }
}
