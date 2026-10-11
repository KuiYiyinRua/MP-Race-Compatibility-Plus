using System;
using System.Collections;
using System.IO;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client;
using RimWorld;
using UnityEngine;
using Verse;
using Mp = Multiplayer.Client.Multiplayer;

internal static class AbilitySelfCastProbe
{
    static readonly bool client = GenCommandLine.CommandLineArgPassed("twclient");
    static readonly string root = Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
    static string Peer => client ? "client" : "host";
    static ISyncMethod prepare;
    static Pawn pawn;
    static Ability ability;
    static HediffDef effectDef;
    static bool requested, prepared, sent, observed, done, failed;
    static int start;
    static float deadline;
    static void Check(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); }
    static string State() => pawn.ThingID + ";ability=" + ability.def.defName + ";effect=" +
        pawn.health.hediffSet.HasHediff(effectDef);

    internal static void Init()
    {
        prepare = MP.RegisterSyncMethod(typeof(AbilitySelfCastProbe), nameof(Prepare));
    }

    public static void Prepare()
    {
        var map = Find.Maps.First(m => m.IsPlayerHome);
        pawn = PawnGenerator.GeneratePawn(PawnKindDef.Named("Milian_Mechanoid_BishopII"), Faction.OfPlayer);
        GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(map.Center, map, 12), map);
        pawn.drafter.Drafted = true;
        var componentDef = DefDatabase<ThingDef>.GetNamed("MilianFitting_AntiKnockModule");
        var componentData = AccessTools.Field(componentDef.GetType(), "milianComponent").GetValue(componentDef);
        var slot = ((IEnumerable)AccessTools.Field(componentData.GetType(), "possibleSlots").GetValue(componentData))
            .Cast<object>().First();
        var modificationType = AccessTools.TypeByName("MilianModification.CompModification");
        var modification = pawn.AllComps.Single(c => c.GetType() == modificationType);
        ((IDictionary)AccessTools.Field(modificationType, "componentSlotInRecipe").GetValue(modification))
            .Add(slot, componentDef);
        var utility = AccessTools.TypeByName("MilianModification.MilianModificationUtility");
        AccessTools.Method(utility, "InstallComponent", new[] { typeof(Pawn), componentDef.GetType() })
            .Invoke(null, new object[] { pawn, componentDef });
        var installed = (IDictionary)AccessTools.Field(modificationType, "componentSlotInstalled").GetValue(modification);
        Check(installed.Contains(slot) && installed[slot] == componentDef, "native ability component install not persisted");
        var abilityDef = DefDatabase<AbilityDef>.GetNamed("MilianAbility_AntiKnockMode");
        ability = pawn.abilities.AllAbilitiesForReading.FirstOrDefault(a => a.def == abilityDef);
        Check(ability != null, "native anti-knock ability missing after install");
        ability.StartCooldown(0);
        effectDef = DefDatabase<HediffDef>.GetNamed("Milian_AntiKnockMode");
        Check(!pawn.health.hediffSet.HasHediff(effectDef), "anti-knock effect already active");
        prepared = true; start = TickPatch.Timer; deadline = Time.realtimeSinceStartup + 120;
        Log.Message("ABILITY_SELF PREPARE " + State());
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
                Check(commandType != null, "native self-cast command type missing");
                var command = (Command)Activator.CreateInstance(commandType, ability, pawn);
                Check(!command.Disabled, "native self-cast command disabled");
                var before = pawn.CurJob;
                command.ProcessInput(new Event());
                Check(ReferenceEquals(before, pawn.CurJob) && !pawn.health.hediffSet.HasHediff(effectDef),
                    "self-cast mutated locally before synced command");
                sent = true;
                Log.Message("ABILITY_SELF DISPATCH native command rollback=True");
            }
            Check(Time.realtimeSinceStartup < deadline, "self-cast timeout");
            if (!observed && pawn.health.hediffSet.HasHediff(effectDef))
            {
                observed = true;
                File.WriteAllText(Path.Combine(root, Peer + ".abilityself"), State());
                Log.Message("ABILITY_SELF EFFECT " + State());
            }
            if (!observed) return false;
            string local = Path.Combine(root, Peer + ".abilityself");
            string other = Path.Combine(root, (client ? "host" : "client") + ".abilityself");
            if (!File.Exists(other)) return false;
            Check(File.ReadAllText(local) == File.ReadAllText(other), "self-cast peer receipt mismatch");
            done = true;
            Log.Message("ABILITY_SELF ACTION_PASS " + File.ReadAllText(local));
            return true;
        }
        catch (Exception e) { failed = true; Log.Error("COMBINED FAILED ABILITY_SELF " + e); }
        return false;
    }
}
