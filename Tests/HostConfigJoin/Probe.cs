using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using HarmonyLib;
using Multiplayer.Client;
using Multiplayer.Client.Util;
using Verse;
using RimWorld;
using Multiplayer.Common;
using UnityEngine;

[StaticConstructorOnStartup]
public static class HostConfigJoinProbe
{
    const string Package = "local.mp.meowonlineshop.sellslingshot";
    static bool ran;
    static readonly string result = Path.Combine(GenFilePaths.SaveDataFolderPath, "result.txt");
    static HostConfigJoinProbe()
    {
        if (!GenCommandLine.CommandLineArgPassed("hostconfigjoinprobe")) return;
        new Harmony("local.meow.hostconfigjoinprobe").Patch(AccessTools.Method(typeof(Root_Entry), "Update"),
            postfix: new HarmonyMethod(typeof(HostConfigJoinProbe), nameof(Run)));
    }
    static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    static object Settings(Type mod) => AccessTools.Field(mod, "RuntimeSettings").GetValue(null);
    static RemoteData Remote(string contents, bool unsafeMain = false)
    {
        var r = new RemoteData { hasConfigs = true, remoteFiles = JoinData.modFilesSnapshot,
            remoteRwVersion = VersionControl.CurrentVersionString, remoteMpVersion = MpVersion.Version };
        foreach (var m in JoinData.activeModsSnapshot)
            r.remoteMods.Add(new ModInfo { packageId = m.PackageIdNonUnique, name = m.Name, source = m.Source });
        foreach (var c in SyncConfigs.GetSyncableConfigContents(r.RemoteModIds.ToList()))
            if (!(c.ModId == Package && (c.FileName == "TurretCombatSleepMod" || (unsafeMain && c.FileName == "MpMeowOnlineShopMod"))))
                r.remoteModConfigs.Add(c);
        r.remoteModConfigs.Add(new ModConfig(Package, "TurretCombatSleepMod", contents));
        if (unsafeMain) r.remoteModConfigs.Add(new ModConfig(Package,"MpMeowOnlineShopMod",
            "<SettingsBlock><ModSettings><mp_meow_compatibility_enabled>false</mp_meow_compatibility_enabled></ModSettings></SettingsBlock>"));
        return r;
    }
    static string Xml(bool on) => "<SettingsBlock><ModSettings Class=\"Meow.TurretCombatSleep.TurretCombatSleepSettings\"><turretCombatSleepEnabled>" + on + "</turretCombatSleepEnabled></ModSettings></SettingsBlock>";
    public static void Run()
    {
        if (ran || LongEventHandler.AnyEventNowOrWaiting) return;
        ran = true;
        try
        {
            var mod = AccessTools.TypeByName("Meow.TurretCombatSleep.TurretCombatSleepMod");
            var core = AccessTools.TypeByName("MP_MeowOnlineShop.Patch_MpConfigHotSync");
            Log.Message("HOST_CONFIG START coreMvid=" + core.Module.ModuleVersionId + " turretMvid=" + mod.Module.ModuleVersionId);
            AccessTools.Method(typeof(JoinData), "TakeModDataSnapshot").Invoke(null, null);
            bool optOut = GenCommandLine.TryGetCommandLineArg("mpmeowhotcfg", out string opt) && opt == "false";
            int assertions = 0;
            foreach (bool enabled in new[] { true, false, true, false })
            {
                int calls = 0;
                var w = new JoinDataWindow(Remote(Xml(enabled))) { connectAnywayCallback = () => calls++ };
                Find.WindowStack.Add(w); // actual PostOpen, comparison and Harmony callback
                if (optOut)
                {
                    Check(!string.IsNullOrEmpty(w.connectAnywayDisabled), "opt-out permits unsafe manual join");
                    w.connectAnywayCallback(); Check(calls == 0, "opt-out delegate bypass");
                    w.Close(false); assertions++; break;
                }
                Check(calls == 1, "host setting was not applied before automatic continuation enabled=" + enabled + " calls=" + calls);
                object settings = Settings(mod);
                Check((bool)AccessTools.Field(settings.GetType(), "enabled").GetValue(settings) == enabled, "runtime switch differs from host");
                Log.Message("HOST_CONFIG PASS hot-switch=" + enabled + " callbackCount=" + calls); assertions++;
            }
            if (!optOut)
            {
                int runtimeCalls = 0;
                var live = Remote(Xml(false));
                for (int i = live.remoteModConfigs.Count - 1; i >= 0; i--)
                    if (live.remoteModConfigs[i].ModId == Package && live.remoteModConfigs[i].FileName == "MpMeowOnlineShopMod")
                        live.remoteModConfigs.RemoveAt(i);
                live.remoteModConfigs.Add(new ModConfig(Package, "MpMeowOnlineShopMod",
                    "<SettingsBlock><ModSettings><mp_meow_modcfg_opt_telemetry_enabled>false</mp_meow_modcfg_opt_telemetry_enabled></ModSettings></SettingsBlock>"));
                var liveWindow = new JoinDataWindow(live) { connectAnywayCallback = () => runtimeCalls++ };
                Find.WindowStack.Add(liveWindow);
                Check(runtimeCalls == 1, "main runtime-only config did not hot-apply");
                var mainSettings = AccessTools.Property(AccessTools.TypeByName("MP_MeowOnlineShop.MpMeowOnlineShopMod"), "Settings").GetValue(null);
                Check(!(bool)AccessTools.Field(mainSettings.GetType(), "enableOptimizationTelemetry").GetValue(mainSettings), "main runtime field not imported");
                assertions++;
                int calls = 0;
                var unsafeWindow = new JoinDataWindow(Remote(Xml(true), true)) { connectAnywayCallback = () => calls++ };
                Find.WindowStack.Add(unsafeWindow);
                Check(!string.IsNullOrEmpty(unsafeWindow.connectAnywayDisabled), "startup category mismatch permits manual join");
                unsafeWindow.connectAnywayCallback(); Check(calls == 0, "unsafe delegate bypass");
                Check(!(bool)AccessTools.Field(Settings(mod).GetType(), "enabled").GetValue(Settings(mod)), "preflight changed turret before rejecting batch");
                unsafeWindow.Close(false); assertions++;
            }
            string pass = "PASS native JoinDataWindow assertions=" + assertions + " optOut=" + optOut;
            File.WriteAllText(result, pass); Log.Message("HOST_CONFIG " + pass);
        }
        catch (Exception e) { File.WriteAllText(result, "FAIL " + e); Log.Error("HOST_CONFIG FAIL " + e); }
        Application.Quit();
    }
}
