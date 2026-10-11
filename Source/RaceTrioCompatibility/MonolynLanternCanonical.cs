using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.RaceTrioCompatibility
{
    // The production lantern adapter may call TrySave first, then fall back to
    // its existing legacy reader when this new-format marker is absent.
    internal static class MonolynLanternCanonical
    {
        sealed class Capture { internal bool Present; }
        static readonly ConditionalWeakTable<ThingComp, Capture> captures = new ConditionalWeakTable<ThingComp, Capture>();
        static FieldInfo[] fields;
        internal static void Initialize()
        {
            var type = AccessTools.TypeByName("ASEL.CompLightCharge") ?? throw new TypeLoadException("ASEL.CompLightCharge");
            fields = new[] { AccessTools.DeclaredField(type, "lcI"), AccessTools.DeclaredField(type, "faith") };
            foreach (var field in fields)
                if (field?.FieldType != typeof(Hediff)) throw new MissingFieldException(type.FullName, "native Hediff cache");
        }
        internal static bool TrySave(ThingComp instance)
        {
            if (!MP.IsInMultiplayer) return false;
            var capture = captures.GetValue(instance, _ => new Capture());
            if (Scribe.mode == LoadSaveMode.Saving) capture.Present = true;
            Scribe_Values.Look(ref capture.Present, "meowLanternCanonicalPresent", false, true);
            if (!capture.Present) return false;
            foreach (var field in fields)
            {
                var value = (Hediff)field.GetValue(instance);
                // The same cache object is referenced by every capturing comp;
                // its owner/deep root is chosen once after world/maps save.
                MonolynRetainedRoots.Look(ref value, "meowLanternCanonical_" + field.Name);
                field.SetValue(instance, value);
            }
            if (Scribe.mode == LoadSaveMode.PostLoadInit) captures.Remove(instance);
            return true;
        }
    }
}
