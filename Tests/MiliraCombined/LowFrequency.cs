using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Mp = Multiplayer.Client.Multiplayer;

internal static class LowFrequencyProbe
{
    static readonly bool client = GenCommandLine.CommandLineArgPassed("twclient");
    static readonly string root = Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
    static string Peer => client ? "client" : "host";
    static ISyncMethod prepare;
    static Pawn pawn, second, caller;
    static Ability ability, secondAbility;
    static Caravan caravan;
    static Faction church;
    static RoyalTitlePermitDef permit;
    static CompProjectileInterceptor shield;
    static ThingWithComps chip;
    static object gameComp;
    static string[] beforeAcquire;
    static object farewell;
    static PersistentDialog Farewell => (PersistentDialog)farewell;
    static int round, start, effects, lettersBefore;
    static float deadline;
    static IntVec3 destination;
    static bool requested, prepared, sent, secondarySent, observed, advancing, finished, failed;
    static string effectReceipt;
    static void Check(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }
    static object Get(object value, string field) => AccessTools.Field(value.GetType(), field).GetValue(value);
    static string[] Acquired => ((IEnumerable)Get(gameComp, "milianComponentAcquire")).Cast<ThingDef>().Select(d => d.defName).OrderBy(x => x).ToArray();
    static string Inventory => string.Join(",", caravan.PawnsListForReading.SelectMany(p => p.inventory.innerContainer).OrderBy(t => t.thingIDNumber).Select(t => t.ThingID + ":" + t.stackCount));
    static DiaNode Node => (DiaNode)Get(Farewell.Dialog, "curNode");

    internal static void Init(Harmony harmony)
    {
        if (!GenCommandLine.CommandLineArgPassed("miliralowfrequency")) return;
        prepare = MP.RegisterSyncMethod(typeof(LowFrequencyProbe), nameof(Prepare));
        harmony.Patch(AccessTools.DeclaredMethod(AccessTools.TypeByName("MilianModification.CompAbilityShieldEnergyPulse"), "Apply", new[] { typeof(LocalTargetInfo), typeof(LocalTargetInfo) }),
            postfix: new HarmonyMethod(typeof(LowFrequencyProbe), nameof(PulseApplied)));
        harmony.Patch(AccessTools.DeclaredMethod(AccessTools.TypeByName("MilianModification.CompAbilityTransitionDrive"), "Apply", new[] { typeof(GlobalTargetInfo) }),
            postfix: new HarmonyMethod(typeof(LowFrequencyProbe), nameof(WorldApplied)));
        harmony.Patch(AccessTools.DeclaredMethod(AccessTools.TypeByName("MilianModification.CompMermeisterLetterFarewell"), "DataApplied"),
            postfix: new HarmonyMethod(typeof(LowFrequencyProbe), nameof(FarewellApplied)));
    }

    static void PulseApplied(CompAbilityEffect __instance)
    {
        if (__instance.parent != ability) return;
        effects++;
        Check(shield.currentHitPoints == 1 && pawn.stances.stunner.Stunned, "EMP pulse shield/stun missing at mutation");
        effectReceipt = pawn.ThingID + ";shield=" + shield.currentHitPoints + ";stunned=True;effects=" + effects;
        Log.Message("LOW_FREQUENCY PULSE_EXECUTED round=" + round + " " + effectReceipt);
    }
    static void WorldApplied(CompAbilityEffect __instance)
    {
        if (__instance.parent != ability) return;
        effects++;
        Check(caravan.ContainsPawn(pawn) && !pawn.Spawned, "world ability did not join target caravan");
        effectReceipt = pawn.ThingID + ";caravan=" + caravan.GetUniqueLoadID() + ";joined=True;effects=" + effects;
        Log.Message("LOW_FREQUENCY CARAVAN_EXECUTED round=" + round + " " + effectReceipt);
    }
    static void FarewellApplied(ThingComp __instance)
    {
        if (__instance.parent != chip) return;
        effects++;
        farewell = pawn.Map.MpComp().mapDialogs.Last();
        Check(Node.options.Count == 1 && Node.options[0].action != null, "native farewell dialog missing");
        Log.Message("LOW_FREQUENCY FAREWELL_EXECUTED round=" + round + " dialog=" + Farewell.id);
    }
    static Ability Install(Pawn target, string fitting, string abilityName)
    {
        var def = ThingDef.Named(fitting);
        var data = Get(def, "milianComponent");
        var slot = ((IEnumerable)Get(data, "possibleSlots")).Cast<object>().First();
        var modification = target.AllComps.Single(c => c.GetType().FullName == "MilianModification.CompModification");
        ((IDictionary)Get(modification, "componentSlotInRecipe")).Add(slot, def);
        AccessTools.Method(AccessTools.TypeByName("MilianModification.MilianModificationUtility"), "InstallComponent", new[] { typeof(Pawn), def.GetType() }).Invoke(null, new object[] { target, def });
        var result = target.abilities.AllAbilitiesForReading.Single(a => a.def.defName == abilityName);
        result.StartCooldown(0);
        if (target.needs.energy != null) target.needs.energy.CurLevelPercentage = 1;
        return result;
    }
    static Command Command(Ability value) => (Command)Activator.CreateInstance(AccessTools.TypeByName("MilianModification.Command_MilianAbility"), value, value.pawn);

