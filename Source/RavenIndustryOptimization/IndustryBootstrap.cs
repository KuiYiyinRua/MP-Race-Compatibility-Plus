using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using HarmonyLib;
using Multiplayer.API;
using RavenRace.Features.RavenConveyor;
using Verse;

namespace Meow.RavenIndustryOptimization
{
    internal static class IndustryBootstrap
    {
        internal const string Id = "meow.ravenindustry.mp";
        internal const string RavenSha256 = "74AE9E1DDC75F97B49A2AF08B6F3C0AA73B5F620F2D60F6BF9E65E624AD1834B";
        internal static bool Ready;
        internal static string Status = "正在初始化";

        internal static void Install()
        {
            var harmony = new Harmony(Id);
            try
            {
                // Keep the status/settings entry even if a simulation prerequisite fails.
                IndustrySettings.Install(new Harmony(Id + ".settings"));
                if (!MP.enabled) throw new NotSupportedException("未启用 Multiplayer");
                using (var stream = File.OpenRead(typeof(ConveyorPacket).Assembly.Location))
                using (var sha = SHA256.Create())
                {
                    string hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
                    if (hash != RavenSha256) throw new NotSupportedException("渡鸦 DLL 版本与已核验版本不同");
                }
                var compatibility = AccessTools.TypeByName("MP_MeowOnlineShop.RavenConveyorPacketState");
                if (compatibility == null) throw new TypeLoadException("需要 Meow 渡鸦联机兼容模块");
                RequirePostfix(AccessTools.Method(typeof(Map), nameof(Map.ExposeData)), Method(compatibility, "Expose"));
                RequirePostfix(Method(typeof(MapComponent_RavenConveyorSystem), "EnsureTopology"), Method(compatibility, "AfterTopology"));
                ConveyorMotion.Install(harmony);
                CapacityIndex.Install(harmony);
                SchedulerCompaction.Install(harmony);
                Ready = true;
                Status = "可用";
                Log.Message("[Meow.RavenIndustry] READY 1.0.0 MP-only; default OFF; Raven=" + RavenSha256 + "; MVID=" + typeof(IndustryBootstrap).Assembly.ManifestModule.ModuleVersionId);
            }
            catch (Exception e)
            {
                Ready = false;
                ConveyorMotion.FlushAll();
                harmony.UnpatchAll(Id);
                Status = e.Message;
                Log.Error("[Meow.RavenIndustry] DISABLED; original simulation retained: " + e);
            }
        }

        private static void RequirePostfix(MethodBase target, MethodInfo patch)
        {
            var info = Harmony.GetPatchInfo(target);
            if (info == null || !info.Postfixes.Any(p => p.PatchMethod == patch))
                throw new NotSupportedException("渡鸦联机存档兼容补丁未安装；请开启渡鸦兼容分类并重启");
        }

        internal static MethodInfo Method(Type type, string name) =>
            AccessTools.DeclaredMethod(type, name) ?? throw new MissingMethodException(type?.FullName, name);

        internal static void Patch(Harmony h, Type target, string method, Type patch, string prefix = null, string postfix = null)
        {
            h.Patch(Method(target, method),
                prefix: prefix == null ? null : new HarmonyMethod(patch, prefix),
                postfix: postfix == null ? null : new HarmonyMethod(patch, postfix));
        }

        internal static Func<TSource, TValue> Getter<TSource, TValue>(FieldInfo field)
        {
            if (field == null || field.FieldType != typeof(TValue)) throw new MissingFieldException("Typed field accessor");
            var dm = new DynamicMethod("RavenIndustryGet_" + field.Name, typeof(TValue), new[] { typeof(TSource) }, typeof(IndustryBootstrap).Module, true);
            var il = dm.GetILGenerator();
            if (typeof(TSource).IsValueType) il.Emit(OpCodes.Ldarga_S, (byte)0);
            else
            {
                il.Emit(OpCodes.Ldarg_0);
                if (typeof(TSource) != field.DeclaringType) il.Emit(OpCodes.Castclass, field.DeclaringType);
            }
            il.Emit(OpCodes.Ldfld, field);
            il.Emit(OpCodes.Ret);
            return (Func<TSource, TValue>)dm.CreateDelegate(typeof(Func<TSource, TValue>));
        }

        internal static void RequireUnpatched(MethodBase method)
        {
            var info = Harmony.GetPatchInfo(method);
            if (info != null && info.Owners.Any(owner => owner != Id))
                throw new NotSupportedException("其他补丁改写了优化目标：" + method.DeclaringType.FullName + "." + method.Name);
        }
    }
}
