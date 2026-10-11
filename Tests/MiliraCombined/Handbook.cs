using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;

internal static class HandbookProbe
{
    static bool client, requested, prepared, clicked, complete, failed, drawing;
    static int round, repaints;
    static bool screenshotRequested;
    static float deadline, settle;
    static ISyncMethod prepare;
    static Thing table;
    static Type handbook;
    static Texture2D illustration;
    static Window opened;
    static bool roundStarted;
    public static bool Manual => GenCommandLine.CommandLineArgPassed("handbookmanual");
    public static bool ManualTwoPeer => GenCommandLine.CommandLineArgPassed("handbooktwopeer");
    static bool manualReady;
    static bool manualButtonLogged, manualScreenshotRequested;
    static string Root => Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
    static string Peer => client ? "client" : "host";
    static object Field(object o, string n) => AccessTools.Field(o.GetType(), n).GetValue(o);
    public static void Init(Harmony h)
    {
        client = GenCommandLine.CommandLineArgPassed("twclient");
        handbook = AccessTools.TypeByName("MilianModification.Dialog_MilianComponentIllustratedHandbook");
        prepare = MP.RegisterSyncMethod(typeof(HandbookProbe), nameof(Prepare));
        h.Patch(AccessTools.Method(AccessTools.TypeByName("MilianModification.ITab_Pawn_MilianComponentResearch"), "FillTab"), prefix: Hook(nameof(TabBefore)), finalizer: Hook(nameof(TabAfter)));
        h.Patch(AccessTools.Method(typeof(Widgets), "ButtonImage", new[] { typeof(Rect), typeof(Texture2D), typeof(bool), typeof(string) }), prefix: Hook(nameof(Button)));
        h.Patch(AccessTools.Method(typeof(WindowStack), "Add", new[] { typeof(Window) }), prefix: Hook(nameof(AddBefore)), postfix: Hook(nameof(AddAfter)));
        h.Patch(AccessTools.DeclaredMethod(handbook, "DoWindowContents"), postfix: Hook(nameof(Drawn)), finalizer: Hook(nameof(DrawError)));
        h.Patch(AccessTools.Method(typeof(Widgets), "ButtonImage", new[] { typeof(Rect), typeof(Texture2D), typeof(bool), typeof(string) }), postfix: Hook(nameof(RealButton)));
        h.Patch(AccessTools.Method(typeof(Window), "Close", new[] { typeof(bool) }), prefix: Hook(nameof(CloseBefore)));
        LongEventHandler.ExecuteWhenFinished(() => {
            foreach (var n in new[] { "PLAMilira.CompSwitch_New", "PLAMilira.CompSwitch_New_New", "PLAMilira.Comp_ChangeSupport", "AncotLibrary.CompRangeWeaponVerbSwitch", "FunnelBit.CompTurretGun_FunnelBit" })
                Log.Message("COMBINED ELIGIBILITY " + n + " defs=" + string.Join(",", DefDatabase<ThingDef>.AllDefs.Where(d => d.comps?.Any(c => c.compClass.FullName == n) == true).Select(d => d.defName)));
        });
    }
    static HarmonyMethod Hook(string n) => new HarmonyMethod(typeof(HandbookProbe), n);
    public static void Prepare()
    {
        PrepareLocal();
    }
    private static void PrepareLocal()
    {
        var def = DefDatabase<ThingDef>.AllDefs.Single(d => d.thingClass.FullName == "MilianModification.Building_MilianComponentTable");
        var map = Find.Maps.First(m => m.IsPlayerHome);
        table = GenSpawn.Spawn(ThingMaker.MakeThing(def, GenStuff.DefaultStuffFor(def)), CellFinder.RandomClosewalkCellNear(map.Center, map, 12), map);
        table.SetFaction(Faction.OfPlayer);
        prepared = true;
        Log.Message("HANDBOOK PREPARED table=" + table.ThingID + " def=" + def.defName);
    }
    static bool Button(Texture2D __1, ref bool __result)
    {
        if (Manual) return true;
        if (!drawing || clicked || __1 != illustration || Event.current.type != EventType.Repaint) return true;
        clicked = true; __result = true;
        Log.Message("HANDBOOK NATIVE_BUTTON round=" + round + " interface=" + MP.InInterface);
        return false;
    }
    static void RealButton(Rect __0, Texture2D __1, bool __result)
    {
        if (!Manual || __1 != illustration) return;
        if (!manualButtonLogged && Event.current.type == EventType.Repaint)
        {
            var center = GUIUtility.GUIToScreenPoint(__0.center);
            Log.Message("HANDBOOK REAL_BUTTON_RECT gui=" + __0 + " screenCenter=" + center + " screen=" + Screen.width + "x" + Screen.height);
            manualButtonLogged = true;
        }
        if (__result) Log.Message("HANDBOOK REAL_MOUSE_BUTTON interface=" + MP.InInterface + " event=" + Event.current.type);
    }
    public static void ManualUpdate()
    {
        if (manualReady) return;
        table = Find.Maps.SelectMany(m => m.listerThings.AllThings)
            .FirstOrDefault(t => t?.def?.thingClass?.FullName == "MilianModification.Building_MilianComponentTable" && t.Spawned && t.Map != null);
        if (table == null)
        {
            if (ManualTwoPeer)
            {
                if (client && !requested) requested = prepare.DoSync(null);
                return;
            }
            // A single-host UI probe creates a local fixture; a two-peer probe uses the synced wrapper.
            PrepareLocal();
        }
        Log.Message("HANDBOOK MANUAL_FIXTURE game=" + (Current.Game != null) + " table=" + table?.ThingID +
            " spawned=" + table?.Spawned + " map=" + table?.Map?.uniqueID);
        if (Current.Game == null || table?.Map == null) throw new InvalidOperationException("manual handbook fixture lacks an active game/map");
        Current.Game.CurrentMap = table.Map;
        Find.Selector.ClearSelection(); Find.Selector.Select(table);
        Find.CameraDriver.JumpToCurrentMapLoc(table.Position.ToVector3Shifted());
        Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Inspect);
        ((MainTabWindow_Inspect)MainButtonDefOf.Inspect.TabWindow).OpenTabType = AccessTools.TypeByName("MilianModification.ITab_Pawn_MilianComponentResearch");
        illustration = (Texture2D)AccessTools.Field(AccessTools.TypeByName("AncotLibrary.AncotLibraryIcon"), "Illustration").GetValue(null);
        var info = Harmony.GetPatchInfo(AccessTools.Method(typeof(WindowStack), "Add", new[] { typeof(Window) }));
        Log.Message("HANDBOOK WINDOW_PATCHES " + string.Join(";", info.Prefixes.Select(p => p.owner + ":" + p.PatchMethod.DeclaringType.FullName + "." + p.PatchMethod.Name)));
        File.WriteAllText(Path.Combine(Root, ManualTwoPeer ? Peer + ".manual.ready" : "manual.ready"), table.ThingID + ":map=" + table.Map.uniqueID);
        manualReady = true;
        Log.Message("HANDBOOK MANUAL_READY " + table.ThingID);
    }
    static void AddBefore(Window window)
    {
        if (handbook.IsInstanceOfType(window)) Log.Message("HANDBOOK ADD_BEFORE round=" + round + " rect=" + window.windowRect);
    }
    static void AddAfter(Window window)
    {
        if (!handbook.IsInstanceOfType(window)) return;
        opened = window;
        Log.Message("HANDBOOK ADD_AFTER present=" + Find.WindowStack.Windows.Contains(window) + " components=" + ((IList)Field(window, "currentComponentPage"))?.Count);
    }
    static void CloseBefore(Window __instance)
    {
        if (Manual && handbook.IsInstanceOfType(__instance))
            Log.Message("HANDBOOK REAL_CLOSE event=" + Event.current?.type + " rect=" + __instance.windowRect);
    }
    static void Drawn(Window __instance)
    {
        if (__instance == opened && Event.current.type == EventType.Repaint) repaints++;
        if (Manual && __instance == opened && Event.current.type == EventType.Repaint && !manualScreenshotRequested)
        {
            manualScreenshotRequested = true;
            ScreenCapture.CaptureScreenshot(Path.Combine(Root, "manual-handbook-open.png"));
        }
    }
    static Exception DrawError(Exception __exception)
    {
        if (__exception != null) Fail("draw", __exception);
        return __exception;
    }
    static void TabBefore() { drawing = prepared && roundStarted && !complete && !Manual; }
    static Exception TabAfter(Exception __exception)
    {
        if (drawing && __exception != null) Fail("native inspect tab", __exception);
        drawing = false;
        return __exception;
    }
    static void Fail(string reason, Exception e = null)
    {
        if (failed) return;
        failed = true;
        File.WriteAllText(Path.Combine(Root, Peer + ".failed"), "HANDBOOK " + reason + " " + e);
        Log.Error("COMBINED FAILED HANDBOOK " + reason + " " + e);
    }
    public static bool Update()
    {
        if (complete)
        {
            bool both = File.Exists(Path.Combine(Root, "host.handbook.complete")) && File.Exists(Path.Combine(Root, "client.handbook.complete"));
            if (both && client && !File.Exists(Path.Combine(Root, "handbook.complete"))) File.WriteAllText(Path.Combine(Root, "handbook.complete"), "host/client native button/render/reopen x3 each; synthetic button result, not physical hit-testing");
            return both;
        }
        if (failed) return false;
        try
        {
            if (!prepared) { if (client && !requested) requested = prepare.DoSync(null); return false; }
            if (!roundStarted)
            {
                Current.Game.CurrentMap = table.Map;
                Find.Selector.ClearSelection(); Find.Selector.Select(table);
                Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Inspect);
                ((MainTabWindow_Inspect)MainButtonDefOf.Inspect.TabWindow).OpenTabType = AccessTools.TypeByName("MilianModification.ITab_Pawn_MilianComponentResearch");
                illustration = (Texture2D)AccessTools.Field(AccessTools.TypeByName("AncotLibrary.AncotLibraryIcon"), "Illustration").GetValue(null);
                roundStarted = true;
                deadline = Time.realtimeSinceStartup + 45;
                settle = Time.realtimeSinceStartup + 4;
            }
            if (Time.realtimeSinceStartup > deadline) { Fail("open/render timeout clicked=" + clicked + " repaints=" + repaints); return false; }
            if (repaints < 3 || Time.realtimeSinceStartup < settle) return false;
            if (!Find.WindowStack.Windows.Contains(opened)) { Fail("window removed after open"); return false; }
            // Take the actual game framebuffer, not a reconstructed UI.
            var screenshot = Path.Combine(Root, "handbook-" + Peer + "-" + round + ".png");
            if (!screenshotRequested) { screenshotRequested = true; ScreenCapture.CaptureScreenshot(screenshot); return false; }
            if (!File.Exists(screenshot)) return false;
            Log.Message("HANDBOOK RENDERED round=" + round + " repaints=" + repaints + " rect=" + opened.windowRect);
            opened.Close(false);
            ((MainTabWindow_Inspect)MainButtonDefOf.Inspect.TabWindow).OpenTabType = null;
            opened = null; roundStarted = false; clicked = screenshotRequested = false; repaints = 0;
            if (++round == 3)
            {
                File.WriteAllText(Path.Combine(Root, Peer + ".handbook.complete"), "native button/render/reopen x3; synthetic button result");
                complete = true;
            }
        }
        catch (Exception e) { Fail("driver", e); }
        return complete && File.Exists(Path.Combine(Root, "host.handbook.complete")) && File.Exists(Path.Combine(Root, "client.handbook.complete"));
    }
}
