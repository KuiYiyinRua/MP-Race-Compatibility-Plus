using System;
using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace MP_MeowOnlineShop
{
    internal sealed class RigorMortisStorySession
    {
        internal int SessionId;
        internal int TargetThingId;
        internal string ManagerDefName;
        internal Window SourceWindow;
        internal int Version;
        internal bool IsClosed;
        internal int LastTouchedTick;
    }

    internal static class RigorMortisStorySessionManager
    {
        private const string LogTag = "[MP-MeowOnlineShop] RMStorySession";
        private static readonly Dictionary<int, RigorMortisStorySession> SessionsById = new Dictionary<int, RigorMortisStorySession>();
        private static readonly Dictionary<int, int> SessionByThingId = new Dictionary<int, int>();
        private static readonly HashSet<string> RuntimeLogOnce = new HashSet<string>();

        internal static int UpsertFromWindow(Thing target, object dialogManager, Window sourceWindow)
        {
            int thingId = target?.thingIDNumber ?? 0;
            string managerDefName = GetManagerDefName(dialogManager);
            int sessionId = BuildSessionId(thingId, sourceWindow, managerDefName);
            if (sessionId <= 0)
                sessionId = thingId > 0 ? thingId : Gen.HashCombineInt(DeterministicStringHash(sourceWindow?.GetType().FullName), DeterministicStringHash(managerDefName));

            if (!SessionsById.TryGetValue(sessionId, out var session))
            {
                session = new RigorMortisStorySession
                {
                    SessionId = sessionId,
                    Version = 0
                };
                SessionsById[sessionId] = session;
            }

            session.TargetThingId = thingId;
            session.ManagerDefName = managerDefName;
            session.SourceWindow = sourceWindow;
            session.IsClosed = false;
            session.LastTouchedTick = Find.TickManager?.TicksGame ?? 0;

            if (thingId > 0)
                SessionByThingId[thingId] = sessionId;

            LogRuntimeOnce($"upsert:{sessionId}", $"session upserted id={sessionId} thing={thingId} manager={session.ManagerDefName ?? "null"} window={sourceWindow?.GetType().FullName ?? "null"}");
            return sessionId;
        }

        internal static int TryGetSessionIdByThingId(int thingId)
        {
            if (thingId <= 0)
                return 0;
            return SessionByThingId.TryGetValue(thingId, out var sessionId) ? sessionId : 0;
        }

        internal static bool TryGetById(int sessionId, out RigorMortisStorySession session)
        {
            if (sessionId <= 0)
            {
                session = null;
                return false;
            }

            if (!SessionsById.TryGetValue(sessionId, out session))
                return false;
            if (session == null || session.IsClosed)
                return false;
            return true;
        }

        internal static bool TryGetByWindow(Window sourceWindow, out RigorMortisStorySession session)
        {
            session = null;
            if (sourceWindow == null)
                return false;

            foreach (var pair in SessionsById)
            {
                var candidate = pair.Value;
                if (candidate == null || candidate.IsClosed)
                    continue;
                if (!ReferenceEquals(candidate.SourceWindow, sourceWindow))
                    continue;
                session = candidate;
                return true;
            }

            return false;
        }

        internal static void MarkClosed(int sessionId, string reason)
        {
            if (!TryGetById(sessionId, out var session))
                return;

            session.IsClosed = true;
            session.SourceWindow = null;
            if (session.TargetThingId > 0 && SessionByThingId.TryGetValue(session.TargetThingId, out var mapped) && mapped == sessionId)
                SessionByThingId.Remove(session.TargetThingId);
            Log.Message($"{LogTag}: close session id={sessionId} reason={reason ?? "unknown"}.");
        }

        internal static void IncrementVersion(RigorMortisStorySession session)
        {
            if (session == null)
                return;
            session.Version++;
            session.LastTouchedTick = Find.TickManager?.TicksGame ?? 0;
        }

        internal static void CleanupClosed()
        {
            if (SessionsById.Count == 0)
                return;

            var removeIds = new List<int>();
            foreach (var pair in SessionsById)
            {
                var session = pair.Value;
                if (session == null || session.IsClosed)
                {
                    removeIds.Add(pair.Key);
                    continue;
                }

                if (session.SourceWindow == null)
                {
                    removeIds.Add(pair.Key);
                    continue;
                }

                bool sourceOpen = Find.WindowStack != null && Find.WindowStack.IsOpen(session.SourceWindow);
                if (!sourceOpen)
                    removeIds.Add(pair.Key);
            }

            for (int i = 0; i < removeIds.Count; i++)
            {
                int id = removeIds[i];
                if (SessionsById.TryGetValue(id, out var session) && session?.TargetThingId > 0)
                {
                    if (SessionByThingId.TryGetValue(session.TargetThingId, out var mapped) && mapped == id)
                        SessionByThingId.Remove(session.TargetThingId);
                }
                SessionsById.Remove(id);
            }
        }

        private static int BuildSessionId(int thingId, Window sourceWindow, string managerDefName)
        {
            int baseId = thingId > 0 ? thingId : 0;
            var windowType = sourceWindow?.GetType();
            baseId = Gen.HashCombineInt(baseId, DeterministicStringHash(windowType?.FullName));
            baseId = Gen.HashCombineInt(baseId, DeterministicStringHash(managerDefName));
            if (baseId == 0)
                return 0;
            if (baseId < 0)
                baseId = -baseId;
            return baseId;
        }

        private static int DeterministicStringHash(string value)
        {
            if (string.IsNullOrEmpty(value))
                return 0;

            int hash = 0;
            for (int i = 0; i < value.Length; i++)
                hash = Gen.HashCombineInt(hash, value[i]);
            return hash;
        }

        private static string GetManagerDefName(object dialogManager)
        {
            if (dialogManager == null)
                return null;
            try
            {
                var type = dialogManager.GetType();
                return AccessTools.Field(type, "defName")?.GetValue(dialogManager) as string
                    ?? AccessTools.Property(type, "defName")?.GetValue(dialogManager) as string
                    ?? AccessTools.Field(type, "dialogDefName")?.GetValue(dialogManager) as string
                    ?? AccessTools.Property(type, "dialogDefName")?.GetValue(dialogManager) as string
                    ?? type.Name;
            }
            catch
            {
                return dialogManager.GetType().Name;
            }
        }

        private static void LogRuntimeOnce(string key, string detail)
        {
            if (!RuntimeLogOnce.Add(key))
                return;
            Log.Message($"{LogTag}: {detail}");
        }
    }
}
