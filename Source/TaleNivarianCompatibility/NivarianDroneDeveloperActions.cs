using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    internal static class NivarianDroneDeveloperActions
    {
        static Type movingType, batteryType;
        static MethodInfo navigate, orbit, clear, reset, navigateAction, orbitAction;
        static PropertyInfo props;
        static FieldInfo radius;
        static ISyncMethod moveCommand, resetCommand;
        internal static void Apply(Harmony harmony)
        {
            movingType=AccessTools.TypeByName("Nivarian.NivarianDrones.NivarianDroneComps.NivarianDroneComp_MovingBase")
                ?? throw new TypeLoadException("Nivarian moving component");
            batteryType=AccessTools.TypeByName("Nivarian.NivarianDrones.NivarianDroneComps.NivarianDroneComp_BatteryBase")
                ?? throw new TypeLoadException("Nivarian battery component");
            navigate=AccessTools.DeclaredMethod(movingType,"NavigateTo",new[]{typeof(LocalTargetInfo)}) ?? throw new MissingMethodException("NavigateTo");
            orbit=AccessTools.DeclaredMethod(movingType,"OrbitTarget",new[]{typeof(LocalTargetInfo),typeof(float)}) ?? throw new MissingMethodException("OrbitTarget");
            clear=AccessTools.DeclaredMethod(movingType,"ClearBehavior",Type.EmptyTypes) ?? throw new MissingMethodException("ClearBehavior");
            props=AccessTools.Property(movingType,"Props") ?? throw new MissingMemberException("moving Props");
            radius=AccessTools.Field(props.PropertyType,"orbitRadius") ?? throw new MissingFieldException("orbitRadius");
            reset=AccessTools.DeclaredMethod(batteryType,"<CompGetGizmosExtra>b__9_0",Type.EmptyTypes)
                ?? throw new MissingMethodException("native drone battery reset action");
            navigateAction=AccessTools.DeclaredMethod(movingType,"<CompGetGizmosExtra>b__42_0",new[]{typeof(LocalTargetInfo)}) ?? throw new MissingMethodException("native drone navigate action");
            orbitAction=AccessTools.DeclaredMethod(movingType,"<CompGetGizmosExtra>b__42_1",new[]{typeof(LocalTargetInfo)}) ?? throw new MissingMethodException("native drone orbit action");
            moveCommand=MP.RegisterSyncMethod(typeof(NivarianDroneDeveloperActions),nameof(Move)).SetDebugOnly();
            resetCommand=MP.RegisterSyncMethod(typeof(NivarianDroneDeveloperActions),nameof(ResetBattery)).SetDebugOnly();
            harmony.Patch(AccessTools.DeclaredMethod(movingType,"CompGetGizmosExtra"),postfix:new HarmonyMethod(typeof(NivarianDroneDeveloperActions),nameof(MovingGizmos)));
            harmony.Patch(AccessTools.DeclaredMethod(batteryType,"CompGetGizmosExtra"),postfix:new HarmonyMethod(typeof(NivarianDroneDeveloperActions),nameof(BatteryGizmos)));
            Log.Message("[TaleNivarianCompat] Drone developer navigate/orbit/stop/battery-reset gizmos synchronized with debug-only commands.");
        }
        static void MovingGizmos(ThingComp __instance,ref IEnumerable<Gizmo> __result)
        {
            if(MP.IsInMultiplayer&&MP.InInterface)__result=WrapMoving(__instance,__result);
        }
        static IEnumerable<Gizmo> WrapMoving(ThingComp comp,IEnumerable<Gizmo> original)
        {
            foreach(var gizmo in original)
            {
                if(gizmo is Command_Target target&&target.action?.Method.DeclaringType==movingType)
                {
                    // Exact two compiler actions of this installed component. Do not
                    // capture delegates in the command or wrap another mod's gizmo.
                    var method=target.action.Method;
                    int mode=method==navigateAction?0:method==orbitAction?1:-1;
                    if(mode>=0)target.action=t=>moveCommand.DoSync(null,comp,t,mode);
                }
                else if(gizmo is Command_Action action&&action.action?.Method.Name=="ClearBehavior"&&movingType.IsInstanceOfType(action.action.Target))
                    action.action=()=>moveCommand.DoSync(null,comp,LocalTargetInfo.Invalid,2);
                yield return gizmo;
            }
        }
        static void BatteryGizmos(ThingComp __instance,ref IEnumerable<Gizmo> __result)
        {
            if(MP.IsInMultiplayer&&MP.InInterface)__result=WrapBattery(__instance,__result);
        }
        static IEnumerable<Gizmo> WrapBattery(ThingComp comp,IEnumerable<Gizmo> original)
        {
            foreach(var gizmo in original)
            {
                if(gizmo is Command_Action action&&action.action?.Method==reset)
                    action.action=()=>resetCommand.DoSync(null,comp);
                yield return gizmo;
            }
        }
        static void Move(ThingComp comp,LocalTargetInfo target,int mode)
        {
            if(comp?.parent==null||comp.parent.Destroyed||!comp.parent.Spawned||!movingType.IsInstanceOfType(comp))return;
            if(mode==0)navigate.Invoke(comp,new object[]{target});
            else if(mode==1)
            {
                float value=(float)radius.GetValue(props.GetValue(comp,null));
                orbit.Invoke(comp,new object[]{target,value>0f?value:4f});
            }
            else if(mode==2)clear.Invoke(comp,null); // Reflection preserves virtual override dispatch.
        }
        static void ResetBattery(ThingComp comp)
        {
            if(comp?.parent==null||comp.parent.Destroyed||!batteryType.IsInstanceOfType(comp))return;
            reset.Invoke(comp,null);
        }
    }
}