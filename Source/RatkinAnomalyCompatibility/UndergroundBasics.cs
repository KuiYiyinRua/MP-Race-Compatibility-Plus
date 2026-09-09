using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace Meow.RatkinCompatibility
{
    [StaticConstructorOnStartup]
    public static class UndergroundBasics
    {
        static readonly Harmony harmony=new Harmony("meow.ratkin.underground");
        static ISyncMethod cargo,eject;
        static Type cargoDialog;
        static Type T(string n)=>AccessTools.TypeByName("RatkinUnderground."+n)??throw new MissingMemberException(n);
        static MethodInfo M(string t,string m)=>AccessTools.Method(T(t),m)??throw new MissingMethodException(t,m);
        static HarmonyMethod H(string n)=>new HarmonyMethod(typeof(UndergroundBasics),n);
        public static object Get(object o,string f)=>AccessTools.Field(o.GetType(),f).GetValue(o);
        public static void Set(object o,string f,object v)=>AccessTools.Field(o.GetType(),f).SetValue(o,v);
        static UndergroundBasics()
        {
            if(!MP.enabled||!ModsConfig.IsActive("rku.ratkinunderground"))return;
            try{
                foreach(var pair in new[]{
                    new[]{"Comp_RKU_Radio","<CompGetGizmosExtra>b__10_1"},
                    new[]{"RKU_DrillingVehicle","<GetGizmos>b__16_0"},new[]{"RKU_DrillingVehicle","<GetGizmos>b__16_2"},
                    new[]{"RKU_DrillingVehicleInEnemyMap","<GetGizmos>b__15_2"},new[]{"RKU_DrillingVehicleInEnemyMap","<GetGizmos>b__15_3"},
                    new[]{"RKU_DrillingVehicleWithTurret","ResetForcedTarget"},new[]{"RKU_DrillingVehicleWithTurret","FireFlashbang"},
                    new[]{"RKU_DrillingVehicleWithTurret","<GetGizmos>b__36_0"},new[]{"RKU_DrillingVehicleWithTurret","<GetGizmos>b__36_2"}})
                    MP.RegisterSyncMethod(M(pair[0],pair[1]));
                MP.RegisterSyncDelegate(T("Comp_RKU_BackpackRadio"),"<>c__DisplayClass9_0","<CallBunkerBuster>b__0",new[]{"<>4__this"});
                MP.RegisterSyncDelegate(T("Comp_RKU_BackpackRadio"),"<>c__DisplayClass14_0","<CallDrillerGun>b__0",new[]{"<>4__this"});
                cargoDialog=T("RKU_DrillingVehicleCargo+Dialog_LoadDrillingCargo");
                cargo=MP.RegisterSyncMethod(typeof(UndergroundBasics),nameof(SetCargo));
                eject=MP.RegisterSyncMethod(typeof(UndergroundBasics),nameof(Eject));
                harmony.Patch(AccessTools.Method(cargoDialog,"TryAccept"),prefix:H(nameof(CargoPrefix)));
                harmony.Patch(M("Dialog_ManagePassengers","DoWindowContents"),prefix:H(nameof(PassengersPrefix)));
                harmony.Patch(M("RKU_DrillingVehiclePatch+CostToPayThisTick_Patch","Prefix"),transpiler:H(nameof(SeedRandom)));
                harmony.Patch(M("RKU_GenStep_Flesh","SpawnBranch"),transpiler:H(nameof(SeedRandom)));
                Log.Message("[RatkinMP] Underground basic targets resolved=17");
            }catch(Exception e){Log.Error("[RatkinMP] REQUIRED TARGET FAILED Underground "+e);}
        }
        static IEnumerable<CodeInstruction> SeedRandom(IEnumerable<CodeInstruction> instructions)
        {
            var constructor=AccessTools.Constructor(typeof(System.Random),Type.EmptyTypes);
            foreach(var instruction in instructions){
                if(instruction.opcode==OpCodes.Newobj&&Equals(instruction.operand,constructor)){instruction.opcode=OpCodes.Call;instruction.operand=AccessTools.Method(typeof(UndergroundBasics),nameof(NewRandom));}
                yield return instruction;
            }
        }
        static System.Random NewRandom()=>MP.IsInMultiplayer?new System.Random(Rand.Int):new System.Random();
        static bool CargoPrefix(object __instance,ref bool __result)
        {
            if(!MP.IsInMultiplayer||!MP.InInterface)return true;
            if(!(bool)AccessTools.Method(cargoDialog,"CheckForErrors").Invoke(__instance,null)){__result=false;return false;}
            var rows=(List<TransferableOneWay>)Get(__instance,"transferables");
            var selected=rows.Where(r=>r.HasAnyThing).ToList();
            cargo.DoSync(null,(ThingWithComps)Get(__instance,"vehicle"),selected.Select(r=>r.AnyThing).ToList(),selected.Select(r=>r.CountToTransfer).ToList());
            __result=true;return false;
        }
        public static void SetCargo(ThingWithComps vehicle,List<Thing> things,List<int> counts)
        {
            if(vehicle==null||!vehicle.Spawned||things==null||counts==null||things.Count!=counts.Count)return;
            var dialog=Activator.CreateInstance(cargoDialog,new object[]{vehicle,vehicle.TryGetComp<CompTransporter>()});
            AccessTools.Method(cargoDialog,"CalculateAndRecacheTransferables").Invoke(dialog,null);
            var rows=(List<TransferableOneWay>)Get(dialog,"transferables");
            for(int i=0;i<things.Count;i++){
                var row=rows.FirstOrDefault(r=>r.things.Contains(things[i]));
                if(row==null||counts[i]<0||counts[i]>row.GetMaximumToTransfer())return;
                row.AdjustTo(counts[i]);
            }
            AccessTools.Method(cargoDialog,"TryAccept").Invoke(dialog,null);
        }
        public static void Eject(ThingWithComps vehicle,Pawn pawn)
        {
            if(vehicle==null||!vehicle.Spawned||pawn==null||!(vehicle is IThingHolder holder))return;
            var passengers=holder.GetDirectlyHeldThings();if(!passengers.Contains(pawn))return;
            passengers.Remove(pawn);GenSpawn.Spawn(pawn,vehicle.Position,vehicle.Map);
            var counter=AccessTools.Field(vehicle.GetType(),"enterPawns");if(counter!=null)counter.SetValue(vehicle,Math.Max(0,(int)counter.GetValue(vehicle)-1));
        }
        static bool PassengersPrefix(Window __instance,Rect inRect)
        {
            if(!MP.IsInMultiplayer)return true;
            var vehicle=Get(__instance,"vehicle") as ThingWithComps;
            if(!(vehicle is IThingHolder holder))return true;
            Text.Font=GameFont.Medium;Widgets.Label(new Rect(0,0,inRect.width,35),"RKU.ManagePassengers".Translate());Text.Font=GameFont.Small;
            var scroll=(Vector2)Get(__instance,"scrollPosition");
            var pawns=holder.GetDirectlyHeldThings().OfType<Pawn>().ToList();
            Widgets.BeginScrollView(new Rect(0,40,inRect.width,inRect.height-100),ref scroll,new Rect(0,0,inRect.width-16,pawns.Count*35));
            for(int i=0;i<pawns.Count;i++){
                Widgets.Label(new Rect(35,i*35,inRect.width-135,30),pawns[i].LabelCap);
                if(Widgets.ButtonText(new Rect(inRect.width-100,i*35,70,30),"RKU.Eject".Translate()))eject.DoSync(null,vehicle,pawns[i]);
            }
            Widgets.EndScrollView();Set(__instance,"scrollPosition",scroll);
            if(Widgets.ButtonText(new Rect(inRect.width/2-100,inRect.height-35,200,30),"Close".Translate()))__instance.Close();
            return false;
        }
    }
}
