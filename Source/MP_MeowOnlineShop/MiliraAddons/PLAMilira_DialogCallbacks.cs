using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat;

[StaticConstructorOnStartup]
public static class PLAMilira_DialogCallbacks
{
    sealed class Owner { internal Hediff Value; }
    static readonly ConditionalWeakTable<object, Owner> owners = new ConditionalWeakTable<object, Owner>();
    static FieldInfo routerField;
    static ConstructorInfo nodeConstructor;
    [ThreadStatic] static bool rebuilding;

    static PLAMilira_DialogCallbacks()
    {
        if (!CompatibilityPatchCategories.IsEnabled("milira") || MiliraMpCompatGate.ReferenceModActive ||
            !MP.enabled || !ModsConfig.IsActive("sleepycot.wingsofdemocracy")) return;
        LongEventHandler.ExecuteWhenFinished(() =>
        {
            try
            {
                var hediffType = AccessTools.TypeByName("PLAMilira.Hediff_PLAMiliraDialog") ?? throw new TypeLoadException("Hediff_PLAMiliraDialog");
                var nodeType = AccessTools.TypeByName("PLAMilira.CustomDiaNode") ?? throw new TypeLoadException("CustomDiaNode");
                routerField = AccessTools.DeclaredField(hediffType, "DialogRouter") ?? throw new MissingFieldException("DialogRouter");
                nodeConstructor = AccessTools.Constructor(nodeType, new[] { typeof(string), routerField.FieldType, typeof(Pawn) }) ?? throw new MissingMethodException("CustomDiaNode constructor");
                var harmony = new Harmony("Meow.PLAMilira.DialogCallbacks");
                MiliraDialogDelegates.Register(AccessTools.DeclaredMethod(typeof(SavedLink), nameof(SavedLink.Invoke), Type.EmptyTypes));
                MiliraDialogDelegates.Register(AccessTools.DeclaredMethod(typeof(SavedAction), nameof(SavedAction.Invoke), Type.EmptyTypes));
                int count = 0;
                foreach (var type in hediffType.Assembly.GetTypes().Where(t => hediffType.IsAssignableFrom(t)))
                {
                    var method = AccessTools.DeclaredMethod(type, "InitializeDialog", Type.EmptyTypes);
                    if (method == null) continue;
                    harmony.Patch(method, postfix: new HarmonyMethod(typeof(PLAMilira_DialogCallbacks), nameof(RegisterOwner)));
                    count++;
                }
                if (count < 2) throw new MissingMethodException("Student dialogue initializers");
                harmony.Patch(nodeConstructor, postfix: new HarmonyMethod(typeof(PLAMilira_DialogCallbacks), nameof(Wrap)));
                Log.Message("[PLAMilira_DialogCallbacks] REQUIRED_TARGETS_OK native node callbacks; initializers=" + count);
            }
            catch (Exception e) { Log.Error("[PLAMilira_DialogCallbacks] REQUIRED_TARGET_FAILED " + e); }
        });
    }

    static void RegisterOwner(Hediff __instance)
    {
        object router = routerField.GetValue(__instance);
        if (router != null) owners.GetValue(router, _ => new Owner()).Value = __instance;
    }

    static void Wrap(DiaNode __instance, string __0, object __1, Pawn __2)
    {
        if (rebuilding || !MP.IsInMultiplayer) return;
        if (!owners.TryGetValue(__1, out var owner) || owner.Value == null)
            throw new InvalidOperationException("Student dialogue router has no registered owner");
        for (int i = 0; i < __instance.options.Count; i++)
        {
            var option = __instance.options[i];
            if (option.action != null)
            {
                var savedAction = new SavedAction
                {
                    owner = owner.Value, speaker = __2, sourceNode = __0, optionIndex = i,
                    signature = ActionSignature(option.action)
                };
                savedAction.SetLive(option.action);
                option.action = savedAction.Invoke;
            }
            if (option.linkLateBind == null) continue;
            var callback = new SavedLink
            {
                owner = owner.Value, speaker = __2, sourceNode = __0, optionIndex = i,
                signature = Signature(option.linkLateBind)
            };
            callback.SetLive(option.linkLateBind);
            option.linkLateBind = callback.Invoke;
        }
    }

