using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;
namespace MP_MeowOnlineShop
{
 internal static class RavenLogisticsBoxActions
 {
  private sealed class Pending { public WeakReference editor; public string before; }
  private static readonly Dictionary<int, Pending> pending = new Dictionary<int, Pending>();
  private static Type editorType, boxType, modeType;
  private static ISyncMethod execute;
  private static int next;
  private static object Read(object o, string name) => AccessTools.Field(editorType, name).GetValue(o);
  private static void Write(object o, string name, object value) => AccessTools.Field(editorType, name).SetValue(o, value);
  private static readonly string[] inputs = { "mode", "thingDef", "pinned", "targetBuffer", "capacityAutomatic", "capacityBuffer" };
  private static string Fingerprint(object editor) => string.Join("|", inputs.Select(n => Read(editor, n)?.ToString() ?? "<null>"));
  internal static void Apply(Harmony h)
  {
   const string ns = "RavenRace.Features.Drone.Logistics.";
   editorType = AccessTools.TypeByName(ns + "DroneStorageEditorPanel"); boxType = AccessTools.TypeByName(ns + "Building_RavenDroneLogisticsBox"); modeType = AccessTools.TypeByName(ns + "DroneStorageCellMode");
   execute = MP.RegisterSyncMethod(typeof(RavenLogisticsBoxActions), nameof(Execute));
   foreach (string method in new[] { "ApplySettings", "ResetCells", "EjectCells" })
    h.Patch(AccessTools.DeclaredMethod(editorType, method), prefix: new HarmonyMethod(typeof(RavenLogisticsBoxActions), nameof(BeforeAction)));
   h.Patch(AccessTools.DeclaredMethod(typeof(Game), "ExposeSmallComponents"), prefix: new HarmonyMethod(typeof(RavenLogisticsBoxActions), nameof(BeforeLoad)));
  }
  private static void BeforeLoad() { if (Scribe.mode == LoadSaveMode.LoadingVars) pending.Clear(); }
  private static bool BeforeAction(object __instance, IReadOnlyCollection<int> __0, MethodBase __originalMethod)
  {
   if (!MP.InInterface) return true;
   // A window may outlive its building. Reject before MP serializes an inaccessible Thing.
   var box = Read(__instance, "box") as Thing;
   if (box == null || box.Destroyed || !box.Spawned)
   {
    Write(__instance, "statusText", "物流箱已被移除，请关闭此窗口。");
    return false;
   }
   int operation = __originalMethod.Name == "ApplySettings" ? 0 : __originalMethod.Name == "ResetCells" ? 1 : 2;
   var ids = __0?.Distinct().OrderBy(i => i).ToArray() ?? Array.Empty<int>();
   if (ids.Length == 0) return false;
   // Keep parsing/clamping local. The command carries validated numeric input,
   // never a machine's culture-dependent text buffer or a window reference.
   int target = operation == 0 ? (int)AccessTools.Method(editorType, "ParsedTarget").Invoke(__instance, null) : 0;
   int capacity = operation == 0 ? (int)AccessTools.Method(editorType, "ParsedCapacityLimit").Invoke(__instance, null) : 0;
   int token = ++next; pending[token] = new Pending { editor = new WeakReference(__instance), before = Fingerprint(__instance) };
   var options = new[] { Convert.ToInt32(Read(__instance, "mode")), target, capacity, ((bool)Read(__instance, "pinned") ? 1 : 0) | ((bool)Read(__instance, "capacityAutomatic") ? 2 : 0) };
   execute.DoSync(null, box, operation, ids, (ThingDef)Read(__instance, "thingDef"), options, token);
   return false;
  }
  private static void Execute(Thing box, int operation, int[] ids, ThingDef def, int[] options, int token)
  {
   if (box == null || box.GetType() != boxType || !box.Spawned || operation < 0 || operation > 2 || ids == null || ids.Length == 0 || options == null || options.Length != 4 || !Enum.IsDefined(modeType, options[0]))
   {
    if (MP.IsExecutingSyncCommandIssuedBySelf) pending.Remove(token);
    return;
   }
   int mode = options[0], target = options[1], capacity = options[2]; bool pinned = (options[3] & 1) != 0, automatic = (options[3] & 2) != 0;
   // This non-Window editor is an ephemeral action context. Its exact original
   // methods validate occupied cells, apply the full settings transaction,
   // notify logistics, and eject in cell order; its selection/list stays local.
   var editor = Activator.CreateInstance(editorType, box);
   Write(editor, "mode", Enum.ToObject(modeType, mode)); Write(editor, "thingDef", def); Write(editor, "pinned", pinned);
   Write(editor, "targetBuffer", target.ToString(CultureInfo.InvariantCulture)); Write(editor, "capacityAutomatic", automatic); Write(editor, "capacityBuffer", capacity.ToString(CultureInfo.InvariantCulture));
   AccessTools.DeclaredMethod(editorType, operation == 0 ? "ApplySettings" : operation == 1 ? "ResetCells" : "EjectCells").Invoke(editor, new object[] { ids });
   if (!MP.IsExecutingSyncCommandIssuedBySelf || !pending.TryGetValue(token, out var request)) return;
   pending.Remove(token); var original = request.editor.Target; if (original == null) return;
   Write(original, "statusText", Read(editor, "statusText"));
   if (operation == 1 && request.before == Fingerprint(original)) foreach (var name in inputs) Write(original, name, Read(editor, name));
  }
 }
}
