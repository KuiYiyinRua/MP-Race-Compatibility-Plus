using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    // Native MP sends only an option index and chooses the first world dialog.
    // Reward dialogs carry the destroyed event object and caravan in their closures;
    // their IDs identify the intended option without serializing window instances.
    internal static class TaleSupplyDialog
    {
        sealed class Capture
        {
            internal MethodInfo Method;
            internal FieldInfo[] Site, Caravan;
        }
        sealed class RaidSite { internal int Id; }
        static readonly ConditionalWeakTable<object, RaidSite> raidSites = new ConditionalWeakTable<object, RaidSite>();
        static readonly Dictionary<int, Capture> captures = new Dictionary<int, Capture>();
        static readonly FieldInfo nodeField = AccessTools.Field(typeof(Dialog_NodeTree), "curNode");
        static FieldInfo worldDialogOpen;
        static MethodInfo createPersistentDialog, findPersistentDialog, mpMapComp;
        static FieldInfo persistentDialogId, mapDialogs;
        static PropertyInfo persistentDialogValue;
        static Assembly nativeAssembly;
        static readonly MethodInfo activate = AccessTools.DeclaredMethod(typeof(DiaOption), "Activate", Type.EmptyTypes);
        static readonly MethodInfo queueLongEvent = AccessTools.Method(typeof(LongEventHandler), nameof(LongEventHandler.QueueLongEvent),
            new[] { typeof(Action), typeof(string), typeof(bool), typeof(Action<Exception>), typeof(bool), typeof(bool), typeof(Action) });
        static readonly FieldInfo apparelPairs = AccessTools.Field(typeof(PawnApparelGenerator), "allApparelPairs");
        static ISyncMethod choose;

        internal static void Apply(Harmony harmony)
        {
            var type = AccessTools.TypeByName("TheTaleofMilira.TaleOfMilira_TheMiliraSupply")
                ?? throw new TypeLoadException("Tale supply");
            nativeAssembly = type.Assembly;
            foreach (string outcome in new[] { "SolarCrystal", "SunLightFuel", "MealSurvivalPack", "SupplyPack", "MedicinePack", "NoThing" })
            {
                var method = type.GetNestedTypes(AccessTools.all)
                    .SelectMany(t => t.GetMethods(AccessTools.allDeclared))
                    .Single(m => m.Name == "<Outcome_" + outcome + ">b__0" && m.ReturnType == typeof(void) && m.GetParameters().Length == 0);
                var fields = method.DeclaringType.GetFields(AccessTools.allDeclared);
                captures.Add(method.MetadataToken, new Capture {
                    Method = method,
                    Site = new[] { fields.Single(f => f.FieldType == type) },
                    Caravan = new[] { fields.Single(f => f.FieldType == typeof(Caravan)) }
                });
            }
            var raidMethods = type.GetNestedTypes(AccessTools.all).SelectMany(t => t.GetMethods(AccessTools.allDeclared))
                .Where(m => m.Name == "<Outcome_Raid>b__0" || m.Name == "<Outcome_Raid>b__1").ToArray();
            if (raidMethods.Length != 2 || raidMethods.Select(m => m.DeclaringType).Distinct().Count() != 1)
                throw new MissingMethodException("Tale supply raid dialog callbacks changed");
            foreach (var method in raidMethods)
                captures.Add(method.MetadataToken, new Capture { Method = method,
                    Caravan = new[] { method.DeclaringType.GetFields(AccessTools.allDeclared).Single(f => f.FieldType == typeof(Caravan)) } });
            if (queueLongEvent == null) throw new MissingMethodException("Tale raid long-event overload");
            harmony.Patch(raidMethods.Single(m => m.Name == "<Outcome_Raid>b__1"),
                transpiler: new HarmonyMethod(typeof(TaleSupplyDialog), nameof(RunRaidMapInsideCommand)));
            if (apparelPairs == null) throw new MissingFieldException("PawnApparelGenerator.allApparelPairs");
            harmony.Patch(AccessTools.DeclaredMethod(typeof(PawnApparelGenerator), nameof(PawnApparelGenerator.GenerateStartingApparelFor)),
                prefix: new HarmonyMethod(typeof(TaleSupplyDialog), nameof(NormalizeApparelPairs)));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(PawnApparelGenerator), nameof(PawnApparelGenerator.GenerateApparelOfDefFor)),
                prefix: new HarmonyMethod(typeof(TaleSupplyDialog), nameof(NormalizeApparelPairs)));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(PawnApparelGenerator), "GenerateWorkingPossibleApparelSetFor"),
                prefix: new HarmonyMethod(typeof(TaleSupplyDialog), nameof(NormalizeApparelCandidates)));
            // The ten other arrival dialogs also send only an option index through MP.
            // Recover the exact event/caravan from their generated closure paths.
            foreach (var nested in nativeAssembly.GetTypes().Where(t => t.Name.Contains("DisplayClass")))
            foreach (var method in nested.GetMethods(AccessTools.allDeclared))
            {
                if (captures.ContainsKey(method.MetadataToken) || method.IsStatic || method.ReturnType != typeof(void)
                    || method.GetParameters().Length != 0 || !method.Name.StartsWith("<Outcome_", StringComparison.Ordinal)) continue;
                var site = FindCapturePath(nested, t => t.Assembly == nativeAssembly && typeof(WorldObject).IsAssignableFrom(t), 0);
                var caravan = FindCapturePath(nested, t => t == typeof(Caravan), 0);
                if (site == null || caravan == null) continue;
                captures.Add(method.MetadataToken, new Capture { Method = method, Site = site, Caravan = caravan });
            }
            harmony.Patch(AccessTools.DeclaredMethod(type, "Outcome_Raid", new[] { typeof(Caravan) }),
                prefix: new HarmonyMethod(typeof(TaleSupplyDialog), nameof(BeforeRaidDialog)),
                postfix: new HarmonyMethod(typeof(TaleSupplyDialog), nameof(TagFirstRaidDialog)));
            worldDialogOpen = AccessTools.Field(AccessTools.TypeByName("Multiplayer.Client.SyncUtil"), "isDialogNodeTreeOpen")
                ?? throw new MissingFieldException("MP world-dialog marker");
            var persistentType = AccessTools.TypeByName("Multiplayer.Client.PersistentDialog")
                ?? throw new TypeLoadException("MP persistent dialog");
            createPersistentDialog = AccessTools.Method(persistentType, "CreateInstance", new[] { typeof(Map), typeof(Dialog_NodeTree) })
                ?? throw new MissingMethodException("MP PersistentDialog.CreateInstance");
            findPersistentDialog = AccessTools.Method(persistentType, "FindDialog", new[] { typeof(Window) })
                ?? throw new MissingMethodException("MP PersistentDialog.FindDialog");
            persistentDialogId = AccessTools.Field(persistentType, "id")
                ?? throw new MissingFieldException("MP PersistentDialog.id");
            persistentDialogValue = AccessTools.Property(persistentType, "Dialog")
                ?? throw new MissingMemberException("MP PersistentDialog.Dialog");
            mpMapComp = AccessTools.Method(AccessTools.TypeByName("Multiplayer.Client.Extensions"), "MpComp", new[] { typeof(Map) })
                ?? throw new MissingMethodException("MP Extensions.MpComp");
            mapDialogs = AccessTools.Field(AccessTools.TypeByName("Multiplayer.Client.MultiplayerMapComp"), "mapDialogs")
                ?? throw new MissingFieldException("MP MultiplayerMapComp.mapDialogs");
            var methodGate = AccessTools.Method(AccessTools.TypeByName("Multiplayer.Client.DelegateSerialization"),
                "CheckMethodAllowed", new[] { typeof(MethodInfo) })
                ?? throw new MissingMethodException("MP DelegateSerialization.CheckMethodAllowed");
            harmony.Patch(methodGate, prefix: new HarmonyMethod(typeof(TaleSupplyDialog), nameof(AllowNativeDialogMethod)));
            if (nodeField == null) throw new MissingFieldException("Dialog_NodeTree.curNode");
            choose = MP.RegisterSyncMethod(typeof(TaleSupplyDialog), nameof(Choose));
            harmony.Patch(AccessTools.Method(typeof(WindowStack), nameof(WindowStack.Add), new[] { typeof(Window) }),
                postfix: new HarmonyMethod(typeof(TaleSupplyDialog), nameof(PersistWorldDialog)) { priority = Priority.Last });
            harmony.Patch(AccessTools.DeclaredMethod(typeof(DiaOption), "Activate", Type.EmptyTypes),
                prefix: new HarmonyMethod(typeof(TaleSupplyDialog), nameof(BeforeActivate)) {
                    priority = Priority.First, before = new[] { "multiplayer" }
                });
            Log.Message("[TaleNivarianCompat] Exact Tale event/caravan dialog routes=" + captures.Count + ".");
        }

        static FieldInfo[] FindCapturePath(Type type, Func<Type, bool> match, int depth)
        {
            if (depth > 3) return null;
            var fields = type.GetFields(AccessTools.allDeclared).Where(f => !f.IsStatic).ToArray();
            var direct = fields.Where(f => match(f.FieldType)).ToArray();
            if (direct.Length > 1) throw new InvalidOperationException("Ambiguous Tale closure field on " + type.FullName);
            if (direct.Length == 1) return new[] { direct[0] };
            foreach (var field in fields.Where(f => f.FieldType.Assembly == nativeAssembly && f.FieldType.Name.Contains("DisplayClass")))
            {
                var tail = FindCapturePath(field.FieldType, match, depth + 1);
                if (tail != null) return new[] { field }.Concat(tail).ToArray();
            }
            return null;
        }

        static IEnumerable<CodeInstruction> RunRaidMapInsideCommand(IEnumerable<CodeInstruction> instructions)
        {
            int replaced = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(queueLongEvent))
                {
                    var code = new CodeInstruction(instruction);
                    code.opcode = OpCodes.Call;
                    code.operand = AccessTools.Method(typeof(TaleSupplyDialog), nameof(QueueRaidMap));
                    replaced++;
                    yield return code;
                }
                else yield return instruction;
            }
            if (replaced != 1) throw new InvalidOperationException("Tale raid map queue changed: " + replaced);
        }

        static void QueueRaidMap(Action action, string textKey, bool doAsynchronously,
            Action<Exception> exceptionHandler, bool showExtraUIInfo, bool forceHideUI, Action callback)
        {
            if (!MP.IsInMultiplayer)
            {
                LongEventHandler.QueueLongEvent(action, textKey, doAsynchronously, exceptionHandler,
                    showExtraUIInfo, forceHideUI, callback);
                return;
            }
            if (MP.InInterface || action == null || doAsynchronously || exceptionHandler != null || callback != null
                || textKey != "GeneratingMapForNewEncounter")
                throw new InvalidOperationException("Unexpected Tale raid map generation context");
            action();
        }

        static void NormalizeApparelPairs()
        {
            if (!MP.IsInMultiplayer) return;
            var pairs = (List<ThingStuffPair>)apparelPairs.GetValue(null);
            // Other apparel generation code can reorder this list in place.
            // A cached list reference does not prove that its order is stable.
            for (int i = 1; i < pairs.Count; i++)
                if (CompareApparelPairs(pairs[i - 1], pairs[i]) > 0)
                {
                    pairs.Sort(CompareApparelPairs);
                    break;
                }
        }

        static int CompareApparelPairs(ThingStuffPair a, ThingStuffPair b)
        {
            int byThing = string.CompareOrdinal(a.thing.defName, b.thing.defName);
            return byThing != 0 ? byThing : string.CompareOrdinal(a.stuff?.defName, b.stuff?.defName);
        }

        // Pawn generation can reach this method with the same candidates in a
        // different order after a cold join. Weighted apparel selection uses
        // list order, so normalize the actual draw list as well as the cache.
        static void NormalizeApparelCandidates(List<ThingStuffPair> apparelCandidates)
        {
            if (MP.IsInMultiplayer) apparelCandidates.Sort(CompareApparelPairs);
        }


        static object Resolve(object value, FieldInfo[] path)
        {
            foreach (var field in path) value = value == null ? null : field.GetValue(value);
            return value;
        }

        static bool AllowNativeDialogMethod(MethodInfo method, ref MethodInfo __result)
        {
            if (method?.DeclaringType?.Assembly == nativeAssembly
                && captures.TryGetValue(method.MetadataToken, out var capture) && Equals(method, capture.Method))
            {
                __result = method;
                return false;
            }
            return true;
        }

        static IList StoredDialogs(Map map)
        {
            var component = mpMapComp.Invoke(null, new object[] { map });
            return component == null ? null : (IList)mapDialogs.GetValue(component);
        }

        static object Persistent(Dialog_NodeTree dialog) => findPersistentDialog.Invoke(null, new object[] { dialog });

        static int PersistentId(Dialog_NodeTree dialog)
        {
            var wrapper = Persistent(dialog);
            return wrapper == null ? -1 : (int)persistentDialogId.GetValue(wrapper);
        }

        // MP only creates PersistentDialog when a map command is active. Tale's
        // world-arrival commands have no map context, so attach the existing
        // visible dialog to the caravan owner's map after WindowStack.Add.
        static void PersistWorldDialog(Window window)
        {
            if (!Bootstrap.Ready || !MP.IsInMultiplayer || MP.InInterface || !(window is Dialog_NodeTree dialog)
                || Current.Game == null || Find.WindowStack?.Windows.Contains(window) != true || Persistent(dialog) != null)
                return;
            var node = nodeField.GetValue(dialog) as DiaNode;
            if (node == null) return;
            var caravans = node.options.Select(option => option.action)
                .Where(action => action?.Target != null && action.Method.DeclaringType?.Assembly == nativeAssembly
                    && captures.TryGetValue(action.Method.MetadataToken, out var capture) && Equals(action.Method, capture.Method))
                .Select(action => Resolve(action.Target, captures[action.Method.MetadataToken].Caravan) as Caravan)
                .Where(caravan => caravan != null).Distinct().ToArray();
            if (caravans.Length == 0) return;
            if (caravans.Length != 1) throw new InvalidOperationException("Tale dialog has multiple caravans");
            var map = Find.Maps.Where(m => m.IsPlayerHome && m.ParentFaction == caravans[0].Faction)
                .OrderBy(m => m.uniqueID).FirstOrDefault();
            if (map == null) throw new InvalidOperationException("No player map for Tale world dialog caravan");
            var stored = StoredDialogs(map) ?? throw new InvalidOperationException("MP map dialog store unavailable");
            var wrapper = createPersistentDialog.Invoke(null, new object[] { map, dialog })
                ?? throw new InvalidOperationException("MP cannot persist Tale world dialog");
            stored.Add(wrapper);
        }

        static IEnumerable<Dialog_NodeTree> ActiveDialogs()
        {
            var seen = new HashSet<Dialog_NodeTree>();
            foreach (var dialog in Find.WindowStack.Windows.OfType<Dialog_NodeTree>())
                if (seen.Add(dialog)) yield return dialog;
            foreach (var map in Find.Maps)
            {
                var stored = StoredDialogs(map);
                if (stored == null) continue;
                foreach (var wrapper in stored)
                {
                    var dialog = persistentDialogValue.GetValue(wrapper) as Dialog_NodeTree;
                    if (dialog != null && seen.Add(dialog)) yield return dialog;
                }
            }
        }

        static void BeforeRaidDialog(out HashSet<Dialog_NodeTree> __state)
        {
            __state = new HashSet<Dialog_NodeTree>(Find.WindowStack.Windows.OfType<Dialog_NodeTree>());
        }
        static void TagFirstRaidDialog(WorldObject __instance, Caravan caravan, HashSet<Dialog_NodeTree> __state)
        {
            if (!MP.IsInMultiplayer) return;
            TagRaidDialog(__instance.ID, caravan.ID, "<Outcome_Raid>b__0", __state);
        }
        static void TagRaidDialog(int siteId, int caravanId, string methodName, HashSet<Dialog_NodeTree> prior)
        {
            var matches = Find.WindowStack.Windows.OfType<Dialog_NodeTree>()
                .Where(dialog => !prior.Contains(dialog))
                .SelectMany(dialog => ((nodeField.GetValue(dialog) as DiaNode)?.options ?? Enumerable.Empty<DiaOption>())
                    .Select(option => new { dialog, option }))
                .Where(item => item.option.action?.Method.Name == methodName
                    && item.option.action.Method.DeclaringType?.Assembly == nativeAssembly
                    && captures.TryGetValue(item.option.action.Method.MetadataToken, out var capture)
                    && (Resolve(item.option.action.Target, capture.Caravan) as Caravan)?.ID == caravanId)
                .ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("Expected one Tale supply raid dialog: " + methodName);
            var target = matches[0].option.action.Target;
            raidSites.Remove(target);
            raidSites.Add(target, new RaidSite { Id = siteId });
            var persistentId = PersistentId(matches[0].dialog);
            if (persistentId < 0) throw new InvalidOperationException("Tale raid dialog was not persisted by MP");
            Current.Game.GetComponent<TaleWorldDialogState>().Remember(persistentId, siteId);
        }
        static bool Identity(DiaOption option, out int siteId, out int caravanId, out int token)
        {
            siteId = caravanId = token = -1;
            var action = option.action;
            if (action == null || action.Target == null || action.Method.DeclaringType?.Assembly != nativeAssembly) return false;
            token = action.Method.MetadataToken;
            if (!captures.TryGetValue(token, out var capture) || !Equals(action.Method, capture.Method)) return false;
            var caravan = Resolve(action.Target, capture.Caravan) as Caravan;
            if (caravan == null) return false;
            if (capture.Site != null)
            {
                var site = Resolve(action.Target, capture.Site) as WorldObject;
                if (site == null) return false;
                siteId = site.ID;
            }
            else
            {
                if (!raidSites.TryGetValue(action.Target, out var site))
                {
                    var dialog = option.dialog as Dialog_NodeTree;
                    var persistentId = dialog == null ? -1 : PersistentId(dialog);
                    if (persistentId < 0 || !Current.Game.GetComponent<TaleWorldDialogState>().TryGetSite(persistentId, out var savedSiteId))
                        throw new InvalidOperationException("Supply raid dialog lacks its event identity");
                    site = new RaidSite { Id = savedSiteId };
                    raidSites.Add(action.Target, site);
                }
                siteId = site.Id;
            }
            caravanId = caravan.ID;
            return true;
        }

        static bool BeforeActivate(DiaOption __instance)
        {
            if (!Bootstrap.Ready || !MP.IsInMultiplayer || !MP.InInterface) return true;
            if (!Identity(__instance, out int site, out int caravan, out int token)) return true;
            // Do not fall back to the ambiguous native route if dispatch is unavailable.
            choose.DoSync(null, site, caravan, token);
            return false;
        }

        static void Choose(int siteId, int caravanId, int token)
        {
            if (!captures.ContainsKey(token)) return;
            DiaOption match = null;
            foreach (var dialog in ActiveDialogs())
            {
                var node = nodeField.GetValue(dialog) as DiaNode;
                if (node == null) continue;
                foreach (var option in node.options)
                {
                    if (!Identity(option, out int site, out int caravan, out int method)
                        || site != siteId || caravan != caravanId || method != token) continue;
                    if (match != null) throw new InvalidOperationException("Ambiguous Tale supply dialog identity");
                    match = option;
                }
            }
            if (match == null) return; // Another queued click may already have closed it.
            bool previous = (bool)worldDialogOpen.GetValue(null);
            worldDialogOpen.SetValue(null, false);
            var prior = new HashSet<Dialog_NodeTree>(Find.WindowStack.Windows.OfType<Dialog_NodeTree>());
            try
            {
                activate.Invoke(match, null);
                if (captures[token].Method.Name == "<Outcome_Raid>b__0")
                    TagRaidDialog(siteId, caravanId, "<Outcome_Raid>b__1", prior);
            }
            finally
            {
                // PostClose clears the native marker globally. Preserve synchronization
                // for other world dialogs that are still present after this one closes.
                worldDialogOpen.SetValue(null, previous && Find.WindowStack.Windows.OfType<Dialog_NodeTree>().Any());
            }
        }
    }
}
