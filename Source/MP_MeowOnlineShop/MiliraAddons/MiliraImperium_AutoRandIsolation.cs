using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat;

/// <summary>
/// Bounded safety net for MiliraImperium randomness: every declared method in
/// the target assembly whose IL directly references <see cref="Rand"/> gets a
/// deterministic push/pop scope while a multiplayer game is active. This keeps
/// the port complete when the mod adds new projectile/tick/UI Rand calls.
/// Iterator MoveNext bodies are included because they live in compiler
/// generated nested types inside the same assembly.
/// </summary>
[StaticConstructorOnStartup]
public static class MiliraImperium_AutoRandIsolation
{
	private const string HarmonyId = "Ariandel.MiliraImperium.AutoRandIsolation";
	private const string TargetAssemblyName = "MiliraImperium";

	private static readonly Harmony harmony;
	private static readonly HashSet<MethodBase> PatchedMethods = new HashSet<MethodBase>();
	private static Dictionary<ushort, OpCode> opCodeByValue;
	private static int patchedCount;
	private static int failedCount;

	static MiliraImperium_AutoRandIsolation()
	{
        if (!MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("milira")) return;
		if (MiliraMpCompatGate.ReferenceModActive)
		{
			Log.Message("[MP-MeowOnlineShop] Milira auto Rand isolation skipped: usamiseika.fixmod.miliramultiplayer is active.");
			return;
		}
		harmony = new Harmony(HarmonyId);
		if (!MP.enabled || !ModsConfig.IsActive("Ariandel.MiliraImperium"))
		{
			return;
		}
		try
		{
			Initialize();
		}
		catch (Exception arg)
		{
			Log.Error("[MP-MeowOnlineShop] MiliraImperium auto Rand isolation init failed - " + arg);
		}
	}

