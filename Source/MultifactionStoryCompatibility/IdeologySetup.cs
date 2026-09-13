using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client;
using Multiplayer.Client.Factions;
using RimWorld;
using UnityEngine;
using Verse;

namespace Meow.MultifactionStory
{
    // Drafts never enter IdeoManager or an existing faction's tracker.
    internal static class IdeologySetup
    {
        internal sealed class Draft { public Ideo Ideo; }
        internal static readonly ConditionalWeakTable<object, Draft> Drafts = new ConditionalWeakTable<object, Draft>();
        private static readonly ConditionalWeakTable<object, Draft> Payloads = new ConditionalWeakTable<object, Draft>();
        [ThreadStatic] private static int creatingPlayer;

        internal static void Install(Harmony h)
        {
            MP.RegisterSyncMethod(typeof(IdeologySetup), nameof(SendDraft)).ExposeParameter(1);
            Patch(h, typeof(Page_ChooseIdeo_Multifaction), "DoWindowContents", null, nameof(Draw));
            Patch(h, typeof(Page_ChooseIdeo_Multifaction), "CanDoNext", nameof(CanNext), null);
            Patch(h, typeof(Page_ChooseIdeo_Multifaction), "GetIdeologyData", null, nameof(Data));
            h.Patch(AccessTools.Method(typeof(FactionSidebar), "DoCreateFaction"), transpiler: new HarmonyMethod(typeof(IdeologySetup), nameof(SendTranspiler)));
            Patch(h, typeof(FactionCreator), "PrepareGameInitData", null, nameof(Prepare));
            Patch(h, typeof(FactionCreator), "NewFactionWithIdeo", null, nameof(FactionCreated));
        }

        internal static void Patch(Harmony h, Type type, string name, string prefix, string postfix, int priority = Priority.Normal)
        {
            var method = AccessTools.Method(type, name) ?? throw new MissingMethodException(type?.FullName, name);
            h.Patch(method, prefix == null ? null : new HarmonyMethod(typeof(IdeologySetup), prefix) { priority = priority },
                postfix == null ? null : new HarmonyMethod(typeof(IdeologySetup), postfix));
        }

        private static void Draw(Page_ChooseIdeo_Multifaction __instance, Rect inRect)
        {
            var draft = Drafts.GetOrCreateValue(__instance);
            if (Widgets.ButtonText(new Rect(inRect.xMax - 290, inRect.y + 4, 280, 32),
                draft.Ideo == null ? "自定义文化 / 演进文化" : "编辑文化：" + draft.Ideo.name))
                Find.WindowStack.Add(new DraftEditor(draft));
        }

        private static bool CanNext(Page_ChooseIdeo_Multifaction __instance, ref bool __result)
        {
            if (!Drafts.TryGetValue(__instance, out var draft) || draft.Ideo == null) return true;
            __result = DraftEditor.Valid(draft.Ideo);
            return false;
        }

        private static void Data(Page_ChooseIdeo_Multifaction __instance, IdeologyData __result)
        {
            if (Drafts.TryGetValue(__instance, out var draft) && draft.Ideo != null)
            {
                Payloads.Remove(__result);
                Payloads.Add(__result, draft);
            }
        }

