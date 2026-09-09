using System;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    internal static class RavenTrashSettings
    {
        private static ISyncField[] fields;
        internal static void Apply(Harmony harmony)
        {
            var building = AccessTools.TypeByName("RavenRace.Buildings.Building_RavenTrashCan");
            var dialog = AccessTools.TypeByName("RavenRace.Buildings.Dialog_RavenTrashCan");
            if (building == null || dialog == null) throw new TypeLoadException("Raven trash settings");
            string[] names = { "AllowToxic", "AllowRotten", "AllowDeadmansApparel", "AllowBiocodedWeapons", "MaxItemValue" };
            foreach (string name in names)
                if (AccessTools.DeclaredField(building, name) == null) throw new MissingFieldException(building.FullName, name);
            fields = names.Select(n => MP.RegisterSyncField(building, n)).ToArray();
            harmony.Patch(AccessTools.DeclaredMethod(dialog, "DoWindowContents"),
                prefix: new HarmonyMethod(typeof(RavenTrashSettings), nameof(BeforeDraw)),
                finalizer: new HarmonyMethod(typeof(RavenTrashSettings), nameof(AfterDraw)));
        }
        private static void BeforeDraw(Thing ___trashCan, out bool __state)
        {
            __state = MP.InInterface && ___trashCan != null && !___trashCan.Destroyed;
            if (!__state) return;
            MP.WatchBegin();
            foreach (var field in fields) field.Watch(___trashCan);
        }
        private static void AfterDraw(bool __state)
        {
            if (__state) MP.WatchEnd();
        }
    }
}
