using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections.Generic;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    // LetterStack visibility is local; expiry must also visit the shared archive.
    public sealed class NivarianLetterClock : GameComponent
    {
        static Type aid, guest, asyncType;
        static FieldInfo aidMap, guestMap, aidStatus, guestStatus, mapTicks;
        static MethodInfo preContext, postContext, getClock;
        static readonly FieldInfo[] managers = new[] { "designationManager", "areaManager", "zoneManager", "planManager", "haulDestinationManager", "listerHaulables", "resourceCounter", "listerFilthInHomeArea", "listerMergeables" }.Select(n => AccessTools.Field(typeof(Map), n)).ToArray();
        public NivarianLetterClock(Game game) { }
        internal static void Apply(Harmony harmony)
        {
            aid = NivarianUpdateActions.RequiredType("Nivarian_Race.Code.Incidents.ChoiceLetter_NivarianAid");
            guest = NivarianUpdateActions.RequiredType("Nivarian_Race.Code.Incidents.ChoiceLetter_NivarianHostVisitors");
            aidMap = Field(aid, "targetMap"); guestMap = Field(guest, "targetMap");
            aidStatus = Field(aid, "stat"); guestStatus = Field(guest, "stat");
            asyncType = NivarianUpdateActions.RequiredType("Multiplayer.Client.AsyncTimeComp");
            getClock = NivarianUpdateActions.RequiredMethod(NivarianUpdateActions.RequiredType("Multiplayer.Client.Extensions"), "AsyncTime", typeof(Map));
            mapTicks = Field(asyncType, "mapTicks");
            preContext = NivarianUpdateActions.RequiredMethod(asyncType, "PreContext");
            postContext = NivarianUpdateActions.RequiredMethod(asyncType, "PostContext");
            if (managers.Any(f => f == null)) throw new MissingFieldException("MP map manager context");
            harmony.Patch(AccessTools.DeclaredMethod(typeof(LetterWithTimeout), nameof(LetterWithTimeout.StartTimeout), new[] { typeof(int) }), prefix: new HarmonyMethod(typeof(NivarianLetterClock), nameof(StartTimeout)));
            harmony.Patch(AccessTools.PropertyGetter(typeof(LetterWithTimeout), nameof(LetterWithTimeout.TimeoutPassed)), prefix: new HarmonyMethod(typeof(NivarianLetterClock), nameof(Passed)));
            harmony.Patch(AccessTools.PropertyGetter(typeof(LetterWithTimeout), nameof(LetterWithTimeout.LastTickBeforeTimeout)), prefix: new HarmonyMethod(typeof(NivarianLetterClock), nameof(LastTick)));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(LetterWithTimeout), "PostProcessedLabel"), transpiler: new HarmonyMethod(typeof(NivarianLetterClock), nameof(LabelClock)));
            foreach (var type in new[] { aid, guest })
                harmony.Patch(NivarianUpdateActions.RequiredMethod(type, "Removed"), prefix: new HarmonyMethod(typeof(NivarianLetterClock), nameof(Removal)));
            Log.Message("[TaleNivarianCompat] aid/hosting expiry and displayed countdown use target-map time; shared archive expires under the map's clock, faction and Rand context.");
        }
        static FieldInfo Field(Type type, string name) => AccessTools.Field(type, name) ?? throw new MissingFieldException(type.FullName, name);
        static bool Ours(Letter letter) => aid != null && letter != null && (aid.IsInstanceOfType(letter) || guest.IsInstanceOfType(letter));
        static FieldInfo Status(Letter letter) => aid.IsInstanceOfType(letter) ? aidStatus : guestStatus;
        static Map MapOf(Letter letter) => (Map)(aid.IsInstanceOfType(letter) ? aidMap : guestMap).GetValue(letter);
        static object Clock(Map map) => getClock.Invoke(null, new object[] { map });
        static int Ticks(LetterWithTimeout letter)
        {
            if (!MP.IsInMultiplayer || !Ours(letter)) return Find.TickManager.TicksGame;
            var map = MapOf(letter);
            var clock = map != null && Find.Maps.Contains(map) ? Clock(map) : null;
            return clock == null ? Find.TickManager.TicksGame : (int)mapTicks.GetValue(clock);
        }
        static bool StartTimeout(LetterWithTimeout __instance, int duration)
        {
            if (!MP.IsInMultiplayer || !Ours(__instance)) return true;
            __instance.disappearAtTick = Ticks(__instance) + duration; return false;
        }
        static bool Passed(LetterWithTimeout __instance, ref bool __result)
        {
            if (!MP.IsInMultiplayer || !Ours(__instance)) return true;
            __result = __instance.TimeoutActive && Ticks(__instance) >= __instance.disappearAtTick; return false;
        }
        static bool LastTick(LetterWithTimeout __instance, ref bool __result)
        {
            if (!MP.IsInMultiplayer || !Ours(__instance)) return true;
            __result = __instance.TimeoutActive && __instance.disappearAtTick <= Ticks(__instance) + 1; return false;
        }
        static int LabelTicks(TickManager unused, LetterWithTimeout letter) => Ticks(letter);
        static IEnumerable<CodeInstruction> LabelClock(IEnumerable<CodeInstruction> instructions)
        {
            var getter = AccessTools.PropertyGetter(typeof(TickManager), nameof(TickManager.TicksGame));
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(getter))
                {
                    var self = new CodeInstruction(OpCodes.Ldarg_0);
                    self.labels.AddRange(instruction.labels); instruction.labels.Clear();
                    self.blocks.AddRange(instruction.blocks); instruction.blocks.Clear();
                    yield return self;
                    instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(NivarianLetterClock), nameof(LabelTicks)); count++;
                }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException("Letter label clock sites=" + count);
        }
        static bool Removal(Letter __instance) => !MP.IsInMultiplayer || !MP.InInterface || !Ours(__instance) || Convert.ToInt32(Status(__instance).GetValue(__instance)) != 0;
        public override void GameComponentTick()
        {
            if (!MP.IsInMultiplayer || aid == null || Find.Archive == null) return;
            // The native timeout is already an absolute target-map tick. No migration is needed.
            var pending = Find.Archive.ArchivablesListForReading.OfType<LetterWithTimeout>().Where(l => Ours(l) && Convert.ToInt32(Status(l).GetValue(l)) == 0).OrderBy(l => l.ID).ToArray();
            foreach (var letter in pending)
            {
                var map = MapOf(letter);
                if (map == null || !Find.Maps.Contains(map)) { Status(letter).SetValue(letter, Enum.ToObject(Status(letter).FieldType, 2)); continue; }
                if (!letter.TimeoutActive || Ticks(letter) < letter.disappearAtTick) continue;
                var clock = Clock(map);
                if (clock == null) throw new InvalidOperationException("Nivarian letter target lacks AsyncTimeComp");
                var saved = managers.Select(f => f.GetValue(map)).ToArray();
                preContext.Invoke(clock, null);
                try
                {
                    // Removed contains the complete native cooldown/Lord notification, without rejection goodwill.
                    letter.Removed();
                    Find.LetterStack.RemoveLetter(letter);
                }
                finally
                {
                    postContext.Invoke(clock, null);
                    for (int i = 0; i < managers.Length; i++) managers[i].SetValue(map, saved[i]);
                }
            }
        }
    }
}

