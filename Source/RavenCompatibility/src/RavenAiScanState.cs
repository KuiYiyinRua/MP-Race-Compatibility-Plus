using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;
namespace MP_MeowOnlineShop
{
 public sealed class RavenAiScanState : MapComponent
 {
  internal Dictionary<int,int> mountScans=new Dictionary<int,int>();
  internal Dictionary<int,int> medicScans=new Dictionary<int,int>();
  public RavenAiScanState(Map map):base(map){}
  public override void ExposeData()
  {
   base.ExposeData();Scribe_Collections.Look(ref mountScans,"ravenMpMountScans",LookMode.Value,LookMode.Value);
   Scribe_Collections.Look(ref medicScans,"ravenMpMedicScans",LookMode.Value,LookMode.Value);
   if(mountScans==null)mountScans=new Dictionary<int,int>();
   if(medicScans==null)medicScans=new Dictionary<int,int>();
  }
 }
 internal static class RavenAiScanPersistence
 {
  private sealed class Binding{public FieldInfo states,next;public MethodInfo get;public bool mount;}
  private static readonly Dictionary<MethodBase,Binding> bindings=new Dictionary<MethodBase,Binding>();
  private sealed class Scope{public Dictionary<int,int> ticks;public object value;public int id;public Binding binding;}
  internal static void Apply(Harmony h)
  {
   foreach(var name in new[]{"JobGiver_RavenAutoMountArchon","JobGiver_RavenCombatMedic"})
   {
    var type=AccessTools.TypeByName("RavenRace.Features.FusangOrganization.AI."+name)??throw new TypeLoadException(name);
    var states=AccessTools.DeclaredField(type,"ScanStates")??throw new MissingFieldException(type.FullName,"ScanStates");
    var get=AccessTools.Method(states.FieldType,"GetOrCreateValue",new[]{typeof(Pawn)})??throw new MissingMethodException(name+" scan table getter");
    var next=AccessTools.DeclaredField(states.FieldType.GetGenericArguments()[1],"nextScanTick")??throw new MissingFieldException(name+" nextScanTick");
    var method=AccessTools.DeclaredMethod(type,"TryGiveJob",new[]{typeof(Pawn)})??throw new MissingMethodException(name,"TryGiveJob");
    bindings.Add(method,new Binding{states=states,get=get,next=next,mount=name.EndsWith("Archon",StringComparison.Ordinal)});
    h.Patch(method,prefix:new HarmonyMethod(typeof(RavenAiScanPersistence),nameof(Before)),finalizer:new HarmonyMethod(typeof(RavenAiScanPersistence),nameof(After)),transpiler:new HarmonyMethod(typeof(RavenAiScanPersistence),nameof(OrderPatients)));
   }
  }
  // Native ConditionalWeakTable entries survive on a host but disappear when a
  // client reloads. Preserve the same cooldown in the owning map's save state.
  private static void Before(Pawn __0,MethodBase __originalMethod,out Scope __state)
  {
   __state=null;if(!MP.IsInMultiplayer||__0?.Map==null)return;
   var binding=bindings[__originalMethod];
   if(__0.Faction?.def.defName!="Fusang_Hidden"||(binding.mount&&__0.kindDef.defName!="Raven_FusangImperialGuard"&&__0.kindDef.defName!="Raven_FusangGuard"))return;
   var map=__0.Map.GetComponent<RavenAiScanState>();var ticks=binding.mount?map.mountScans:map.medicScans;
   var value=binding.get.Invoke(binding.states.GetValue(null),new object[]{__0});
   ticks.TryGetValue(__0.thingIDNumber,out int tick);binding.next.SetValue(value,tick);
   __state=new Scope{ticks=ticks,value=value,id=__0.thingIDNumber,binding=binding};
  }
  private static void After(Scope __state)
  {
   if(__state!=null)__state.ticks[__state.id]=(int)__state.binding.next.GetValue(__state.value);
  }
  private static IReadOnlyList<Pawn> Ordered(IReadOnlyList<Pawn> pawns)=>MP.IsInMultiplayer?pawns.OrderBy(p=>p.thingIDNumber).ToArray():pawns;
  private static IEnumerable<CodeInstruction> OrderPatients(IEnumerable<CodeInstruction> instructions)
  {
   var getter=AccessTools.PropertyGetter(typeof(MapPawns),nameof(MapPawns.AllPawnsSpawned));int count=0;
   foreach(var instruction in instructions){yield return instruction;if(instruction.Calls(getter)){count++;yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(RavenAiScanPersistence),nameof(Ordered)));}}
   if(count!=1)throw new InvalidOperationException("Raven AI pawn scan target count: "+count);
  }
 }
}
