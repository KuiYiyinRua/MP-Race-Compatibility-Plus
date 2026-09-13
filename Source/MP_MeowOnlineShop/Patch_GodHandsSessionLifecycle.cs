using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;
using Verse.Profile;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Disconnects enqueue deterministic releases on every peer. World teardown
    /// clears old object references; GodHandDragState restores hand sessions
    /// from the incoming snapshot. UI previews are never restored from a save.
    /// </summary>
    internal static class Patch_GodHandsSessionLifecycle
    {
        private static bool _applied;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || harmony == null)
                return;
            _applied = true;

            try
            {
                Type playerManagerType =
                    AccessTools.TypeByName("Multiplayer.Common.PlayerManager");
                Type multiplayerType =
                    AccessTools.TypeByName("Multiplayer.Client.Multiplayer");

                MethodInfo setDisconnected = playerManagerType?.GetMethod(
                    "SetDisconnected",
                    BindingFlags.Instance | BindingFlags.Public);
                MethodInfo stopMultiplayer = multiplayerType?.GetMethod(
                    "StopMultiplayer",
                    BindingFlags.Static | BindingFlags.Public);

                if (setDisconnected != null)
                {
                    harmony.Patch(
                        setDisconnected,
                        postfix: new HarmonyMethod(
                            typeof(Patch_GodHandsSessionLifecycle),
                            nameof(SetDisconnectedPostfix))
                        {
                            priority = Priority.Last
                        });
                }

                if (stopMultiplayer != null)
                {
                    harmony.Patch(
                        stopMultiplayer,
                        postfix: new HarmonyMethod(
                            typeof(Patch_GodHandsSessionLifecycle),
                            nameof(StopMultiplayerPostfix))
                        {
                            priority = Priority.Last
                        });
                }

                MethodInfo clearAllMapsAndWorld = AccessTools.Method(
                    typeof(MemoryUtility),
                    nameof(MemoryUtility.ClearAllMapsAndWorld));
                if (clearAllMapsAndWorld != null)
                {
                    harmony.Patch(
                        clearAllMapsAndWorld,
                        postfix: new HarmonyMethod(
                            typeof(Patch_GodHandsSessionLifecycle),
                            nameof(ClearAllMapsAndWorldPostfix))
                        {
                            priority = Priority.Last
                        });
                }

                Log.Message(
                    "[MP-MeowOnlineShop] God Hands session lifecycle guard " +
                    "active: synchronized disconnect release; hand snapshots restore gameplay state.");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] God Hands session lifecycle guard " +
                    "install failed: " + e);
            }
        }

        private static void SetDisconnectedPostfix(object conn)
        {
            if (conn == null)
                return;

            try
            {
                FieldInfo serverPlayerField =
                    AccessTools.Field(conn.GetType(), "serverPlayer");
                object serverPlayer = serverPlayerField?.GetValue(conn);
                if (serverPlayer == null)
                    return;

                FieldInfo idField =
                    AccessTools.Field(serverPlayer.GetType(), "id");
                int playerId =
                    idField != null && idField.GetValue(serverPlayer) is int id
                        ? id
                        : -1;
                if (playerId < 0)
                    return;

                // SetDisconnected can run on the server thread. Defer the
                // dictionary mutation to the main thread so it can never race
                // command execution or UI dispatch.
                Type onMainThreadType =
                    AccessTools.TypeByName("Multiplayer.Client.OnMainThread");
                MethodInfo enqueue = onMainThreadType?.GetMethod(
                    "Enqueue",
                    BindingFlags.Static | BindingFlags.Public);
                enqueue?.Invoke(
                    null,
                    new object[]
                    {
                        new Action(() =>
                        {
                            if (MP.IsInMultiplayer && MP.IsHosting)
                                foreach (Map map in Find.Maps)
                                    GodHandSync.DisconnectSync?.DoSync(null, map, playerId);
                        })
                    });
            }
            catch
            {
                // Session cleanup must never break disconnection handling.
            }
        }

        private static void StopMultiplayerPostfix()
        {
            try
            {
                GodHandSync.ResetAllSessions();
            }
            catch
            {
                // Session cleanup must never break stopping multiplayer.
            }
        }

        private static void ClearAllMapsAndWorldPostfix()
        {
            try
            {
                // Discard the old graph before loading replacement references.
                GodHandSync.ResetAllSessions();
            }
            catch
            {
                // Session cleanup must never break map/world teardown.
            }
        }
    }
}
