using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Meow.WolfeinAchtungCompatibility
{
    [StaticConstructorOnStartup]
    public static class WolfeinAbilities
    {
        private static readonly FieldInfo AbilityField = AccessTools.Field(typeof(Verb_CastAbility), "ability");
        private static ISyncMethod jump;

        static WolfeinAbilities()
        {
            if (!MP.enabled || !ModsConfig.IsActive("melondove.wolfeinrace")) return;
            LongEventHandler.ExecuteWhenFinished(Install);
        }

        private static void Install()
        {
            var type = AccessTools.TypeByName("Wolfein.Verb_CastAbilitySprint");
            var order = AccessTools.Method(type, "OrderForceTarget", new[] { typeof(LocalTargetInfo) });
            if (order == null || AbilityField == null)
                throw new MissingMethodException("Wolfein sprint ability target could not be resolved");
            order = AccessTools.DeclaredMethod(order.DeclaringType, order.Name, new[] { typeof(LocalTargetInfo) });
            jump = MP.RegisterSyncMethod(typeof(WolfeinAbilities), nameof(OrderSprint))
                .SetContext(SyncContext.QueueOrder_Down);
            new Harmony("meow.wolfein.abilities").Patch(order,
                prefix: new HarmonyMethod(typeof(WolfeinAbilities), nameof(BeforeOrder)) { priority = Priority.First });
            Log.Message("[MP Race] Wolfein sprint: stable Ability sync installed; native ability paths retained.");
        }

        private static bool BeforeOrder(Verb_CastAbility __instance, LocalTargetInfo target)
        {
            if (!MP.IsInMultiplayer || !MP.InInterface ||
                __instance.GetType().FullName != "Wolfein.Verb_CastAbilitySprint") return true;
            var ability = (Ability)AbilityField.GetValue(__instance);
            if (ability == null) throw new InvalidOperationException("Wolfein sprint has no ability owner");
            jump.DoSync(null, ability, target);
            return false;
        }

        // An interface-created CastJump Job cannot carry the custom runtime verb
        // through MP's deep Job serializer. MP already serializes Ability identity;
        // create the Job with its peer-local verb only during command execution.
        public static void OrderSprint(Ability ability, LocalTargetInfo target)
        {
            if (ability?.pawn?.Spawned != true || ability.verb == null || !target.IsValid) return;
            ability.verb.OrderForceTarget(target);
        }
    }
}