	private static void Initialize()
	{
		opCodeByValue = BuildOpCodeMap();
		if (opCodeByValue == null || opCodeByValue.Count == 0)
		{
			return;
		}

		Assembly target = null;
		foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
		{
			if (assembly == null)
			{
				continue;
			}
			try
			{
				if (assembly.GetName().Name == TargetAssemblyName)
				{
					target = assembly;
					break;
				}
			}
			catch
			{
			}
		}
		if (target == null)
		{
			Log.Message("[MP-MeowOnlineShop] MiliraImperium auto Rand isolation skipped: target assembly not loaded.");
			return;
		}

		MethodInfo prefix = AccessTools.Method(typeof(MiliraImperium_AutoRandIsolation), "RandScopePrefix", new Type[3] { typeof(object), typeof(MethodBase), typeof(int).MakeByRefType() }, (Type[])null);
		MethodInfo finalizer = AccessTools.Method(typeof(MiliraImperium_AutoRandIsolation), "RandScopeFinalizer", new Type[2] { typeof(int), typeof(Exception) }, (Type[])null);
		if (prefix == null || finalizer == null)
		{
			Log.Error("[MP-MeowOnlineShop] MiliraImperium auto Rand isolation target resolution failed.");
			return;
		}

		foreach (Type type in GetLoadableTypes(target))
		{
			if (type == null)
			{
				continue;
			}
			MethodInfo[] methods;
			try
			{
				methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
			}
			catch
			{
				continue;
			}
			foreach (MethodInfo method in methods)
			{
				if (method == null || method.IsAbstract || method.IsGenericMethod || method.IsGenericMethodDefinition)
				{
					continue;
				}
				if (!MethodReferencesVerseRand(method))
				{
					continue;
				}
				if (!PatchedMethods.Add(method))
				{
					continue;
				}
				try
				{
					harmony.Patch(method, prefix: new HarmonyMethod(prefix), finalizer: new HarmonyMethod(finalizer));
					patchedCount++;
				}
				catch
				{
					failedCount++;
				}
			}
			ConstructorInfo[] constructors;
			try
			{
				constructors = type.GetConstructors(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
			}
			catch
			{
				constructors = new ConstructorInfo[0];
			}
			foreach (ConstructorInfo ctor in constructors)
			{
				if (ctor == null || ctor.IsAbstract)
				{
					continue;
				}
				if (!MethodReferencesVerseRand(ctor))
				{
					continue;
				}
				if (!PatchedMethods.Add(ctor))
				{
					continue;
				}
				try
				{
					harmony.Patch(ctor, prefix: new HarmonyMethod(prefix), finalizer: new HarmonyMethod(finalizer));
					patchedCount++;
				}
				catch
				{
					failedCount++;
				}
			}
		}

		Log.Message($"[MP-MeowOnlineShop] MiliraImperium auto Rand isolation active: patched={patchedCount}, failed={failedCount}.");
	}

	private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
	{
		try
		{
			return assembly.GetTypes();
		}
		catch (ReflectionTypeLoadException ex)
		{
			return ex.Types.Where((Type t) => t != null);
		}
	}

	private static Dictionary<ushort, OpCode> BuildOpCodeMap()
	{
		try
		{
			Dictionary<ushort, OpCode> map = new Dictionary<ushort, OpCode>();
			foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
			{
				if (field.FieldType != typeof(OpCode))
				{
					continue;
				}
				OpCode op = (OpCode)field.GetValue(null);
				map[(ushort)op.Value] = op;
			}
			return map;
		}
		catch
		{
			return null;
		}
	}

	private static bool MethodReferencesVerseRand(MethodBase method)
	{
		try
		{
			if (method.IsAbstract)
			{
				return false;
			}
			if (method is MethodInfo methodInfo && (methodInfo.IsGenericMethod || methodInfo.IsGenericMethodDefinition))
			{
				return false;
			}
			MethodBody body = method.GetMethodBody();
			if (body == null)
			{
				return false;
			}
			byte[] il = body.GetILAsByteArray();
			if (il == null || il.Length == 0)
			{
				return false;
			}
			Module module = method.Module;
			int i = 0;
			while (i < il.Length)
			{
				ushort opValue;
				if (il[i] == 0xFE)
				{
					if (i + 1 >= il.Length)
					{
						break;
					}
					opValue = (ushort)(0xFE00 | il[i + 1]);
					i += 2;
				}
				else
				{
					opValue = il[i];
					i += 1;
				}
				if (!opCodeByValue.TryGetValue(opValue, out OpCode opcode))
				{
					break;
				}
				if (opcode.OperandType == OperandType.InlineSwitch)
				{
					if (i + 4 > il.Length)
					{
						break;
					}
					int count = il[i] | (il[i + 1] << 8) | (il[i + 2] << 16) | (il[i + 3] << 24);
					i += 4 + count * 4;
					continue;
				}
				int operandSize = GetOperandSize(opcode.OperandType);
				if (operandSize < 0)
				{
					break;
				}
				if ((opcode == OpCodes.Call || opcode == OpCodes.Callvirt) && i + 4 <= il.Length)
				{
					int token = il[i] | (il[i + 1] << 8) | (il[i + 2] << 16) | (il[i + 3] << 24);
					try
					{
						MethodBase called = module.ResolveMethod(token);
						if (called != null && called.DeclaringType == typeof(Rand))
						{
							return true;
						}
					}
					catch
					{
					}
				}
				i += operandSize;
			}
			return false;
		}
		catch
		{
			return false;
		}
	}

	private static int GetOperandSize(OperandType type)
	{
		switch (type)
		{
			case OperandType.InlineNone:
				return 0;
			case OperandType.ShortInlineI:
			case OperandType.ShortInlineBrTarget:
			case OperandType.ShortInlineVar:
				return 1;
			case OperandType.InlineVar:
				return 2;
			case OperandType.InlineI:
			case OperandType.InlineBrTarget:
			case OperandType.InlineField:
			case OperandType.InlineMethod:
			case OperandType.InlineType:
			case OperandType.InlineString:
			case OperandType.InlineSig:
			case OperandType.InlineTok:
			case OperandType.ShortInlineR:
				return 4;
			case OperandType.InlineI8:
			case OperandType.InlineR:
				return 8;
			default:
				return -1;
		}
	}

	public static void RandScopePrefix(object __instance, MethodBase __originalMethod, ref int __state)
	{
		__state = 0;
		if (!MP.IsInMultiplayer)
		{
			return;
		}
		try
		{
			int seed = Find.TickManager?.TicksGame ?? 0;
			if (__instance is Thing thing)
			{
				seed = Gen.HashCombineInt(seed, thing.thingIDNumber);
			}
			else if (__instance is ThingComp comp && comp.parent != null)
			{
				seed = Gen.HashCombineInt(seed, comp.parent.thingIDNumber);
			}
			else if (__instance is Map map)
			{
				seed = Gen.HashCombineInt(seed, map.uniqueID);
			}
			else if (__instance is MapComponent mc && mc.map != null)
			{
				seed = Gen.HashCombineInt(seed, mc.map.uniqueID);
			}
			string key = (__originalMethod?.DeclaringType?.FullName ?? "?") + "." + (__originalMethod?.Name ?? "?");
			seed = Gen.HashCombineInt(seed, DeterministicStringHash(key));
			Rand.PushState(seed);
			__state = 1;
		}
		catch
		{
			__state = 0;
		}
	}

	public static void RandScopeFinalizer(int __state, Exception __exception)
	{
		if (__state == 0)
		{
			return;
		}
		try
		{
			Rand.PopState();
		}
		catch
		{
		}
	}

	private static int DeterministicStringHash(string value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return 0;
		}
		int hash = 0;
		for (int i = 0; i < value.Length; i++)
		{
			hash = Gen.HashCombineInt(hash, value[i]);
		}
		return hash;
	}
}
