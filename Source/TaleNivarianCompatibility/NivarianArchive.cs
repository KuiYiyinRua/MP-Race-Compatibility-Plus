using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    internal static class NivarianArchive
    {
        static Type componentType,entryType,windowType;
        static FieldInfo nameField,claimedField;
        static PropertyInfo entries;
        static MethodInfo grantItems,itemValue,coinValue,grantCoins;
        static ISyncMethod claim;
        static readonly Dictionary<MethodBase,bool> Choices=new Dictionary<MethodBase,bool>();
        static object Component=>Current.Game?.components.FirstOrDefault(c=>componentType.IsInstanceOfType(c));
        static object FindEntry(string name)=>name==null||Component==null?null:((IEnumerable)entries.GetValue(Component)).Cast<object>().FirstOrDefault(e=>(string)nameField.GetValue(e)==name);
        internal static void Apply(Harmony harmony)
        {
            componentType=AccessTools.TypeByName("Nivarian_Race.Code.GameComponent.GameComp_NivarianArchive")??throw new TypeLoadException("Niva archive");
            entryType=AccessTools.TypeByName("Nivarian_Race.Code.GameComponent.CollectedJournalEntry")??throw new TypeLoadException("Niva archive entry");
            windowType=AccessTools.TypeByName("Nivarian_Race.Code.UI.Window_NivarianArchiveDatabase")??throw new TypeLoadException("Niva archive window");
            entries=AccessTools.Property(componentType,"Entries");nameField=AccessTools.Field(entryType,"JournalDefName");claimedField=AccessTools.Field(entryType,"TagRewardClaimed");
            AccessTools.Method(typeof(NivarianArchive),nameof(RegisterWorker)).MakeGenericMethod(entryType).Invoke(null,null);
            foreach(var method in new[]{"AddTag","RemoveTag","AddCustomTagName"})MP.RegisterSyncMethod(AccessTools.DeclaredMethod(componentType,method));
            grantItems=AccessTools.DeclaredMethod(windowType,"GrantItemReward");itemValue=AccessTools.DeclaredMethod(componentType,"CalcItemRewardValue");coinValue=AccessTools.DeclaredMethod(componentType,"CalcPowerCoinReward");grantCoins=AccessTools.DeclaredMethod(componentType,"GivePowerCoinReward");
            claim=MP.RegisterSyncMethod(typeof(NivarianArchive),nameof(Claim),new[]{new SyncType(typeof(Map)){contextMap=true},new SyncType(typeof(string)),new SyncType(typeof(bool))});
            foreach(var nested in windowType.GetNestedTypes(AccessTools.all))
                foreach(var method in AccessTools.GetDeclaredMethods(nested).Where(m=>m.Name.StartsWith("<OpenRewardChoice>") && m.ReturnType==typeof(void) && m.GetParameters().Length==0))
                {
                    var calls=PatchProcessor.GetOriginalInstructions(method).Select(i=>i.operand).OfType<MethodInfo>().ToArray();
                    if(!calls.Contains(grantItems) && !calls.Contains(grantCoins))continue;
                    Choices.Add(method,calls.Contains(grantItems));
                    harmony.Patch(method,prefix:new HarmonyMethod(typeof(NivarianArchive),nameof(BeforeChoice)));
                }
            if(Choices.Count!=2)throw new InvalidOperationException("Archive reward callbacks changed: "+Choices.Count);
            if(PatchProcessor.GetOriginalInstructions(grantItems).Any(i=>(i.opcode==System.Reflection.Emit.OpCodes.Ldfld || i.opcode==System.Reflection.Emit.OpCodes.Ldflda) && i.operand is FieldInfo field && field.DeclaringType.IsAssignableFrom(windowType)))throw new InvalidOperationException("Archive item executor now requires window state");
            Log.Message("[TaleNivarianCompat] archive tags synchronized by journal identity; reward choice executes once on all peers.");
        }
        static void RegisterWorker<T>() where T:class=>MP.RegisterSyncWorker<T>(Entry);
        static void Entry<T>(SyncWorker sync,ref T value) where T:class
        {
            string name=sync.isWriting && value!=null?(string)nameField.GetValue(value):null;
            sync.Bind(ref name);if(!sync.isWriting)value=FindEntry(name) as T;
        }
        static bool BeforeChoice(object __instance,MethodBase __originalMethod)
        {
            if(!MP.IsInMultiplayer || !MP.InInterface)return true;
            var fields=AccessTools.GetDeclaredFields(__instance.GetType());
            var entry=fields.First(f=>f.FieldType==entryType).GetValue(__instance);
            var map=(Map)fields.First(f=>f.FieldType==typeof(Map)).GetValue(__instance);
            if(entry!=null && !(bool)claimedField.GetValue(entry))claim.DoSync(null,map,(string)nameField.GetValue(entry),Choices[__originalMethod]);
            return false;
        }
        static void Claim(Map map,string journal,bool items)
        {
            var entry=FindEntry(journal);
            if(entry==null || (bool)claimedField.GetValue(entry))return;
            if(map!=null && (!Find.Maps.Contains(map)||map.ParentFaction!=Faction.OfPlayer))return;
            if(items && map==null)return;
            claimedField.SetValue(entry,true);
            if(items)
                grantItems.Invoke(FormatterServices.GetUninitializedObject(windowType),new object[]{itemValue.Invoke(null,new object[]{map}),map});
            else
            {
                int amount=(int)coinValue.Invoke(null,null);grantCoins.Invoke(null,new object[]{amount});
                Messages.Message("Nivarian.Archive.Reward.PowerCoinGranted".Translate(amount),MessageTypeDefOf.PositiveEvent,true);
            }
        }
    }
}
