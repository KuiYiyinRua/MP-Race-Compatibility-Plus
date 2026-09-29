using System;
using HarmonyLib;
using Meow.FactionStoryIsolation;
using Multiplayer.API;
using RimWorld;
using Verse;
using MC=Multiplayer.Client.Multiplayer;

internal static class AcceptanceTests
{
    private static int passed, contextCalls;
    private static void Assert(bool ok,string name) { if(!ok) throw new Exception(name); passed++; Console.WriteLine("PASS "+name); }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void MapContext(Quest __instance,ref object __state) { contextCalls++; __state=new object(); }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static bool HideForeign(Quest quest,ref bool __result) { __result=false; return false; }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static bool ShouldList(Quest quest) => !quest.Hidden && quest.MatchesTab;
    internal static int Run()
    {
        var a=new Faction {loadID=100,def=new FactionDef {isPlayer=true}};
        var b=new Faction {loadID=200,def=new FactionDef {isPlayer=true}};
        var state=new IsolationSession(null){RoutingEnabled=true}; Current.Game=new Game {Component=state};
        MP.IsInMultiplayer=true; MC.GameComp.multifaction=true; MC.ExecutingCmds=true; Faction.OfPlayer=a;
        var quest=new Quest {id=7}; QuestAcceptanceGuard.Created(quest);
        Assert(QuestAcceptanceGuard.Record(quest).OwnerId==a.loadID,"ownership captured before any quest acceptance");
        // Exercise actual Harmony prefix ordering against an MP-shaped context prefix.
        var mp=new Harmony("multiplayer"); var guard=new Harmony("test.acceptance.guard");
        var accept=AccessTools.Method(typeof(Quest),nameof(Quest.Accept));
        mp.Patch(accept,prefix:new HarmonyMethod(typeof(AcceptanceTests),nameof(MapContext)));
        guard.Patch(accept,prefix:new HarmonyMethod(typeof(QuestAcceptanceGuard),nameof(QuestAcceptanceGuard.Accept)){before=new[]{"multiplayer"},priority=Priority.First},finalizer:new HarmonyMethod(typeof(QuestAcceptanceGuard),nameof(QuestAcceptanceGuard.AcceptFinalizer)));
        guard.Patch(AccessTools.Method(typeof(Quest),nameof(Quest.SetInitiallyAccepted)),prefix:new HarmonyMethod(typeof(QuestAcceptanceGuard),nameof(QuestAcceptanceGuard.AutoAccept)){before=new[]{"multiplayer"},priority=Priority.First});
        guard.Patch(AccessTools.Method(typeof(AcceptanceTests),nameof(MapContext)),prefix:new HarmonyMethod(typeof(QuestAcceptanceGuard),nameof(QuestAcceptanceGuard.AcceptMapContext)));
        Faction.OfPlayer=b; quest.Accept(new Pawn {Faction=b});
        Assert(!quest.Accepted && quest.SideEffects==0 && contextCalls==0,"B denied before acceptance body AND MP map context prefix");
        quest.Accept(new Pawn {Faction=a}); Assert(!quest.Accepted,"passing A pawn cannot impersonate A command faction");
        quest.SetInitiallyAccepted(); Assert(!quest.Accepted && quest.SideEffects==0,"foreign automatic acceptance blocked");
        Faction.OfPlayer=a; quest.Accept(new Pawn {Faction=b}); Assert(!quest.Accepted,"owner cannot accept using foreign pawn");
        quest.Accept(new Pawn {Faction=a}); Assert(quest.Accepted && quest.SideEffects==1 && contextCalls==1,"owner accepts through real patched executor once");
        quest.Accept(new Pawn {Faction=a}); Assert(quest.SideEffects==1,"repeated owner acceptance preserves executor idempotence");
        var unknown=new Quest(); unknown.Accept(null); Assert(!unknown.Accepted,"legacy or bypass-created quest fails closed");
        state.RoutingEnabled=false; unknown.Accept(null); Assert(unknown.Accepted,"switch off preserves unrecorded quest acceptance");
        var whileOff=new Quest(); QuestAcceptanceGuard.Created(whileOff); Assert(QuestAcceptanceGuard.Record(whileOff).OwnerId==a.loadID,"ownership still captured while enforcement off");
        state.RoutingEnabled=true;
        MC.ExecutingCmds=false; MC.Ticking=false; MP.InInterface=true; MC.RealPlayerFaction=b; Faction.OfPlayer=a;
        whileOff.Accept(new Pawn {Faction=a}); Assert(!whileOff.Accepted,"UI view of A cannot authorize B local player");
        MC.RealPlayerFaction=a; Faction.OfPlayer=b; whileOff.Accept(new Pawn {Faction=a}); Assert(whileOff.Accepted,"UI view of B does not veto actual A player");
        var uiCreated=new Quest(); QuestAcceptanceGuard.Created(uiCreated); Assert(QuestAcceptanceGuard.Record(uiCreated).OwnerId==-1,"UI-only creation never acquires simulation ownership");
        MP.InInterface=false; MC.ExecutingCmds=true; Faction.OfPlayer=a;
        Scribe_Values.Loading=false; QuestAcceptanceGuard.Expose(quest); var loaded=new Quest {id=7};
        Scribe_Values.Loading=true; QuestAcceptanceGuard.Expose(loaded); Faction.OfPlayer=b;
        loaded.Accept(null); Assert(!loaded.Accepted && QuestAcceptanceGuard.Record(loaded).OwnerId==a.loadID,"saved owner survives load and B remains denied (scribe model)");
        Faction.OfPlayer=a; loaded.Accept(null); Assert(loaded.Accepted,"loaded owner still accepts");
        Scribe_Values.IntValues.Clear(); var legacy=new Quest(); QuestAcceptanceGuard.Expose(legacy); legacy.Accept(null); Assert(!legacy.Accepted,"missing owner fields are not assigned to loading player");
        var corrupt=new Quest(); QuestAcceptanceGuard.Record(corrupt).OwnerId=a.loadID; QuestAcceptanceGuard.Record(corrupt).Version=99;
        corrupt.Accept(null); Assert(!corrupt.Accepted,"unsupported ownership schema fails closed");
        var automatic=new Quest(); QuestAcceptanceGuard.Created(automatic); automatic.SetInitiallyAccepted();
        Assert(automatic.Accepted,"owner automatic acceptance remains functional");
        var throwing=new Quest {ThrowOnAccept=true}; QuestAcceptanceGuard.Created(throwing); bool threw=false;
        try { throwing.Accept(null); } catch(InvalidOperationException) { threw=true; }
        Assert(threw,"acceptance exceptions propagate without being swallowed");
        var afterError=new Quest(); QuestAcceptanceGuard.Created(afterError); afterError.Accept(null);
        Assert(afterError.Accepted,"acceptance context restored after exception");
        MC.WorldComp=new Multiplayer.Client.WorldComp {spectatorFaction=a};
        var spectator=new Quest(); QuestAcceptanceGuard.Created(spectator); Assert(QuestAcceptanceGuard.Record(spectator).OwnerId==-1,"spectator cannot originate owned quests");
        MC.WorldComp=null; Faction.OfPlayer=a;
        var visible=new Quest(); QuestAcceptanceGuard.Created(visible);
        MC.ExecutingCmds=false; MP.InInterface=true; MC.RealPlayerFaction=b;
        guard.Patch(AccessTools.Method(typeof(AcceptanceTests),nameof(HideForeign)),
            prefix:new HarmonyMethod(typeof(QuestAcceptanceGuard),nameof(QuestAcceptanceGuard.KeepOtherFactionQuestsVisible)));
        mp.Patch(AccessTools.Method(typeof(AcceptanceTests),nameof(ShouldList)),
            prefix:new HarmonyMethod(typeof(AcceptanceTests),nameof(HideForeign)));
        Assert(ShouldList(visible),"foreign quest visible despite MP hiding prefix");
        visible.Accept(null); Assert(!visible.Accepted && visible.SideEffects==0,"viewable foreign quest still cannot be accepted");
        visible.Hidden=true; Assert(!ShouldList(visible),"intrinsically hidden quests remain hidden");
        visible.Hidden=false; visible.MatchesTab=false; Assert(!ShouldList(visible),"vanilla quest tab filtering remains active");
        visible.MatchesTab=true; state.RoutingEnabled=false; Assert(!ShouldList(visible),"disabled protection honors original MP hiding preference");
        state.RoutingEnabled=true; MP.IsInMultiplayer=false; Assert(!ShouldList(visible),"visibility override inactive outside multiplayer");
        guard.UnpatchAll("test.acceptance.guard"); mp.UnpatchAll("multiplayer"); return passed;
    }
}
