using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// 运行时解析 QuestEditor 对话相关符号，避免写死签名。
    /// </summary>
    internal static class RigorMortisStoryRuntimeMap
    {
        private const string LogTag = "[MP-MeowOnlineShop] RMStoryRuntimeMap";
        private static readonly HashSet<string> LogOnce = new HashSet<string>();

        internal static readonly Type DialogManagerDefType = AccessTools.TypeByName("QuestEditor_Library.DialogManagerDef");
        internal static readonly Type GameComponentEditorType = AccessTools.TypeByName("QuestEditor_Library.GameComponent_Editor");
        internal static readonly Type CqfRemoveDialogManagerType = AccessTools.TypeByName("QuestEditor_Library.CQFAction_RemoveDialogManager");

        internal static readonly MethodInfo AddDialogMethod = ResolveAddDialogMethod();
        internal static readonly MethodInfo CreateCqfDialogMethod = ResolveCreateCqfDialogMethod();
        internal static readonly MethodInfo RemoveDialogManagerRealWorkMethod = ResolveRemoveDialogManagerRealWorkMethod();
        internal static readonly MethodInfo DiaOptionActivateMethod = AccessTools.Method(typeof(DiaOption), "Activate");
        private static readonly PropertyInfo DiaOptionTextProperty = AccessTools.Property(typeof(DiaOption), "text");
        private static readonly FieldInfo DiaOptionTextField = AccessTools.Field(typeof(DiaOption), "text");
        private static readonly PropertyInfo DiaOptionDisabledProperty = AccessTools.Property(typeof(DiaOption), "Disabled");
        private static readonly PropertyInfo DiaOptionDisabledReasonProperty = AccessTools.Property(typeof(DiaOption), "disabledReason");
        private static readonly FieldInfo DiaOptionDisabledReasonField = AccessTools.Field(typeof(DiaOption), "disabledReason");
        internal static readonly FieldInfo CurNodeField = ResolveCurNodeField();
        internal static readonly PropertyInfo CurNodeProperty = ResolveCurNodeProperty();

        internal static void LogStartupSummary()
        {
            LogMessageOnce(
                "startup-summary",
                $"symbols: GameComponent_Editor={(GameComponentEditorType != null)}, DialogManagerDef={(DialogManagerDefType != null)}, " +
                $"AddDialog={DescribeMethod(AddDialogMethod)}, CreateCQFDialog={DescribeMethod(CreateCqfDialogMethod)}, " +
                $"RemoveDialogManager.RealWork={DescribeMethod(RemoveDialogManagerRealWorkMethod)}, DiaOption.Activate={(DiaOptionActivateMethod != null)}, " +
                $"curNodeField={((CurNodeField?.DeclaringType?.FullName + "." + CurNodeField?.Name) ?? "missing")}, " +
                $"curNodeProp={((CurNodeProperty?.DeclaringType?.FullName + "." + CurNodeProperty?.Name) ?? "missing")}.");
        }

        internal static DiaNode TryGetCurrentNode(Window window)
        {
            if (window == null)
                return null;

            try
            {
                if (CurNodeField != null)
                    return CurNodeField.GetValue(window) as DiaNode;
            }
            catch
            {
            }

            try
            {
                if (CurNodeProperty != null)
                    return CurNodeProperty.GetValue(window, null) as DiaNode;
            }
            catch
            {
            }

            return null;
        }

        internal static bool LooksLikeQuestEditorWindow(Window window)
        {
            if (window == null)
                return false;

            var type = window.GetType();
            var asmName = type.Assembly?.GetName().Name ?? "";
            if (asmName.IndexOf("QuestEditor", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if ((type.FullName ?? "").IndexOf("QuestEditor", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }

        internal static bool TryActivateOption(DiaOption option)
        {
            if (option == null || DiaOptionActivateMethod == null)
                return false;
            try
            {
                DiaOptionActivateMethod.Invoke(option, null);
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static string GetOptionText(DiaOption option, int index)
        {
            if (option == null)
                return $"Option #{index}";

            try
            {
                var value = DiaOptionTextProperty?.GetValue(option, null) ?? DiaOptionTextField?.GetValue(option);
                if (value != null)
                    return value.ToString();
            }
            catch
            {
            }

            return $"Option #{index}";
        }

        internal static string GetOptionDisabledReason(DiaOption option)
        {
            if (option == null)
                return null;
            try
            {
                var value = DiaOptionDisabledReasonProperty?.GetValue(option, null) ?? DiaOptionDisabledReasonField?.GetValue(option);
                return value?.ToString();
            }
            catch
            {
                return null;
            }
        }

        internal static bool IsOptionDisabled(DiaOption option)
        {
            if (option == null)
                return true;

            try
            {
                if (DiaOptionDisabledProperty != null)
                {
                    var value = DiaOptionDisabledProperty.GetValue(option, null);
                    if (value is bool disabled)
                        return disabled;
                }
            }
            catch
            {
            }

            var reason = GetOptionDisabledReason(option);
            return !string.IsNullOrEmpty(reason);
        }

        internal static string DescribeMethod(MethodInfo method)
        {
            if (method == null)
                return "missing";
            var ps = method.GetParameters();
            var args = string.Join(", ", ps.Select(p => $"{p.ParameterType.Name} {p.Name}"));
            return $"{method.DeclaringType?.FullName}.{method.Name}({args})";
        }

        private static MethodInfo ResolveAddDialogMethod()
        {
            if (GameComponentEditorType == null)
                return null;

            return GameComponentEditorType
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m =>
                {
                    if (m == null || m.Name != "AddDialog")
                        return false;
                    var ps = m.GetParameters();
                    if (ps.Length != 2)
                        return false;
                    if (!typeof(Thing).IsAssignableFrom(ps[0].ParameterType))
                        return false;
                    if (DialogManagerDefType != null && !DialogManagerDefType.IsAssignableFrom(ps[1].ParameterType))
                        return false;
                    return true;
                });
        }

        private static MethodInfo ResolveCreateCqfDialogMethod()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm == null || asm.IsDynamic)
                    continue;

                var asmName = asm.GetName().Name ?? "";
                if (asmName.IndexOf("QuestEditor", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types?.Where(t => t != null).ToArray() ?? Array.Empty<Type>();
                }
                catch
                {
                    continue;
                }

                for (int i = 0; i < types.Length; i++)
                {
                    var type = types[i];
                    if (type == null)
                        continue;

                    var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    for (int j = 0; j < methods.Length; j++)
                    {
                        var method = methods[j];
                        if (method == null || method.Name != "CreateCQFDialog")
                            continue;
                        if (!typeof(Window).IsAssignableFrom(method.ReturnType))
                            continue;
                        return method;
                    }
                }
            }

            return null;
        }

        private static MethodInfo ResolveRemoveDialogManagerRealWorkMethod()
        {
            if (CqfRemoveDialogManagerType == null)
                return null;

            return CqfRemoveDialogManagerType
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == "RealWork" && m.GetParameters().Length == 2);
        }

        private static FieldInfo ResolveCurNodeField()
        {
            return AccessTools.Field(typeof(Dialog_NodeTree), "curNode");
        }

        private static PropertyInfo ResolveCurNodeProperty()
        {
            return AccessTools.Property(typeof(Dialog_NodeTree), "CurNode");
        }

        private static void LogMessageOnce(string key, string message)
        {
            if (string.IsNullOrEmpty(key))
                return;
            if (!LogOnce.Add(key))
                return;
            Log.Message($"{LogTag}: {message}");
        }
    }
}
