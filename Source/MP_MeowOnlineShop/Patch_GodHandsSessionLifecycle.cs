using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// GodHand drag/wrench sessions are process-local and are not serialized
    /// into Multiplayer snapshots. After a disconnect/rejoin the host can keep
    /// a session that the rejoining client no longer has; later GodHand
    /// commands then mutate only one side and the command Rand state diverges.
    /// This guard removes the disconnected player's sessions on the host when
    /// the server drops the connection, and clears all sessions when a local
    /// multiplayer session stops.
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

                Log.Message(
                    "[MP-MeowOnlineShop] God Hands session lifecycle guard " +
                    "active: stale drag/wrench sessions are removed on " +
                    "disconnect and session stop.");
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
                        new Action(() => GodHandSync.RemovePlayerSessions(playerId))
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
    }
}
