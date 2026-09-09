using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    // Design references: Thaipho, PreventAsyncVehiclePathRequestInMp:
    // https://github.com/Thaipho/Multiplayer-Vehicle-framework-Compatibility-patch/blob/b6a33884f0aa07c84e890c199016f84311fc375a/Source/VehicleFramework.cs
    // VSauce Michael, Vehicle Framework — Multiplayer Desync Fix:
    // https://steamcommunity.com/sharedfiles/filedetails/?id=3779000026
    // Independent implementation against the installed VF binary. Unlike the
    // referenced prefix, preserve RequestStatus.Calculating before GeneratePath.
    // Existing caravan guards remain necessary: map path sync does not implement
    // VF's seat/transfer/caravan dialog session protocol.
    internal static class Patch_VehiclePathMp
    {
        private static FieldInfo cancellation, task;
        private static MethodInfo generate, setStatus;
        private static object calculating;
        private static bool applied;

        internal static void Apply(Harmony harmony)
        {
            if (applied || harmony == null || !MP.enabled) return;
            var type = AccessTools.TypeByName("Vehicles.VehiclePathFollower");
            if (type == null) return;
            var request = AccessTools.DeclaredMethod(type, "RequestNewPath", Type.EmptyTypes);
            generate = AccessTools.DeclaredMethod(type, "GeneratePath", new[] { typeof(CancellationToken) });
            cancellation = AccessTools.Field(type, "pathCancellationTokenSource");
            task = AccessTools.Field(type, "curPathTask");
            var status = AccessTools.Property(type, "RequestStatus");
            setStatus = status?.GetSetMethod(true);
            if (request == null || generate == null || cancellation?.FieldType != typeof(CancellationTokenSource) ||
                task?.FieldType != typeof(Task) || setStatus == null || !status.PropertyType.IsEnum ||
                !Enum.IsDefined(status.PropertyType, "Calculating"))
            {
                Log.Warning("[MP-MeowOnlineShop] VF path layout changed; synchronous path patch skipped.");
                return;
            }
            calculating = Enum.Parse(status.PropertyType, "Calculating");
            harmony.Patch(request, prefix: new HarmonyMethod(typeof(Patch_VehiclePathMp), nameof(RequestPrefix)) { priority = Priority.First });
            applied = true;
            Log.Message("[MP-MeowOnlineShop] VF synchronous map path patch registered.");
        }

        private static bool RequestPrefix(object __instance)
        {
            if (!MP.IsInMultiplayer) return true;
            // A save join does not serialize Tasks. A live transition must never
            // race a previously running worker against the deterministic thread.
            var oldTask = (Task)task.GetValue(__instance);
            var tokenSource = (CancellationTokenSource)cancellation.GetValue(__instance);
            if (oldTask != null && !oldTask.IsCompleted)
            {
                tokenSource?.Cancel();
                try { oldTask.GetAwaiter().GetResult(); }
                catch (OperationCanceledException) { }
            }
            if (tokenSource == null || tokenSource.IsCancellationRequested)
            {
                tokenSource?.Dispose();
                tokenSource = new CancellationTokenSource();
                cancellation.SetValue(__instance, tokenSource);
            }
            task.SetValue(__instance, null);
            setStatus.Invoke(__instance, new[] { calculating });
            // Do not fall back to async execution or suppress pathfinder errors.
            generate.Invoke(__instance, new object[] { tokenSource.Token });
            return false;
        }
    }
}
