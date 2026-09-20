using System;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using Verse;
using RimWorld;

namespace Meow.TaleNivarianCompatibility
{
    internal static class NivarianWindowDebugTools
    {
        static System.Reflection.MethodInfo findBombardment, spawnCallback;
        static System.Reflection.FieldInfo spawnDef;
        static ISyncMethod spawn;
        internal static void Apply(Harmony harmony)
        {
            var owner = AccessTools.TypeByName("TestIconWindow")
                ?? throw new TypeLoadException("TestIconWindow");
            Register(owner, "<>c", "<DoWindowContents>b__18_2", Array.Empty<string>());
            Register(owner, "<>c", "<SpawnContinuumAssaultCraft01Tool>b__25_0", Array.Empty<string>());
            RegisterSpawn(harmony, owner);
            var defType = AccessTools.TypeByName("Nivarian_Race.Code.Defs.NivarianBombardmentDef")
                ?? throw new TypeLoadException("NivarianBombardmentDef");
            findBombardment = AccessTools.Method(typeof(DefDatabase<>).MakeGenericType(defType), "GetNamedSilentFail", new[] { typeof(string) })
                ?? throw new MissingMethodException("Bombardment DefDatabase.GetNamedSilentFail");
            harmony.Patch(AccessTools.DeclaredMethod(AccessTools.Inner(owner, "<>c"), "<DoWindowContents>b__18_2", Type.EmptyTypes),
                prefix: new HarmonyMethod(typeof(NivarianWindowDebugTools), nameof(ValidBombardment)));
            Log.Message("[TaleNivarianCompat] custom debug-window tools synchronized=3; map/mouse context and developer permission required.");
        }

        static void RegisterSpawn(Harmony harmony, Type owner)
        {
            var closure = AccessTools.Inner(owner, "<>c__DisplayClass26_0")
                ?? throw new TypeLoadException("TestIconWindow spawn closure");
            spawnDef = AccessTools.Field(closure, "def") ?? throw new MissingFieldException(closure.FullName, "def");
            if (spawnDef.FieldType != typeof(ThingDef) || AccessTools.GetDeclaredFields(closure).Count(f => !f.IsStatic) != 1)
                throw new InvalidOperationException("Unexpected programmable spawn captures");
            spawnCallback = AccessTools.DeclaredMethod(closure, "<SpawnAttackerTool>b__0", Type.EmptyTypes)
                ?? throw new MissingMethodException(closure.FullName, "spawn callback");
            // MP's field path parser treats a type name without a namespace dot as
            // an unqualified member path. Send the Def through a normal sync method
            // and reconstruct this one-field native closure only during execution.
            spawn = MP.RegisterSyncMethod(typeof(NivarianWindowDebugTools), nameof(Spawn))
                .SetContext(SyncContext.MapMouseCell).SetDebugOnly();
            harmony.Patch(spawnCallback, prefix: new HarmonyMethod(typeof(NivarianWindowDebugTools), nameof(SpawnPrefix)));
        }

        static bool SpawnPrefix(object __instance)
        {
            if (!MP.IsInMultiplayer || !MP.InInterface) return true;
            return !spawn.DoSync(null, (ThingDef)spawnDef.GetValue(__instance));
        }

        static void Spawn(ThingDef def)
        {
            if (def == null || Find.CurrentMap == null) return;
            var closure = Activator.CreateInstance(spawnCallback.DeclaringType, true);
            spawnDef.SetValue(closure, def);
            spawnCallback.Invoke(closure, null);
        }
        static bool ValidBombardment()
        {
            if (!MP.IsInMultiplayer || MP.InInterface) return true;
            if (findBombardment.Invoke(null, new object[] { "NivarianBombardment_HeavyExplosive" }) != null)
                return true;
            Messages.Message("Nivarian debug bombardment is unavailable: NivarianBombardment_HeavyExplosive is missing.", MessageTypeDefOf.RejectInput, false);
            return false;
        }
        static void Register(Type owner, string nestedName, string methodName, string[] fields)
        {
            var nested = AccessTools.Inner(owner, nestedName)
                ?? throw new TypeLoadException(owner.FullName + "+" + nestedName);
            var method = AccessTools.DeclaredMethod(nested, methodName, Type.EmptyTypes)
                ?? throw new MissingMethodException(nested.FullName, methodName);
            if (method.ReturnType != typeof(void) || method.IsStatic)
                throw new InvalidOperationException("Unexpected debug callback signature: " + method);
            var captured = AccessTools.GetDeclaredFields(nested).Where(f => !f.IsStatic).ToArray();
            if (captured.Length != fields.Length || captured.Any(f => !fields.Contains(f.Name)))
                throw new InvalidOperationException("Unexpected debug callback captures: " + nested.FullName);
            if (fields.Length != 0 && captured.Single().FieldType != typeof(ThingDef))
                throw new InvalidOperationException("Unexpected spawn definition capture");
            // Tools installed by a custom window do not pass through DebugSync.HandleCmd.
            // Synchronize the click itself; opening and closing the window stay local.
            MP.RegisterSyncDelegate(owner, nestedName, methodName, fields)
                .SetContext(SyncContext.MapMouseCell).SetDebugOnly();
        }
    }
}
