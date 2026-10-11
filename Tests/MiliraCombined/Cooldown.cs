using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client;
using RimWorld;
using UnityEngine;
using Verse;

internal static class CooldownProbe
{
    static readonly bool client = GenCommandLine.CommandLineArgPassed("twclient");
    static bool requested, prepared, opened, advance, finished, failed;
    static int round, start, frames;
    static float deadline;
    static Pawn pawn;
    static Ability ability;
    static Window window;
    static ISyncMethod prepare;
    static Type commandType;
    static readonly System.Reflection.FieldInfo inCooldown = AccessTools.Field(typeof(Ability), "inCooldown");
    static readonly System.Reflection.FieldInfo endTick = AccessTools.Field(typeof(Ability), "cooldownEndTick");
    static string Root => Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
    static string Peer => client ? "client" : "host";
    static void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); }
    internal static void Init()
    {
        commandType = AccessTools.TypeByName("FunnelBit.Command_FunnelBitAbility");
        Check(commandType != null && inCooldown != null && endTick != null, "cooldown probe targets unresolved");
        prepare = MP.RegisterSyncMethod(typeof(CooldownProbe), nameof(Prepare));
    }
    public static void Prepare(int n)
    {
        round = n; frames = 0; opened = advance = false; window?.Close(false); window = null;
        if (pawn == null)
        {
            var map = Find.Maps.First(m => m.IsPlayerHome);
            pawn = PawnGenerator.GeneratePawn(PawnKindDef.Named("Milian_Mechanoid_BishopIII_ArsenalCarrier"), Faction.OfPlayer);
            GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(map.Center, map, 10), map);
            pawn.drafter.Drafted = true;
            ability = pawn.apparel.WornApparel.Where(a => a.AllComps.Any(c => c.GetType().FullName == "FunnelBit.CompTurretGun_FunnelBit"))
                .SelectMany(a => a.AllAbilitiesForReading).First(a => a.def.comps.Any(c => AccessTools.TypeByName("FunnelBit.CompProperties_AbilityLaunchFunnel").IsInstanceOfType(c)));
        }
        ability.StartCooldown(180);
        Check((bool)inCooldown.GetValue(ability), "native StartCooldown did not set cooldown");
        start = TickPatch.Timer; deadline = Time.realtimeSinceStartup + 120; prepared = true;
        Log.Message("COOLDOWN PREPARE " + n + " pawn=" + pawn.ThingID + " ability=" + ability.def.defName);
    }
    sealed class DrawFixture : Window
    {
        public override Vector2 InitialSize => new Vector2(300, 150);
        public override void DoWindowContents(Rect inRect)
        {
            if (Event.current.type != EventType.Repaint || frames >= 3 || failed) return;
            object oldFlag = inCooldown.GetValue(ability), oldEnd = endTick.GetValue(ability);
            try
            {
                // Arrange a stale expired UI snapshot only within this draw, then
                // restore it before returning to simulation on this same thread.
                int expired = Find.TickManager.TicksGame - 1;
                inCooldown.SetValue(ability, true); endTick.SetValue(ability, expired);
                var command = (Gizmo)Activator.CreateInstance(commandType, ability, pawn);
                command.GizmoOnGUI(Vector2.zero, 200, default(GizmoRenderParms));
                Check((bool)inCooldown.GetValue(ability) && (int)endTick.GetValue(ability) == expired, "native Funnel gizmo mutated expired cooldown locally");
                frames++;
            }
            catch (Exception e) { failed = true; Log.Error("COMBINED FAILED COOLDOWN UI " + e); }
            finally { inCooldown.SetValue(ability, oldFlag); endTick.SetValue(ability, oldEnd); }
        }
    }
    internal static bool Update()
    {
        if (finished) return true;
        if (failed) return false;
        try
        {
            if (!prepared) { if (client && !requested) requested = prepare.DoSync(null, 0); return false; }
            Check(Time.realtimeSinceStartup < deadline, "cooldown probe timeout");
            if (client && !opened) { opened = true; window = new DrawFixture(); Find.WindowStack.Add(window); }
            if (TickPatch.Timer - start < 300 || (client && frames < 3)) return false;
            Check(!(bool)inCooldown.GetValue(ability), "native ability cooldown did not finish in simulation");
            string receipt = pawn.ThingID + ":" + ability.def.defName + ":" + inCooldown.GetValue(ability) + ":" + endTick.GetValue(ability);
            string path = Path.Combine(Root, Peer + ".cooldown" + round);
            if (!File.Exists(path)) { File.WriteAllText(path, receipt); Log.Message("COOLDOWN ASSERT " + round + " " + receipt + " UIframes=" + frames); }
            string other = Path.Combine(Root, (client ? "host" : "client") + ".cooldown" + round);
            if (!File.Exists(other)) return false;
            Check(File.ReadAllText(other) == receipt, "cooldown peer mismatch");
            if (round == 2) { window?.Close(false); finished = true; return true; }
            if (client && !advance) { advance = true; prepare.DoSync(null, round + 1); }
        }
        catch (Exception e) { failed = true; Log.Error("COMBINED FAILED COOLDOWN " + e); }
        return false;
    }
}
