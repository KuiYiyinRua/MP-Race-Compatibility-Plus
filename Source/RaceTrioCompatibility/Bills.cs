using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.RaceTrioCompatibility
{
    internal static class Bills
    {
        static Type billType,compType;
        static ISyncMethod paste;
        internal static void Apply(Harmony harmony)
        {
            billType=AccessTools.TypeByName("Nivarian.NivarianBill");
            compType=AccessTools.TypeByName("Nivarian_Race.Code.Comps.BuildingComps.Comp_NivarianBillDoerCompBase");
            AccessTools.Method(typeof(Bills),nameof(RegisterWorker)).MakeGenericMethod(billType).Invoke(null,null);
            MP.RegisterSyncField(billType,"targetCount");MP.RegisterSyncField(billType,"repeatMode");
            var window=AccessTools.TypeByName("Nivarian_Race.Code.UI.Window_CryoBase");
            harmony.Patch(AccessTools.Method(window,"DrawQueueRow"),prefix:new HarmonyMethod(typeof(Bills),nameof(RowPrefix)),finalizer:new HarmonyMethod(typeof(Bills),nameof(WatchFinalizer)));
            foreach(var nested in window.GetNestedTypes(AccessTools.all))
                if(AccessTools.GetDeclaredFields(nested).Any(f=>f.FieldType==billType))
                    foreach(var method in AccessTools.GetDeclaredMethods(nested).Where(m=>m.Name.StartsWith("<GenerateRepeatModeMenu>")&&m.ReturnType==typeof(void)&&m.GetParameters().Length==0))
                        harmony.Patch(method,prefix:new HarmonyMethod(typeof(Bills),nameof(MenuPrefix)),finalizer:new HarmonyMethod(typeof(Bills),nameof(WatchFinalizer)));
            paste=MP.RegisterSyncMethod(typeof(Bills),nameof(Paste));
            harmony.Patch(AccessTools.Method(compType,"AddBills"),prefix:new HarmonyMethod(typeof(Bills),nameof(PastePrefix)));
        }
        static IList Queue(ThingComp comp)
        {
            var system=AccessTools.Field(compType,"BillSystem").GetValue(comp);
            return (IList)AccessTools.Property(system.GetType(),"Queue").GetValue(system);
        }
        static void RegisterWorker<T>() where T:class
        {
            MP.RegisterSyncWorker<T>(SyncBill);
        }
        static void SyncBill<T>(SyncWorker sync,ref T bill) where T:class
        {
            ThingComp owner=null;int index=-1;RecipeDef recipe=null;
            if(sync.isWriting&&bill!=null)
            {
                foreach(var map in Find.Maps)
                {
                    foreach(var thing in map.listerThings.AllThings)
                    {
                        if(!(thing is ThingWithComps twc))continue;
                        foreach(var comp in twc.AllComps)
                            if(compType.IsInstanceOfType(comp)){
                                int found=Queue(comp).IndexOf(bill);
                                if(found>=0){owner=comp;index=found;break;}
                            }
                        if(owner!=null)break;
                    }
                    if(owner!=null)break;
                }
                recipe=(RecipeDef)AccessTools.Field(billType,"recipe").GetValue(bill);
            }
            sync.Bind(ref owner);sync.Bind(ref index);sync.Bind(ref recipe);
            if(!sync.isWriting)
            {
                bill=null;
                if(owner==null)return;
                var queue=Queue(owner);
                if(index>=0&&index<queue.Count&&Equals(AccessTools.Field(billType,"recipe").GetValue(queue[index]),recipe))bill=queue[index] as T;
            }
        }
        static void Watch(object bill,out bool state)
        {
            state=MP.IsInMultiplayer&&MP.InInterface;
            if(!state)return;
            MP.WatchBegin();MP.Watch(bill,"targetCount");MP.Watch(bill,"repeatMode");
        }
        static void RowPrefix(object bill,out bool __state)=>Watch(bill,out __state);
        static void MenuPrefix(object __instance,out bool __state)
        {
            object bill=AccessTools.GetDeclaredFields(__instance.GetType()).First(f=>f.FieldType==billType).GetValue(__instance);
            Watch(bill,out __state);
        }
        static Exception WatchFinalizer(Exception __exception,bool __state)
        {
            if(__state)MP.WatchEnd();return __exception;
        }
        static bool PastePrefix(ThingComp __instance,IEnumerable __0)
        {
            if(!MP.IsInMultiplayer||!MP.InInterface)return true;
            var recipes=new List<RecipeDef>();var counts=new List<int>();var modes=new List<int>();
            foreach(var bill in __0){recipes.Add((RecipeDef)AccessTools.Field(billType,"recipe").GetValue(bill));counts.Add((int)AccessTools.Field(billType,"targetCount").GetValue(bill));modes.Add(Convert.ToInt32(AccessTools.Field(billType,"repeatMode").GetValue(bill)));}
            paste.DoSync(null,__instance,recipes.ToArray(),counts.ToArray(),modes.ToArray());return false;
        }
        static void Paste(ThingComp comp,RecipeDef[] recipes,int[] counts,int[] modes)
        {
            if(recipes.Length!=counts.Length||recipes.Length!=modes.Length)return;
            var list=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(billType));
            for(int i=0;i<recipes.Length;i++){
                var bill=Activator.CreateInstance(billType);
                AccessTools.Field(billType,"recipe").SetValue(bill,recipes[i]);AccessTools.Field(billType,"targetCount").SetValue(bill,counts[i]);
                var mode=AccessTools.Field(billType,"repeatMode");mode.SetValue(bill,Enum.ToObject(mode.FieldType,modes[i]));list.Add(bill);
            }
            AccessTools.Method(compType,"AddBills").Invoke(comp,new object[]{list});
        }
    }
}
