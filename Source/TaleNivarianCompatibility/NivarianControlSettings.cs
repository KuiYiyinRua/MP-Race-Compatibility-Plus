using System;
using System.Collections.Generic;
using Verse;
using HarmonyLib;
using Multiplayer.API;

namespace Meow.TaleNivarianCompatibility
{
    internal static class NivarianControlSettings
    {
        static ISyncField[] settings;
        static readonly string[] Names = { "evaluationIntervalDays", "activePeriodDays", "dormantPeriodDays", "activeEventFrequency", "baseEventFrequency", "raidThreatMultiplier", "rapidEventRate" };
        internal static void Apply(Harmony harmony)
        {
            var metrics=AccessTools.TypeByName("Nivarian.GameComp_NivarianNiraMetrics") ?? throw new TypeLoadException("Nivarian Nira metrics");
            settings=new ISyncField[Names.Length];
            for(int i=0;i<Names.Length;i++)
            {
                if(AccessTools.Field(metrics,Names[i])==null)throw new MissingFieldException(metrics.FullName,Names[i]);
                settings[i]=MP.RegisterSyncField(metrics,Names[i]).SetBufferChanges();
            }
            // Setters only clamp/write their own saved scalar. Watch rolls local edits back and buffers rapid slider changes.
            var draw=AccessTools.DeclaredMethod(AccessTools.TypeByName("Nivarian_Race.Code.UI.UplinkNiraMetricsTabPanel"),"DrawSettings") ?? throw new MissingMethodException("Nira settings panel");
            harmony.Patch(draw,prefix:new HarmonyMethod(typeof(NivarianControlSettings),nameof(Begin)),finalizer:new HarmonyMethod(typeof(NivarianControlSettings),nameof(End)));
            MP.RegisterSyncMethod(AccessTools.DeclaredMethod(metrics,"SetRankLevel")).SetDebugOnly();
            MP.RegisterSyncMethod(AccessTools.DeclaredMethod(metrics,"ForceEvaluateTitle")
                ?? throw new MissingMethodException(metrics.FullName,"ForceEvaluateTitle")).SetDebugOnly();
            MP.RegisterSyncMethod(AccessTools.DeclaredMethod(metrics,"AddHoloDie")
                ?? throw new MissingMethodException(metrics.FullName,"AddHoloDie")).SetDebugOnly();
            // God-mode buttons change saved progression; use the native GameComponent/Def serializers.
            var progress=AccessTools.TypeByName("Nivarian_Race.Code.Progress.ProgressManager") ?? throw new TypeLoadException("Nivarian progress manager");
            var node=AccessTools.TypeByName("Nivarian_Race.Code.Defs.ProgressNodeDef") ?? throw new TypeLoadException("Nivarian progress node");
            foreach(var name in new[]{"DebugForceUnlock","DebugForceLock"})
                MP.RegisterSyncMethod(AccessTools.DeclaredMethod(progress,name,new[]{node}) ?? throw new MissingMethodException(progress.FullName,name)).SetDebugOnly();
            var tower=AccessTools.TypeByName("Nivarian_Race.Code.Comps.BuildingComps.Comp_NivarianEnergyTower");
            foreach(var name in new[]{"ToggleBatteryTarget","SelectAllBatteries","ClearBatteryTargets"})
                MP.RegisterSyncMethod(AccessTools.DeclaredMethod(tower,name) ?? throw new MissingMethodException(tower.FullName,name));
            harmony.Patch(AccessTools.DeclaredMethod(tower,"SelectAllBatteries"),postfix:new HarmonyMethod(typeof(NivarianControlSettings),nameof(OrderBatteries)));
        }
        static void OrderBatteries(List<Thing> ___targetBatteries)
        {
            if(MP.IsInMultiplayer&&!MP.InInterface)___targetBatteries.Sort((a,b)=>a.thingIDNumber.CompareTo(b.thingIDNumber));
        }
        static void Begin(object __3,out bool __state)
        {
            __state=MP.IsInMultiplayer&&MP.InInterface;
            if(!__state)return;
            MP.WatchBegin();
            foreach(var field in settings)field.Watch(__3);
        }
        static Exception End(Exception __exception,bool __state)
        {
            if(__state)MP.WatchEnd();
            return __exception;
        }
    }
}


