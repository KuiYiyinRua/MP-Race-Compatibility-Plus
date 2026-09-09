using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using Verse;
using Verse.Sound;
using RimWorld;
namespace MP_MeowOnlineShop
{
 // One versioned conversation per radio, shared by its open windows.
 // Start and option actions run only in map commands; display state stays local.
 internal static class RavenStoryActions
 {
  private sealed class Session : IExposable
  {
   public Thing radio; public string story,node; public int generation,revision;
   public Session(){}
   public void ExposeData(){Scribe_References.Look(ref radio,"radio");Scribe_Values.Look(ref story,"story");Scribe_Values.Look(ref node,"node");Scribe_Values.Look(ref generation,"generation");Scribe_Values.Look(ref revision,"revision");}
  }
  private sealed class State {public List<Session> sessions=new List<Session>();}
  private sealed class Local {public bool sent;public int expectedGeneration,generation=-1,revision=-1;}
  private static readonly ConditionalWeakTable<Game,State> states=new ConditionalWeakTable<Game,State>();
  private static ConditionalWeakTable<Window,Local> locals=new ConditionalWeakTable<Window,Local>();
  private static Type dialog,handler,storyDef;private static FieldInfo storyField,nodeField;
  private static ISyncMethod start,choose;
  private static object Read(object o,string name)=>AccessTools.Field(o.GetType(),name).GetValue(o);
  private static void Write(object o,string name,object value)=>AccessTools.Field(o.GetType(),name).SetValue(o,value);
  private static State Current=>states.GetOrCreateValue(Verse.Current.Game);
  private static Session FindSession(Thing radio)=>Current.sessions.FirstOrDefault(s=>s.radio==radio);
  internal static void Apply(Harmony h)
  {
   dialog=AccessTools.TypeByName("RavenRace.Dialog_FusangComm");handler=AccessTools.TypeByName("RavenRace.Features.StoryEngine.StoryHandler");storyDef=AccessTools.TypeByName("RavenRace.Features.StoryEngine.StoryDef");
   storyField=AccessTools.Field(handler,"<CurrentStory>k__BackingField");nodeField=AccessTools.Field(handler,"<CurrentNode>k__BackingField");
   if(dialog==null||storyField==null||nodeField==null)throw new MissingMemberException("Raven story targets");
   start=MP.RegisterSyncMethod(typeof(RavenStoryActions),nameof(Start));choose=MP.RegisterSyncMethod(typeof(RavenStoryActions),nameof(Choose));
   h.Patch(AccessTools.DeclaredMethod(dialog,"InitializeDialogue"),prefix:new HarmonyMethod(typeof(RavenStoryActions),nameof(Initialize)));
   h.Patch(AccessTools.DeclaredMethod(dialog,"DoWindowContents"),prefix:new HarmonyMethod(typeof(RavenStoryActions),nameof(Draw)));
   h.Patch(AccessTools.DeclaredMethod(dialog,"HandleChoice"),prefix:new HarmonyMethod(typeof(RavenStoryActions),nameof(BeforeChoice)));
   h.Patch(AccessTools.DeclaredMethod(AccessTools.TypeByName("RavenRace.CompFusangRadio"),"<CompGetGizmosExtra>b__3_0"),transpiler:new HarmonyMethod(typeof(RavenStoryActions),nameof(RadioReject)));

   h.Patch(AccessTools.DeclaredMethod(typeof(Game),"ExposeSmallComponents"),postfix:new HarmonyMethod(typeof(RavenStoryActions),nameof(Expose)));
  }
  private static IEnumerable<CodeInstruction> RadioReject(IEnumerable<CodeInstruction> instructions)
  {
   var message=AccessTools.Method(typeof(Messages),nameof(Messages.Message),new[]{typeof(string),typeof(LookTargets),typeof(MessageTypeDef),typeof(bool)});int count=0;
   foreach(var instruction in instructions){if(instruction.Calls(message)){instruction.opcode=OpCodes.Call;instruction.operand=AccessTools.Method(typeof(RavenStoryActions),nameof(ReportRadioReject));count++;}yield return instruction;}
   if(count!=1)throw new InvalidOperationException("Raven radio reject target="+count);
  }
  private static void ReportRadioReject(string text,LookTargets targets,MessageTypeDef type,bool historical)=>Messages.Message(text,targets,type,historical&&!MP.InInterface);
  private static bool Initialize(Window __instance)
  {
   if(!MP.IsInMultiplayer)return true;
   // Gift response text is presentation only. Its existing branch calls EndStory,
   // so leave that branch intact and do not start a second story underneath it.
   var gift=AccessTools.Field(AccessTools.TypeByName("RavenRace.Features.Operator.WorldComponent_OperatorManager"),"PostGiftMessage");
   if(gift.GetValue(null)!=null)return true;
   locals.GetOrCreateValue(__instance).expectedGeneration=FindSession((Thing)Read(__instance,"radio"))?.generation??0;Write(__instance,"fullDialogueText","正在同步通讯……");return false;
  }
  private static bool Draw(Window __instance)
  {
   if(!MP.InInterface||!locals.TryGetValue(__instance,out var local))return true;
   var radio=(Thing)Read(__instance,"radio");
   if(radio==null||!radio.Spawned){__instance.Close(false);return false;}
   var state=FindSession(radio);
   if(!local.sent){local.sent=true;start.DoSync(null,radio,local.expectedGeneration);return false;}
   if(state==null)return false;
   if(local.generation!=state.generation||local.revision!=state.revision)
   {
    Restore(Read(__instance,"storyHandler"),state);
    local.generation=state.generation;local.revision=state.revision;
    if(state.node!=null)AccessTools.DeclaredMethod(dialog,"UpdateCurrentNodeDisplay").Invoke(__instance,null);
    else Write(__instance,"fullDialogueText","......（信号保持静默）");
   }
   return true;
  }
  private static bool BeforeChoice(Window __instance,object __0)
  {
   if(!MP.InInterface)return true;
   if(!locals.TryGetValue(__instance,out var local))return false;
   var radio=(Thing)Read(__instance,"radio");if(radio==null||!radio.Spawned)return false;
   var node=nodeField.GetValue(Read(__instance,"storyHandler"));if(node==null)return false;
   int index=((IList)Read(node,"options")).IndexOf(__0);if(index<0)return false;
   SoundDefOf.Click.PlayOneShotOnCamera();choose.DoSync(null,radio,local.generation,local.revision,index);return false;
  }
  internal static string NodeFor(Thing radio)=>FindSession(radio)?.node;
  private static bool Conditions(object o)=>((IEnumerable)Read(o,"conditions")).Cast<object>().All(c=>(bool)AccessTools.Method(c.GetType(),"IsMet").Invoke(c,null));
  private static object ResolveStory(string name)=>name==null?null:AccessTools.Method(typeof(DefDatabase<>).MakeGenericType(storyDef),"GetNamedSilentFail").Invoke(null,new object[]{name});
  private static void Restore(object instance,Session s)
  {
   var def=ResolveStory(s.story);storyField.SetValue(instance,def);
   nodeField.SetValue(instance,def==null?null:((IEnumerable)Read(def,"nodes")).Cast<object>().FirstOrDefault(n=>(string)Read(n,"id")==s.node));
  }
  private static void Capture(object instance,Session s)
  {
   s.story=((Def)storyField.GetValue(instance))?.defName;var node=nodeField.GetValue(instance);s.node=node==null?null:(string)Read(node,"id");
  }
  private static void Start(Thing radio,int expectedGeneration)
  {
   if(radio==null||!radio.Spawned)return;
   var state=FindSession(radio);
   if((state?.generation??0)!=expectedGeneration)return;
   if(state==null){state=new Session{radio=radio};Current.sessions.Add(state);}
   var instance=Activator.CreateInstance(handler);
   AccessTools.DeclaredMethod(handler,"TryStartNewDialogue").Invoke(instance,null);
   Capture(instance,state);state.generation++;state.revision=0;
  }
  private static void Choose(Thing radio,int generation,int revision,int index)
  {
   if(radio==null||!radio.Spawned)return;var state=FindSession(radio);
   if(state==null||state.generation!=generation||state.revision!=revision)return;
   var instance=Activator.CreateInstance(handler);Restore(instance,state);var node=nodeField.GetValue(instance);if(node==null)return;
   var options=(IList)Read(node,"options");if(index<0||index>=options.Count||!Conditions(options[index]))return;
   AccessTools.DeclaredMethod(handler,"SelectOption").Invoke(instance,new[]{options[index]});Capture(instance,state);state.revision++;
  }
  private static void Expose(Game __instance)
  {
   var state=states.GetOrCreateValue(__instance);Scribe_Collections.Look(ref state.sessions,"mpRavenStorySessions",LookMode.Deep);
   if(Scribe.mode==LoadSaveMode.LoadingVars){locals=new ConditionalWeakTable<Window,Local>();if(state.sessions==null)state.sessions=new List<Session>();}
   if(Scribe.mode==LoadSaveMode.PostLoadInit)state.sessions.RemoveAll(s=>s.radio==null);
  }
 }
}
