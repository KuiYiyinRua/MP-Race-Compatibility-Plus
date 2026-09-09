using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// The two MoeLotl crossbow verb implementations wrap their Qi-energy
    /// branches and base TryCastShot in retry-prone control flow. If the first
    /// base call throws after spawning a projectile, or returns false on the
    /// final supplemental shot, the original code can call base again and
    /// advance the shared ThingID/Rand streams. In multiplayer keep the
    /// weapon-specific state machine, but call the base shot at most once.
    /// </summary>
    internal static class Patch_AxolotlCrossbowVerbDeterminism
    {
        private const string VerbTypeName = "Axolotl.Verb_LotlQiIntensifyShoot";
        private const string WeaponCompTypeName = "Axolotl.CompLotiQiRangedWeapon_ChangeProjectile";
        private const string WeaponPropsTypeName = "Axolotl.CompProperties_LotiQiRangedWeapon_ChangeProjectile";
        private const string SupplementVerbTypeName = "Axolotl.Verb_LotlQiSupplementShoot";
        private const string SupplementWeaponCompTypeName = "Axolotl.CompLotiQiRangedWeapon_SupplementShoot";
        private const string SupplementWeaponPropsTypeName = "Axolotl.CompProperties_LotiQiRangedWeapon_SupplementShoot";
        private const string EnergyCompTypeName = "Axolotl.CompAxolotlEnergy";
        private const string EnergyUtilityTypeName = "Axolotl.AxolotlUtility+MoelotlEnergyUtility";

        private static bool _applied;

        private static Type _verbType;
        private static PropertyInfo _getPawnProperty;
        private static PropertyInfo _pawnCompProperty;
        private static PropertyInfo _canUseProperty;
        private static PropertyInfo _weaponCompProperty;
        private static PropertyInfo _weaponPropsProperty;
        private static FieldInfo _costPerShootField;
        private static MethodInfo _doEnergyCostMethod;
        private static MethodInfo _autoCloseMethod;
        private static MethodInfo _baseTryCastShotMethod;
        private static Action<Verb> _runQiEnergySideEffects;
        private static BaseTryCastShotDelegate _baseTryCastShot;

        private static Type _supplementVerbType;
        private static PropertyInfo _supplementGetPawnProperty;
        private static PropertyInfo _supplementPawnCompProperty;
        private static PropertyInfo _supplementCanUseProperty;
        private static PropertyInfo _supplementWeaponCompProperty;
        private static PropertyInfo _supplementWeaponPropsProperty;
        private static FieldInfo _supplementCostPerShootField;
        private static FieldInfo _supplementIsSupplementShootField;
        private static FieldInfo _supplementBurstShotsLeftField;
        private static FieldInfo _supplementBurstShotCountField;
        private static Action<Verb> _runSupplementQiEnergySideEffects;

        private delegate bool BaseTryCastShotDelegate(Verb_Shoot instance);

        internal static void Apply(Harmony harmony)
        {
            if (_applied || harmony == null || !MP.enabled)
                return;
            _applied = true;

            ResolveTargets();
            bool installed = false;
            if (IntensifyTargetsReady())
            {
                MethodInfo target = AccessTools.DeclaredMethod(
                    _verbType, "TryCastShot", Type.EmptyTypes);
                MethodInfo prefix = AccessTools.Method(
                    typeof(Patch_AxolotlCrossbowVerbDeterminism),
                    nameof(TryCastShotPrefix));
                try
                {
                    _runQiEnergySideEffects = CreateQiEnergySideEffectsInvoker(
                        _verbType,
                        _getPawnProperty,
                        _pawnCompProperty,
                        _canUseProperty,
                        _weaponCompProperty,
                        _weaponPropsProperty,
                        _costPerShootField);
                    if (_runQiEnergySideEffects != null &&
                        target != null && prefix != null &&
                        InstallBaseTryCastShotReversePatch(harmony))
                    {
                        harmony.Patch(
                            target,
                            prefix: new HarmonyMethod(prefix)
                            {
                                priority = Priority.First
                            });
                        installed = true;
                        Log.Message(
                            "[MP-MeowOnlineShop] Axolotl crossbow single-shot guard " +
                            "active for IntensifyShoot.");
                    }
                }
                catch (Exception e)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Axolotl IntensifyShoot guard " +
                        "install failed: " + e.Message);
                }
            }
            else
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Axolotl IntensifyShoot guard: " +
                    "target members were not fully resolved; skipped.");
            }

            ResolveSupplementTargets();
            if (SupplementTargetsReady())
            {
                MethodInfo target = AccessTools.DeclaredMethod(
                    _supplementVerbType, "TryCastShot", Type.EmptyTypes);
                MethodInfo prefix = AccessTools.Method(
                    typeof(Patch_AxolotlCrossbowVerbDeterminism),
                    nameof(SupplementTryCastShotPrefix));
                try
                {
                    _runSupplementQiEnergySideEffects = CreateQiEnergySideEffectsInvoker(
                        _supplementVerbType,
                        _supplementGetPawnProperty,
                        _supplementPawnCompProperty,
                        _supplementCanUseProperty,
                        _supplementWeaponCompProperty,
                        _supplementWeaponPropsProperty,
                        _supplementCostPerShootField);
                    if (_runSupplementQiEnergySideEffects != null &&
                        target != null && prefix != null &&
                        InstallBaseTryCastShotReversePatch(harmony))
                    {
                        harmony.Patch(
                            target,
                            prefix: new HarmonyMethod(prefix)
                            {
                                priority = Priority.First
                            });
                        installed = true;
                        Log.Message(
                            "[MP-MeowOnlineShop] Axolotl crossbow single-shot guard " +
                            "active for SupplementShoot (BattlebreakerCrossbow).");
                    }
                }
                catch (Exception e)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Axolotl SupplementShoot guard " +
                        "install failed: " + e.Message);
                }
            }
            else
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Axolotl SupplementShoot guard: " +
                    "target members were not fully resolved; skipped.");
            }

            if (!installed)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Axolotl crossbow single-shot guards " +
                    "were not installed.");
            }
        }

        // MethodInfo.Invoke on the virtual Verb_Shoot.TryCastShot still
        // dispatches to the Axolotl override, so a prefix that re-enters it
        // recurses forever. A Harmony reverse patch copies the base method's
        // IL into our stub and calls it non-virtually.
        private static bool InstallBaseTryCastShotReversePatch(Harmony harmony)
        {
            if (_baseTryCastShotMethod == null || harmony == null)
                return false;
            if (_baseTryCastShot != null)
                return true;

            try
            {
                MethodInfo reversePatchedMethod = harmony.CreateReversePatcher(
                        _baseTryCastShotMethod,
                        new HarmonyMethod(AccessTools.Method(
                            typeof(Patch_AxolotlCrossbowVerbDeterminism),
                            nameof(CallOriginalVerbShootTryCastShot))))
                    .Patch();

                // Harmony returns the generated replacement that contains the
                // copied Verb_Shoot.TryCastShot IL. Bind that method once and
                // invoke it directly on every shot, bypassing the detoured
                // reverse-patch stand-in and its extra wrapper. Some older
                // Harmony/Mono combinations cannot create this delegate; the
                // already-installed stand-in remains a safe compatibility
                // fallback with the exact previous behavior.
                try
                {
                    _baseTryCastShot = (BaseTryCastShotDelegate)
                        reversePatchedMethod.CreateDelegate(
                            typeof(BaseTryCastShotDelegate));
                }
                catch (Exception e)
                {
                    _baseTryCastShot = CallOriginalVerbShootTryCastShot;
                    Log.Warning(
                        "[MP-MeowOnlineShop] Axolotl crossbow direct base-shot " +
                        "delegate unavailable; using reverse-patch stand-in: " +
                        e.Message);
                }

                return _baseTryCastShot != null;
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Axolotl crossbow base TryCastShot " +
                    "reverse patch failed: " + e.Message);
                return false;
            }
        }

        private static bool CallOriginalVerbShootTryCastShot(Verb_Shoot instance)
        {
            throw new NotSupportedException("Harmony reverse patch stub");
        }

        private static Action<Verb> CreateQiEnergySideEffectsInvoker(
            Type verbType,
            PropertyInfo getPawnProperty,
            PropertyInfo pawnCompProperty,
            PropertyInfo canUseProperty,
            PropertyInfo weaponCompProperty,
            PropertyInfo weaponPropsProperty,
            FieldInfo costPerShootField)
        {
            MethodInfo getPawn = getPawnProperty.GetGetMethod(true);
            MethodInfo getPawnComp = pawnCompProperty.GetGetMethod(true);
            MethodInfo getCanUse = canUseProperty.GetGetMethod(true);
            MethodInfo getWeaponComp = weaponCompProperty.GetGetMethod(true);
            MethodInfo getWeaponProps = weaponPropsProperty.GetGetMethod(true);
            if (getPawn == null || getPawnComp == null || getCanUse == null ||
                getWeaponComp == null || getWeaponProps == null ||
                costPerShootField == null || costPerShootField.FieldType != typeof(float))
            {
                return null;
            }

            // The installed Axolotl assembly is an optional dependency, so a
            // normal C# delegate would put Axolotl types in this assembly's
            // metadata and prevent loading without that mod. Emit one startup-
            // time adapter instead. Its hot path is the same direct property,
            // field and method calls as Axolotl's source, with no reflection,
            // boxing, argument arrays or per-shot allocation.
            DynamicMethod dynamicMethod = new DynamicMethod(
                "MPMOS_AxolotlQiEnergySideEffects",
                typeof(void),
                new[] { typeof(Verb) },
                verbType.Module,
                true);
            ILGenerator il = dynamicMethod.GetILGenerator();
            LocalBuilder instance = il.DeclareLocal(verbType);
            Label done = il.DefineLabel();

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, verbType);
            il.Emit(OpCodes.Stloc, instance);

            il.Emit(OpCodes.Ldloc, instance);
            il.Emit(OpCodes.Call, getCanUse);
            il.Emit(OpCodes.Brfalse, done);

            il.Emit(OpCodes.Ldloc, instance);
            il.Emit(OpCodes.Call, getPawn);
            il.Emit(OpCodes.Ldloc, instance);
            il.Emit(OpCodes.Call, getWeaponComp);
            il.Emit(OpCodes.Callvirt, getWeaponProps);
            il.Emit(OpCodes.Ldfld, costPerShootField);
            il.Emit(OpCodes.Call, _doEnergyCostMethod);
            if (_doEnergyCostMethod.ReturnType != typeof(void))
                il.Emit(OpCodes.Pop);

            il.Emit(OpCodes.Ldloc, instance);
            il.Emit(OpCodes.Call, getPawnComp);
            il.Emit(OpCodes.Callvirt, _autoCloseMethod);

            il.MarkLabel(done);
            il.Emit(OpCodes.Ret);
            return (Action<Verb>)dynamicMethod.CreateDelegate(typeof(Action<Verb>));
        }

        private static bool IntensifyTargetsReady()
        {
            return _verbType != null && _baseTryCastShotMethod != null &&
                _getPawnProperty != null && _pawnCompProperty != null &&
                _canUseProperty != null && _weaponCompProperty != null &&
                _weaponPropsProperty != null && _costPerShootField != null &&
                _doEnergyCostMethod != null && _autoCloseMethod != null;
        }

        private static bool SupplementTargetsReady()
        {
            return _supplementVerbType != null && _baseTryCastShotMethod != null &&
                _supplementGetPawnProperty != null &&
                _supplementPawnCompProperty != null &&
                _supplementCanUseProperty != null &&
                _supplementWeaponCompProperty != null &&
                _supplementWeaponPropsProperty != null &&
                _supplementCostPerShootField != null &&
                _supplementIsSupplementShootField != null &&
                _supplementBurstShotsLeftField != null &&
                _supplementBurstShotCountField != null &&
                _doEnergyCostMethod != null && _autoCloseMethod != null;
        }

        private static void ResolveTargets()
        {
            try
            {
                _verbType = AccessTools.TypeByName(VerbTypeName);
                _baseTryCastShotMethod = AccessTools.Method(
                    typeof(Verb_Shoot), "TryCastShot", Type.EmptyTypes);
                if (_verbType == null)
                    return;

                _getPawnProperty = FindPropertySafe(_verbType, "GetPawn");
                _pawnCompProperty = FindPropertySafe(_verbType, "PawnComp");
                _canUseProperty = FindPropertySafe(_verbType, "CanUse");
                _weaponCompProperty = FindPropertySafe(_verbType, "WeaponComp");

                Type weaponCompType = AccessTools.TypeByName(WeaponCompTypeName);
                Type weaponPropsType = AccessTools.TypeByName(WeaponPropsTypeName);
                Type energyCompType = AccessTools.TypeByName(EnergyCompTypeName);
                Type energyUtilityType = AccessTools.TypeByName(EnergyUtilityTypeName);

                _weaponPropsProperty = weaponCompType == null
                    ? null
                    : FindPropertySafe(weaponCompType, "Props");
                _costPerShootField = weaponPropsType == null
                    ? null
                    : AccessTools.Field(weaponPropsType, "costPerShoot")
                      ?? AccessTools.Field(weaponPropsType.BaseType, "costPerShoot");
                _autoCloseMethod = energyCompType == null
                    ? null
                    : AccessTools.Method(
                        energyCompType,
                        "AutoCloseLotlQiWeaponMode",
                        Type.EmptyTypes);
                if (energyUtilityType == null)
                    energyUtilityType = GenTypes.AllTypes.FirstOrDefault(t =>
                        t != null &&
                        (t.FullName?.IndexOf("MoelotlEnergyUtility", StringComparison.Ordinal) ?? -1) >= 0);
                _doEnergyCostMethod = energyUtilityType == null
                    ? null
                    : AccessTools.Method(
                        energyUtilityType,
                        "DoEnergyCost",
                        new[] { typeof(Pawn), typeof(float) });
            }
            catch (Exception e)
            {
                // Never let one Axolotl version's ambiguous property layout abort
                // the compatibility bootstrap. Fail closed and log once.
                _verbType = null;
                Log.Warning(
                    "[MP-MeowOnlineShop] Axolotl crossbow target resolution " +
                    "failed safely: " + e.Message);
            }
        }

        private static void ResolveSupplementTargets()
        {
            try
            {
                _supplementVerbType = AccessTools.TypeByName(SupplementVerbTypeName);
                if (_supplementVerbType == null)
                    return;

                _supplementGetPawnProperty = FindPropertySafe(_supplementVerbType, "GetPawn");
                _supplementPawnCompProperty = FindPropertySafe(_supplementVerbType, "PawnComp");
                _supplementCanUseProperty = FindPropertySafe(_supplementVerbType, "CanUse");
                _supplementWeaponCompProperty = FindPropertySafe(_supplementVerbType, "WeaponComp");

                Type weaponCompType = AccessTools.TypeByName(SupplementWeaponCompTypeName);
                Type weaponPropsType = AccessTools.TypeByName(SupplementWeaponPropsTypeName);
                _supplementWeaponPropsProperty = weaponCompType == null
                    ? null
                    : FindPropertySafe(weaponCompType, "Props");
                _supplementCostPerShootField = FindFieldSafe(weaponPropsType, "costPerShoot");
                _supplementIsSupplementShootField = FindFieldSafe(
                    _supplementVerbType, "IsSupplementShoot");
                _supplementBurstShotsLeftField = FindFieldSafe(
                    _supplementVerbType, "burstShotsLeft");
                _supplementBurstShotCountField = FindFieldSafe(
                    weaponPropsType, "burstShotCount");
            }
            catch (Exception e)
            {
                _supplementVerbType = null;
                Log.Warning(
                    "[MP-MeowOnlineShop] Axolotl SupplementShoot target resolution " +
                    "failed safely: " + e.Message);
            }
        }

        private static PropertyInfo FindPropertySafe(Type type, string name)
        {
            if (type == null || string.IsNullOrEmpty(name))
                return null;

            for (Type t = type; t != null; t = t.BaseType)
            {
                try
                {
                    PropertyInfo property = t.GetProperty(
                        name,
                        BindingFlags.Instance |
                        BindingFlags.Public |
                        BindingFlags.NonPublic |
                        BindingFlags.DeclaredOnly);
                    if (property != null)
                        return property;
                }
                catch (AmbiguousMatchException)
                {
                    // A derived and base property can collide after obfuscation;
                    // prefer the first declared match and keep startup alive.
                    PropertyInfo[] declared = t.GetProperties(
                        BindingFlags.Instance |
                        BindingFlags.Public |
                        BindingFlags.NonPublic |
                        BindingFlags.DeclaredOnly);
                    for (int i = 0; i < declared.Length; i++)
                    {
                        if (string.Equals(declared[i].Name, name, StringComparison.Ordinal))
                            return declared[i];
                    }
                }
            }

            return null;
        }

        private static FieldInfo FindFieldSafe(Type type, string name)
        {
            if (type == null || string.IsNullOrEmpty(name))
                return null;

            for (Type t = type; t != null; t = t.BaseType)
            {
                FieldInfo field = t.GetField(
                    name,
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
                if (field != null)
                    return field;
            }

            return null;
        }

        private static bool TryCastShotPrefix(
            Verb __instance,
            ref bool __result)
        {
            if (!MP.IsInMultiplayer || __instance == null)
                return true;

            try
            {
                _runQiEnergySideEffects(__instance);
            }
            catch
            {
                // The original method also swallowed Qi-energy failures; only
                // the base shot must still happen exactly once.
            }

            __result = _baseTryCastShot((Verb_Shoot)__instance);

            return false;
        }

        private static bool SupplementTryCastShotPrefix(
            Verb __instance,
            ref bool __result)
        {
            if (!MP.IsInMultiplayer || __instance == null)
                return true;

            bool isSupplementShoot = false;
            try
            {
                isSupplementShoot = (bool)_supplementIsSupplementShootField.GetValue(
                    __instance);
            }
            catch
            {
                // Keep the guard fail-closed: the base shot is still called
                // exactly once below, without charging Qi or changing state.
            }

            if (isSupplementShoot)
            {
                try
                {
                    _runSupplementQiEnergySideEffects(__instance);
                }
                catch
                {
                    // Match the source verb's swallowed Qi-energy failures;
                    // do not retry the projectile shot.
                }
            }

            int burstShotsLeft;
            try
            {
                burstShotsLeft = (int)_supplementBurstShotsLeftField.GetValue(
                    __instance);
            }
            catch
            {
                __result = _baseTryCastShot((Verb_Shoot)__instance);
                return false;
            }

            // This is the only base call in this invocation. The original
            // method retries it from both catch/fallback paths.
            bool shotSucceeded = _baseTryCastShot((Verb_Shoot)__instance);
            if (burstShotsLeft != 1 || !shotSucceeded)
            {
                __result = shotSucceeded;
                return false;
            }

            try
            {
                if (!isSupplementShoot)
                {
                    bool canUse = (bool)_supplementCanUseProperty.GetValue(
                        __instance, null);
                    if (canUse)
                    {
                        _supplementIsSupplementShootField.SetValue(
                            __instance, true);
                        object weaponComp = _supplementWeaponCompProperty.GetValue(
                            __instance, null);
                        object weaponProps = _supplementWeaponPropsProperty.GetValue(
                            weaponComp, null);
                        int supplementBurstShotCount = (int)
                            _supplementBurstShotCountField.GetValue(weaponProps);
                        _supplementBurstShotsLeftField.SetValue(
                            __instance,
                            burstShotsLeft + supplementBurstShotCount);
                    }
                }
                else
                {
                    _supplementIsSupplementShootField.SetValue(
                        __instance, false);
                }
            }
            catch
            {
                // State transition failures must not cause another base shot.
            }

            __result = true;
            return false;
        }
    }
}