    static string Signature(Func<DiaNode> callback)
    {
        var target = callback.Target ?? throw new InvalidOperationException("Student link target missing");
        var type = target.GetType();
        var destination = AccessTools.DeclaredField(type, "targetNodeId") ?? throw new MissingFieldException(type.FullName, "targetNodeId");
        var actionField = AccessTools.DeclaredField(type, "onSelect") ?? throw new MissingFieldException(type.FullName, "onSelect");
        var action = (Action)actionField.GetValue(target);
        return (string)destination.GetValue(target) + "|" + action?.Method.DeclaringType?.FullName + "|" + action?.Method.Name;
    }

    // MP enumerates declared delegate-target fields. Keep the live native closure
    // on a base class; only stable context appears on the exposed target itself.
    public abstract class LiveLink
    {
        protected Func<DiaNode> live;
        internal void SetLive(Func<DiaNode> value) => live = value;
    }

    static string ActionSignature(Action action) => action.Method.DeclaringType.FullName + "|" + action.Method.Name;

    public abstract class LiveAction
    {
        protected Action live;
        internal void SetLive(Action value) => live = value;
    }

    public sealed class SavedAction : LiveAction, IExposable
    {
        public Hediff owner;
        public Pawn speaker;
        public string sourceNode, signature;
        public int optionIndex;

        public void ExposeData()
        {
            Scribe_References.Look(ref owner, "owner");
            Scribe_References.Look(ref speaker, "speaker");
            Scribe_Values.Look(ref sourceNode, "sourceNode");
            Scribe_Values.Look(ref signature, "signature");
            Scribe_Values.Look(ref optionIndex, "optionIndex");
        }

        public void Invoke()
        {
            if (live == null)
            {
                if (owner == null || speaker == null) throw new InvalidOperationException("Student action references were not restored");
                bool previous = rebuilding;
                try
                {
                    rebuilding = true;
                    var node = (DiaNode)nodeConstructor.Invoke(new[] { (object)sourceNode, routerField.GetValue(owner), speaker });
                    if (optionIndex < 0 || optionIndex >= node.options.Count) throw new InvalidOperationException("Student action no longer exists");
                    var restored = node.options[optionIndex].action;
                    if (restored == null || ActionSignature(restored) != signature) throw new InvalidOperationException("Student action identity changed");
                    live = restored;
                }
                finally { rebuilding = previous; }
            }
            live();
        }
    }

    public sealed class SavedLink : LiveLink, IExposable
    {
        public Hediff owner;
        public Pawn speaker;
        public string sourceNode, signature;
        public int optionIndex;

        public void ExposeData()
        {
            Scribe_References.Look(ref owner, "owner");
            Scribe_References.Look(ref speaker, "speaker");
            Scribe_Values.Look(ref sourceNode, "sourceNode");
            Scribe_Values.Look(ref signature, "signature");
            Scribe_Values.Look(ref optionIndex, "optionIndex");
        }

        public DiaNode Invoke()
        {
            if (live == null)
            {
                if (owner == null || speaker == null) throw new InvalidOperationException("Student dialogue references were not restored");
                bool previous = rebuilding;
                try
                {
                    rebuilding = true;
                    var node = (DiaNode)nodeConstructor.Invoke(new[] { (object)sourceNode, routerField.GetValue(owner), speaker });
                    if (optionIndex < 0 || optionIndex >= node.options.Count) throw new InvalidOperationException("Student dialogue option no longer exists");
                    var restored = node.options[optionIndex].linkLateBind;
                    if (restored == null || Signature(restored) != signature) throw new InvalidOperationException("Student dialogue option identity changed");
                    live = restored;
                }
                finally { rebuilding = previous; }
            }
            return live();
        }
    }
}
