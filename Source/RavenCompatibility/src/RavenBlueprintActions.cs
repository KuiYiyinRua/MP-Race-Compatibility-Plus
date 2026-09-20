using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;
namespace MP_MeowOnlineShop
{
 public sealed class RavenBlueprintTransfer : IExposable
 {
  public string id; public IntVec3 origin; public int total, began;
  public List<string> chunks = new List<string>();
  public void ExposeData()
  {
   Scribe_Values.Look(ref id, "id"); Scribe_Values.Look(ref origin, "origin"); Scribe_Values.Look(ref total, "total"); Scribe_Values.Look(ref began, "began");
   Scribe_Collections.Look(ref chunks, "chunks", LookMode.Value);
  }
 }
 public sealed class RavenBlueprintTransfers : MapComponent
 {
  public List<RavenBlueprintTransfer> pending = new List<RavenBlueprintTransfer>();
  public RavenBlueprintTransfers(Map map) : base(map) { }
  public override void ExposeData() { Scribe_Collections.Look(ref pending, "mpRavenBlueprintTransfers", LookMode.Deep); if (Scribe.mode == LoadSaveMode.PostLoadInit && pending == null) pending = new List<RavenBlueprintTransfer>(); }
  public override void MapComponentTick()
  {
   if (!CompatibilityPatchCategories.IsEnabled("raven")) return;
   int now = Find.TickManager.TicksGame;
   if (now % 3000 == 0) pending.RemoveAll(p => now - p.began > 60000);
  }
 }
 public sealed class RavenBlueprintFeedback : GameComponent
 {
  public RavenBlueprintFeedback(Game game) { }
  public override void GameComponentUpdate() { if (CompatibilityPatchCategories.IsEnabled("raven")) RavenBlueprintActions.DrainFeedback(); }
 }
 internal static class RavenBlueprintActions
 {
  private const string Ns = "RavenRace.Features.RavenIndustrialBlueprint.";
  private const int ChunkSize = 24000, MaxText = 22369624;
  private static Type placeType, captureType;
  private static MethodInfo toFile, fromFile, encode, decode, place, finish;
  private static ISyncMethod receive;
  private sealed class UiState
  {
   public readonly Dictionary<string, WeakReference> pending = new Dictionary<string, WeakReference>();
   public readonly Queue<Action> feedback = new Queue<Action>();
  }
  private static readonly ConditionalWeakTable<Game, UiState> ui = new ConditionalWeakTable<Game, UiState>();
  internal static void Apply(Harmony h)
  {
   placeType = AccessTools.TypeByName(Ns + "Designator_PlaceRavenIndustrialBlueprint");
   captureType = AccessTools.TypeByName(Ns + "Designator_SaveRavenIndustrialBlueprintRect");
   var mapper = AccessTools.TypeByName(Ns + "RavenIndustrialBlueprintFileMapper"); var codec = AccessTools.TypeByName(Ns + "RavenIndustrialBlueprintBinaryCodec");
   toFile = AccessTools.DeclaredMethod(mapper, "ToFileData"); fromFile = AccessTools.DeclaredMethod(mapper, "FromFileData"); encode = AccessTools.DeclaredMethod(codec, "Write"); decode = AccessTools.DeclaredMethod(codec, "Read");
   place = AccessTools.DeclaredMethod(AccessTools.TypeByName(Ns + "RavenIndustrialBlueprintPlacementUtility"), "TryPlaceAt"); finish = AccessTools.DeclaredMethod(placeType, "FinishSelection");
   if (captureType == null || toFile == null || fromFile == null || encode == null || decode == null || place == null || finish == null) throw new MissingMemberException("Raven industrial blueprint actions");
   receive = MP.RegisterSyncMethod(typeof(RavenBlueprintActions), nameof(Receive)).SetContext(SyncContext.MapSelected);
   // The capture tool writes only the issuer's local library. Placement sends its
   // full transformed payload through our map command, not the native designator worker.
   var native = AccessTools.TypeByName("Multiplayer.Client.DesignatorPatches");
   foreach (string name in new[] { "DesignateSingleCell", "DesignateMultiCell" })
    h.Patch(AccessTools.DeclaredMethod(native, name), prefix: new HarmonyMethod(typeof(RavenBlueprintActions), nameof(BypassNative)));
   h.Patch(AccessTools.DeclaredMethod(placeType, "DesignateSingleCell"), prefix: new HarmonyMethod(typeof(RavenBlueprintActions), nameof(BeforePlace)));
   h.Patch(AccessTools.DeclaredMethod(AccessTools.TypeByName(Ns + "MapComponent_RavenIndustrialBlueprintPlacement"), "RegisterPlacement"), transpiler: new HarmonyMethod(typeof(RavenBlueprintActions), nameof(PlacementId)));
  }
  private static bool BypassNative(Designator __0, ref bool __result)
  {
   if (__0.GetType() != placeType && __0.GetType() != captureType) return true;
   __result = true; return false;
  }
  private static bool BeforePlace(Designator __instance, IntVec3 __0)
  {
   if (!MP.InInterface) return true;
   var state = ui.GetOrCreateValue(Current.Game);
   if (state.pending.Values.Any(r => r.Target == __instance)) return false;
   var report = __instance.CanDesignateCell(__0);
   if (!report.Accepted) { Messages.Message(report.Reason ?? "RavenIndustrialBlueprint_PlaceFailed".Translate(), MessageTypeDefOf.RejectInput, false); return false; }
   var blueprint = AccessTools.Field(placeType, "transformedBlueprint").GetValue(__instance);
   byte[] bytes = (byte[])encode.Invoke(null, new[] { toFile.Invoke(null, new[] { blueprint }) });
   string text = Convert.ToBase64String(bytes), id = Guid.NewGuid().ToString("N");
   if (text.Length > MaxText) throw new InvalidOperationException("Raven blueprint exceeds original codec limit");
   var origin = __0 - (IntVec3)AccessTools.Field(placeType, "transformedAnchorOffset").GetValue(__instance);
   state.pending[id] = new WeakReference(__instance);
   int total = (text.Length + ChunkSize - 1) / ChunkSize;
   for (int i = 0; i < total; i++) receive.DoSync(null, Find.CurrentMap, id, origin, total, i, text.Substring(i * ChunkSize, Math.Min(ChunkSize, text.Length - i * ChunkSize)));
   return false;
  }
  private static void Receive(Map map, string id, IntVec3 origin, int total, int index, string chunk)
  {
   if (map == null || id == null || id.Length != 32 || total < 1 || total > (MaxText + ChunkSize - 1) / ChunkSize || index < 0 || index >= total || chunk == null || chunk.Length > ChunkSize) return;
   var transfers = map.GetComponent<RavenBlueprintTransfers>();
   var transfer = transfers.pending.FirstOrDefault(p => p.id == id);
   if (transfer == null)
   {
    if (index != 0) return;
    transfer = new RavenBlueprintTransfer { id = id, origin = origin, total = total, began = Find.TickManager.TicksGame }; transfers.pending.Add(transfer);
   }
   if (transfer.total != total || transfer.origin != origin || transfer.chunks.Count != index) return;
   transfer.chunks.Add(chunk);
   if (transfer.chunks.Count != total) return;
   transfers.pending.Remove(transfer);
   bool success = false; object result = null; string error = null;
   try
   {
    string text = string.Concat(transfer.chunks);
    if (text.Length > MaxText) throw new InvalidOperationException("Raven blueprint payload too large");
    var blueprint = fromFile.Invoke(null, new[] { decode.Invoke(null, new object[] { Convert.FromBase64String(text) }) });
    object[] args = { map, origin, blueprint, null, null }; success = (bool)place.Invoke(null, args); result = args[3]; error = (string)args[4];
   }
   catch (Exception e) { error = (e.InnerException ?? e).Message; Log.Error("[MeowMP] Raven blueprint placement failed: " + e); }
   if (!MP.IsExecutingSyncCommandIssuedBySelf) return;
   var state = ui.GetOrCreateValue(Current.Game);
   if (!state.pending.TryGetValue(id, out var reference)) return;
   state.pending.Remove(id);
   string message;
   if (success)
   {
    var t = result.GetType();
    bool nano = (bool)AccessTools.Field(t, "UsedNanoConstruction").GetValue(result), complete = (bool)AccessTools.Field(t, "SpawnedFinishedBuildings").GetValue(result);
    string mode = (nano ? "RavenIndustrialBlueprint_ModeNano" : complete ? "RavenIndustrialBlueprint_ModeGod" : "RavenIndustrialBlueprint_ModePlan").Translate();
    message = "RavenIndustrialBlueprint_PlaceResult".Translate((int)AccessTools.Field(t, "PlacedCount").GetValue(result), (int)AccessTools.Field(t, "SkippedPumpCount").GetValue(result), mode);
   }
   else message = error ?? "RavenIndustrialBlueprint_PlaceFailed".Translate();
   bool accepted = success;
   state.feedback.Enqueue(() =>
   {
    Messages.Message(message, accepted ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.RejectInput, false);
    if (!accepted || !(reference.Target is Designator designator)) return;
    finish.Invoke(designator, null);
    if (Find.DesignatorManager.SelectedDesignator == designator) Find.DesignatorManager.Deselect();
   });
  }
  internal static void DrainFeedback()
  {
   if (Current.Game == null || !ui.TryGetValue(Current.Game, out var state)) return;
   while (state.feedback.Count > 0) state.feedback.Dequeue()();
  }
  private static IEnumerable<CodeInstruction> PlacementId(IEnumerable<CodeInstruction> instructions)
  {
   var target = AccessTools.Method(typeof(Guid), nameof(Guid.NewGuid)); int count = 0;
   foreach (var instruction in instructions)
   {
    if (instruction.Calls(target)) { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(RavenBlueprintActions), nameof(NextPlacementGuid)); count++; }
    yield return instruction;
   }
   if (count != 1) throw new InvalidOperationException("Raven placement GUID call count " + count);
  }
  private static Guid NextPlacementGuid()
  {
   if (!MP.IsInMultiplayer) return Guid.NewGuid();
   byte[] bytes = new byte[16];
   for (int i = 0; i < 4; i++) Array.Copy(BitConverter.GetBytes(Rand.Int), 0, bytes, i * 4, 4);
   return new Guid(bytes);
  }
 }
}
