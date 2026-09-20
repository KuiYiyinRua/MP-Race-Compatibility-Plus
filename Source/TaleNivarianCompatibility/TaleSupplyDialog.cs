using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
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
            internal FieldInfo Site, Caravan;
        }
        sealed class RaidSite { internal int Id; }
        static readonly ConditionalWeakTable<object, RaidSite> raidSites = new ConditionalWeakTable<object, RaidSite>();
        [ThreadStatic] static WorldObject currentRaidSite;
        static readonly Dictionary<int, Capture> captures = new Dictionary<int, Capture>();
        static readonly FieldInfo nodeField = AccessTools.Field(typeof(Dialog_NodeTree), "curNode");
        static FieldInfo worldDialogOpen;
        static Assembly nativeAssembly;
        static readonly MethodInfo activate = AccessTools.DeclaredMethod(typeof(DiaOption), "Activate", Type.EmptyTypes);
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
                    Site = fields.Single(f => f.FieldType == type),
                    Caravan = fields.Single(f => f.FieldType == typeof(Caravan))
                });
            }
            var raidMethods = type.GetNestedTypes(AccessTools.all).SelectMany(t => t.GetMethods(AccessTools.allDeclared))
                .Where(m => m.Name == "<Outcome_Raid>b__0" || m.Name == "<Outcome_Raid>b__1").ToArray();
            if (raidMethods.Length != 2 || raidMethods.Select(m => m.DeclaringType).Distinct().Count() != 1)
                throw new MissingMethodException("Tale supply raid dialog callbacks changed");
            foreach (var method in raidMethods)
                captures.Add(method.MetadataToken, new Capture { Method = method,
                    Caravan = method.DeclaringType.GetFields(AccessTools.allDeclared).Single(f => f.FieldType == typeof(Caravan)) });
            harmony.Patch(AccessTools.DeclaredMethod(type, "Outcome_Raid", new[] { typeof(Caravan) }),
                prefix: new HarmonyMethod(typeof(TaleSupplyDialog), nameof(BeginRaid)),
                finalizer: new HarmonyMethod(typeof(TaleSupplyDialog), nameof(EndRaid)));
            harmony.Patch(AccessTools.Constructor(raidMethods[0].DeclaringType, Type.EmptyTypes),
                postfix: new HarmonyMethod(typeof(TaleSupplyDialog), nameof(TagRaidClosure)));
            worldDialogOpen = AccessTools.Field(AccessTools.TypeByName("Multiplayer.Client.SyncUtil"), "isDialogNodeTreeOpen")
                ?? throw new MissingFieldException("MP world-dialog marker");
            if (nodeField == null) throw new MissingFieldException("Dialog_NodeTree.curNode");
            choose = MP.RegisterSyncMethod(typeof(TaleSupplyDialog), nameof(Choose));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(DiaOption), "Activate", Type.EmptyTypes),
                prefix: new HarmonyMethod(typeof(TaleSupplyDialog), nameof(BeforeActivate)) {
                    priority = Priority.First, before = new[] { "multiplayer" }
                });
            Log.Message("[TaleNivarianCompat] Eight supply reward/raid dialog callbacks select exact event/caravan identities instead of first-window indices.");
        }

        static void BeginRaid(WorldObject __instance, out WorldObject __state)
        {
            __state = currentRaidSite;
            currentRaidSite = MP.IsInMultiplayer ? __instance : null;
        }
        static Exception EndRaid(Exception __exception, WorldObject __state)
        {
            currentRaidSite = __state;
            return __exception;
        }
        static void TagRaidClosure(object __instance)
        {
            if (currentRaidSite != null) raidSites.Add(__instance, new RaidSite { Id = currentRaidSite.ID });
        }
        static bool Identity(DiaOption option, out int siteId, out int caravanId, out int token)
        {
            siteId = caravanId = token = -1;
            var action = option.action;
            if (action == null || action.Target == null || action.Method.DeclaringType?.Assembly != nativeAssembly) return false;
            token = action.Method.MetadataToken;
            if (!captures.TryGetValue(token, out var capture) || !Equals(action.Method, capture.Method)) return false;
            var caravan = capture.Caravan.GetValue(action.Target) as Caravan;
            if (caravan == null) return false;
            if (capture.Site != null)
            {
                var site = capture.Site.GetValue(action.Target) as WorldObject;
                if (site == null) return false;
                siteId = site.ID;
            }
            else
            {
                if (!raidSites.TryGetValue(action.Target, out var site))
                    throw new InvalidOperationException("Supply raid dialog lacks its event identity");
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
            foreach (var dialog in Find.WindowStack.Windows.OfType<Dialog_NodeTree>())
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
            try { activate.Invoke(match, null); }
            finally
            {
                // PostClose clears the native marker globally. Preserve synchronization
                // for other world dialogs that are still present after this one closes.
                worldDialogOpen.SetValue(null, previous && Find.WindowStack.Windows.OfType<Dialog_NodeTree>().Any());
            }
        }
    }
}
