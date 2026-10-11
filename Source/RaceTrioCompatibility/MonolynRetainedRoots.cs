using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.RaceTrioCompatibility
{
    // Keep retained native captures canonical after the ordinary deep owners
    // have saved, without inserting old objects into the world's tick lists.
    internal static class MonolynRetainedRoots
    {
        sealed class Identity : IEqualityComparer<object>
        {
            public new bool Equals(object a, object b) => ReferenceEquals(a, b);
            public int GetHashCode(object value) => RuntimeHelpers.GetHashCode(value);
        }
        sealed class SaveScope
        {
            internal readonly HashSet<object> Deep = new HashSet<object>(new Identity());
            internal readonly HashSet<object> WantedSet = new HashSet<object>(new Identity());
            internal readonly List<object> Wanted = new List<object>();
            internal bool Written;
        }
        sealed class Loaded { internal readonly List<MonolynRetainedRecord> Records = new List<MonolynRetainedRecord>(); }
        static readonly ConditionalWeakTable<Game, Loaded> loaded = new ConditionalWeakTable<Game, Loaded>();
        [ThreadStatic] static SaveScope saving;
        [ThreadStatic] static int loading;
        [ThreadStatic] static Thing allowedReference;
        [ThreadStatic] static HashSet<object> allowedCollection;

        internal static void Apply(Harmony harmony)
        {
            var saveDoc = AccessTools.Method(AccessTools.TypeByName("Multiplayer.Client.SaveLoad"), "SaveGameToDoc", Type.EmptyTypes)
                ?? throw new MissingMethodException("Multiplayer.Client.SaveLoad", "SaveGameToDoc");
            foreach (var method in new[] { saveDoc, AccessTools.DeclaredMethod(typeof(Game), nameof(Game.ExposeData), Type.EmptyTypes) })
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(MonolynRetainedRoots), nameof(BeginSave)),
                    finalizer: new HarmonyMethod(typeof(MonolynRetainedRoots), nameof(EndSave)));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(Game), nameof(Game.LoadGame), Type.EmptyTypes),
                prefix: new HarmonyMethod(typeof(MonolynRetainedRoots), nameof(BeginLoad)),
                finalizer: new HarmonyMethod(typeof(MonolynRetainedRoots), nameof(EndLoad)));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(CameraDriver), "Expose", Type.EmptyTypes),
                postfix: new HarmonyMethod(typeof(MonolynRetainedRoots), nameof(AtCamera)));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(DebugLoadIDsSavingErrorsChecker), "RegisterDeepSaved", new[] { typeof(object), typeof(string) }),
                prefix: new HarmonyMethod(typeof(MonolynRetainedRoots), nameof(DeepSaved)));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(Scribe_References), "CheckSaveReferenceToDestroyedThing", new[] { typeof(Thing), typeof(string), typeof(bool) }),
                prefix: new HarmonyMethod(typeof(MonolynRetainedRoots), nameof(CheckReference)));
            var compression = AccessTools.TypeByName("Multiplayer.Client.SaveCompression") ?? throw new TypeLoadException("Multiplayer.Client.SaveCompression");
            foreach (var name in new[] { "SaveRock", "SaveRockRubble", "SavePlant" })
            {
                var method = AccessTools.DeclaredMethod(compression, name, new[] { typeof(Map), typeof(System.IO.BinaryWriter), typeof(IntVec3) })
                    ?? throw new MissingMethodException(compression.FullName, name);
                harmony.Patch(method, transpiler: new HarmonyMethod(typeof(MonolynRetainedRoots), nameof(CompressedOwner)));
            }
            Log.Message("[MonolynRetainedRoots] resolved=9 native save/load/deep/compressed-owner seams, canonical retained identity; missing=0");
        }
        static void BeginSave(out SaveScope __state)
        {
            __state = saving;
            if (MP.IsInMultiplayer && saving == null) saving = new SaveScope();
        }
        static Exception EndSave(Exception __exception, SaveScope __state)
        {
            saving = __state;
            return __exception;
        }
        static void BeginLoad(out int __state) { __state = loading; loading++; }
        static Exception EndLoad(Exception __exception, int __state) { loading = __state; return __exception; }
        static void DeepSaved(object obj) { if (saving != null && obj != null) saving.Deep.Add(obj); }
        static IEnumerable<CodeInstruction> CompressedOwner(IEnumerable<CodeInstruction> instructions)
        {
            int count = 0;
            var id = AccessTools.DeclaredField(typeof(Thing), nameof(Thing.thingIDNumber));
            foreach (var code in instructions)
            {
                // This read is inside the native successful binary-save branch,
                // after IsSaveCompressible; do not infer ownership from Spawned.
                if (code.opcode == OpCodes.Ldfld && Equals(code.operand, id))
                {
                    code.opcode = OpCodes.Call;
                    code.operand = AccessTools.Method(typeof(MonolynRetainedRoots), nameof(CompressedID));
                    count++;
                }
                yield return code;
            }
            if (count != 1) throw new InvalidOperationException("Expected one native compressed Thing ID write branch; found " + count);
        }
        static int CompressedID(Thing thing)
        {
            DeepSaved(thing);
            return thing.thingIDNumber;
        }
        static bool CheckReference(Thing th, ref bool __result)
        {
            // Only the explicit retained-cache reference writer may retain a
            // discarded Thing. Unrelated native references keep native policy.
            if (saving == null || (!ReferenceEquals(th, allowedReference) && !(allowedCollection?.Contains(th) ?? false))) return true;
            __result = false;
            return false;
        }
        internal static void Look<T>(ref T value, string key) where T : class, ILoadReferenceable
        {
            if (Scribe.mode == LoadSaveMode.Saving && saving != null) Want(value);
            var previous = allowedReference;
            allowedReference = value as Thing;
            try { Scribe_References.Look(ref value, key, true); }
            finally { allowedReference = previous; }
        }
        internal static void Look(ref LocalTargetInfo value, string key)
        {
            if (Scribe.mode == LoadSaveMode.Saving && saving != null) Want(value.Thing);
            var previous = allowedReference;
            allowedReference = value.Thing;
            try { Scribe_TargetInfo.Look(ref value, true, key, LocalTargetInfo.Invalid, true); }
            finally { allowedReference = previous; }
        }
        internal static void Look(ref List<Pawn> values, string key)
        {
            var previous = allowedCollection;
            allowedCollection = new HashSet<object>(new Identity());
            try
            {
                if (Scribe.mode == LoadSaveMode.Saving && saving != null && values != null)
                    foreach (var pawn in values)
                    {
                        Want(pawn);
                        if (pawn != null) allowedCollection.Add(pawn);
                    }
                Scribe_Collections.Look(ref values, key, true, LookMode.Reference);
            }
            finally { allowedCollection = previous; }
        }
        static void Add(object value)
        {
            if (value != null && saving.WantedSet.Add(value)) saving.Wanted.Add(value);
        }
        static void Want(object value)
        {
            if (value is Hediff hediff)
            {
                Want(hediff.pawn);
                Add(hediff);
            }
            else if (value is Thing thing)
            {
                // Retain a native holder before any captured contents, so its
                // deep serialization registers the contents only once.
                var chain = new List<Thing>();
                var seen = new HashSet<object>(new Identity());
                for (IThingHolder holder = thing.ParentHolder; holder != null; holder = holder.ParentHolder)
                {
                    if (!seen.Add(holder)) throw new InvalidOperationException("Retained native holder cycle");
                    if (holder is Thing parent) chain.Add(parent);
                }
                for (int i = chain.Count - 1; i >= 0; i--) Add(chain[i]);
                Add(thing);
            }
        }
        static object NextRoot()
        {
            // A captured Pawn must precede all captured Hediffs: its health
            // tracker may already own them. Order within each class is capture
            // traversal order, never HashSet enumeration order.
            foreach (var value in saving.Wanted)
                if (value is Thing && !saving.Deep.Contains(value)) return value;
            foreach (var value in saving.Wanted)
                if (value is Hediff && !saving.Deep.Contains(value)) return value;
            return null;
        }
        static void AtCamera()
        {
            if (Scribe.mode == LoadSaveMode.Saving && saving != null && !saving.Written)
            {
                saving.Written = true;
                if (!Scribe.EnterNode("meowMonolynRetainedRoots")) return;
                try
                {
                    int count = 0;
                    object root;
                    // Serializing a root can discover another native capture.
                    // Reevaluate the ordered work list after each full record.
                    while ((root = NextRoot()) != null)
                    {
                        var record = new MonolynRetainedRecord(root);
                        Scribe_Deep.Look(ref record, "root" + count);
                        if (!saving.Deep.Contains(root)) throw new InvalidOperationException("Retained root was not deep-saved");
                        count++;
                    }
                    Scribe_Values.Look(ref count, "count", 0, true);
                }
                finally { Scribe.ExitNode(); }
            }
            else if (Scribe.mode == LoadSaveMode.LoadingVars && loading != 0)
            {
                var store = loaded.GetValue(Current.Game, _ => new Loaded());
                store.Records.Clear();
                if (!Scribe.EnterNode("meowMonolynRetainedRoots")) return;
                // These are additional restoration constructors, not native
                // simulation actions. Do not consume the caller's random state.
                Rand.PushState();
                try
                {
                    int count = 0;
                    Scribe_Values.Look(ref count, "count");
                    if (count < 0 || count > 100000) throw new InvalidOperationException("Invalid retained root count");
                    for (int i = 0; i < count; i++)
                    {
                        MonolynRetainedRecord record = null;
                        Scribe_Deep.Look(ref record, "root" + i);
                        if (record == null) throw new InvalidOperationException("Missing retained root record");
                        store.Records.Add(record);
                    }
                }
                finally { Rand.PopState(); Scribe.ExitNode(); }
            }
        }
    }
    public sealed class MonolynRetainedRecord : IExposable
    {
        Thing thing;
        Hediff hediff;
        Pawn owner;
        public MonolynRetainedRecord() { }
        internal MonolynRetainedRecord(object value)
        {
            thing = value as Thing;
            hediff = value as Hediff;
            owner = hediff?.pawn;
        }
        public void ExposeData()
        {
            MonolynRetainedRoots.Look(ref owner, "owner");
            Scribe_Deep.Look(ref thing, true, "thing");
            Scribe_Deep.Look(ref hediff, "hediff");
            if (hediff != null && Scribe.mode == LoadSaveMode.ResolvingCrossRefs) hediff.pawn = owner;
        }
    }
}
