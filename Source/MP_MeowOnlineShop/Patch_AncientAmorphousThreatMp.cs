using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Ancient Amorphous Threat schedules its delayed boss arrival from a local confirmation
    /// callback. Route the final mutation through a primitive-only global command so the
    /// world GameComponent and its Rand-consuming delay selection change on every peer.
    /// </summary>
    internal static class Patch_AncientAmorphousThreatMp
    {
        private const string DelayedCompTypeName = "AncientAmorphousThreat.CompUseEffect_DelayedTriggerArrival";
        private const int MaxWarnings = 8;

        private static Type _delayedCompType;
        private static MethodInfo _doEffectFinal;
        private static bool _executingReplay;
        private static int _warningCount;
        private static int _dispatchLogBudget = 8;
        private static int _replayLogBudget = 8;

        public static void Apply(Harmony harmony)
        {
            if (!MP.enabled || harmony == null)
                return;

            _delayedCompType = AccessTools.TypeByName(DelayedCompTypeName);
            if (_delayedCompType == null)
            {
                Log.Message("[MP-MeowOnlineShop] Ancient Amorphous Threat compatibility skipped: target mod is not active.");
                return;
            }

            _doEffectFinal = AccessTools.Method(_delayedCompType, "DoEffectFinal", Type.EmptyTypes);
            if (_doEffectFinal == null || _doEffectFinal.IsStatic || _doEffectFinal.ReturnType != typeof(void) ||
                !typeof(ThingComp).IsAssignableFrom(_delayedCompType))
            {
                Log.Error("[MP-MeowOnlineShop] Ancient Amorphous Threat DoEffectFinal() signature changed; delayed-arrival sync was NOT installed.");
                return;
            }

            try
            {
                // Primitive arguments intentionally keep this on Multiplayer's global/world queue.
                MP.RegisterSyncMethod(typeof(Patch_AncientAmorphousThreatMp), nameof(SyncDoEffectFinalById));
                harmony.Patch(_doEffectFinal,
                    prefix: new HarmonyMethod(typeof(Patch_AncientAmorphousThreatMp), nameof(DoEffectFinalPrefix))
                    {
                        priority = Priority.First
                    });
            }
            catch (Exception exception)
            {
                Log.Error("[MP-MeowOnlineShop] Ancient Amorphous Threat delayed-arrival sync installation failed: " + exception);
                return;
            }

            Log.Message("[MP-MeowOnlineShop] Ancient Amorphous Threat delayed boss confirmation routed to primitive-only global command.");
        }

        private static bool DoEffectFinalPrefix(object __instance)
        {
            if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand || _executingReplay)
                return true;

            // A warning-less use effect can reach this method during the deterministic simulation.
            // Only convert the local dialog/UI callback that Multiplayer does not replay.
            if (!MP.InInterface)
                return true;

            ThingComp comp = __instance as ThingComp;
            Thing parent = comp?.parent;
            Map map = parent?.MapHeld;
            if (map == null || parent.thingIDNumber < 0)
            {
                Warn("Ancient Amorphous Threat confirmation had no stable map/thing identity; mutation was blocked to prevent a host-only schedule.");
                return false;
            }

            if (_dispatchLogBudget-- > 0)
                Log.Message($"[MP-MeowOnlineShop] AAT delayed-arrival dispatch: map={map.uniqueID}, thing={parent.thingIDNumber}.");

            SyncDoEffectFinalById(map.uniqueID, parent.thingIDNumber);
            return false;
        }

        /// <summary>
        /// Resolve the original component from stable primitive IDs, then execute its complete
        /// final callback (delay Rand, GameComponent schedule, target map and notification).
        /// </summary>
        private static void SyncDoEffectFinalById(int mapUniqueId, int parentThingId)
        {
            Map map = Find.Maps?.FirstOrDefault(candidate => candidate != null && candidate.uniqueID == mapUniqueId);
            Thing parent = FindThingById(map, parentThingId);
            ThingComp comp = (parent as ThingWithComps)?.AllComps?
                .FirstOrDefault(candidate => candidate != null && _delayedCompType.IsInstanceOfType(candidate));

            if (map == null || parent == null || comp == null || parent.MapHeld != map)
            {
                Warn($"AAT delayed-arrival replay could not resolve target map={mapUniqueId}, thing={parentThingId}.");
                return;
            }

            if (_replayLogBudget-- > 0)
                Log.Message($"[MP-MeowOnlineShop] AAT delayed-arrival replay: map={mapUniqueId}, thing={parentThingId}.");

            _executingReplay = true;
            try
            {
                _doEffectFinal.Invoke(comp, null);
            }
            catch (TargetInvocationException invocation) when (invocation.InnerException != null)
            {
                throw invocation.InnerException;
            }
            finally
            {
                _executingReplay = false;
            }
        }

        private static Thing FindThingById(Map map, int thingId)
        {
            if (map?.listerThings?.AllThings == null)
                return null;

            var allThings = map.listerThings.AllThings;
            for (int i = 0; i < allThings.Count; i++)
            {
                Thing thing = allThings[i];
                if (thing != null && thing.thingIDNumber == thingId)
                    return thing;

                if (!(thing is IThingHolder holder))
                    continue;

                var heldThings = ThingOwnerUtility.GetAllThingsRecursively(holder);
                for (int j = 0; j < heldThings.Count; j++)
                {
                    Thing heldThing = heldThings[j];
                    if (heldThing != null && heldThing.thingIDNumber == thingId)
                        return heldThing;
                }
            }

            return null;
        }

        private static void Warn(string message)
        {
            if (_warningCount++ < MaxWarnings)
                Log.Warning("[MP-MeowOnlineShop] " + message);
        }
    }
}
