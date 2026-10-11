using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    internal static class NivarianGuestHosting
    {
        static Type letterType;
        static FieldInfo mapField, status;
        static MethodInfo accept, decline;
        static ISyncMethod decide;
        internal static void Apply(Harmony harmony)
        {
            letterType = NivarianUpdateActions.RequiredType("Nivarian_Race.Code.Incidents.ChoiceLetter_NivarianHostVisitors");
            mapField = AccessTools.Field(letterType, "targetMap") ?? throw new MissingFieldException(letterType.FullName, "targetMap");
            status = AccessTools.Field(letterType, "stat") ?? throw new MissingFieldException(letterType.FullName, "stat");
            accept = NivarianUpdateActions.RequiredMethod(letterType, "ExecuteHost");
            decline = NivarianUpdateActions.RequiredMethod(letterType, "ExecuteDecline");
            decide = MP.RegisterSyncMethod(typeof(NivarianGuestHosting), nameof(Decide));
            foreach (var method in new[] { accept, decline })
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(NivarianGuestHosting), nameof(Decision)));
            var cull = typeof(Letter).GetInterfaceMap(typeof(IArchivable));
            int index = Array.FindIndex(cull.InterfaceMethods, m => m.Name == "get_CanCullArchivedNow");
            if (index < 0) throw new MissingMethodException("IArchivable.CanCullArchivedNow");
            harmony.Patch(cull.TargetMethods[index], prefix: new HarmonyMethod(typeof(NivarianGuestHosting), nameof(CanCull)));
            Log.Message("[TaleNivarianCompat] guest hosting accept/decline resolved; letter/map identity and native Lord transition preserved.");
        }
        static bool Pending(Letter letter) => letter != null && letterType.IsInstanceOfType(letter) && Convert.ToInt32(status.GetValue(letter)) == 0;
        static bool Decision(Letter __instance, MethodBase __originalMethod)
        {
            if (!MP.IsInMultiplayer || !MP.InInterface) return true;
            if (Pending(__instance) && mapField.GetValue(__instance) is Map map)
                decide.DoSync(null, map, __instance, __originalMethod == accept);
            return false;
        }
        static void Decide(Map map, Letter letter, bool host)
        {
            if (map == null || !Find.Maps.Contains(map) || map.ParentFaction != Faction.OfPlayer
                || !Pending(letter) || !ReferenceEquals(mapField.GetValue(letter), map)) return;
            (host ? accept : decline).Invoke(letter, null);
        }
        static bool CanCull(Letter __instance, ref bool __result)
        {
            if (!MP.IsInMultiplayer || !letterType.IsInstanceOfType(__instance)) return true;
            var map = mapField.GetValue(__instance) as Map;
            __result = !Pending(__instance) || map == null || !Find.Maps.Contains(map);
            return false;
        }
    }
}
