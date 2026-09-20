using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    // One saved metrics component owns one history/title and shared event counters.
    // Pool participating colonies before the original formulas evaluate those inputs.
    internal static class NivarianMetricInputs
    {
        static Action<Map,Faction,bool> push;
        static Func<Map,Faction> pop;
        static MethodInfo worldGetter;
        static FieldInfo factionData,spectator;
        [ThreadStatic] static int nativeRead;
        internal static void Apply(Harmony h)
        {
            var ext=AccessTools.TypeByName("Multiplayer.Client.Factions.FactionExtensions");
            push=(Action<Map,Faction,bool>)Delegate.CreateDelegate(typeof(Action<Map,Faction,bool>),AccessTools.Method(ext,"PushFaction",new[]{typeof(Map),typeof(Faction),typeof(bool)}));
            pop=(Func<Map,Faction>)Delegate.CreateDelegate(typeof(Func<Map,Faction>),AccessTools.Method(ext,"PopFaction",new[]{typeof(Map)}));
            worldGetter=AccessTools.PropertyGetter(AccessTools.TypeByName("Multiplayer.Client.Multiplayer"),"WorldComp");
            factionData=AccessTools.Field(worldGetter.ReturnType,"factionData");
            spectator=AccessTools.Field(worldGetter.ReturnType,"spectatorFaction");
            var t=AccessTools.TypeByName("Nivarian.GameComp_NivarianNiraMetrics");
            foreach(var n in new[]{"TotalPlayerWealth","StoredFood","GrowingZoneFood","BarnAnimalFood","FreeColonistCount"})
                h.Patch(AccessTools.Method(t,n),prefix:new HarmonyMethod(typeof(NivarianMetricInputs),nameof(Sum)));
            h.Patch(AccessTools.Method(t,"AverageMood"),prefix:new HarmonyMethod(typeof(NivarianMetricInputs),nameof(Mood)));
            h.Patch(AccessTools.Method(t,"AverageFactionGoodwillTrend"),prefix:new HarmonyMethod(typeof(NivarianMetricInputs),nameof(Goodwill)));
            h.Patch(AccessTools.Method(t,"ResearchProjectsFinished"),prefix:new HarmonyMethod(typeof(NivarianMetricInputs),nameof(Research)));
            h.Patch(AccessTools.Method(t,"AssessArmed"),transpiler:new HarmonyMethod(typeof(NivarianMetricInputs),nameof(ArmedReads)));
            Log.Message("[TaleNivarianCompat] Nira metrics/history pool registered player colonies, excluding spectator; original score formulas retained.");
        }
        internal static List<Faction> Players()
        {
            var world=worldGetter.Invoke(null,null);
            var ids=((IDictionary)factionData.GetValue(world)).Keys.Cast<int>().ToHashSet();
            var excluded=(Faction)spectator.GetValue(world);
            return Find.FactionManager.AllFactionsListForReading.Where(f=>f!=excluded&&f.def.isPlayer&&ids.Contains(f.loadID)).OrderBy(f=>f.loadID).ToList();
        }
        static T InFaction<T>(Faction f,Func<T> read)
        {
            push(null,f,true);
            NivarianMetricsRefresh.Scope scope=null;
            try { NivarianMetricsRefresh.Before(out scope); return read(); }
            finally { try { NivarianMetricsRefresh.After(scope); } finally { pop(null); } }
        }
        static bool Sum(object __instance,MethodBase __originalMethod,ref float __result)
        {
            if(!MP.IsInMultiplayer||nativeRead!=0)return true;
            __result=0f;nativeRead++;
            try { foreach(var f in Players())__result+=InFaction(f,()=> (float)((MethodInfo)__originalMethod).Invoke(__instance,null)); }
            finally { nativeRead--; }
            return false;
        }
        static List<Pawn> Colonists()
        {
            if(!MP.IsInMultiplayer)return PawnsFinder.AllMaps_FreeColonists;
            return Players().SelectMany(f=>InFaction(f,()=>PawnsFinder.AllMaps_FreeColonists.ToArray())).Distinct().OrderBy(p=>p.thingIDNumber).ToList();
        }
        static List<Pawn> Spawned(Faction ignored)
        {
            if(!MP.IsInMultiplayer)return PawnsFinder.AllMaps_SpawnedPawnsInFaction(ignored);
            return Players().SelectMany(f=>InFaction(f,()=>PawnsFinder.AllMaps_SpawnedPawnsInFaction(f).ToArray())).Distinct().OrderBy(p=>p.thingIDNumber).ToList();
        }
        static bool Mood(ref float __result)
        {
            if(!MP.IsInMultiplayer)return true;
            var pawns=Colonists().Where(p=>p.needs?.mood!=null).ToArray();
            __result=pawns.Length==0?0.5f:UnityEngine.Mathf.Clamp01(pawns.Average(p=>p.needs.mood.CurLevel));
            return false;
        }
        static bool Goodwill(object __instance,MethodBase __originalMethod,ref float __result)
        {
            if(!MP.IsInMultiplayer||nativeRead!=0)return true;
            var factions=Players();__result=0f;nativeRead++;
            try { foreach(var f in factions)__result+=InFaction(f,()=> (float)((MethodInfo)__originalMethod).Invoke(__instance,null)); }
            finally { nativeRead--; }
            if(factions.Count>0)__result/=factions.Count;
            return false;
        }
        static bool Research(ref int __result)
        {
            if(!MP.IsInMultiplayer)return true;
            __result=DefDatabase<ResearchProjectDef>.AllDefs.Count(NivarianProgressOwnership.ResearchFinished);
            return false;
        }
        static bool Home(Map map)
        {
            if(!MP.IsInMultiplayer)return map.IsPlayerHome;
            var owner=map.ParentFaction;
            return Players().Contains(owner)&&InFaction(owner,()=>map.IsPlayerHome);
        }
        // Only replaces the two comparisons to OfPlayer inside AssessArmed.
        static Faction OwnedFaction(Thing thing)
        {
            var f=thing.Faction;
            return !MP.IsInMultiplayer?f:Players().Contains(f)?Faction.OfPlayer:null;
        }
        static bool ColonyMech(Pawn pawn)
        {
            if(!MP.IsInMultiplayer)return pawn.IsColonyMech;
            return Players().Contains(pawn.Faction)&&InFaction(pawn.Faction,()=>pawn.IsColonyMech);
        }
        static IEnumerable<CodeInstruction> ArmedReads(IEnumerable<CodeInstruction> instructions)
        {
            var old=new[]{AccessTools.PropertyGetter(typeof(PawnsFinder),"AllMaps_FreeColonists"),AccessTools.Method(typeof(PawnsFinder),"AllMaps_SpawnedPawnsInFaction"),AccessTools.PropertyGetter(typeof(Map),"IsPlayerHome"),AccessTools.PropertyGetter(typeof(Thing),"Faction"),AccessTools.PropertyGetter(typeof(Pawn),"IsColonyMech")};
            var replacements=new[]{"Colonists","Spawned","Home","OwnedFaction","ColonyMech"};
            var count=new int[old.Length];
            foreach(var ins in instructions)
            {
                for(int i=0;i<old.Length;i++)if(ins.Calls(old[i])){ins.opcode=OpCodes.Call;ins.operand=AccessTools.Method(typeof(NivarianMetricInputs),replacements[i]);count[i]++;break;}
                yield return ins;
            }
            var expected=new[]{1,1,1,2,1};
            if(!count.SequenceEqual(expected))throw new InvalidOperationException("Nira armed metric input sites changed: "+string.Join(",",count));
        }
    }
}
