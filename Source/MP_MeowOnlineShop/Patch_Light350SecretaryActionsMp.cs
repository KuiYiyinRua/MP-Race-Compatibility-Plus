using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    internal static class Patch_Light350SecretaryActionsMp
    {
        private static bool applied;
        internal static void Apply(Harmony harmony)
        {
            if (applied || !MP.enabled || !ModsConfig.IsActive("secretarynexus.secretarynexusracemod")) return;
            applied = true;
            try
            {
                var actions = new List<MethodInfo>();
                foreach (string name in new[] { "Comp_SEC_Change_Projectile", "Comp_SEC_Change_Projectile_Turret" })
                {
                    Type type = AccessTools.TypeByName("SEC_Assemblies." + name) ?? throw new TypeLoadException(name);
                    if (type.BaseType != typeof(ThingComp)) throw new InvalidOperationException(name + ": component base changed");
                    FieldInfo selected = AccessTools.DeclaredField(type, "projectilechosen");
                    if (selected == null || selected.IsStatic || selected.FieldType != typeof(int))
                        throw new MissingFieldException(name, "projectilechosen");
                    var candidates = type.GetMethods(AccessTools.allDeclared).Where(m => !m.IsStatic && m.ReturnType == typeof(void) &&
                        m.GetParameters().Length == 0 && m.IsDefined(typeof(CompilerGeneratedAttribute), false) && m.GetMethodBody() != null)
                        .Where(m => PatchProcessor.GetOriginalInstructions(m).Count(i => i.opcode == OpCodes.Stfld && Equals(i.operand, selected)) == 2).ToArray();
                    if (candidates.Length != 1) throw new InvalidOperationException(name + ": expected one original ammo cycling action");
                    actions.Add(candidates[0]);
                }
                // Sync the entire original action, including the equipped weapon's verbProps update.
                // Native ThingComp serialization retains both equipped and spawned turret identities.
                foreach (MethodInfo action in actions) MP.RegisterSyncMethod(action);
                Log.Message("[MP-MeowOnlineShop][Light350-B6] Secretary: 2 original ammo callbacks registered.");
            }
            catch (Exception e) { Log.Error("[MP-MeowOnlineShop][Light350-B6] REQUIRED TARGET FAILED Secretary ammo: " + e); }
        }
    }
}