    public static void Prepare(int n)
    {
        round = n; effects = 0; sent = secondarySent = observed = advancing = false; effectReceipt = ""; farewell = null;
        var map = Find.Maps.First(m => m.IsPlayerHome);
        gameComp = Current.Game.components.Single(c => c.GetType().FullName == "MilianModification.MiliraGameComponent_MilianComponentRecipe");
        if (n < 3)
        {
            pawn = PawnGenerator.GeneratePawn(PawnKindDef.Named("Milian_Mechanoid_BishopII"), Faction.OfPlayer);
            second = PawnGenerator.GeneratePawn(PawnKindDef.Named("Milian_Mechanoid_BishopII"), Faction.OfPlayer);
            GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(map.Center, map, 10), map);
            GenSpawn.Spawn(second, CellFinder.RandomClosewalkCellNear(pawn.Position, map, 4), map);
            pawn.drafter.Drafted = second.drafter.Drafted = true;
            ability = Install(pawn, "MilianFitting_JumpPack", "MilianAbility_ShortJump");
            secondAbility = Install(second, "MilianFitting_JumpPack", "MilianAbility_ShortJump");
            destination = GenRadial.RadialCellsAround(second.Position, 8, false).First(c => c.InBounds(map) && c.Standable(map) && !c.Fogged(map) &&
                c.DistanceToSquared(second.Position) > 12 && c.DistanceToSquared(second.Position) < c.DistanceToSquared(pawn.Position) &&
                ability.verb.ValidateTarget(c, false) && secondAbility.verb.ValidateTarget(c, false));
        }
        else if (n < 6)
        {
            pawn = PawnGenerator.GeneratePawn(PawnKindDef.Named("Milian_Mechanoid_RookI"), Faction.OfPlayer);
            GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(map.Center, map, 15), map);
            pawn.drafter.Drafted = true;
            ability = Install(pawn, "MilianFitting_EnergyPulseDevice", "MilianAbility_ShieldEnergyPulse");
            shield = pawn.TryGetComp<CompProjectileInterceptor>();
            Check(shield != null && shield.Active, "native Rook shield unavailable");
            shield.currentHitPoints = 400;
        }
        else if (n < 9)
        {
            church = Find.FactionManager.AllFactions.Single(f => f.def.defName == "Milira_AngelismChurch");
            permit = DefDatabase<RoyalTitlePermitDef>.GetNamed("Milira_DropMilianComponentAndUnlockRecipe");
            caller = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
            caller.royalty.SetTitle(church, DefDatabase<RoyalTitleDef>.GetNamed("Milira_ChurchTitleII"), false, false, false);
            caller.royalty.AddPermit(permit, church); caller.royalty.SetFavor(church, 20, false);
            caller.royalty.GetPermit(permit, church).Notify_Used();
            caravan = CaravanMaker.MakeCaravan(new[] { caller }, Faction.OfPlayer, map.Tile, true);
            var food = ThingMaker.MakeThing(ThingDefOf.MealSurvivalPack); food.stackCount = 10; caller.inventory.innerContainer.TryAdd(food);
            beforeAcquire = Acquired;
            pawn = PawnGenerator.GeneratePawn(PawnKindDef.Named("Milian_Mechanoid_BishopII"), Faction.OfPlayer);
            GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(map.Center, map, 10), map);
            pawn.drafter.Drafted = true;
            ability = Install(pawn, "MilianFitting_TransitionDrive", "MilianAbility_TransitionDrive");
            Check(ability.ValidateGlobalTarget((GlobalTargetInfo)(WorldObject)caravan), "caravan world ability target invalid");
        }
        else
        {
            var acquired = Get(gameComp, "milianComponentAcquire");
            var add = AccessTools.Method(acquired.GetType(), "Add");
            foreach (object def in (IEnumerable)Get(gameComp, "milianComponentAll")) add.Invoke(acquired, new[] { def });
            AccessTools.Field(AccessTools.TypeByName("MilianModification.MilianModificationSettings"), "MermeisterStorytellerAvaliable").SetValue(null, false);
            pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer, forceGenerateNewPawn: true,
                validatorPostGear: p => !p.WorkTypeIsDisabled(WorkTypeDefOf.Research) && !p.WorkTagIsDisabled(WorkTags.Intellectual) && p.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)));
            var bench = map.listerThings.AllThings.First(t => t.def.defName == "Milira_ComponentBench");
            GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(bench.Position, map, 4), map);
            chip = (ThingWithComps)GenSpawn.Spawn(ThingMaker.MakeThing(ThingDef.Named("Milian_MermeisterFarewell")), CellFinder.RandomClosewalkCellNear(pawn.Position, map, 3), map);
            lettersBefore = Find.LetterStack.LettersListForReading.Count;
        }
        prepared = true; start = TickPatch.Timer; deadline = Time.realtimeSinceStartup + 180;
        Log.Message("LOW_FREQUENCY PREPARE round=" + round + " pawn=" + pawn.ThingID);
    }

    internal static bool Update()
    {
        if (finished) return true;
        if (failed) return false;
        try
        {
            Check(!Mp.session.desynced && Mp.session.players.Count == 2, "network health");
            if (!prepared) { if (client && !requested) requested = prepare.DoSync(null, 0); return false; }
            Check(Time.realtimeSinceStartup < deadline, "low-frequency timeout round=" + round + " job=" + pawn.CurJob);
            if (client && !sent && TickPatch.Timer - start > 60)
            {
                var before = pawn.CurJob;
                if (round < 3)
                {
                    Current.Game.CurrentMap = pawn.Map; Find.Selector.ClearSelection(); Find.Selector.Select(pawn); Find.Selector.Select(second);
                    var command = Command(ability);
                    if (round > 0) AccessTools.Method(command.GetType(), "GroupAbilityCommands").Invoke(command, new object[] { new List<Gizmo> { command, Command(secondAbility) } });
                    command.ProcessInput(new Event()); Check(Find.Targeter.IsTargeting, "jump targeting did not open");
                    Find.Targeter.StopTargeting(); command.ProcessInput(new Event());
                    var source = Find.Targeter.targetingSourceGetter?.Invoke(destination) ?? Find.Targeter.targetingSource;
                    Check(source == (round == 0 ? ability.verb : secondAbility.verb), "group jump selected wrong caster");
                    source.OrderForceTarget(destination); Find.Targeter.StopTargeting();
                    Check(ReferenceEquals(before, pawn.CurJob) && pawn.Spawned && second.Spawned, "jump local mutation before sync");
                }
                else if (round < 6)
                {
                    Command(ability).ProcessInput(new Event()); Check(ReferenceEquals(before, pawn.CurJob) && effects == 0, "pulse local mutation before sync");
                }
                else if (round < 9)
                {
                    var inventoryBefore = Inventory;
                    var command = permit.Worker.GetCaravanGizmos(caller, church).OfType<Command_Action>().Single();
                    Check(!command.Disabled, "caravan permit disabled: " + command.disabledReason);
                    command.action(); Check(Inventory == inventoryBefore && Acquired.SequenceEqual(beforeAcquire) && caller.royalty.GetFavor(church) == 20, "caravan permit local mutation before sync");
                }
                else
                {
                    var comp = chip.AllComps.Single(c => c.GetType().FullName == "MilianModification.CompMermeisterLetterFarewell");
                    var option = comp.CompFloatMenuOptions(pawn).Single(o => o.action != null); Check(!option.Disabled, "farewell menu disabled");
                    option.action(); Check(ReferenceEquals(before, pawn.CurJob) && !chip.Destroyed && effects == 0, "farewell local mutation before sync");
                }
                sent = true; Log.Message("LOW_FREQUENCY DISPATCH round=" + round + " native command/menu rollback=True");
            }
            if (round < 3)
            {
                var selected = round == 0 ? pawn : second;
                if (!selected.Spawned || selected.Position != destination || selected.abilities.AllAbilitiesForReading.Single(a => a.def == ability.def).CooldownTicksRemaining <= 0) return false;
                effectReceipt = selected.ThingID + ";target=" + destination + ";landed=True;group=" + (round > 0);
            }
            else if (round < 6) { if (effects != 1) return false; }
            else if (round < 9)
            {
                if (Acquired.Except(beforeAcquire).Count() != 1 || caller.royalty.GetFavor(church) != 20 - permit.royalAid.favorCost) return false;
                var unlocked = Acquired.Except(beforeAcquire).Single();
                Check(caravan.PawnsListForReading.SelectMany(p => p.inventory.innerContainer).Count(t => t.def.defName == unlocked && t.stackCount == 1) == 1, "caravan permit inventory result missing");
                if (client && !secondarySent)
                {
                    var command = Command(ability);
                    var callback = command.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic).Single(m => m.Name.StartsWith("<ProcessInput>b__") && m.ReturnType == typeof(bool) && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(GlobalTargetInfo));
                    Check((bool)callback.Invoke(command, new object[] { (GlobalTargetInfo)(WorldObject)caravan }), "world target callback rejected caravan");
                    Check(pawn.Spawned && !caravan.ContainsPawn(pawn) && effects == 0, "world caravan local mutation before sync");
                    secondarySent = true; Log.Message("LOW_FREQUENCY CARAVAN_DISPATCH round=" + round);
                }
                if (effects != 1) return false;
                effectReceipt += ";permit=" + unlocked + ";favor=" + caller.royalty.GetFavor(church) + ";inventory=" + Inventory;
            }
            else
            {
                if (effects != 1 || !chip.Destroyed) return false;
                if (client && !secondarySent)
                {
                    AccessTools.Method(typeof(DiaOption), "Activate", Type.EmptyTypes).Invoke(Node.options[0], null); Check(Find.LetterStack.LettersListForReading.Count == lettersBefore, "farewell choice mutated before sync");
                    secondarySent = true; Log.Message("LOW_FREQUENCY FAREWELL_CHOICE_DISPATCH round=" + round);
                }
                bool unlocked = (bool)AccessTools.Field(AccessTools.TypeByName("MilianModification.MilianModificationSettings"), "MermeisterStorytellerAvaliable").GetValue(null);
                if (!unlocked || Find.LetterStack.LettersListForReading.Count != lettersBefore + 1) return false;
                effectReceipt = chip.ThingID + ";consumed=True;dialog=" + Farewell.id + ";storyteller=True;letterDelta=1";
            }
            string local = Path.Combine(root, Peer + ".lowfrequency" + round), other = Path.Combine(root, (client ? "host" : "client") + ".lowfrequency" + round);
            if (!observed) { observed = true; File.WriteAllText(local + ".tmp", effectReceipt); File.Move(local + ".tmp", local); Log.Message("LOW_FREQUENCY ACTION_PASS round=" + round + " " + effectReceipt); }
            if (!File.Exists(other)) return false;
            Check(File.ReadAllText(local) == File.ReadAllText(other), "low-frequency peer mismatch round=" + round);
            if (round == 11) { finished = true; return true; }
            if (client && !advancing) { advancing = true; prepare.DoSync(null, round + 1); }
        }
        catch (Exception e) { failed = true; Log.Error("COMBINED FAILED LOW_FREQUENCY " + e); }
        return false;
    }
}
