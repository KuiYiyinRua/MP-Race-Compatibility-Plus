using System;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Meow.RaceTrioCompatibility
{
    internal static class Mothership
    {
        static Type componentType,cellWorker;
        static ISyncMethod execute;
        internal static void Apply(Harmony harmony)
        {
            componentType=AccessTools.TypeByName("Nivarian.GameComp_NivarianMotherShip");
            cellWorker=AccessTools.TypeByName("Nivarian.ShipSupports.ShipSupportWorkerCellSelectorBase");
            execute=MP.RegisterSyncMethod(typeof(Mothership),nameof(Execute));
            harmony.Patch(AccessTools.Method(componentType,"TryUse"),prefix:new HarmonyMethod(typeof(Mothership),nameof(TryUsePrefix)));
        }
        static object Worker(Def def)=>AccessTools.Property(def.GetType(),"Worker").GetValue(def);
        static bool TryUsePrefix(Def sup,Map map,ref bool __result)
        {
            if(!MP.IsInMultiplayer||!MP.InInterface)return true;
            object worker=Worker(sup);
            if(worker.GetType().FullName=="Nivarian.SupportWorker_Gunship")
            {
                Find.Targeter.BeginTargeting(new TargetingParameters{canTargetLocations=true},target=>execute.DoSync(null,sup,map,target.Cell));
            }
            else if(cellWorker.IsInstanceOfType(worker))
            {
                Find.Targeter.BeginTargeting(new TargetingParameters{canTargetPawns=true,canTargetBuildings=true,canTargetLocations=true},
                    target=>execute.DoSync(null,sup,map,target.Cell),null,null,null,null,null,true,
                    target=>AccessTools.Method(worker.GetType(),"OnGUIDraw").Invoke(worker,new object[]{sup}),
                    target=>AccessTools.Method(worker.GetType(),"OnWorldDraw",new[]{typeof(IntVec3),sup.GetType()}).Invoke(worker,new object[]{UI.MouseCell(),sup}));
            }
            else execute.DoSync(null,sup,map,IntVec3.Invalid);
            __result=true;return false;
        }
        static void Execute(Def def,Map map,IntVec3 cell)
        {
            if(def==null||map==null)return;
            object component=Current.Game.components.First(c=>c.GetType()==componentType);
            if(!(bool)AccessTools.Method(componentType,"CanUse").Invoke(component,new object[]{def,map}))return;
            object worker=Worker(def);
            if(worker.GetType().FullName=="Nivarian.SupportWorker_Gunship")
            {
                if(!cell.InBounds(map))return;
                AccessTools.Method(worker.GetType(),"SpawnGunshipController").Invoke(worker,new object[]{map,cell,def});
                AccessTools.Method(worker.GetType(),"Used",new[]{def.GetType(),typeof(Map)}).Invoke(worker,new object[]{def,map});
            }
            else if(cellWorker.IsInstanceOfType(worker))
            {
                if(!cell.InBounds(map))return;
                if(worker.GetType().FullName=="Nivarian.ShipSupports.NivarianDroppodSupportWorker")
                {
                    if(!(bool)AccessTools.Method(worker.GetType(),"TryDropAt").Invoke(null,new object[]{cell,map,def}))return;
                }
                else AccessTools.Method(worker.GetType(),"OnSelect").Invoke(worker,new object[]{cell,map,def});
                AccessTools.Method(worker.GetType(),"Used",new[]{def.GetType(),typeof(Map)}).Invoke(worker,new object[]{def,map});
            }
            else AccessTools.Method(worker.GetType(),"DoEffect").Invoke(worker,new object[]{map,def});
        }
    }
}
