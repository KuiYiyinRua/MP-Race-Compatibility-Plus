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

public sealed class StaticConfigProbeSettings : ModSettings
{
    public static bool enabled;
    public override void ExposeData() => Scribe_Values.Look(ref enabled, "enabled", false);
}
public sealed class StaticConfigProbeMod : Mod
{
    public static StaticConfigProbeMod Instance;
    public static bool FailWrite;
    public StaticConfigProbeMod(ModContentPack pack) : base(pack)
    {
        Instance = this;
        GetSettings<StaticConfigProbeSettings>();
    }
    public override void WriteSettings()
    {
        base.WriteSettings();
        if (FailWrite) throw new Exception("intentional callback failure after writing");
    }
}

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
    static string PathFor(string folder, string handle) => (string)AccessTools.Method(typeof(LoadedModManager), "GetSettingsFilename").Invoke(null, new object[]{folder,handle});
    static void RestartMode(bool on) => AccessTools.PropertySetter(typeof(SyncConfigs), "Applicable").Invoke(null,new object[]{on});
    static int PersistenceAssertions(Type turret)
    {
        string permanent=PathFor("Candidate","TurretCombatSleepMod");
        string backup=Path.Combine(GenFilePaths.SaveDataFolderPath,"MPMeowHostConfigBackup",Path.GetFileName(permanent)+".original");
        string firstOriginal=File.Exists(backup)?File.ReadAllText(backup):Xml(false);
        File.WriteAllText(permanent,Xml(false));
        string absent=Path.Combine(GenFilePaths.ConfigFolderPath,"Mod_Probe_StaticConfigProbeMod.xml");
        File.WriteAllText(absent,"<SettingsBlock><ModSettings><enabled>true</enabled></ModSettings></SettingsBlock>");
        SyncConfigs.SaveConfigs(new List<ModConfig>{new ModConfig(Package,"TurretCombatSleepMod",Xml(true))});
        Check(!File.Exists(absent), "parent restart staging did not reset host-absent config");
        try
        {
            RestartMode(true);
            Check(PathFor("Candidate","TurretCombatSleepMod")==permanent,"restart child still uses temporary config");
            Check(File.ReadAllText(permanent)==Xml(true),"host config not migrated to permanent file");
            Check(File.ReadAllText(backup)==firstOriginal,"original config backup missing or overwritten");
            // Model the settings object already loaded by MP before our late
            // migration hook, then join a host using a different live switch.
            var settings=Settings(turret);
            AccessTools.Field(settings.GetType(),"enabled").SetValue(settings,true);
            int calls=0;
            var window=new JoinDataWindow(Remote(Xml(false))){connectAnywayCallback=()=>calls++};
            Find.WindowStack.Add(window);
            Check(calls==1,"restart child cannot hot-sync another host");
            Check(File.ReadAllText(permanent)==Xml(false),"restart-child hot sync did not persist");
            Check(!(bool)AccessTools.Field(settings.GetType(),"enabled").GetValue(settings),"restart child live switch stale");
            Check(PathFor("Candidate","TurretCombatSleepMod")==permanent && File.ReadAllText(permanent)==Xml(false),"stale temp file overwrote later hot sync");

            // Native absence means defaults; do not revive a client-only file
            // at the next normal launch.
            Check(PathFor("Probe","StaticConfigProbeMod")==absent && !File.Exists(absent),"host-absent config was not durably reset");
        }
        finally { RestartMode(false); }
        Check(PathFor("Candidate","TurretCombatSleepMod")==permanent,"normal launch path differs");
        var read=AccessTools.Method(typeof(LoadedModManager),"ReadModSettings").MakeGenericMethod(Settings(turret).GetType());
        var fresh=read.Invoke(null,new object[]{"Candidate","TurretCombatSleepMod"});
        Check(!(bool)AccessTools.Field(fresh.GetType(),"enabled").GetValue(fresh),"fresh settings instance restored stale client value");
        Log.Message("HOST_CONFIG PASS restart persistence, backup, child hot sync, stale-temp protection, host-absent reset, fresh normal read");
        return 6;
    }
    static int StaticAssertions()
    {
        const string id="local.meow.hostconfigjoinprobe", handle="StaticConfigProbeMod";
        StaticConfigProbeSettings.enabled=false;
        StaticConfigProbeMod.Instance.WriteSettings();
        foreach(bool reject in new[]{false,true})
        {
            string path=PathFor("Probe",handle), before=File.ReadAllText(path);
            var remote=Remote(Xml(false));
            for(int i=remote.remoteModConfigs.Count-1;i>=0;i--)
                if(remote.remoteModConfigs[i].ModId==id && remote.remoteModConfigs[i].FileName==handle)remote.remoteModConfigs.RemoveAt(i);
            remote.remoteModConfigs.Add(new ModConfig(id,handle,"<SettingsBlock><ModSettings><enabled>"+(!reject)+"</enabled></ModSettings></SettingsBlock>"));
            StaticConfigProbeMod.FailWrite=reject;
            int calls=0;
            var window=new JoinDataWindow(remote){connectAnywayCallback=()=>calls++};
            try
            {
                Find.WindowStack.Add(window);
                Check(calls==(reject?0:1),"static-field reload/rollback continuation incorrect");
                Check(StaticConfigProbeSettings.enabled,"static host value missing or callback rollback failed");
                if(reject)
                {
                    Check(File.ReadAllText(path).Contains("<enabled>False</enabled>"), "restart-only host setting was not durably staged");
                    Check(string.IsNullOrEmpty(window.connectAnywayDisabled), "failed hot reload blocks native manual join");
                    window.connectAnywayCallback(); Check(calls == 1, "manual delegate replaced on failed reload");
                }
            }
            finally{StaticConfigProbeMod.FailWrite=false;window.Close(false);}
        }
        Log.Message("HOST_CONFIG PASS static settings hot import and failed-callback disk/runtime rollback");
        return 2;
    }
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
                    Check(string.IsNullOrEmpty(w.connectAnywayDisabled), "opt-out blocks native manual join");
                    w.connectAnywayCallback(); Check(calls == 1, "opt-out delegate replaced");
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
                Check(string.IsNullOrEmpty(unsafeWindow.connectAnywayDisabled), "startup category mismatch blocks manual join");
                Check(calls == 0, "restart-only item automatically joined");
                unsafeWindow.connectAnywayCallback(); Check(calls == 1, "native delegate replaced");
                Check((bool)AccessTools.Field(Settings(mod).GetType(), "enabled").GetValue(Settings(mod)), "mixed batch rolled back independently hot turret setting");
                Check(File.ReadAllText(PathFor("Candidate", "MpMeowOnlineShopMod")).Contains("<mp_meow_compatibility_enabled>false"), "startup setting not durable");
                unsafeWindow.Close(false); assertions++;
                assertions+=PersistenceAssertions(mod);
                assertions+=StaticAssertions();
            }
            string pass = "PASS native JoinDataWindow assertions=" + assertions + " optOut=" + optOut;
            File.WriteAllText(result, pass); Log.Message("HOST_CONFIG " + pass);
        }
        catch (Exception e) { File.WriteAllText(result, "FAIL " + e); Log.Error("HOST_CONFIG FAIL " + e); }
        Application.Quit();
    }
}