        private static IEnumerable<CodeInstruction> SendTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var original = AccessTools.Method(typeof(FactionCreator), nameof(FactionCreator.CreateFaction));
            int found = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(original)) { instruction.operand = AccessTools.Method(typeof(IdeologySetup), nameof(SendWithDraft)); found++; }
                yield return instruction;
            }
            if (found != 1) throw new InvalidOperationException("Expected exactly one faction submission call, found " + found);
        }

        private static void SendWithDraft(int playerId, FactionCreationData creationData)
        {
            if (creationData.chooseIdeoInfo != null && Payloads.TryGetValue(creationData.chooseIdeoInfo, out var draft))
                SendDraft(playerId, draft.Ideo);
            else SendDraft(playerId, null);
            FactionCreator.CreateFaction(playerId, creationData);
        }

        public static void SendDraft(int playerId, Ideo ideo)
        {
            var state = Current.Game.GetComponent<StoryState>();
            if (ideo == null) state.Pending.Remove(playerId);
            else state.Pending[playerId] = ideo;
        }

        private static void Prepare(int sessionId) { creatingPlayer = sessionId; }

        private static void FactionCreated(Faction __result)
        {
            var state = Current.Game.GetComponent<StoryState>();
            if (!state.Pending.TryGetValue(creatingPlayer, out var draft)) return;
            state.Pending.Remove(creatingPlayer);
            // Allocate every identity in the deterministic faction-creation event, never in drawing.
            var ideo = draft;
            ideo.id = Find.UniqueIDsManager.GetNextIdeoID();
            foreach (var precept in ideo.PreceptsListForReading)
                AccessTools.Field(typeof(Precept), "ID").SetValue(precept, Find.UniqueIDsManager.GetNextPreceptID());
            if (ideo.development != null) ideo.development.ideo = ideo;
            ideo.style.ideo = ideo;
            ideo.initialPlayerIdeo = true;
            Find.IdeoManager.Add(ideo);
            __result.ideos.SetPrimary(ideo);
            Log.Message("[Meow.MultifactionStory] Applied ideology faction=" + __result.loadID + " fluid=" + ideo.Fluid + " memes=" + ideo.memes.Count);
        }
    }

    internal sealed class DraftEditor : Window
    {
        private readonly IdeologySetup.Draft owner;
        private Ideo draft;
        private Vector2 scroll;
        private float height;
        public override Vector2 InitialSize => new Vector2(1000, 760);
        public DraftEditor(IdeologySetup.Draft owner)
        {
            this.owner = owner;
            if (owner.Ideo != null) { draft = new Ideo(); owner.Ideo.CopyTo(draft); }
            forcePause = true;
            absorbInputAroundWindow = true;
            doCloseX = true;
            closeOnAccept = false;
        }
        private void Create(bool fluid)
        {
            draft = IdeoUtility.MakeEmptyIdeo();
            draft.Fluid = fluid;
            Find.WindowStack.Add(new Dialog_ChooseMemes(draft, MemeCategory.Structure, initialSelection: true));
        }
        public override void DoWindowContents(Rect rect)
        {
            if (Widgets.ButtonText(new Rect(0, 0, 210, 32), "创建演进文化")) Create(true);
            if (Widgets.ButtonText(new Rect(220, 0, 210, 32), "创建固定文化")) Create(false);
            if (Widgets.ButtonText(new Rect(440, 0, 220, 32), "复制现有文化"))
                Find.WindowStack.Add(new FloatMenu(Find.IdeoManager.IdeosListForReading.Select(i => new FloatMenuOption(i.name, () =>
                { draft = new Ideo(); i.CopyTo(draft); })).ToList()));
            if (draft != null)
                IdeoUIUtility.DoIdeoDetails(new Rect(0, 44, rect.width, rect.height - 94), draft, ref scroll, ref height,
                    editMode: true, ideoLoadedFromFile: loaded => draft = loaded, allowLoad: true, allowSave: true);
            else Widgets.Label(new Rect(0, 60, rect.width, 80), "选择演进或固定文化，然后选择模因、文化与具体戒律。编辑只影响本次新派系。");
            if (Widgets.ButtonText(new Rect(rect.width - 220, rect.height - 40, 210, 36), "保存并返回") && Valid(draft))
            { owner.Ideo = draft; Close(); }
            if (Widgets.ButtonText(new Rect(0, rect.height - 40, 210, 36), "取消自定义，使用预设"))
            { owner.Ideo = null; Close(); }
        }
        internal static bool Valid(Ideo ideo)
        {
            string error = null;
            if (ideo == null || ideo.name.NullOrEmpty() || !ideo.memes.Any(m => m.category == MemeCategory.Normal)) error = "请选择完整文化和模因。";
            else if (ideo.FirstIncompatiblePreceptPair() != default(Pair<Precept, Precept>)) error = "存在互不兼容的戒律。";
            else if (ideo.FirstRitualMissingTarget() != null || ideo.FirstConsumableBuildingMissingRitual() != null) error = "请补全仪式目标及仪式建筑。";
            else if (ideo.Fluid && (ideo.memes.Count(m => m.category == MemeCategory.Normal) > IdeoFoundation.MemeCountRangeFluidAbsolute.max || ideo.memes.Any(m => !IdeoUtility.IsMemeAllowedForInitialFluidIdeo(m)))) error = "演进文化的初始模因不符合原版限制。";
            if (error == null) return true;
            Messages.Message(error, MessageTypeDefOf.RejectInput, false);
            return false;
        }
    }
}
