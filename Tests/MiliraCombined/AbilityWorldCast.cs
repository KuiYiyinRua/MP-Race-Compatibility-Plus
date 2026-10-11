using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Mp = Multiplayer.Client.Multiplayer;

internal static class AbilityWorldCastProbe
{
    static readonly bool client = GenCommandLine.CommandLineArgPassed("twclient");
    static readonly string root = Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
    static string Peer => client ? "client" : "host";
    static ISyncMethod prepare;
    static Pawn pawn;
    static Ability ability;
    static Map sourceMap, targetMap;
    static GlobalTargetInfo target;
    static int applyCount, start;
    static bool requested, prepared, sent, done, failed;
    static float deadline;
    static void Check(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }

    internal static void Init(Harmony harmony)
    {
        if (!MiliraActionsProbe.NaglfarOnly) return;
        var compType = AccessTools.TypeByName("MilianModification.CompAbilityTransitionDrive");
        var apply = compType == null ? null : AccessTools.DeclaredMethod(compType, "Apply", new[] { typeof(GlobalTargetInfo) });
        Check(apply != null, "native transition-drive Apply missing");
        harmony.Patch(apply, postfix: new HarmonyMethod(typeof(AbilityWorldCastProbe), nameof(ApplyPostfix)));
        prepare = MP.RegisterSyncMethod(typeof(AbilityWorldCastProbe), nameof(Prepare));
    }

    public static void Prepare()
    {
        var maps = Find.Maps.Where(m => m.IsPlayerHome).OrderBy(m => m.uniqueID).ToArray();
        Check(maps.Length == 2, "world ability requires two player maps");
        sourceMap = maps[0]; targetMap = maps[1];
        Check(targetMap.mapPawns.AllPawnsSpawned.Any(p => p.IsColonist && p.HomeFaction == Faction.OfPlayer),
            "destination has no allied colonist");
        pawn = PawnGenerator.GeneratePawn(PawnKindDef.Named("Milian_Mechanoid_BishopII"), Faction.OfPlayer);
        GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(sourceMap.Center, sourceMap, 12), sourceMap);
        pawn.drafter.Drafted = true;
        var componentDef = DefDatabase<ThingDef>.GetNamed("MilianFitting_TransitionDrive");
        var data = AccessTools.Field(componentDef.GetType(), "milianComponent").GetValue(componentDef);
        var slot = ((IEnumerable)AccessTools.Field(data.GetType(), "possibleSlots").GetValue(data)).Cast<object>().First();
        var modificationType = AccessTools.TypeByName("MilianModification.CompModification");
        var modification = pawn.AllComps.Single(c => c.GetType() == modificationType);
        ((IDictionary)AccessTools.Field(modificationType, "componentSlotInRecipe").GetValue(modification)).Add(slot, componentDef);
        var utility = AccessTools.TypeByName("MilianModification.MilianModificationUtility");
        AccessTools.Method(utility, "InstallComponent", new[] { typeof(Pawn), componentDef.GetType() })
            .Invoke(null, new object[] { pawn, componentDef });
        var installed = (IDictionary)AccessTools.Field(modificationType, "componentSlotInstalled").GetValue(modification);
        Check(installed.Contains(slot) && installed[slot] == componentDef, "native transition-drive install not persisted");
        var abilityDef = DefDatabase<AbilityDef>.GetNamed("MilianAbility_TransitionDrive");
        ability = pawn.abilities.AllAbilitiesForReading.FirstOrDefault(a => a.def == abilityDef);
        Check(ability != null, "transition-drive ability missing");
        ability.StartCooldown(0);
        Check(pawn.needs.energy != null, "transition-drive pawn has no energy need");
        pawn.needs.energy.CurLevelPercentage = 1f;
        target = (GlobalTargetInfo)(WorldObject)targetMap.Parent;
        Check(ability.ValidateGlobalTarget(target), "native world target invalid");
        prepared = true; start = TickPatch.Timer; deadline = UnityEngine.Time.realtimeSinceStartup + 180;
        Log.Message("ABILITY_WORLD PREPARE pawn=" + pawn.ThingID + " source=" + sourceMap.uniqueID +
            " target=" + targetMap.uniqueID);
    }

    static void ApplyPostfix(CompAbilityEffect __instance)
    {
        if (!prepared || __instance.parent != ability) return;
        applyCount++;
        Log.Message("ABILITY_WORLD APPLY count=" + applyCount + " pawn=" + pawn.ThingID +
            " spawned=" + pawn.Spawned + " map=" + pawn.Map?.uniqueID);
    }

    internal static bool Update()
    {
        if (done) return true;
        if (failed) return false;
        try
        {
            Check(!Mp.session.desynced && Mp.session.players.Count == 2, "network health");
            if (!prepared) { if (client && !requested) requested = prepare.DoSync(null); return false; }
            if (client && !sent && TickPatch.Timer - start > 60)
            {
                var commandType = AccessTools.TypeByName("MilianModification.Command_MilianAbility");
                Check(commandType != null, "native world ability command missing");
                var command = Activator.CreateInstance(commandType, ability, pawn);
                var callbacks = commandType.GetMethods(BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(m => m.Name.StartsWith("<ProcessInput>b__", StringComparison.Ordinal) &&
                        m.ReturnType == typeof(bool) && m.GetParameters().Length == 1 &&
                        m.GetParameters()[0].ParameterType == typeof(GlobalTargetInfo)).ToArray();
                Check(callbacks.Length == 1, "native world target callback ambiguous");
                var before = pawn.CurJob;
                Check((bool)callbacks[0].Invoke(command, new object[] { target }), "native world callback rejected target");
                Check(ReferenceEquals(before, pawn.CurJob) && pawn.Map == sourceMap && applyCount == 0,
                    "world ability changed simulation before sync");
                sent = true;
                Log.Message("ABILITY_WORLD DISPATCH native target callback rollback=True");
            }
            Check(UnityEngine.Time.realtimeSinceStartup < deadline, "world ability timeout");
            Check(applyCount <= 1, "world ability applied more than once");
            if (applyCount != 1 || !pawn.Spawned || pawn.Map != targetMap) return false;
            string receipt = pawn.ThingID + ";source=" + sourceMap.uniqueID + ";target=" +
                targetMap.uniqueID + ";apply=1;landed=True";
            string local = Path.Combine(root, Peer + ".abilityworld");
            string other = Path.Combine(root, (client ? "host" : "client") + ".abilityworld");
            if (!File.Exists(local)) { File.WriteAllText(local + ".tmp", receipt); File.Move(local + ".tmp", local); }
            if (!File.Exists(other)) return false;
            Check(File.ReadAllText(local) == File.ReadAllText(other), "world ability peer receipt mismatch");
            done = true;
            Log.Message("ABILITY_WORLD ACTION_PASS " + receipt);
            return true;
        }
        catch (Exception e) { failed = true; Log.Error("COMBINED FAILED ABILITY_WORLD " + e); }
        return false;
    }
}
