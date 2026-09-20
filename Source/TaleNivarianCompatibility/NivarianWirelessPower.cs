using System;
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
    internal static class NivarianWirelessPower
    {
        static PropertyInfo research;
        static Action<Map,Faction,bool> push;
        static Func<Map,Faction> pop;
        internal sealed class Scope { internal Map Map; internal SavedMapManagers Managers; }
        internal static void Apply(Harmony harmony)
        {
            var network=AccessTools.TypeByName("Nivarian.GameComp_NivarianGlobalPowerTransmitter") ?? throw new TypeLoadException("Niva power network");
            var adapter=AccessTools.TypeByName("Nivarian_Race.Code.Comps.BuildingComps.Comp_NivarianWirelessPowerAdapter") ?? throw new TypeLoadException("Niva power adapter");
            research=AccessTools.Property(network,"IsWirelessResearchCompleted") ?? throw new MissingMemberException("Wireless research");
            var context=AccessTools.TypeByName("Multiplayer.Client.Factions.FactionExtensions");
            push=(Action<Map,Faction,bool>)Delegate.CreateDelegate(typeof(Action<Map,Faction,bool>),AccessTools.DeclaredMethod(context,"PushFaction",new[]{typeof(Map),typeof(Faction),typeof(bool)}));
            pop=(Func<Map,Faction>)Delegate.CreateDelegate(typeof(Func<Map,Faction>),AccessTools.DeclaredMethod(context,"PopFaction",new[]{typeof(Map)}));
            harmony.Patch(AccessTools.DeclaredMethod(network,"TickWirelessAdapters"),transpiler:new HarmonyMethod(typeof(NivarianWirelessPower),nameof(StableDistribution)));
            foreach(var method in new[]{"TickWireless","CanPowerNow"})
                harmony.Patch(AccessTools.DeclaredMethod(adapter,method),prefix:new HarmonyMethod(typeof(NivarianWirelessPower),nameof(Before)),finalizer:new HarmonyMethod(typeof(NivarianWirelessPower),nameof(After)));
            Log.Message("[TaleNivarianCompat] wireless distribution ordered by thing ID; research checked per adapter owner.");
        }
        static IEnumerable<CodeInstruction> StableDistribution(IEnumerable<CodeInstruction> instructions)
        {
            int gates=0,copies=0;
            foreach(var instruction in instructions)
            {
                if(instruction.Calls(research.GetGetMethod()))
                {
                    instruction.opcode=OpCodes.Call;
                    instruction.operand=AccessTools.Method(typeof(NivarianWirelessPower),nameof(GlobalGate));gates++;
                }
                else if(instruction.operand is MethodInfo method && method.Name=="AddRange" && method.DeclaringType.IsGenericType && method.DeclaringType.GetGenericTypeDefinition()==typeof(List<>))
                {
                    instruction.opcode=OpCodes.Call;
                    instruction.operand=AccessTools.Method(typeof(NivarianWirelessPower),nameof(CopyOrdered)).MakeGenericMethod(method.DeclaringType.GetGenericArguments());copies++;
                }
                yield return instruction;
            }
            if(gates!=1 || copies!=1)throw new InvalidOperationException("Wireless distribution IL changed: "+gates+"/"+copies);
        }
        // The original adapter executor retains its research check, now in the
        // owning faction context. The outer world/spectator check cannot gate it.
        static bool GlobalGate(object network)=>MP.IsInMultiplayer || (bool)research.GetValue(network);
        static void CopyOrdered<T>(List<T> target,IEnumerable<T> source) where T:ThingComp
        {
            target.AddRange(MP.IsInMultiplayer ? source.OrderBy(c=>c?.parent?.thingIDNumber ?? int.MaxValue) : source);
        }
        static void Before(ThingComp __instance,out Scope __state)
        {
            __state=null;
            if(!MP.IsInMultiplayer)return;
            var map=__instance.parent?.MapHeld;
            var faction=__instance.parent?.Faction;
            if(map==null || faction?.def?.isPlayer!=true)return;
            __state=new Scope{Map=map,Managers=new SavedMapManagers(map)};push(map,faction,true);
        }
        static void After(Scope __state){if(__state!=null)try{pop(__state.Map);}finally{__state.Managers.Restore();}}
    }
}
