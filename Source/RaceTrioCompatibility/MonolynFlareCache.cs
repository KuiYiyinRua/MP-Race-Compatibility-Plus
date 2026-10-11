using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.RaceTrioCompatibility
{
    // Native flare logic keeps its first light-storage comp across apparel
    // transfer. Serialize its actual parent and comp slot, including null.
    internal static class MonolynFlareCache
    {
        sealed class Capture
        {
            internal ThingWithComps Parent;
            internal int Index = -1;
            internal bool Present;
        }
        static readonly ConditionalWeakTable<ThingComp, Capture> captures = new ConditionalWeakTable<ThingComp, Capture>();
        static FieldInfo storage;
        internal static void Apply(Harmony harmony)
        {
            var type = AccessTools.TypeByName("ASEL.CompAutoRepelFlarePack") ?? throw new TypeLoadException("ASEL.CompAutoRepelFlarePack");
            storage = AccessTools.DeclaredField(type, "lightStorage");
            if (storage == null || storage.FieldType.FullName != "ASEL.CompLightStorageLantern")
                throw new MissingFieldException(type.FullName, "lightStorage");
            harmony.Patch(AccessTools.DeclaredMethod(typeof(ThingComp), nameof(ThingComp.PostExposeData), Type.EmptyTypes),
                postfix: new HarmonyMethod(typeof(MonolynFlareCache), nameof(Save)));
            Log.Message("[MonolynFlareCache] resolved=native retained storage parent and comp slot; missing=0");
        }
        static void Save(ThingComp __instance)
        {
            if (!MP.IsInMultiplayer || !storage.DeclaringType.IsInstanceOfType(__instance)) return;
            var capture = captures.GetValue(__instance, _ => new Capture());
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                var value = (ThingComp)storage.GetValue(__instance);
                capture.Parent = value?.parent;
                capture.Index = value == null ? -1 : capture.Parent.AllComps.IndexOf(value);
                if (value != null && capture.Index < 0) throw new InvalidOperationException("Retained flare storage is not in its native parent");
                capture.Present = true;
            }
            Scribe_Values.Look(ref capture.Present, "meowFlareCapturePresent", false, true);
            if (!capture.Present) return; // Older/native saves retain lazy null.
            MonolynRetainedRoots.Look(ref capture.Parent, "meowFlareStorageParent");
            Scribe_Values.Look(ref capture.Index, "meowFlareStorageIndex", -1, true);
            if (Scribe.mode == LoadSaveMode.ResolvingCrossRefs || Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                ThingComp value = null;
                if (capture.Index >= 0)
                {
                    if (capture.Parent == null || capture.Index >= capture.Parent.AllComps.Count)
                        throw new InvalidOperationException("Missing retained flare storage parent/slot");
                    value = capture.Parent.AllComps[capture.Index];
                    if (!storage.FieldType.IsInstanceOfType(value))
                        throw new InvalidOperationException("Changed retained flare storage comp type");
                }
                storage.SetValue(__instance, value);
            }
            if (Scribe.mode == LoadSaveMode.PostLoadInit) captures.Remove(__instance);
        }
    }
}
