using System;
using System.Reflection;
using UnityEngine;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Access UnityEngine.Input without a compile-time dependency on
    /// UnityEngine.InputLegacyModule. Prepatcher reflects over this assembly
    /// with reflection-only APIs, and a missing dependent module aborts startup.
    /// The game always loads the Input type before these helpers are called.
    /// </summary>
    internal static class UnityInputCompat
    {
        private static readonly Type InputType =
            Type.GetType(
                "UnityEngine.Input, UnityEngine.InputLegacyModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null",
                false) ??
            Type.GetType(
                "UnityEngine.Input, UnityEngine, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null",
                false);

        private static readonly MethodInfo GetKeyMethod =
            InputType == null
                ? null
                : InputType.GetMethod(
                    "GetKey",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(KeyCode) },
                    null);

        private static readonly MethodInfo GetMouseButtonMethod =
            InputType == null
                ? null
                : InputType.GetMethod(
                    "GetMouseButton",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(int) },
                    null);

        public static bool GetKey(KeyCode key)
        {
            try
            {
                if (GetKeyMethod == null)
                    return false;
                return (bool)GetKeyMethod.Invoke(null, new object[] { key });
            }
            catch
            {
                return false;
            }
        }

        public static bool GetMouseButton(int button)
        {
            try
            {
                if (GetMouseButtonMethod == null)
                    return false;
                return (bool)GetMouseButtonMethod.Invoke(null, new object[] { button });
            }
            catch
            {
                return false;
            }
        }
    }
}
