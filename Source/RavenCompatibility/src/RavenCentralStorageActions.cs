using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    internal static class RavenCentralStorageActions
    {
        private sealed class Scope { public object state; public bool queued; }
        private static Scope current;
        private static int nextRequest;
        private static readonly Dictionary<int, WeakReference> pending = new Dictionary<int, WeakReference>();
        private static ISyncMethod execute;
        private static MethodInfo eject, setStatus, clamp, storedCount;
        internal static void Apply(Harmony harmony)
        {
            var storage = AccessTools.TypeByName("RavenRace.Features.CentralHub.Storage.CompRavenCentralResourceStorage");
            var state = AccessTools.TypeByName("RavenRace.Features.CentralHub.UI.RavenCentralStorageUIState");
            var page = AccessTools.TypeByName("RavenRace.Features.CentralHub.UI.RavenCentralStoragePageDrawer");
            eject = AccessTools.DeclaredMethod(storage, "TryEjectResource", new[] { typeof(ThingDef), typeof(int), typeof(int).MakeByRefType() });
            setStatus = AccessTools.DeclaredMethod(state, "SetStatus");
            clamp = AccessTools.DeclaredMethod(state, "ClampAmount");
            storedCount = AccessTools.DeclaredMethod(storage, "StoredCount");
            if (eject == null || setStatus == null || clamp == null || storedCount == null) throw new MissingMethodException("Raven central storage action");
            execute = MP.RegisterSyncMethod(typeof(RavenCentralStorageActions), nameof(Execute));
            harmony.Patch(eject, prefix: new HarmonyMethod(typeof(RavenCentralStorageActions), nameof(BeforeEject)));
            harmony.Patch(AccessTools.DeclaredMethod(page, "DrawResourceDetail"),
                prefix: new HarmonyMethod(typeof(RavenCentralStorageActions), nameof(BeforeDraw)),
                finalizer: new HarmonyMethod(typeof(RavenCentralStorageActions), nameof(AfterDraw)));
            harmony.Patch(setStatus, prefix: new HarmonyMethod(typeof(RavenCentralStorageActions), nameof(BeforeStatus)));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(Game), "ExposeSmallComponents"),
                prefix: new HarmonyMethod(typeof(RavenCentralStorageActions), nameof(BeforeLoad)));
        }
        private static void BeforeDraw(object __4, out Scope __state)
        {
            __state = current;
            current = MP.InInterface ? new Scope { state = __4 } : null;
        }
        private static void AfterDraw(Scope __state) => current = __state;
        private static bool BeforeStatus(object __instance)
        {
            // The original immediate bool is unavailable while queuing. Retain the
            // old UI status until this player's actual command result arrives.
            if (current == null || !current.queued || current.state != __instance) return true;
            current.queued = false;
            return false;
        }
        private static bool BeforeEject(ThingComp __instance, ThingDef __0, int __1, ref int __2, ref bool __result)
        {
            if (!MP.InInterface) return true;
            __2 = 0; __result = false;
            int token = ++nextRequest;
            if (current != null)
            {
                current.queued = true;
                pending[token] = new WeakReference(current.state);
            }
            execute.DoSync(null, __instance, __0, __1, token);
            return false;
        }
        private static void Execute(ThingComp storage, ThingDef def, int amount, int token)
        {
            bool success = false;
            object[] args = { def, amount, 0 };
            if (storage?.parent?.Spawned == true && def != null && amount > 0)
                success = (bool)eject.Invoke(storage, args);
            if (!MP.IsExecutingSyncCommandIssuedBySelf || !pending.TryGetValue(token, out var reference)) return;
            pending.Remove(token);
            var state = reference.Target;
            if (state == null) return;
            string status = success ? "Raven_CentralStorage_Ejected".Translate(def.LabelCap, (int)args[2])
                : "Raven_CentralStorage_EjectFailed".Translate(def?.LabelCap ?? "?");
            setStatus.Invoke(state, new object[] { status, !success });
            if (storage?.parent?.Spawned == true)
            {
                var selected = (ThingDef)AccessTools.Field(state.GetType(), "SelectedThingDef").GetValue(state);
                clamp.Invoke(state, new[] { storedCount.Invoke(storage, new object[] { selected }) });
            }
        }
        private static void BeforeLoad()
        {
            if (Scribe.mode == LoadSaveMode.LoadingVars) { pending.Clear(); current = null; }
        }
    }
}
