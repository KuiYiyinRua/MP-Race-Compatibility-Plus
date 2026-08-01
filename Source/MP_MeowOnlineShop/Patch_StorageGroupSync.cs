using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Desync-157: the official Multiplayer registration for the storage-group
    /// link lambda serializes `(IStorageGroupMember member,
    /// StorageGroupUtility.tmpMembers)`. On a rejoining client, replaying that
    /// command fails with
    /// `Sync Error ... List<RimWorld.IStorageGroupMember>` /
    /// `The value "Filth_Dirt362276" is not of type
    /// "RimWorld.IStorageGroupMember"`, leaving the map/world state permanently
    /// divergent.
    ///
    /// Fix: replace the link/unlink gizmo actions in
    /// `StorageGroupUtility.StorageGroupMemberGizmos` with primitive-only
    /// synchronized commands (thing IDs), mirroring the SearchAndDestroy
    /// pattern. The official lambda path is never used, so its fragile
    /// list/tuple serialization cannot poison a rejoin command stream.
    /// Singleplayer and the select-linked gizmo are untouched.
    /// </summary>
    internal static class Patch_StorageGroupSync
    {
        private const string StorageGroupUtilityTypeName =
            "RimWorld.StorageGroupUtility";
        private const string StorageGroupUtilityNamespace =
            "RimWorld.StorageGroupUtility";

        private static bool _applied;
        private static bool _loggedActive;
        private static bool _loggedFailure;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            try
            {
                Type storageType =
                    AccessTools.TypeByName(StorageGroupUtilityTypeName);
                MethodInfo target = storageType == null
                    ? null
                    : AccessTools.Method(
                        storageType,
                        "StorageGroupMemberGizmos",
                        new[] { typeof(IStorageGroupMember) });
                MethodInfo postfix = AccessTools.Method(
                    typeof(Patch_StorageGroupSync),
                    nameof(StorageGroupMemberGizmosPostfix));

                if (target == null || postfix == null)
                {
                    Log.Message(
                        "[MP-MeowOnlineShop] Storage-group sync replacement " +
                        "skipped (vanilla target not resolved).");
                    return;
                }

                MP.RegisterSyncMethod(
                    typeof(Patch_StorageGroupSync),
                    nameof(SyncLinkStorageGroups));
                MP.RegisterSyncMethod(
                    typeof(Patch_StorageGroupSync),
                    nameof(SyncUnlinkStorageGroup));

                harmony.Patch(
                    target,
                    postfix: new HarmonyMethod(postfix)
                    {
                        priority = Priority.Last
                    });

                Log.Message(
                    "[MP-MeowOnlineShop] Storage-group link/unlink sync replacement " +
                    "active: primitive thing-ID commands bypass the fragile " +
                    "official tuple serialization.");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Storage-group sync replacement apply " +
                    "failed: " + e.Message);
            }
        }

        private static void StorageGroupMemberGizmosPostfix(
            IStorageGroupMember member,
            ref IEnumerable<Gizmo> __result)
        {
            if (!MP.IsInMultiplayer || __result == null)
                return;

            try
            {
                int memberThingId = ThingIdOf(member);
                if (memberThingId == 0)
                    return;

                List<Gizmo> gizmos = __result.ToList();
                bool replaced = false;
                for (int i = 0; i < gizmos.Count; i++)
                {
                    if (!(gizmos[i] is Command_Action action) ||
                        action.action == null)
                    {
                        continue;
                    }

                    string methodName =
                        action.action.Method?.Name ?? "";
                    bool isLink =
                        methodName.IndexOf(
                            "StorageGroupMemberGizmos>b__0",
                            StringComparison.Ordinal) >= 0;
                    bool isUnlink =
                        methodName.IndexOf(
                            "StorageGroupMemberGizmos>b__2",
                            StringComparison.Ordinal) >= 0;

                    if (isLink)
                    {
                        int[] selectedIds = SelectedMemberThingIds();
                        Action original = action.action;
                        action.action = () =>
                        {
                            SyncLinkStorageGroups(memberThingId, selectedIds);
                        };
                        replaced = true;
                    }
                    else if (isUnlink)
                    {
                        Action original = action.action;
                        action.action = () =>
                        {
                            SyncUnlinkStorageGroup(memberThingId);
                        };
                        replaced = true;
                    }
                }

                if (!replaced)
                    return;

                __result = gizmos;
                if (!_loggedActive)
                {
                    _loggedActive = true;
                    Log.Message(
                        "[MP-MeowOnlineShop] Storage-group link/unlink clicks routed " +
                        "through primitive MP commands.");
                }
            }
            catch (Exception e)
            {
                if (!_loggedFailure)
                {
                    _loggedFailure = true;
                    Log.Warning(
                        "[MP-MeowOnlineShop] Storage-group gizmo replacement failed " +
                        "open: " + e.Message);
                }
            }
        }

        private static int[] SelectedMemberThingIds()
        {
            try
            {
                Type storageType =
                    AccessTools.TypeByName(StorageGroupUtilityTypeName);
                FieldInfo tmp = storageType == null
                    ? null
                    : AccessTools.Field(storageType, "tmpMembers");
                if (tmp == null ||
                    !(tmp.GetValue(null) is IEnumerable<IStorageGroupMember> members))
                {
                    return Array.Empty<int>();
                }

                List<int> ids = new List<int>();
                foreach (IStorageGroupMember m in members)
                {
                    int id = ThingIdOf(m);
                    if (id != 0 && !ids.Contains(id))
                        ids.Add(id);
                }
                return ids.ToArray();
            }
            catch
            {
                return Array.Empty<int>();
            }
        }

        private static int ThingIdOf(IStorageGroupMember member)
        {
            return (member as Thing)?.thingIDNumber ?? 0;
        }

        private static Thing FindThingById(int thingId)
        {
            if (Find.Maps != null)
            {
                for (int m = 0; m < Find.Maps.Count; m++)
                {
                    Map map = Find.Maps[m];
                    if (map?.listerThings?.AllThings == null)
                        continue;

                    for (int i = 0; i < map.listerThings.AllThings.Count; i++)
                    {
                        Thing thing = map.listerThings.AllThings[i];
                        if (thing != null && thing.thingIDNumber == thingId &&
                            !thing.Destroyed && thing.Spawned)
                        {
                            return thing;
                        }
                    }
                }
            }

            return null;
        }

        private static void SyncLinkStorageGroups(
            int memberThingId,
            int[] selectedThingIds)
        {
            if (selectedThingIds == null || selectedThingIds.Length < 2)
                return;

            List<IStorageGroupMember> members = new List<IStorageGroupMember>();
            Thing firstThing = null;
            for (int i = 0; i < selectedThingIds.Length; i++)
            {
                Thing thing = FindThingById(selectedThingIds[i]);
                if (thing is IStorageGroupMember sg && !members.Contains(sg))
                {
                    members.Add(sg);
                    if (firstThing == null)
                        firstThing = thing;
                }
            }

            if (members.Count < 2 || firstThing == null)
                return;

            IStorageGroupMember first = members[0];
            bool hadGroup = first.Group != null;
            StorageGroup group = first.Group ?? firstThing.Map.storageGroups.NewGroup();
            if (!hadGroup)
                group.InitFrom(first);

            foreach (IStorageGroupMember m in members)
                m.SetStorageGroup(group);

            if (members.Count > 1)
            {
                Messages.Message(
                    "SettingsLinkedFor".Translate(members.Count),
                    null,
                    MessageTypeDefOf.NeutralEvent,
                    false);
            }
            else
            {
                Messages.Message(
                    "SettingsLinkedForSingular".Translate(),
                    null,
                    MessageTypeDefOf.NeutralEvent,
                    false);
            }
        }

        private static void SyncUnlinkStorageGroup(int memberThingId)
        {
            Thing thing = FindThingById(memberThingId);
            if (!(thing is IStorageGroupMember member) ||
                member.Group == null)
            {
                return;
            }

            StorageSettings storeSettings = member.Group.GetStoreSettings();
            member.Group.RemoveMember(member);
            member.Group = null;
            if (member is Building_Storage buildingStorage)
                buildingStorage.settings.CopyFrom(storeSettings);

            Messages.Message(
                "SettingsUnlinkedForSingular".Translate(),
                null,
                MessageTypeDefOf.NeutralEvent,
                false);
        }
    }
}
