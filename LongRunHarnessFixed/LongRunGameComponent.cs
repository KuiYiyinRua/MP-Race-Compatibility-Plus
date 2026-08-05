using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client;
using Multiplayer.Client.Patches;
using Multiplayer.Client.Util;
using Multiplayer.Client.Windows;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Vehicles;
using Verse;
using Verse.AI;

namespace MP_MeowOnlineShop.Testing
{
    /// <summary>
    /// Temporary, command-line-gated runtime driver used only in isolated
    /// BuildValidation save-data folders. It never activates during normal play.
    /// </summary>
    [StaticConstructorOnStartup]
    public sealed class LongRunGameComponent : GameComponent
    {
        private const string Tag = "[MP-AutoTest]";
        private const int DefaultPort = 30502;
        private const int DefaultTicks = 120000;

        private static readonly string Role;
        private static readonly int Port;
        private static readonly int DurationTicks;
        private static readonly int PeerDurationTicks;
        private static readonly bool DiagnosticTraces;
        private static readonly bool CaravanUiTest;
        private static readonly bool CaravanMassUiTest;
        private static readonly bool CompatibilityUiTest;
        private static readonly bool RigorMortisTest;
        private static readonly bool AlertProbe;
        private static readonly bool RjwAddonsTest;
        private static readonly bool TraderIncidentTest;
        private static readonly bool PerspectiveShiftTest;
        private static readonly bool TwoMapViewTest;
        private static readonly bool AsyncTimeTest;
        private static readonly string ReplayFilePath;
        private static readonly string ReadyMarkerPath;
        private static readonly string CaptureSaveName;
        private static readonly bool Enabled;

        private static bool launchStarted;
        private static bool replayPreJoinFreezeSent;
        private static bool hostWaitingForPeerLogged;
        private static bool joinedLogged;
        private static bool speedSent;
        private static bool terminalLogged;
        private static int joinedAtTick = -1;
        private static float joinedAtRealtime = -1f;
        private static int nextProgressTick = -1;
        private static float terminalAtRealtime = -1f;
        private static float launchAtRealtime = -1f;
        private static int caravanUiStage;
        private static float caravanUiStageAtRealtime = -1f;
        private static int firstCaravanTransferableCount = -1;
        private static object caravanUiSession;
        private static object caravanUiMapComp;
        private static Dialog_FormCaravan caravanUiProxy;
        private static bool caravanMassUiInstalled;
        private static int drawCaravanInfoCalls;
        private static bool drawCaravanInfoSawProxy;
        private static string drawCaravanInfoLastSnapshot = string.Empty;
        private static int caravanMassUiStage;
        private static float caravanMassUiStageAtRealtime = -1f;
        private static int caravanMassVehicleTransferableIndex = -1;
        private static string caravanMassVehicleDefName = string.Empty;
        private static bool caravanMassVehicleTabDrawn;
        private static int caravanMassVehicleTabDrawCount;
        private static int caravanMassProxyTabCount;
        private static string caravanMassProxyTabLabels = string.Empty;
        private static Type tradeDateDialogType;
        private static FieldInfo tradeDateField;
        private static PropertyInfo tradeDateComponentProperty;
        private static bool compatibilityUiInitialized;
        private static bool compatibilityUiIssued;
        private static bool compatibilityUiCompleted;
        private static bool insideTradeDateDialog;
        private static bool simulateTradeDateConfirm;
        private static int tradeDateButtonCalls;
        private static int initialTradeDate;
        private static int expectedTradeDate;
        private static int initialTradeDateLetterCount;
        private static float compatibilityUiStartedAt = -1f;
        private const string RigorZombieName = "MP_Rigor_Zombie";
        private const string RigorTargetName = "MP_Rigor_Target";
        private static readonly string[] RigorAbilityDefs =
        {
            "RM_ZombieScream",
            "RM_KarmicObstacle",
            "RM_ClawSkill",
            "RM_SkillMove",
            "RM_CatchSkill"
        };
        private static Pawn rigorZombie;
        private static Pawn rigorTarget;
        private static Building rigorClawTarget;
        private static int rigorStage;
        private static bool rigorActionIssued;
        private static bool rigorStageSawReady;
        private static float rigorStageStartedAt = -1f;
        private static Window rigorStoryWindow;
        private static bool simulateRigorStoryOption;
        private static bool rigorStoryClickIssued;
        private static int rigorStoryExpectedNode;
        private static bool rigorOptionDrawActive;
        private static bool rigorTestCompleted;
        private static bool rigorSetupRequested;
        private static float rigorSetupRequestedAt = -1f;
        private static ISyncMethod rigorSetupSyncMethod;
        private static ISyncMethod rigorCleanupSyncMethod;
        private static ISyncMethod rigorStoryFactionSyncMethod;
        private static bool rigorAwaitingCleanup;
        private static bool rigorStoryFactionRequested;
        private static bool alertProbeCompleted;
        private static bool rjwAddonsActionIssued;
        private static bool rjwAddonsStateLogged;
        private static bool rjwAddonsInitialCaptured;
        private static bool rjwAddonsInitialSealed;
        private static bool rjwAddonsInitialDeflate;
        private static ISyncMethod traderIncidentSyncMethod;
        private static bool twoMapViewApplied;
        private static int traderIncidentDispatchedStage;
        private static int traderIncidentExecutedStage;
        private static int traderIncidentAssertedStage;
        private static int traderIncidentExecutionTick = -1;
        private static string traderIncidentExpectedKind = string.Empty;
        private static int traderIncidentBaselinePawnCount = -1;
        private static int traderIncidentExpectedMapId = -1;
        private static readonly string[] TraderIncidentKinds =
        {
            "RJW_Lewd_Trader_Caravan",
            "Caravan_Outlander_Exotic",
            "Caravan_Outlander_BulkGoods"
        };
        private static ISyncMethod perspectiveShiftSyncMethod;
        private static int perspectiveShiftDispatchedStage;
        private static int perspectiveShiftExecutedStage;
        private static int perspectivePawnAId = -1;
        private static int perspectivePawnBId = -1;
        private static IntVec3 perspectivePawnAStart;
        private static IntVec3 perspectivePawnBStart;
        private static IntVec3 perspectivePawnACheckpoint;
        private static IntVec3 perspectivePawnBCheckpoint;
        private static int perspectiveShiftMovementPasses;
        private static int perspectiveShiftMeleePasses;
        private static int perspectiveShiftRangedPasses;
        private static int perspectiveShiftStoragePasses;
        private static int perspectiveShiftContainerPasses;
        private static readonly bool GravshipSyncTest;
        private static int gravshipStage;
        private static float gravshipStageAtRealtime = -1f;
        private static Building_GravEngine gravshipEngine;
        private static int gravshipPrepareDispatched;
        private static int gravshipPrepareExecuted;
        private static int gravshipLaunchDispatched;
        private static int gravshipLaunchExecuted;
        private static int gravshipOrphanCmdCount;
        private static string gravshipTargetTile = string.Empty;
        private static int gravshipBaselineMapCount = -1;
        private static int gravshipLastMapCount = -1;
        private static ISyncMethod gravshipPrepareSyncMethod;
        private static ISyncMethod gravshipLaunchSyncMethod;
        private static readonly Harmony GravshipDiagnosticsHarmony =
            new Harmony("mp.meowonlineshop.testing.gravship");
        private static readonly Harmony AutoConnectHarmony =
            new Harmony("mp.meowonlineshop.testing.autoconnector");
        private static readonly Harmony FixedQuicktestHarmony =
            new Harmony("mp.meowonlineshop.testing.fixedquicktest");
        private static readonly Harmony PerspectiveShiftHarnessHarmony =
            new Harmony("mp.meowonlineshop.testing.perspectiveshift.inputisolation");

        static LongRunGameComponent()
        {
            string role;
            Enabled = GenCommandLine.TryGetCommandLineArg("mpautotestrole", out role) &&
                      (string.Equals(role, "host", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(role, "client", StringComparison.OrdinalIgnoreCase));
            Role = role == null ? string.Empty : role.ToLowerInvariant();

            string portText;
            Port = GenCommandLine.TryGetCommandLineArg("mpautotestport", out portText) &&
                   int.TryParse(portText, out var parsedPort)
                ? parsedPort
                : DefaultPort;

            string ticksText;
            DurationTicks = GenCommandLine.TryGetCommandLineArg("mpautotestticks", out ticksText) &&
                            int.TryParse(ticksText, out var parsedTicks)
                ? Math.Max(1000, parsedTicks)
                : DefaultTicks;

            string peerTicksText;
            PeerDurationTicks = GenCommandLine.TryGetCommandLineArg(
                                    "mpautotestpeerticks",
                                    out peerTicksText) &&
                                int.TryParse(peerTicksText, out var parsedPeerTicks)
                ? Math.Max(1000, parsedPeerTicks)
                : DurationTicks;

            string tracesText;
            DiagnosticTraces =
                GenCommandLine.TryGetCommandLineArg("mpautotesttraces", out tracesText) &&
                bool.TryParse(tracesText, out var parsedTraces) &&
                parsedTraces;

            string caravanUiText;
            CaravanUiTest =
                GenCommandLine.TryGetCommandLineArg("mpautotestcaravanui", out caravanUiText) &&
                bool.TryParse(caravanUiText, out var parsedCaravanUi) &&
                parsedCaravanUi;

            string caravanMassUiText;
            CaravanMassUiTest =
                GenCommandLine.TryGetCommandLineArg("mpautotestcaravanmassui", out caravanMassUiText) &&
                bool.TryParse(caravanMassUiText, out var parsedCaravanMassUi) &&
                parsedCaravanMassUi;

            string compatibilityUiText;
            CompatibilityUiTest =
                GenCommandLine.TryGetCommandLineArg("mpautotestcompatui", out compatibilityUiText) &&
                bool.TryParse(compatibilityUiText, out var parsedCompatibilityUi) &&
                parsedCompatibilityUi;

            string rigorMortisText;
            RigorMortisTest =
                GenCommandLine.TryGetCommandLineArg("mpautotestrigormortis", out rigorMortisText) &&
                bool.TryParse(rigorMortisText, out var parsedRigorMortis) &&
                parsedRigorMortis;

            if (Enabled)
            {
                string loadSave;
                if (Role == "host" &&
                    GenCommandLine.TryGetCommandLineArg("mpautotestloadsave", out loadSave) &&
                    !string.IsNullOrWhiteSpace(loadSave))
                {
                    if (Current.Game == null)
                        Current.Game = new Game();
                    if (Current.Game.InitData == null)
                        Current.Game.InitData = new GameInitData();
                    Current.Game.InitData.gameToLoad = loadSave;
                    Log.Message($"{Tag} host forced deterministic save load={loadSave}.");
                }

                string alertProbeText;
                AlertProbe =
                    GenCommandLine.TryGetCommandLineArg("mpautotestalertprobe", out alertProbeText) &&
                    bool.TryParse(alertProbeText, out var parsedAlertProbe) &&
                    parsedAlertProbe;

                string rjwAddonsText;
            RjwAddonsTest =
                    GenCommandLine.TryGetCommandLineArg("mpautotestrjwaddons", out rjwAddonsText) &&
                    bool.TryParse(rjwAddonsText, out var parsedRjwAddons) &&
                    parsedRjwAddons;

                string traderIncidentText;
                TraderIncidentTest =
                    GenCommandLine.TryGetCommandLineArg(
                        "mpautotesttraderincidents",
                        out traderIncidentText) &&
                    bool.TryParse(traderIncidentText, out var parsedTraderIncidents) &&
                    parsedTraderIncidents;

                string perspectiveShiftText;
                PerspectiveShiftTest =
                    GenCommandLine.TryGetCommandLineArg(
                        "mpautotestperspectiveshift",
                        out perspectiveShiftText) &&
                    bool.TryParse(perspectiveShiftText, out var parsedPerspectiveShift) &&
                    parsedPerspectiveShift;

                string twoMapViewText;
                TwoMapViewTest =
                    GenCommandLine.TryGetCommandLineArg(
                        "mpautotesttwomapviews",
                        out twoMapViewText) &&
                    bool.TryParse(twoMapViewText, out var parsedTwoMapViews) &&
                    parsedTwoMapViews;

                string asyncTimeText;
                AsyncTimeTest =
                    GenCommandLine.TryGetCommandLineArg(
                        "mpautotestasync",
                        out asyncTimeText) &&
                    bool.TryParse(asyncTimeText, out var parsedAsyncTime) &&
                    parsedAsyncTime;

                string gravshipSyncText;
                GravshipSyncTest =
                    GenCommandLine.TryGetCommandLineArg(
                        "mpautotestgravshipsync",
                        out gravshipSyncText) &&
                    bool.TryParse(gravshipSyncText, out var parsedGravshipSync) &&
                    parsedGravshipSync;

                string replayFile;
                ReplayFilePath =
                    Role == "host" &&
                    GenCommandLine.TryGetCommandLineArg("mpautotestreplay", out replayFile)
                        ? replayFile
                        : string.Empty;

                string readyMarker;
                ReadyMarkerPath =
                    GenCommandLine.TryGetCommandLineArg("mpautotestreadymarker", out readyMarker)
                        ? readyMarker
                        : string.Empty;

                string captureSave;
                CaptureSaveName =
                    Role == "host" &&
                    GenCommandLine.TryGetCommandLineArg("mpautotestcapturesave", out captureSave) &&
                    !string.IsNullOrWhiteSpace(captureSave)
                        ? captureSave
                        : string.Empty;

                string autoConnectText;
                bool autoConnectExactMatch =
                    Role == "client" &&
                    GenCommandLine.TryGetCommandLineArg(
                        "mpautotestautoconnectexactmatch",
                        out autoConnectText) &&
                    bool.TryParse(autoConnectText, out var parsedAutoConnect) &&
                    parsedAutoConnect;
                if (autoConnectExactMatch)
                    InstallExactMatchAutoConnect();

                string fixedQuicktestText;
                bool fixedQuicktest =
                    Role == "host" &&
                    GenCommandLine.TryGetCommandLineArg(
                        "mpautotestfixedquicktest",
                        out fixedQuicktestText) &&
                    bool.TryParse(fixedQuicktestText, out var parsedFixedQuicktest) &&
                    parsedFixedQuicktest;
                if (fixedQuicktest)
                    InstallFixedQuicktestSeed();

                Log.Message($"{Tag} enabled role={Role} port={Port} durationTicks={DurationTicks} " +
                            $"diagnosticTraces={DiagnosticTraces} caravanUiTest={CaravanUiTest} " +
                            $"caravanMassUiTest={CaravanMassUiTest} " +
                            $"compatibilityUiTest={CompatibilityUiTest} rigorMortisTest={RigorMortisTest} " +
                            $"alertProbe={AlertProbe} rjwAddonsTest={RjwAddonsTest} " +
                            $"traderIncidentTest={TraderIncidentTest} twoMapViewTest={TwoMapViewTest} " +
                            $"perspectiveShiftTest={PerspectiveShiftTest} " +
                            $"gravshipSyncTest={GravshipSyncTest} " +
                            $"replay={ReplayFilePath} " +
                            $"captureSave={CaptureSaveName} " +
                            $"saveData={GenFilePaths.SaveDataFolderPath}");

                if (CompatibilityUiTest)
                    InstallCompatibilityUiDriver();
                if (CaravanMassUiTest)
                    InstallCaravanMassUiDriver();
                if (RigorMortisTest)
                {
                    InstallRigorMortisUiDriver();
                    rigorSetupSyncMethod = MP.RegisterSyncMethod(
                        typeof(LongRunGameComponent),
                        nameof(SyncPrepareRigorMortisActors));
                    rigorCleanupSyncMethod = MP.RegisterSyncMethod(
                        typeof(LongRunGameComponent),
                        nameof(SyncFinishRigorAbility));
                    rigorStoryFactionSyncMethod = MP.RegisterSyncMethod(
                        typeof(LongRunGameComponent),
                        nameof(SyncPrepareRigorStoryActor));
                }
                if (TraderIncidentTest)
                {
                    traderIncidentSyncMethod = MP.RegisterSyncMethod(
                            typeof(LongRunGameComponent),
                            nameof(SyncTriggerTraderCaravan))
                        .CancelIfAnyArgNull()
                        .SetContext(SyncContext.CurrentMap);
                }
                if (PerspectiveShiftTest)
                {
                    InstallPerspectiveShiftInputIsolation();
                    perspectiveShiftSyncMethod = MP.RegisterSyncMethod(
                        typeof(LongRunGameComponent),
                        nameof(SyncPerspectiveShiftStage))
                        .SetContext(SyncContext.CurrentMap);
                }
                if (GravshipSyncTest)
                {
                    InstallGravshipDiagnostics();
                    gravshipPrepareSyncMethod = MP.RegisterSyncMethod(
                        typeof(LongRunGameComponent),
                        nameof(SyncGravshipPrepare));
                    gravshipLaunchSyncMethod = MP.RegisterSyncMethod(
                        typeof(LongRunGameComponent),
                        nameof(SyncGravshipLaunch));
                }

                // Let Multiplayer's built-in AutoJoinHandler perform client
                // connections from the entry root. Connecting while a local
                // single-player game is still ticking makes TickPatch send
                // Client_FrameTime while the server is in ServerJoining.
                string autoJoinTarget;
                if (Role == "client" &&
                    GenCommandLine.TryGetCommandLineArg("connect", out autoJoinTarget))
                {
                    launchStarted = true;
                    launchAtRealtime = Time.realtimeSinceStartup;
                    Log.Message(
                        $"{Tag} client connection delegated to Multiplayer AutoJoinHandler " +
                        $"target={autoJoinTarget}");
                }

                if (Role == "host" && !string.IsNullOrWhiteSpace(ReplayFilePath))
                {
                    launchStarted = true;
                    launchAtRealtime = Time.realtimeSinceStartup;
                    LongEventHandler.ExecuteWhenFinished(LoadReplayAndStartHost);
                }
            }
        }

        private static void LoadReplayAndStartHost()
        {
            try
            {
                var replayFile = new FileInfo(ReplayFilePath);
                if (!replayFile.Exists)
                {
                    Fail("replay file does not exist: " + ReplayFilePath);
                    return;
                }

                Log.Message(
                    $"{Tag} loading replay source={replayFile.FullName} bytes={replayFile.Length}");
                Replay.LoadReplay(
                    replayFile,
                    true,
                    () =>
                    {
                        Log.Message(
                            $"{Tag} replay reached final section tick={TickPatch.Timer}; hosting loaded snapshot.");
                        StartHost();
                    },
                    () => Fail("replay loading was cancelled"),
                    "MpSimulatingServer",
                    false);
            }
            catch (Exception exception)
            {
                Fail("replay load failed: " + exception);
            }
        }

        public LongRunGameComponent(Game game)
        {
        }

        public override void GameComponentUpdate()
        {
            if (!Enabled || LongEventHandler.AnyEventNowOrWaiting)
                return;

            try
            {
                if (!launchStarted && Current.ProgramState == ProgramState.Playing)
                {
                    launchStarted = true;
                    launchAtRealtime = Time.realtimeSinceStartup;
                    if (Role == "host")
                        StartHost();
                    else
                        StartClient();
                }

                ObserveSession();
            }
            catch (Exception e)
            {
                Fail("unhandled harness exception: " + e);
            }
        }

        private static void StartHost()
        {
            if (TwoMapViewTest && Find.Maps.Count < 2)
            {
                LongEventHandler.QueueLongEvent(
                    () =>
                    {
                        GenerateDeterministicSecondMap();
                        LongEventHandler.ExecuteWhenFinished(StartHost);
                    },
                    "GeneratingMap",
                    false,
                    exception => Fail("second-map generation failed: " + exception));
                return;
            }

            if (!string.IsNullOrEmpty(CaptureSaveName))
            {
                GameDataSaveLoader.SaveGame(CaptureSaveName);
                Log.Message(
                    $"{Tag} host captured deterministic source save={CaptureSaveName} " +
                    $"worldSeed={Find.World?.info?.seedString ?? "<null>"}.");
            }

            if (CaravanMassUiTest)
                SpawnCaravanTestVehicle();

            Multiplayer.Client.Multiplayer.username = "MPAutoHost";
            var settings = new ServerSettings
            {
                gameName = "MP Meow Long Run",
                direct = true,
                directAddress = $"127.0.0.1:{Port}",
                lan = false,
                steam = false,
                arbiter = false,
                asyncTime = AsyncTimeTest,
                multifaction = false,
                debugMode = false,
                desyncTraces = DiagnosticTraces,
                syncConfigs = true,
                // HostUtil has already produced an exact snapshot while
                // converting a replay into a live server. Creating another
                // join point before the first remote client can download that
                // snapshot can leave the replay-hosted game paused at its
                // final tick. Reuse the immutable initial snapshot for this
                // first join; retain automatic join-point creation on desync.
                autoJoinPoint = string.IsNullOrWhiteSpace(ReplayFilePath)
                    ? AutoJoinPointFlags.Join | AutoJoinPointFlags.Desync
                    : AutoJoinPointFlags.Desync,
                pauseOnJoin = false,
                pauseOnDesync = true,
                pauseOnLetter = PauseOnLetter.Never,
                timeControl = TimeControl.HostOnly
            };

            bool replayHost = !string.IsNullOrWhiteSpace(ReplayFilePath);
            bool started = replayHost
                ? HostLoadedReplaySnapshot(settings)
                : HostWindow.HostProgrammatically(settings);
            Log.Message(
                $"{Tag} host launch requested started={started} port={Port} " +
                $"fromReplay={replayHost}");
            if (!started)
                Fail(replayHost
                    ? "Multiplayer replay host path returned false"
                    : "HostProgrammatically returned false");
        }

        private static void GenerateDeterministicSecondMap()
        {
            if (Find.Maps.Count >= 2)
                return;

            Map source = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
            if (source == null)
                throw new InvalidOperationException("No source map is available.");

            var queue = new Queue<Tuple<int, int>>();
            var visited = new HashSet<int>();
            int sourceTile = source.Tile;
            queue.Enqueue(Tuple.Create(sourceTile, 0));
            visited.Add(sourceTile);
            int selectedTile = -1;
            while (queue.Count > 0 && selectedTile < 0)
            {
                Tuple<int, int> current = queue.Dequeue();
                var neighbors = new List<PlanetTile>();
                Find.WorldGrid.GetTileNeighbors(current.Item1, neighbors);
                foreach (int neighbor in neighbors.OrderBy(tile => (int)tile))
                {
                    if (!visited.Add(neighbor))
                        continue;
                    int distance = current.Item2 + 1;
                    if (distance >= 5 &&
                        !Find.WorldGrid[neighbor].WaterCovered &&
                        Find.WorldGrid[neighbor].hilliness != Hilliness.Impassable &&
                        !Find.WorldObjects.AnyWorldObjectAt(neighbor))
                    {
                        selectedTile = neighbor;
                        break;
                    }
                    if (distance < 9)
                        queue.Enqueue(Tuple.Create(neighbor, distance));
                }
            }

            if (selectedTile < 0)
                throw new InvalidOperationException("No deterministic second-map tile was found.");

            MapParent parent = SettleUtility.AddNewHome(selectedTile, Faction.OfPlayer);
            Map generated = GetOrGenerateMapUtility.GetOrGenerateMap(
                selectedTile,
                new IntVec3(80, 1, 80),
                parent.def);
            if (generated == null)
                throw new InvalidOperationException("GetOrGenerateMap returned null.");
            Current.Game.CurrentMap = source;
            Log.Message(
                $"{Tag} TWO_MAP_PREPARED sourceMap={source.uniqueID} " +
                $"secondMap={generated.uniqueID} tile={selectedTile} " +
                $"allMapIds={string.Join(",", Find.Maps.OrderBy(map => map.uniqueID).Select(map => map.uniqueID))}");
        }

        private static bool HostLoadedReplaySnapshot(ServerSettings settings)
        {
            // HostWindow.HostProgrammatically always calls HostUtil.HostServer
            // with fromReplay=false. That creates a fresh server clock even if
            // the current game is the final snapshot of a replay. Use the same
            // private path as HostWindow.HostFromReplay so Multiplayer itself
            // initializes gameTimer/startingTimer from TickPatch.Timer before
            // any client receives join data.
            MethodInfo tryStart = AccessTools.Method(
                typeof(HostWindow),
                "TryStartLocalServer",
                new[] { typeof(ServerSettings) });
            MethodInfo hostFromReplay = AccessTools.Method(
                typeof(HostWindow),
                "HostFromReplay",
                new[] { typeof(ServerSettings) });
            if (tryStart == null || hostFromReplay == null)
            {
                Log.Error(
                    $"{Tag} replay host path unavailable " +
                    $"tryStart={tryStart != null} hostFromReplay={hostFromReplay != null}");
                return false;
            }

            var window = new HostWindow();
            bool started = (bool)tryStart.Invoke(null, new object[] { settings });
            if (!started)
                return false;

            hostFromReplay.Invoke(window, new object[] { settings });
            return true;
        }

        private static void StartClient()
        {
            Multiplayer.Client.Multiplayer.username = "MPAutoClient";
            ClientUtil.TryConnectWithWindow(ConnectorRegistry.LiteNet("127.0.0.1", Port), false);
            Log.Message($"{Tag} client connection requested endpoint=127.0.0.1:{Port}");
        }

        private static void ObserveSession()
        {
            var session = Multiplayer.Client.Multiplayer.session;
            var client = Multiplayer.Client.Multiplayer.Client;

            if (terminalLogged)
            {
                // DesyncedWindow waits up to five seconds for host traces, then
                // writes metadata and the zip asynchronously. Keep both peers
                // alive long enough for that first diagnostic bundle to finish.
                float exitDelay = DiagnosticTraces ? 20f : 2f;
                if (terminalAtRealtime >= 0f &&
                    Time.realtimeSinceStartup - terminalAtRealtime >= exitDelay)
                    Application.Quit();
                return;
            }

            if (joinedLogged &&
                (session == null || client == null ||
                 client.State != ConnectionStateEnum.ClientPlaying ||
                 Current.ProgramState != ProgramState.Playing))
            {
                // The host stops scheduling shared ticks as soon as it reaches
                // the same duration limit. The client can observe the socket
                // closing a handful of ticks before its locally sampled join
                // offset reaches that limit. Treat only this narrow terminal
                // window as a clean completion; earlier disconnects still fail.
                int elapsed = TickPatch.Timer - joinedAtTick;
                // A force-pausing local UI action can make the client's sampled
                // timer trail the host by several hundred ticks even though the
                // shared simulation is healthy.
                int terminalTarget = Role == "host" ? PeerDurationTicks : DurationTicks;
                int terminalGrace = Math.Min(1000, terminalTarget / 3);
                if (elapsed >= terminalTarget - terminalGrace)
                {
                    terminalLogged = true;
                    terminalAtRealtime = Time.realtimeSinceStartup;
                    Log.Message($"{Tag} COMPLETE role={Role} desynced=False tick={TickPatch.Timer} " +
                                $"elapsedTicks={elapsed} peerClosedAtTerminal=True maps={Find.Maps.Count}");
                    return;
                }

                Fail($"session lost after JOINED state={client?.State.ToString() ?? "<null>"}");
                return;
            }

            if (session == null || client == null ||
                client.State != ConnectionStateEnum.ClientPlaying ||
                Current.ProgramState != ProgramState.Playing)
            {
                if (launchStarted && launchAtRealtime >= 0f &&
                    Time.realtimeSinceStartup - launchAtRealtime >= 900f)
                    Fail("did not reach ClientPlaying within 900 seconds");
                return;
            }

            if (session.desynced)
            {
                Fail($"desynced=True tick={TickPatch.Timer} players={session.players.Count}");
                return;
            }

            if (joinedLogged && session.players.Count < 2)
            {
                int elapsed = TickPatch.Timer - joinedAtTick;
                int terminalTarget = Role == "host" ? PeerDurationTicks : DurationTicks;
                int terminalGrace = Math.Min(1000, terminalTarget / 3);
                if (elapsed >= terminalTarget - terminalGrace)
                {
                    terminalLogged = true;
                    terminalAtRealtime = Time.realtimeSinceStartup;
                    Log.Message($"{Tag} COMPLETE role={Role} desynced=False tick={TickPatch.Timer} " +
                                $"elapsedTicks={elapsed} peerClosedAtTerminal=True maps={Find.Maps.Count}");
                    return;
                }

                Fail($"player disconnected before completion tick={TickPatch.Timer} players={session.players.Count}");
                return;
            }

            if (session.players.Count < 2)
            {
                if (Role == "host" && !replayPreJoinFreezeSent)
                {
                    replayPreJoinFreezeSent = true;
                    client.Send(
                        Multiplayer.Common.Networking.Packet.ClientFreezePacket.Freeze());
                    Log.Message(
                        $"{Tag} replay host requested pre-join freeze " +
                        $"tick={TickPatch.Timer}; waiting for remote peer.");
                    LogRandomStates("pre-join-freeze");
                }
                return;
            }

            int tick = TickPatch.Timer;
            if (!joinedLogged)
            {
                joinedLogged = true;
                joinedAtTick = tick;
                joinedAtRealtime = Time.realtimeSinceStartup;
                nextProgressTick = tick + GetProgressIntervalTicks();
                Log.Message($"{Tag} JOINED role={Role} tick={tick} players={session.players.Count} " +
                            $"maps={Find.Maps.Count} desynced={session.desynced} " +
                            $"worldSeed={Find.World?.info?.seedString ?? "<null>"}");
                LogRandomStates("joined-before-unfreeze");

                if (Role == "client" && !string.IsNullOrWhiteSpace(ReadyMarkerPath))
                {
                    File.WriteAllText(
                        ReadyMarkerPath,
                        $"role={Role} tick={tick} maps={Find.Maps.Count}");
                    Log.Message(
                        $"{Tag} CLIENT_LOADED_FROZEN marker={ReadyMarkerPath} " +
                        $"tick={tick} maps={Find.Maps.Count}");
                }
            }

            if (AlertProbe && !alertProbeCompleted)
                RunAlertProbe();

            if (Role == "host" && !speedSent)
            {
                if (!string.IsNullOrWhiteSpace(ReadyMarkerPath) &&
                    !File.Exists(ReadyMarkerPath))
                    return;

                speedSent = true;
                ApplyTwoMapViewDivergence();
                // Replay hosting performs an MpSaving long event before the
                // local server starts. Its freeze marker can outlive that
                // transition, leaving the new server at the replay's final
                // tick even after the remote client is ready. Use MP's normal
                // host unfreeze packet; the server remains authoritative and
                // broadcasts the resulting freeze state to both peers.
                client.Send(
                    Multiplayer.Common.Networking.Packet.ClientFreezePacket.Unfreeze());
                TimeSpeed requestedSpeed = TwoMapViewTest && AsyncTimeTest
                    ? TimeSpeed.Normal
                    : TimeSpeed.Ultrafast;
                client.SendCommand(
                    CommandType.GlobalTimeSpeed,
                    ScheduledCommand.Global,
                    (byte)requestedSpeed);
                if (AsyncTimeTest)
                {
                    foreach (Map map in Find.Maps.OrderBy(candidate => candidate.uniqueID))
                    {
                        client.SendCommand(
                            CommandType.MapTimeSpeed,
                            map.uniqueID,
                            (byte)requestedSpeed);
                    }
                }
                var localServer = Multiplayer.Client.Multiplayer.LocalServer;
                Log.Message(
                    $"{Tag} host requested unfreeze and global speed={requestedSpeed} " +
                    $"mapSpeeds={(AsyncTimeTest ? string.Join(",", Find.Maps.OrderBy(map => map.uniqueID).Select(map => map.uniqueID + ":" + requestedSpeed)) : "n/a")} " +
                    $"tick={tick} tickUntil={TickPatch.tickUntil} workTicks={TickPatch.workTicks} " +
                    $"localServerFrozen={TickPatch.serverFrozen} " +
                    $"serverTimer={localServer?.gameTimer.ToString() ?? "<null>"} " +
                    $"serverFrozen={localServer?.freezeManager.Frozen.ToString() ?? "<null>"} " +
                    $"serverWorkTicks={localServer?.workTicks.ToString() ?? "<null>"}");
            }

            if (Role == "client" && !twoMapViewApplied)
                ApplyTwoMapViewDivergence();

            if (CaravanMassUiTest && Role == "client" && !RunCaravanMassUiTest())
                return;

            if (CaravanUiTest && Role == "client" && !RunCaravanUiTest())
                return;

            if (CompatibilityUiTest && !RunCompatibilityUiTest())
                return;

            if (RigorMortisTest && !RunRigorMortisTest())
                return;

            if (RjwAddonsTest && !RunRjwAddonsTest(tick - joinedAtTick))
                return;

            if (TraderIncidentTest && !RunTraderIncidentTest(tick - joinedAtTick))
                return;

            if (PerspectiveShiftTest && !RunPerspectiveShiftTest(tick - joinedAtTick))
                return;

            if (GravshipSyncTest && Role == "client" &&
                !RunGravshipSyncTest(tick - joinedAtTick))
                return;

            if (tick >= nextProgressTick)
            {
                nextProgressTick += GetProgressIntervalTicks();
                Log.Message($"{Tag} PROGRESS role={Role} tick={tick} elapsedTicks={tick - joinedAtTick} " +
                            $"players={session.players.Count} desynced={session.desynced}");
            }

            if (tick - joinedAtTick >= DurationTicks)
            {
                // The host can advance ahead of a joining client while that
                // client is still applying packets. Do not terminate the
                // shared session merely because the host reached its own
                // safety budget; let the client reach its target and close
                // first. The disconnect handling above then completes the host.
                if (Role == "host" && session.players.Count >= 2)
                {
                    if (!hostWaitingForPeerLogged)
                    {
                        hostWaitingForPeerLogged = true;
                        Log.Message($"{Tag} HOST_WAITING_FOR_PEER tick={tick} " +
                                    $"elapsedTicks={tick - joinedAtTick} peerTarget={PeerDurationTicks}");
                    }
                    return;
                }

                terminalLogged = true;
                terminalAtRealtime = Time.realtimeSinceStartup;
                float elapsedRealtime = Math.Max(
                    0.001f,
                    Time.realtimeSinceStartup - joinedAtRealtime);
                float measuredTps = (tick - joinedAtTick) / elapsedRealtime;
                Log.Message($"{Tag} COMPLETE role={Role} desynced=False tick={tick} " +
                            $"elapsedTicks={tick - joinedAtTick} realtimeSeconds={elapsedRealtime:F3} " +
                            $"measuredTps={measuredTps:F2} players={session.players.Count} maps={Find.Maps.Count}");
                LogAlertDiagnostics();
            }
        }

        private static void InstallPerspectiveShiftInputIsolation()
        {
            Type patchType = AccessTools.TypeByName("MP_MeowOnlineShop.Patch_PerspectiveShiftMp");
            MethodInfo target = AccessTools.Method(patchType, "UpdatePhysicsPrefix");
            MethodInfo prefix = AccessTools.Method(
                typeof(LongRunGameComponent),
                nameof(SuppressPerspectiveShiftLocalInputPrefix));
            if (target == null || prefix == null)
            {
                Log.Error($"{Tag} PERSPECTIVE_SHIFT could not isolate hidden-window local input");
                return;
            }

            PerspectiveShiftHarnessHarmony.Patch(
                target,
                prefix: new HarmonyMethod(prefix) { priority = Priority.First });
            Log.Message(
                $"{Tag} PERSPECTIVE_SHIFT hidden-window local input isolated; " +
                "synchronized movement intents remain authoritative for the runtime test.");
        }

        private static bool SuppressPerspectiveShiftLocalInputPrefix(ref bool __result)
        {
            __result = false;
            return false;
        }

        private static bool RunPerspectiveShiftTest(int elapsedTicks)
        {
            if (perspectiveShiftExecutedStage >= 15)
                return true;

            if (perspectiveShiftExecutedStage >= 14)
            {
                Pawn pawnA = Find.Maps
                    .SelectMany(map => map.mapPawns.AllPawnsSpawned)
                    .FirstOrDefault(p => p.thingIDNumber == perspectivePawnAId);
                Pawn pawnB = Find.Maps
                    .SelectMany(map => map.mapPawns.AllPawnsSpawned)
                    .FirstOrDefault(p => p.thingIDNumber == perspectivePawnBId);
                if (pawnA == null || pawnB == null)
                {
                    Fail("PERSPECTIVE_SHIFT assertion pawns missing");
                    return false;
                }

                if (perspectiveShiftMovementPasses < 3)
                {
                    Fail(
                        $"PERSPECTIVE_SHIFT movement passes missing passes={perspectiveShiftMovementPasses} " +
                        $"a={perspectivePawnAStart}->{pawnA.Position} b={perspectivePawnBStart}->{pawnB.Position}");
                    return false;
                }

                if (perspectiveShiftMeleePasses < 3 ||
                    perspectiveShiftRangedPasses < 3 ||
                    perspectiveShiftStoragePasses < 3 ||
                    perspectiveShiftContainerPasses < 3)
                {
                    Fail(
                        "PERSPECTIVE_SHIFT specialized actions missing " +
                        $"melee={perspectiveShiftMeleePasses}/3 " +
                        $"ranged={perspectiveShiftRangedPasses}/3 " +
                        $"storage={perspectiveShiftStoragePasses}/3 " +
                        $"container={perspectiveShiftContainerPasses}/3");
                    return false;
                }

                if (perspectiveShiftExecutedStage == 14)
                {
                    perspectiveShiftExecutedStage = 15;
                    Log.Message(
                        $"{Tag} PERSPECTIVE_SHIFT_ASSERT role={Role} " +
                        $"pawnA={pawnA.thingIDNumber}:{perspectivePawnAStart}->{pawnA.Position} " +
                        $"pawnB={pawnB.thingIDNumber}:{perspectivePawnBStart}->{pawnB.Position} " +
                        $"movementPasses={perspectiveShiftMovementPasses} " +
                        $"meleePasses={perspectiveShiftMeleePasses} " +
                        $"rangedPasses={perspectiveShiftRangedPasses} " +
                        $"storagePasses={perspectiveShiftStoragePasses} " +
                        $"containerPasses={perspectiveShiftContainerPasses} PASS");
                }
                return true;
            }

            if (Role != "client" || perspectiveShiftSyncMethod == null)
                return false;

            int nextStage = perspectiveShiftDispatchedStage + 1;
            int[] dispatchTicks =
            {
                0,
                250,
                500,
                1000,
                1300,
                1800,
                2100,
                2600,
                3100,
                3500,
                3900,
                4500,
                5000,
                5500,
                6200
            };
            if (nextStage <= 0 || nextStage >= dispatchTicks.Length)
                return false;
            int dispatchAt = dispatchTicks[nextStage];
            if (elapsedTicks < dispatchAt)
                return false;

            perspectiveShiftDispatchedStage = nextStage;
            perspectiveShiftSyncMethod.DoSync(null, nextStage);
            Log.Message(
                $"{Tag} PERSPECTIVE_SHIFT_DISPATCH role={Role} stage={nextStage} tick={TickPatch.Timer}");
            return false;
        }

        public static void SyncPerspectiveShiftStage(int stage)
        {
            Type patchType = AccessTools.TypeByName("MP_MeowOnlineShop.Patch_PerspectiveShiftMp");
            Type perspectiveStateType = AccessTools.TypeByName("PerspectiveShift.State");
            Type perspectiveAvatarType = AccessTools.TypeByName("PerspectiveShift.Avatar");
            PropertyInfo activeProperty = patchType == null
                ? null
                : AccessTools.Property(patchType, "Active");
            MethodInfo claim = AccessTools.Method(patchType, "SyncClaimAvatar");
            MethodInfo move = AccessTools.Method(patchType, "SyncSetMoveIntent");
            MethodInfo mapClick = AccessTools.Method(patchType, "SyncAvatarMapClick");
            bool compatibilityActive = activeProperty != null &&
                                       (bool)activeProperty.GetValue(null, null);
            if (patchType == null || perspectiveStateType == null || perspectiveAvatarType == null ||
                !compatibilityActive || claim == null || move == null || mapClick == null)
            {
                Fail(
                    "PERSPECTIVE_SHIFT precondition failed " +
                    $"stateType={perspectiveStateType != null} avatarType={perspectiveAvatarType != null} " +
                    $"compatibilityActive={compatibilityActive} executors={claim != null}/{move != null}/{mapClick != null}");
                return;
            }

            List<Map> maps = Find.Maps.OrderBy(candidate => candidate.uniqueID).ToList();
            List<Pawn> pawns = maps
                .Select(map => map.mapPawns.FreeColonistsSpawned
                    .Where(pawn => pawn != null && !pawn.Dead)
                    .OrderBy(pawn => pawn.thingIDNumber)
                    .FirstOrDefault())
                .Where(pawn => pawn != null)
                .Take(2)
                .ToList();
            if (pawns.Count < 2)
            {
                pawns = maps
                    .SelectMany(map => map.mapPawns.FreeColonistsSpawned)
                    .Where(pawn => pawn != null && !pawn.Dead)
                    .OrderBy(pawn => pawn.thingIDNumber)
                    .Take(2)
                    .ToList();
            }
            if (pawns == null || pawns.Count < 2)
            {
                Fail($"PERSPECTIVE_SHIFT needs two colonists; actual={pawns?.Count ?? 0}");
                return;
            }
            if (TwoMapViewTest && maps.Count >= 2 && pawns[0].Map == pawns[1].Map)
            {
                Fail("PERSPECTIVE_SHIFT two-map test needs one controlled colonist on each map");
                return;
            }

            Pawn pawnA = pawns[0];
            Pawn pawnB = pawns[1];
            var players = MP.GetPlayers().OrderBy(player => player.Id).Take(2).ToList();
            if (players.Count < 2)
            {
                Fail($"PERSPECTIVE_SHIFT needs two player identities; actual={players.Count}");
                return;
            }
            string ownerA = players[0].Username;
            string ownerB = players[1].Username;
            if (stage == 1)
            {
                perspectivePawnAId = pawnA.thingIDNumber;
                perspectivePawnBId = pawnB.thingIDNumber;
                perspectivePawnAStart = pawnA.Position;
                perspectivePawnBStart = pawnB.Position;
                perspectivePawnACheckpoint = pawnA.Position;
                perspectivePawnBCheckpoint = pawnB.Position;
                perspectiveShiftMovementPasses = 0;
                perspectiveShiftMeleePasses = 0;
                perspectiveShiftRangedPasses = 0;
                perspectiveShiftStoragePasses = 0;
                perspectiveShiftContainerPasses = 0;
                claim.Invoke(null, new object[] { ownerA, pawnA });
                claim.Invoke(null, new object[] { ownerB, pawnB });
            }
            else if (stage == 2 || stage == 4 || stage == 6)
            {
                int directionSalt = stage == 2 ? 0 : stage == 4 ? 1 : 2;
                IntVec3 directionA = FindClearPerspectiveMoveDirection(
                    pawnA,
                    pawnB,
                    directionSalt);
                IntVec3 directionB = FindClearPerspectiveMoveDirection(
                    pawnB,
                    pawnA,
                    directionSalt + 2);
                if (!directionA.IsValid || !directionB.IsValid)
                {
                    Fail(
                        $"PERSPECTIVE_SHIFT clear movement corridor unavailable stage={stage} " +
                        $"pawnA={pawnA.thingIDNumber} directionA={directionA} " +
                        $"pawnB={pawnB.thingIDNumber} directionB={directionB}");
                    return;
                }
                move.Invoke(
                    null,
                    new object[] { ownerA, pawnA, 1, directionA.x, directionA.z, false, false });
                move.Invoke(
                    null,
                    new object[] { ownerB, pawnB, 1, directionB.x, directionB.z, true, false });
            }
            else if (stage == 3 || stage == 5 || stage == 7)
            {
                move.Invoke(null, new object[] { ownerA, pawnA, 1, 0, 0, false, false });
                move.Invoke(null, new object[] { ownerB, pawnB, 1, 0, 0, false, false });
                bool movedA = pawnA.Position != perspectivePawnACheckpoint;
                bool movedB = pawnB.Position != perspectivePawnBCheckpoint;
                if (!movedA || !movedB)
                {
                    Fail(
                        $"PERSPECTIVE_SHIFT cycle movement missing stage={stage} " +
                        $"movedA={movedA} movedB={movedB}");
                    return;
                }
                perspectivePawnACheckpoint = pawnA.Position;
                perspectivePawnBCheckpoint = pawnB.Position;
                perspectiveShiftMovementPasses++;
            }
            else if (stage >= 8 && stage <= 10)
            {
                RunPerspectiveCombatPass(ownerA, pawnA, mapClick, melee: true, stage);
                RunPerspectiveCombatPass(ownerB, pawnB, mapClick, melee: true, stage);
            }
            else if (stage >= 11 && stage <= 13)
            {
                RunPerspectiveCombatPass(ownerA, pawnA, mapClick, melee: false, stage);
                RunPerspectiveCombatPass(ownerB, pawnB, mapClick, melee: false, stage);
            }
            else if (stage == 14)
            {
                RunPerspectiveContainerPasses(ownerA, pawnA, mapClick);
                RunPerspectiveContainerPasses(ownerB, pawnB, mapClick);
            }
            else
            {
                Fail("PERSPECTIVE_SHIFT invalid stage=" + stage);
                return;
            }

            perspectiveShiftExecutedStage = stage;
            Log.Message(
                $"{Tag} PERSPECTIVE_SHIFT_EXECUTED role={Role} stage={stage} " +
                $"ownerA={ownerA} pawnA={pawnA.thingIDNumber}@{pawnA.Position} " +
                $"ownerB={ownerB} pawnB={pawnB.thingIDNumber}@{pawnB.Position}");
        }

        private static void RunPerspectiveCombatPass(
            string owner,
            Pawn pawn,
            MethodInfo mapClick,
            bool melee,
            int stage)
        {
            if (pawn?.Map == null || pawn.Dead)
            {
                Fail($"PERSPECTIVE_SHIFT combat pawn unavailable stage={stage}");
                return;
            }

            pawn.drafter.Drafted = true;
            pawn.jobs?.EndCurrentJob(JobCondition.InterruptForced);
            pawn.pather?.StopDead();
            pawn.stances.SetStance(new Stance_Mobile());

            int minDistance = melee ? 1 : 6;
            int maxDistance = melee ? 1 : 10;
            IntVec3 targetCell = FindPerspectiveTestCell(pawn, minDistance, maxDistance);
            if (!targetCell.IsValid)
            {
                Fail(
                    $"PERSPECTIVE_SHIFT {(melee ? "melee" : "ranged")} target cell unavailable " +
                    $"pawn={pawn.thingIDNumber} map={pawn.Map.uniqueID} stage={stage}");
                return;
            }

            if (!melee)
                EnsurePerspectiveRangedWeapon(pawn);

            ThingDef targetDef = ThingDefOf.Wall;
            Thing target = ThingMaker.MakeThing(targetDef, GenStuff.DefaultStuffFor(targetDef));
            GenSpawn.Spawn(target, targetCell, pawn.Map, WipeMode.Vanish);
            int hitPointsBefore = target.HitPoints;

            mapClick.Invoke(
                null,
                new object[] { owner, pawn, 1, targetCell, 0, 1 });

            bool stanceStarted = pawn.stances.curStance is Stance_Busy;
            bool damaged = target.Destroyed || target.HitPoints < hitPointsBefore;
            if (!stanceStarted && !damaged)
            {
                Fail(
                    $"PERSPECTIVE_SHIFT {(melee ? "melee" : "ranged")} did not execute " +
                    $"pawn={pawn.thingIDNumber} map={pawn.Map.uniqueID} stage={stage} " +
                    $"target={target.thingIDNumber} hp={hitPointsBefore}->{target.HitPoints} " +
                    $"stance={pawn.stances.curStance?.GetType().Name ?? "<null>"}");
                return;
            }

            if (melee)
                perspectiveShiftMeleePasses++;
            else
                perspectiveShiftRangedPasses++;

            Log.Message(
                $"{Tag} PERSPECTIVE_SHIFT_COMBAT_ASSERT role={Role} " +
                $"kind={(melee ? "melee" : "ranged")} stage={stage} " +
                $"owner={owner} pawn={pawn.thingIDNumber} map={pawn.Map.uniqueID} " +
                $"target={target.thingIDNumber} hp={hitPointsBefore}->{target.HitPoints} " +
                $"stanceStarted={stanceStarted} PASS");

            // Melee resolves immediately. Remove its adjacent fixture so the
            // later real storage/container actions still have valid grab-range
            // cells. Ranged fixtures must remain until their queued shot resolves.
            if (melee && !target.Destroyed)
                target.Destroy(DestroyMode.Vanish);
        }

        private static void EnsurePerspectiveRangedWeapon(Pawn pawn)
        {
            Verb current = pawn.equipment?.PrimaryEq?.PrimaryVerb;
            if (current != null && !current.verbProps.IsMeleeAttack)
                return;

            if (pawn.equipment == null)
            {
                Fail($"PERSPECTIVE_SHIFT ranged pawn has no equipment tracker pawn={pawn.thingIDNumber}");
                return;
            }

            foreach (ThingWithComps equipment in pawn.equipment.AllEquipmentListForReading.ToList())
            {
                pawn.equipment.Remove(equipment);
                equipment.Destroy();
            }

            ThingDef gunDef = DefDatabase<ThingDef>.GetNamedSilentFail("Gun_AssaultRifle");
            ThingWithComps gun = gunDef == null
                ? null
                : ThingMaker.MakeThing(gunDef) as ThingWithComps;
            if (gun == null)
            {
                Fail("PERSPECTIVE_SHIFT ranged test could not create Gun_AssaultRifle");
                return;
            }
            pawn.equipment.AddEquipment(gun);
        }

        private static IntVec3 FindPerspectiveTestCell(Pawn pawn, int minDistance, int maxDistance)
        {
            return GenRadial.RadialCellsAround(pawn.Position, maxDistance, true)
                .Where(cell =>
                    cell.InBounds(pawn.Map) &&
                    pawn.Position.DistanceTo(cell) >= minDistance &&
                    pawn.Position.DistanceTo(cell) <= maxDistance &&
                    cell.GetFirstBuilding(pawn.Map) == null &&
                    cell.GetFirstPawn(pawn.Map) == null &&
                    cell.GetThingList(pawn.Map).All(
                        thing => thing.def.category != ThingCategory.Item) &&
                    GenSight.LineOfSight(pawn.Position, cell, pawn.Map, true))
                .OrderBy(cell => pawn.Position.DistanceToSquared(cell))
                .ThenBy(cell => cell.x)
                .ThenBy(cell => cell.z)
                .Select(cell => (IntVec3?)cell)
                .FirstOrDefault() ?? IntVec3.Invalid;
        }

        private static IntVec3 FindClearPerspectiveMoveDirection(Pawn pawn, Pawn other, int salt)
        {
            if (pawn?.Map == null)
                return IntVec3.Invalid;

            IntVec3[] directions =
            {
                new IntVec3(-1, 0, 0),
                new IntVec3(1, 0, 0),
                new IntVec3(0, 0, -1),
                new IntVec3(0, 0, 1)
            };
            for (int offset = 0; offset < directions.Length; offset++)
            {
                IntVec3 direction = directions[(salt + offset) % directions.Length];
                bool clear = true;
                for (int distance = 1; distance <= 4; distance++)
                {
                    IntVec3 cell = pawn.Position + direction * distance;
                    if (!cell.InBounds(pawn.Map) || !cell.Walkable(pawn.Map) ||
                        (other != null && other.Map == pawn.Map && other.Position == cell) ||
                        cell.GetFirstPawn(pawn.Map) != null)
                    {
                        clear = false;
                        break;
                    }
                }
                if (clear)
                    return direction;
            }

            return IntVec3.Invalid;
        }

        private static void RunPerspectiveContainerPasses(
            string owner,
            Pawn pawn,
            MethodInfo mapClick)
        {
            if (pawn?.Map == null || pawn.inventory == null || pawn.carryTracker == null)
            {
                Fail("PERSPECTIVE_SHIFT container pawn is unavailable");
                return;
            }

            pawn.drafter.Drafted = false;
            pawn.jobs?.EndCurrentJob(JobCondition.InterruptForced);
            pawn.pather?.StopDead();

            ThingDef shelfDef = DefDatabase<ThingDef>.GetNamedSilentFail("ShelfSmall");
            ThingDef podDef = DefDatabase<ThingDef>.GetNamedSilentFail("TransportPod");
            if (shelfDef == null || podDef == null)
            {
                Fail(
                    $"PERSPECTIVE_SHIFT container defs unavailable shelf={shelfDef != null} " +
                    $"pod={podDef != null}");
                return;
            }

            for (int repeat = 0; repeat < 3; repeat++)
            {
                // Combat stages can leave the avatar boxed in by the other pawn,
                // a corpse or a spawned fixture. Storage interaction itself does
                // not require the fixture to be on the single adjacent ring, so
                // keep the placement deterministic while allowing a small radius.
                IntVec3 shelfCell = FindPerspectiveTestCell(pawn, 1, 4);
                Building_Storage shelf = ThingMaker.MakeThing(
                    shelfDef,
                    GenStuff.DefaultStuffFor(shelfDef)) as Building_Storage;
                if (shelf == null || !shelfCell.IsValid)
                {
                    Fail($"PERSPECTIVE_SHIFT shelf setup failed repeat={repeat}");
                    return;
                }
                GenSpawn.Spawn(shelf, shelfCell, pawn.Map, WipeMode.Vanish);
                shelf.GetStoreSettings().filter.SetAllow(ThingDefOf.Steel, true);

                Thing steel = ThingMaker.MakeThing(ThingDefOf.Steel);
                steel.stackCount = 5 + repeat;
                GenSpawn.Spawn(steel, pawn.Position, pawn.Map, WipeMode.Vanish);
                int picked = pawn.carryTracker.TryStartCarry(
                    steel,
                    steel.stackCount,
                    reserve: false);
                if (picked <= 0)
                {
                    Fail($"PERSPECTIVE_SHIFT shelf carry setup failed repeat={repeat}");
                    return;
                }

                mapClick.Invoke(
                    null,
                    new object[] { owner, pawn, 1, shelf.Position, 0, 1 });
                bool deposited = pawn.carryTracker.CarriedThing == null &&
                                  shelf.AllSlotCellsList()
                                      .SelectMany(cell => cell.GetThingList(pawn.Map))
                                      .Any(item => item == steel);
                mapClick.Invoke(
                    null,
                    new object[] { owner, pawn, 1, shelf.Position, 0, 1 });
                bool pickedBackUp = pawn.carryTracker.CarriedThing == steel;
                int inventoryBefore = pawn.inventory.innerContainer.TotalStackCountOfDef(
                    ThingDefOf.Steel);
                mapClick.Invoke(
                    null,
                    new object[] { owner, pawn, 1, pawn.Position, 1, 1 });
                int inventoryAfter = pawn.inventory.innerContainer.TotalStackCountOfDef(
                    ThingDefOf.Steel);
                bool packed = pawn.carryTracker.CarriedThing == null &&
                              inventoryAfter > inventoryBefore;
                bool unsafeWindowOpened = Find.WindowStack.Windows.Any(
                    window => string.Equals(
                        window.GetType().FullName,
                        "PerspectiveShift.Dialog_StorageMenu",
                        StringComparison.Ordinal));
                if (!deposited || !pickedBackUp || !packed || unsafeWindowOpened)
                {
                    Fail(
                        $"PERSPECTIVE_SHIFT storage fallback failed repeat={repeat} " +
                        $"deposited={deposited} pickedBackUp={pickedBackUp} packed={packed} " +
                        $"unsafeWindowOpened={unsafeWindowOpened} inventory={inventoryBefore}->{inventoryAfter}");
                    return;
                }
                perspectiveShiftStoragePasses++;
                if (pawn.inventory.innerContainer.Contains(steel))
                {
                    pawn.inventory.innerContainer.Remove(steel);
                    steel.Destroy();
                }
                shelf.Destroy(DestroyMode.Vanish);

                IntVec3 podCell = FindPerspectiveTestCell(pawn, 1, 4);
                Thing pod = ThingMaker.MakeThing(podDef);
                if (!podCell.IsValid || pod == null)
                {
                    Fail($"PERSPECTIVE_SHIFT transport pod setup failed repeat={repeat}");
                    return;
                }
                GenSpawn.Spawn(pod, podCell, pawn.Map, WipeMode.Vanish);
                ThingOwner podContainer = pod.TryGetInnerInteractableThingOwner();
                Thing component = ThingMaker.MakeThing(ThingDefOf.ComponentIndustrial);
                component.stackCount = 2 + repeat;
                GenSpawn.Spawn(component, pawn.Position, pawn.Map, WipeMode.Vanish);
                picked = pawn.carryTracker.TryStartCarry(
                    component,
                    component.stackCount,
                    reserve: false);
                if (picked <= 0 || podContainer == null)
                {
                    Fail(
                        $"PERSPECTIVE_SHIFT transport pod carry setup failed repeat={repeat} " +
                        $"picked={picked} container={podContainer != null}");
                    return;
                }

                mapClick.Invoke(
                    null,
                    new object[] { owner, pawn, 1, pod.Position, 0, 1 });
                bool transferred = pawn.carryTracker.CarriedThing == null &&
                                   podContainer.Contains(component);
                if (!transferred)
                {
                    Fail(
                        $"PERSPECTIVE_SHIFT inner container transfer failed repeat={repeat} " +
                        $"pawn={pawn.thingIDNumber} map={pawn.Map.uniqueID}");
                    return;
                }
                perspectiveShiftContainerPasses++;
                if (podContainer.Contains(component))
                {
                    podContainer.Remove(component);
                    component.Destroy();
                }
                pod.Destroy(DestroyMode.Vanish);
            }

            Log.Message(
                $"{Tag} PERSPECTIVE_SHIFT_CONTAINER_ASSERT role={Role} " +
                $"owner={owner} pawn={pawn.thingIDNumber} map={pawn.Map.uniqueID} " +
                $"storagePasses={perspectiveShiftStoragePasses} " +
                $"containerPasses={perspectiveShiftContainerPasses} PASS");
        }

        private static void ApplyTwoMapViewDivergence()
        {
            if (!TwoMapViewTest || twoMapViewApplied)
                return;

            var maps = Find.Maps.OrderBy(map => map.uniqueID).ToList();
            if (maps.Count < 2)
            {
                Fail($"TWO_MAP_VIEW requires at least two maps; actual={maps.Count}");
                return;
            }

            Map target = Role == "host" ? maps[0] : maps[maps.Count - 1];
            InitializeViewedMapTracking();
            Current.Game.CurrentMap = target;
            twoMapViewApplied = true;
            Log.Message(
                $"{Tag} TWO_MAP_VIEW role={Role} currentMapId={target.uniqueID} " +
                $"mapIndex={target.Index} allMapIds={string.Join(",", maps.Select(map => map.uniqueID))}");
        }

        private static void InitializeViewedMapTracking()
        {
            Map current = Find.CurrentMap;
            if (current == null)
                return;

            Type vtrSync = AccessTools.TypeByName("Multiplayer.Client.Patches.VTRSync");
            MethodInfo send = AccessTools.Method(
                vtrSync,
                "SendViewedMapUpdate",
                new[] { typeof(int), typeof(int) });
            FieldInfo lastMoved = AccessTools.Field(vtrSync, "lastMovedToMapId");
            if (send == null || lastMoved == null)
            {
                Fail("TWO_MAP_VIEW could not resolve Multiplayer VTR map tracking");
                return;
            }

            int tracked = (int)lastMoved.GetValue(null);
            if (tracked == -1)
            {
                send.Invoke(null, new object[] { -1, current.uniqueID });
                Log.Message(
                    $"{Tag} TWO_MAP_VIEW initialized VTR tracking -1->{current.uniqueID}");
            }
        }

        private static void LogRandomStates(string phase)
        {
            var worldAsync = Multiplayer.Client.Multiplayer.AsyncWorldTime;
            Log.Message(
                $"{Tag} RNG_STATE role={Role} phase={phase} tick={TickPatch.Timer} " +
                $"worldTicks={worldAsync?.worldTicks.ToString() ?? "<null>"} " +
                $"worldRand={FormatRandState(worldAsync?.randState)}");

            foreach (Map map in Find.Maps.OrderBy(candidate => candidate.uniqueID))
            {
                var async = map.AsyncTime();
                Log.Message(
                    $"{Tag} RNG_STATE role={Role} phase={phase} mapId={map.uniqueID} " +
                    $"mapIndex={map.Index} mapTicks={async?.mapTicks.ToString() ?? "<null>"} " +
                    $"mapRand={FormatRandState(async?.randState)}");
            }
        }

        private static string FormatRandState(ulong? state)
        {
            if (!state.HasValue)
                return "<null>";

            ulong value = state.Value;
            return $"0x{value:X16}(calls={(uint)(value >> 32)},seed=0x{(uint)value:X8})";
        }

        private static bool RunTraderIncidentTest(int elapsedTicks)
        {
            Map map = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
            if (map == null)
            {
                Fail("TRADER_INCIDENT no player map is available");
                return false;
            }

            if (traderIncidentExecutedStage > traderIncidentAssertedStage)
            {
                if (TickPatch.Timer - traderIncidentExecutionTick < 300)
                    return false;

                Map assertionMap = Find.Maps.FirstOrDefault(
                    candidate => candidate != null &&
                                 candidate.uniqueID == traderIncidentExpectedMapId);
                if (assertionMap == null)
                {
                    Fail(
                        $"TRADER_INCIDENT assertion map missing stage={traderIncidentExecutedStage} " +
                        $"mapId={traderIncidentExpectedMapId}");
                    return false;
                }

                int matchingTraders = assertionMap.mapPawns.AllPawnsSpawned.Count(
                    pawn => string.Equals(
                        pawn?.trader?.traderKind?.defName,
                        traderIncidentExpectedKind,
                        StringComparison.Ordinal));
                int currentPawnCount = assertionMap.mapPawns.AllPawnsSpawned.Count;
                if (matchingTraders <= 0 || currentPawnCount <= traderIncidentBaselinePawnCount)
                {
                    Fail(
                        $"TRADER_INCIDENT assertion failed stage={traderIncidentExecutedStage} " +
                        $"mapId={traderIncidentExpectedMapId} " +
                        $"kind={traderIncidentExpectedKind} matching={matchingTraders} " +
                        $"pawnsBefore={traderIncidentBaselinePawnCount} pawnsNow={currentPawnCount}");
                    return false;
                }

                traderIncidentAssertedStage = traderIncidentExecutedStage;
                Log.Message(
                    $"{Tag} TRADER_INCIDENT_ASSERT role={Role} " +
                    $"stage={traderIncidentAssertedStage} mapId={traderIncidentExpectedMapId} " +
                    $"kind={traderIncidentExpectedKind} " +
                    $"matching={matchingTraders} pawnsBefore={traderIncidentBaselinePawnCount} " +
                    $"pawnsNow={currentPawnCount} tick={TickPatch.Timer}");
            }

            if (traderIncidentAssertedStage >= TraderIncidentKinds.Length)
                return true;

            int nextStage = traderIncidentAssertedStage + 1;
            int dispatchAt = 1000 + (nextStage - 1) * 4000;
            if (Role != "client" || traderIncidentDispatchedStage >= nextStage ||
                elapsedTicks < dispatchAt)
            {
                return false;
            }

            if (traderIncidentSyncMethod == null)
            {
                Fail("TRADER_INCIDENT sync method is unavailable");
                return false;
            }

            string traderKind = TraderIncidentKinds[nextStage - 1];
            traderIncidentDispatchedStage = nextStage;
            traderIncidentSyncMethod.DoSync(null, map, traderKind, nextStage);
            Log.Message(
                $"{Tag} TRADER_INCIDENT_DISPATCH role={Role} stage={nextStage} " +
                $"kind={traderKind} tick={TickPatch.Timer}");
            return false;
        }

        private static void SyncTriggerTraderCaravan(
            Map map,
            string traderKindDefName,
            int stage)
        {
            if (map == null || string.IsNullOrWhiteSpace(traderKindDefName))
                return;

            IncidentDef incident = IncidentDefOf.TraderCaravanArrival;
            TraderKindDef traderKind = DefDatabase<TraderKindDef>.GetNamedSilentFail(
                traderKindDefName);
            var worker = incident?.Worker as IncidentWorker_TraderCaravanArrival;
            if (worker == null || traderKind == null)
            {
                Fail(
                    $"TRADER_INCIDENT target missing incident={incident != null} " +
                    $"worker={worker != null} traderKind={traderKindDefName}:{traderKind != null}");
                return;
            }

            IncidentParms parms = StorytellerUtility.DefaultParmsNow(
                incident.category,
                map);
            parms.forced = true;
            parms.traderKind = traderKind;
            parms.faction = Find.FactionManager.AllFactionsVisible
                .Where(faction =>
                    faction != null &&
                    faction != Faction.OfPlayer &&
                    !faction.defeated &&
                    !faction.HostileTo(Faction.OfPlayer) &&
                    worker.FactionCanBeGroupSource(faction, parms, false))
                .OrderBy(faction => faction.loadID)
                .FirstOrDefault();

            int pawnCountBefore = map.mapPawns.AllPawnsSpawned.Count;
            bool executed = worker.TryExecute(parms);
            if (!executed)
            {
                Fail(
                    $"TRADER_INCIDENT execution returned false stage={stage} " +
                    $"kind={traderKindDefName} faction={parms.faction?.Name ?? "<null>"}");
                return;
            }

            traderIncidentExecutedStage = stage;
            traderIncidentExecutionTick = TickPatch.Timer;
            traderIncidentExpectedKind = traderKindDefName;
            traderIncidentBaselinePawnCount = pawnCountBefore;
            traderIncidentExpectedMapId = map.uniqueID;
            Log.Message(
                $"{Tag} TRADER_INCIDENT_EXECUTED role={Role} stage={stage} " +
                $"mapId={map.uniqueID} kind={traderKindDefName} " +
                $"faction={parms.faction?.Name ?? "<resolved>"} " +
                $"pawnsBefore={pawnCountBefore} pawnsImmediate={map.mapPawns.AllPawnsSpawned.Count} " +
                $"tick={TickPatch.Timer}");
        }

        private static bool RunRjwAddonsTest(int elapsedTicks)
        {
            Type compType = AccessTools.TypeByName("Cumpilation.Leaking.Comp_SealCum");
            FieldInfo sealedField = AccessTools.Field(compType, "cumSealed");
            FieldInfo deflateField = AccessTools.Field(compType, "canDeflate");
            if (compType == null || sealedField == null || deflateField == null)
            {
                Fail("RJW_ADDONS missing Cumpilation Comp_SealCum fields");
                return false;
            }

            Pawn pawn = Find.CurrentMap?.mapPawns?.AllPawnsSpawned
                .Where(candidate => candidate?.AllComps != null &&
                                    candidate.AllComps.Any(compType.IsInstanceOfType))
                .OrderBy(candidate => candidate.thingIDNumber)
                .FirstOrDefault();
            ThingComp comp = pawn?.AllComps?.FirstOrDefault(compType.IsInstanceOfType);
            if (pawn == null || comp == null)
            {
                Fail("RJW_ADDONS no spawned pawn with Cumpilation Comp_SealCum");
                return false;
            }

            if (!rjwAddonsInitialCaptured)
            {
                rjwAddonsInitialCaptured = true;
                rjwAddonsInitialSealed = (bool)sealedField.GetValue(comp);
                rjwAddonsInitialDeflate = (bool)deflateField.GetValue(comp);
                Log.Message(
                    $"{Tag} RJW_ADDONS_INITIAL role={Role} pawn={pawn.thingIDNumber} " +
                    $"cumSealed={rjwAddonsInitialSealed} canDeflate={rjwAddonsInitialDeflate}");
            }

            if (Role == "client" && !rjwAddonsActionIssued)
            {
                MethodInfo[] callbacks = compType
                    .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                    .Where(method =>
                        method.Name.Contains("CompGetGizmosExtra") &&
                        method.ReturnType == typeof(void) &&
                        method.GetParameters().Length == 0)
                    .OrderBy(method => method.Name, StringComparer.Ordinal)
                    .ToArray();
                if (callbacks.Length != 2)
                {
                    Fail($"RJW_ADDONS expected 2 real Cumpilation toggle callbacks, found {callbacks.Length}");
                    return false;
                }

                for (int repeat = 0; repeat < 3; repeat++)
                    foreach (MethodInfo callback in callbacks)
                        callback.Invoke(comp, null);

                rjwAddonsActionIssued = true;
                Log.Message(
                    $"{Tag} RJW_ADDONS_ACTION_ISSUED role={Role} pawn={pawn.thingIDNumber} " +
                    $"callbacks={callbacks.Length} repeats=3 actualCallback=True");
            }

            int replayCount = GetRjwAddonsReplayCount();
            if (replayCount < 6)
            {
                if (elapsedTicks >= 2500)
                    Fail($"RJW_ADDONS replay timeout role={Role} count={replayCount}/6");
                return false;
            }

            if (!rjwAddonsStateLogged)
            {
                bool finalSealed = (bool)sealedField.GetValue(comp);
                bool finalDeflate = (bool)deflateField.GetValue(comp);
                if (finalSealed == rjwAddonsInitialSealed ||
                    finalDeflate == rjwAddonsInitialDeflate)
                {
                    Fail(
                        $"RJW_ADDONS state did not invert after three callbacks pawn={pawn.thingIDNumber} " +
                        $"initial={rjwAddonsInitialSealed}/{rjwAddonsInitialDeflate} " +
                        $"final={finalSealed}/{finalDeflate}");
                    return false;
                }

                rjwAddonsStateLogged = true;
                Log.Message(
                    $"{Tag} RJW_ADDONS_STATE role={Role} pawn={pawn.thingIDNumber} " +
                    $"cumSealed={finalSealed} canDeflate={finalDeflate} " +
                    $"inverted=True replayCount={replayCount}");
            }

            return Role == "host" || rjwAddonsActionIssued;
        }

        private static int GetRjwAddonsReplayCount()
        {
            Type patchType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(
                    "MP_MeowOnlineShop.Patch_RjwAddons",
                    false))
                .FirstOrDefault(type => type != null);
            MethodInfo getter = patchType?.GetMethod(
                "GetTestReplayCount",
                BindingFlags.Static | BindingFlags.NonPublic);
            return getter == null ? -1 : (int)getter.Invoke(null, null);
        }

        private static int GetProgressIntervalTicks()
        {
            return Math.Max(1000, Math.Min(10000, DurationTicks / 10));
        }

        private static void LogAlertDiagnostics()
        {
            try
            {
                Type patchType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(assembly => assembly.GetType(
                        "MP_MeowOnlineShop.Patch_MpSafeAlertThrottling",
                        false))
                    .FirstOrDefault(type => type != null);
                MethodInfo snapshot = patchType?.GetMethod(
                    "GetDiagnosticsSnapshot",
                    BindingFlags.Static | BindingFlags.NonPublic);
                string value = snapshot?.Invoke(null, null) as string;
                Log.Message(
                    $"{Tag} ALERT_DIAGNOSTICS role={Role} " +
                    (value ?? "featureUnavailable=True"));
            }
            catch (Exception e)
            {
                Log.Warning($"{Tag} ALERT_DIAGNOSTICS_FAILED role={Role} error={e.Message}");
            }
        }

        private static void RunAlertProbe()
        {
            alertProbeCompleted = true;
            try
            {
                AlertsReadout readout = Find.Alerts;
                var allAlertsField = AccessTools.Field(typeof(AlertsReadout), "AllAlerts");
                var checkMethod = AccessTools.Method(
                    typeof(AlertsReadout),
                    "CheckAddOrRemoveAlert",
                    new[] { typeof(Alert), typeof(bool) });
                var alerts = allAlertsField?.GetValue(readout) as List<Alert>;
                Alert medium = alerts?.FirstOrDefault(alert =>
                    alert != null && alert.Priority == AlertPriority.Medium);
                Alert urgent = alerts?.FirstOrDefault(alert =>
                    alert != null && alert.Priority != AlertPriority.Medium);
                if (readout == null || checkMethod == null || medium == null || urgent == null)
                {
                    Log.Warning(
                        $"{Tag} ALERT_PROBE_SKIPPED role={Role} " +
                        $"readout={readout != null} check={checkMethod != null} " +
                        $"medium={medium != null} urgent={urgent != null}");
                    return;
                }

                string before = ReadAlertDiagnosticsSnapshot();
                for (int i = 0; i < 100; i++)
                    checkMethod.Invoke(readout, new object[] { medium, false });
                for (int i = 0; i < 100; i++)
                    checkMethod.Invoke(readout, new object[] { urgent, false });
                checkMethod.Invoke(readout, new object[] { medium, true });
                string after = ReadAlertDiagnosticsSnapshot();
                Log.Message(
                    $"{Tag} ALERT_PROBE_COMPLETE role={Role} " +
                    $"medium={medium.GetType().FullName} urgent={urgent.GetType().FullName} " +
                    $"before=({before}) after=({after})");
            }
            catch (Exception e)
            {
                Log.Error($"{Tag} ALERT_PROBE_FAILED role={Role} error={e}");
            }
        }

        private static void InstallExactMatchAutoConnect()
        {
            Type joinDataWindow = AccessTools.TypeByName("Multiplayer.Client.JoinDataWindow");
            MethodInfo postOpen = AccessTools.Method(joinDataWindow, "PostOpen");
            MethodInfo postfix = AccessTools.Method(
                typeof(LongRunGameComponent),
                nameof(JoinDataWindow_PostOpen_Postfix));
            if (postOpen == null || postfix == null)
            {
                Log.Warning($"{Tag} exact-match auto-connect hook unavailable.");
                return;
            }

            AutoConnectHarmony.Patch(postOpen, postfix: new HarmonyMethod(postfix));
            Log.Message($"{Tag} exact-match JoinDataWindow auto-connect hook installed.");
        }

        private static void InstallFixedQuicktestSeed()
        {
            MethodInfo target = AccessTools.Method(
                typeof(Root_Play),
                nameof(Root_Play.SetupForQuickTestPlay));
            MethodInfo prefix = AccessTools.Method(
                typeof(LongRunGameComponent),
                nameof(SetupForQuickTestPlay_Prefix));
            MethodInfo finalizer = AccessTools.Method(
                typeof(LongRunGameComponent),
                nameof(SetupForQuickTestPlay_Finalizer));
            if (target == null || prefix == null || finalizer == null)
            {
                Log.Warning($"{Tag} fixed quicktest seed hook unavailable.");
                return;
            }

            FixedQuicktestHarmony.Patch(
                target,
                prefix: new HarmonyMethod(prefix),
                finalizer: new HarmonyMethod(finalizer));
            Log.Message($"{Tag} fixed quicktest Rand hook installed seed=2072701.");
        }

        private static void SetupForQuickTestPlay_Prefix(ref bool __state)
        {
            Rand.PushState(2072701);
            __state = true;
        }

        private static Exception SetupForQuickTestPlay_Finalizer(
            Exception __exception,
            bool __state)
        {
            try
            {
                Log.Message(
                    $"{Tag} fixed quicktest generated worldSeed=" +
                    $"{Find.World?.info?.seedString ?? "<null>"}.");
            }
            finally
            {
                if (__state)
                    Rand.PopState();
            }

            return __exception;
        }

        private static void JoinDataWindow_PostOpen_Postfix(object __instance)
        {
            try
            {
                if (__instance == null)
                    return;

                Type type = __instance.GetType();
                string disabled = AccessTools.Field(type, "connectAnywayDisabled")
                    ?.GetValue(__instance) as string;
                MethodInfo diffMethod = AccessTools.Method(type, "DiffString");
                string diff = diffMethod?.Invoke(__instance, null) as string;
                bool exactMatch =
                    disabled == null &&
                    diff != null &&
                    diff.Contains("RW version match: True") &&
                    diff.Contains("Mod list diff: None") &&
                    diff.Contains("Files match: True") &&
                    diff.Contains("Configs match: True");
                if (!exactMatch)
                {
                    Log.Warning(
                        $"{Tag} exact-match auto-connect refused disabled={disabled} diff={diff}");
                    return;
                }

                var callback = AccessTools.Field(type, "connectAnywayCallback")
                    ?.GetValue(__instance) as Action;
                if (callback == null)
                {
                    Log.Warning($"{Tag} exact-match auto-connect callback unavailable.");
                    return;
                }

                Log.Message($"{Tag} exact-match JoinDataWindow auto-continued diff={diff}");
                callback();
                (__instance as Window)?.Close(false);
            }
            catch (Exception e)
            {
                Log.Error($"{Tag} exact-match auto-connect failed: {e}");
            }
        }

        private static string ReadAlertDiagnosticsSnapshot()
        {
            Type patchType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(
                    "MP_MeowOnlineShop.Patch_MpSafeAlertThrottling",
                    false))
                .FirstOrDefault(type => type != null);
            MethodInfo snapshot = patchType?.GetMethod(
                "GetDiagnosticsSnapshot",
                BindingFlags.Static | BindingFlags.NonPublic);
            return snapshot?.Invoke(null, null) as string ?? "featureUnavailable=True";
        }

        private static void InstallCaravanMassUiDriver()
        {
            try
            {
                MethodInfo target = AccessTools.Method(
                    typeof(CaravanUIUtility),
                    "DrawCaravanInfo");
                if (target == null)
                {
                    Log.Error($"{Tag} CARAVAN_MASS_UI driver target resolution failed.");
                    return;
                }

                var harmony = new Harmony("local.mp.meowonlineshop.autotest.caravanmassui");
                harmony.Patch(
                    target,
                    postfix: new HarmonyMethod(
                        typeof(LongRunGameComponent),
                        nameof(CaravanMassInfoPostfix)));
                Type transferableVehicleWidget =
                    GenTypes.GetTypeInAnyAssembly("Vehicles.World.TransferableVehicleWidget");
                MethodInfo vehicleOnGui = AccessTools.Method(
                    transferableVehicleWidget,
                    "OnGUI");
                if (vehicleOnGui == null)
                {
                    Log.Error(
                        $"{Tag} CARAVAN_MASS_UI TransferableVehicleWidget.OnGUI " +
                        "resolution failed.");
                    return;
                }
                harmony.Patch(
                    vehicleOnGui,
                    prefix: new HarmonyMethod(
                        typeof(LongRunGameComponent),
                        nameof(CaravanMassVehicleWidgetPrefix)));
                caravanMassUiInstalled = true;
                Log.Message($"{Tag} CARAVAN_MASS_UI driver installed.");
            }
            catch (Exception e)
            {
                Log.Error($"{Tag} CARAVAN_MASS_UI driver install failed: {e}");
            }
        }

        private static void CaravanMassVehicleWidgetPrefix()
        {
            if (!CaravanMassUiTest)
                return;

            if (CaravanFormingProxy.drawing != null)
            {
                caravanMassVehicleTabDrawn = true;
                caravanMassVehicleTabDrawCount++;
            }
        }

        private static void CaravanMassInfoPostfix(CaravanUIUtility.CaravanInfo info)
        {
            if (!CaravanMassUiTest)
                return;

            drawCaravanInfoCalls++;
            if (CaravanFormingProxy.drawing != null)
            {
                drawCaravanInfoSawProxy = true;
                drawCaravanInfoLastSnapshot =
                    $"massUsage={info.massUsage:F2} massCapacity={info.massCapacity:F2}";
            }
        }

        private static void SpawnCaravanTestVehicle()
        {
            try
            {
                Map map = Find.CurrentMap;
                if (map == null)
                {
                    Log.Warning(
                        $"{Tag} CARAVAN_MASS_UI vehicle spawn skipped: no current map.");
                    return;
                }

                Type vehicleDefType = GenTypes.GetTypeInAnyAssembly("Vehicles.VehicleDef");
                Type vehiclePawnType = GenTypes.GetTypeInAnyAssembly("Vehicles.VehiclePawn");
                Type vehicleSpawnerType = GenTypes.GetTypeInAnyAssembly("Vehicles.VehicleSpawner");
                if (vehicleDefType == null || vehiclePawnType == null || vehicleSpawnerType == null)
                {
                    Log.Warning(
                        $"{Tag} CARAVAN_MASS_UI vehicle spawn skipped: " +
                        "Vehicle Framework types are not active.");
                    return;
                }

                Type databaseType = typeof(DefDatabase<>).MakeGenericType(vehicleDefType);
                var allDefs = AccessTools.Property(databaseType, "AllDefsListForReading")
                    ?.GetValue(null) as System.Collections.IEnumerable;
                Def def = allDefs?.Cast<object>()
                    .OfType<Def>()
                    .Where(vehicleDef =>
                        (AccessTools.Field(vehicleDefType, "canCaravan")?.GetValue(vehicleDef) as bool?) == true)
                    .OrderBy(vehicleDef => vehicleDef.defName, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (def == null)
                {
                    Log.Warning(
                        $"{Tag} CARAVAN_MASS_UI vehicle spawn skipped: " +
                        "no canCaravan VehicleDef.");
                    return;
                }

                IntVec3 cell = map.AllCells
                    .Where(c =>
                        c.Standable(map) &&
                        c.GetEdifice(map) == null &&
                        !map.fogGrid.IsFogged(c))
                    .OrderBy(c => c.DistanceTo(map.Center))
                    .FirstOrDefault();
                if (!cell.IsValid)
                {
                    Log.Warning(
                        $"{Tag} CARAVAN_MASS_UI vehicle spawn skipped: no free spawn cell.");
                    return;
                }

                MethodInfo generateVehicle = AccessTools.GetDeclaredMethods(vehicleSpawnerType)
                    .Where(method => method.Name == "GenerateVehicle")
                    .FirstOrDefault(method =>
                    {
                        ParameterInfo[] parameters = method.GetParameters();
                        return parameters.Length == 2 &&
                               parameters[0].ParameterType == vehicleDefType &&
                               parameters[1].ParameterType == typeof(Faction);
                    });
                Thing vehicle = generateVehicle?.Invoke(null, new object[] { def, Faction.OfPlayer }) as Thing;
                if (vehicle == null)
                {
                    Log.Warning(
                        $"{Tag} CARAVAN_MASS_UI vehicle generation failed def={def.defName}.");
                    return;
                }

                GenSpawn.Spawn(vehicle, cell, map, Rot4.North);
                caravanMassVehicleDefName = def.defName;
                Log.Message(
                    $"{Tag} CARAVAN_MASS_UI vehicle spawned def={def.defName} " +
                    $"id={vehicle.thingIDNumber} cell={cell} map={map.Index}.");
            }
            catch (Exception e)
            {
                Log.Error($"{Tag} CARAVAN_MASS_UI vehicle spawn failed: {e}");
            }
        }

        private static bool RunCaravanMassUiTest()
        {
            if (caravanMassUiStage >= 5)
                return true;

            if (caravanMassUiStageAtRealtime < 0f)
                caravanMassUiStageAtRealtime = Time.realtimeSinceStartup;
            if (Time.realtimeSinceStartup - caravanMassUiStageAtRealtime > 180f)
            {
                Fail($"caravan mass UI stage timed out stage={caravanMassUiStage}");
                return false;
            }

            switch (caravanMassUiStage)
            {
                case 0:
                    if (Find.CurrentMap == null)
                        return false;
                    if (!CreateAndOpenLocalCaravanSession())
                    {
                        Fail(
                            "could not create local Multiplayer CaravanFormingSession " +
                            "for mass UI test");
                        return false;
                    }
                    AdvanceCaravanMassUiStage(1);
                    Log.Message($"{Tag} CARAVAN_MASS_UI first open requested");
                    return false;

                case 1:
                    if (caravanUiProxy == null)
                        return false;
                    Log.Message(
                        $"{Tag} CARAVAN_MASS_UI first open {ReadCaravanMassSnapshot()}");
                    caravanMassVehicleTransferableIndex = FindVehicleTransferableIndex();
                    if (caravanMassVehicleTransferableIndex < 0)
                    {
                        Log.Message(
                            $"{Tag} CARAVAN_MASS_UI no vehicle transferable found; " +
                            "remaining checks are pawn/items only.");
                    }
                    else
                    {
                        TransferableOneWay trad =
                            caravanUiProxy.transferables[caravanMassVehicleTransferableIndex];
                        trad.ForceTo(trad.GetMaximumToTransfer());
                        InvokeNotifyTransferablesChanged();
                        Log.Message(
                            $"{Tag} CARAVAN_MASS_UI selected vehicle " +
                            $"index={caravanMassVehicleTransferableIndex} " +
                            $"label={trad.LabelCap ?? trad.AnyThing?.LabelShortCap ?? "unknown"}.");
                    }
                    ReadProxyTabInfo();
                    caravanMassVehicleTabDrawn = false;
                    caravanMassVehicleTabDrawCount = 0;
                    SetVehicleSelectedTab(10);
                    Log.Message(
                        $"{Tag} CARAVAN_MASS_UI switched to Vehicles tab " +
                        $"tabs={caravanMassProxyTabCount} labels={caravanMassProxyTabLabels}");
                    AdvanceCaravanMassUiStage(2);
                    return false;

                case 2:
                    if (caravanUiProxy == null)
                        return false;
                    if (!caravanMassVehicleTabDrawn &&
                        Time.realtimeSinceStartup - caravanMassUiStageAtRealtime < 8f)
                    {
                        return false;
                    }
                    Log.Message(
                        $"{Tag} CARAVAN_MASS_UI vehicle tab draw " +
                        $"vehicleTabDrawn={caravanMassVehicleTabDrawn} " +
                        $"vehicleTabDrawCount={caravanMassVehicleTabDrawCount} " +
                        ReadCaravanMassSnapshot());
                    SetVehicleSelectedTab(0);
                    caravanUiProxy.Close(false);
                    caravanUiProxy = null;
                    AdvanceCaravanMassUiStage(3);
                    return false;

                case 3:
                    if (caravanUiSession == null || Find.CurrentMap == null)
                    {
                        Fail("caravan session disappeared before mass UI reopen");
                        return false;
                    }
                    caravanUiProxy = InvokeSessionOpenWindow(caravanUiSession);
                    if (caravanUiProxy == null)
                    {
                        Fail("could not reopen Multiplayer caravan proxy for mass UI test");
                        return false;
                    }
                    AdvanceCaravanMassUiStage(4);
                    return false;

                case 4:
                    if (caravanUiProxy == null)
                        return false;
                    Log.Message(
                        $"{Tag} CARAVAN_MASS_UI reopen {ReadCaravanMassSnapshot()} " +
                        $"drawCalls={drawCaravanInfoCalls} sawProxy={drawCaravanInfoSawProxy} " +
                        $"lastProxyDraw={drawCaravanInfoLastSnapshot} " +
                        $"vehicleTabDrawn={caravanMassVehicleTabDrawn} " +
                        $"vehicleTabDrawCount={caravanMassVehicleTabDrawCount} " +
                        $"tabs={caravanMassProxyTabCount} labels={caravanMassProxyTabLabels}");
                    caravanUiProxy.Close(false);
                    RemoveLocalCaravanSession();
                    caravanUiProxy = null;
                    caravanUiSession = null;
                    caravanUiMapComp = null;
                    AdvanceCaravanMassUiStage(5);
                    Log.Message(
                        $"{Tag} CARAVAN_MASS_UI_COMPLETE " +
                        $"drawCalls={drawCaravanInfoCalls} " +
                        $"sawProxy={drawCaravanInfoSawProxy} " +
                        $"lastProxyDraw={drawCaravanInfoLastSnapshot} " +
                        $"vehicleTabDrawn={caravanMassVehicleTabDrawn} " +
                        $"vehicleTabDrawCount={caravanMassVehicleTabDrawCount} " +
                        $"tabs={caravanMassProxyTabCount} labels={caravanMassProxyTabLabels} " +
                        $"driverInstalled={caravanMassUiInstalled}");
                    return true;
            }

            return false;
        }

        private static string ReadCaravanMassSnapshot()
        {
            if (caravanUiProxy == null)
                return "proxy=null";

            int transferableCount = caravanUiProxy.transferables?.Count ?? -1;
            int vehicleCount = caravanUiProxy.transferables?
                .Count(t => IsVehiclePawn(t?.AnyThing)) ?? -1;
            float usage = caravanUiProxy.MassUsage;
            float capacity = caravanUiProxy.MassCapacity;
            int selected = ReadVehicleSelectedTab();
            return
                $"transferables={transferableCount} vehicles={vehicleCount} " +
                $"massUsage={usage:F2} massCapacity={capacity:F2} selectedTab={selected}";
        }

        private static int FindVehicleTransferableIndex()
        {
            if (caravanUiProxy?.transferables == null)
                return -1;

            for (int i = 0; i < caravanUiProxy.transferables.Count; i++)
            {
                if (IsVehiclePawn(caravanUiProxy.transferables[i]?.AnyThing))
                    return i;
            }
            return -1;
        }

        private static bool IsVehiclePawn(Thing thing)
        {
            Type vehiclePawnType = GenTypes.GetTypeInAnyAssembly("Vehicles.VehiclePawn");
            return thing != null && vehiclePawnType != null && vehiclePawnType.IsInstanceOfType(thing);
        }

        private static void InvokeNotifyTransferablesChanged()
        {
            try
            {
                AccessTools.Method(
                        typeof(Dialog_FormCaravan),
                        "Notify_TransferablesChanged")
                    ?.Invoke(caravanUiProxy, null);
            }
            catch (Exception e)
            {
                Log.Warning(
                    $"{Tag} CARAVAN_MASS_UI Notify_TransferablesChanged failed: " +
                    e.Message);
            }
        }

        private static void AdvanceCaravanMassUiStage(int stage)
        {
            caravanMassUiStage = stage;
            caravanMassUiStageAtRealtime = Time.realtimeSinceStartup;
        }

        private static bool RunCaravanUiTest()
        {
            if (caravanUiStage >= 5)
                return true;

            if (caravanUiStageAtRealtime < 0f)
                caravanUiStageAtRealtime = Time.realtimeSinceStartup;
            if (Time.realtimeSinceStartup - caravanUiStageAtRealtime > 120f)
            {
                Fail($"caravan UI stage timed out stage={caravanUiStage}");
                return false;
            }

            switch (caravanUiStage)
            {
                case 0:
                    if (Find.CurrentMap == null)
                        return false;
                    if (!CreateAndOpenLocalCaravanSession())
                    {
                        Fail("could not create local Multiplayer CaravanFormingSession");
                        return false;
                    }
                    AdvanceCaravanUiStage(1);
                    Log.Message($"{Tag} CARAVAN_UI first open requested");
                    return false;

                case 1:
                    if (caravanUiProxy == null)
                        return false;
                    firstCaravanTransferableCount = caravanUiProxy.transferables?.Count ?? -1;
                    if (firstCaravanTransferableCount <= 0)
                    {
                        Fail($"first caravan window has no transferables count={firstCaravanTransferableCount}");
                        return false;
                    }
                    caravanUiProxy.Close(false);
                    caravanUiProxy = null;
                    AdvanceCaravanUiStage(2);
                    Log.Message(
                        $"{Tag} CARAVAN_UI first close transferables={firstCaravanTransferableCount} " +
                        $"vehicleSelectedTab={ReadVehicleSelectedTab()}");
                    return false;

                case 2:
                    if (caravanUiProxy != null || Find.CurrentMap == null)
                        return false;
                    if (caravanUiSession == null)
                    {
                        Fail("caravan session disappeared before second opening");
                        return false;
                    }
                    caravanUiProxy = InvokeSessionOpenWindow(caravanUiSession);
                    if (caravanUiProxy == null)
                    {
                        Fail("could not open second Multiplayer caravan proxy");
                        return false;
                    }
                    AdvanceCaravanUiStage(3);
                    Log.Message($"{Tag} CARAVAN_UI second open requested");
                    return false;

                case 3:
                    if (caravanUiProxy == null)
                        return false;
                    int secondCount = caravanUiProxy.transferables?.Count ?? -1;
                    int selectedTab = ReadVehicleSelectedTab();
                    if (secondCount <= 0 || secondCount != firstCaravanTransferableCount || selectedTab != 0)
                    {
                        Fail(
                            $"second caravan window invalid first={firstCaravanTransferableCount} " +
                            $"second={secondCount} vehicleSelectedTab={selectedTab}");
                        return false;
                    }

                    caravanUiProxy.Close(false);
                    RemoveLocalCaravanSession();
                    caravanUiProxy = null;
                    caravanUiSession = null;
                    caravanUiMapComp = null;
                    AdvanceCaravanUiStage(5);
                    Log.Message(
                        $"{Tag} CARAVAN_UI_COMPLETE first={firstCaravanTransferableCount} " +
                        $"second={secondCount} vehicleSelectedTab={selectedTab}");
                    return true;
            }

            return false;
        }

        private static void InstallCompatibilityUiDriver()
        {
            tradeDateDialogType = AccessTools.TypeByName("Kiiro_Event.Dialog_TradeFestivalDate");
            var gameComponentType = AccessTools.TypeByName(
                "Kiiro_Event.KiiroEventGameComponent_OverallControl");
            tradeDateField = AccessTools.Field(gameComponentType, "tradeFestivalDate");
            tradeDateComponentProperty = AccessTools.Property(gameComponentType, "GC");
            var doWindowContents = AccessTools.Method(
                tradeDateDialogType,
                "DoWindowContents",
                new[] { typeof(Rect) });
            var buttonText = typeof(Widgets)
                .GetMethods(BindingFlags.Static | BindingFlags.Public)
                .FirstOrDefault(method =>
                    method.Name == nameof(Widgets.ButtonText) &&
                    method.ReturnType == typeof(bool) &&
                    method.GetParameters().Length == 6 &&
                    method.GetParameters()[0].ParameterType == typeof(Rect));

            if (tradeDateDialogType == null || tradeDateField == null ||
                tradeDateComponentProperty == null || doWindowContents == null ||
                buttonText == null)
            {
                Log.Error($"{Tag} COMPAT_UI driver target resolution failed " +
                          $"dialog={tradeDateDialogType != null} field={tradeDateField != null} " +
                          $"component={tradeDateComponentProperty != null} " +
                          $"window={doWindowContents != null} button={buttonText != null}");
                return;
            }

            var harmony = new Harmony("local.mp.meowonlineshop.autotest.compatui");
            harmony.Patch(
                doWindowContents,
                prefix: new HarmonyMethod(
                    typeof(LongRunGameComponent),
                    nameof(TradeDateWindowPrefix)),
                postfix: new HarmonyMethod(
                    typeof(LongRunGameComponent),
                    nameof(TradeDateWindowPostfix)));
            harmony.Patch(
                buttonText,
                prefix: new HarmonyMethod(
                    typeof(LongRunGameComponent),
                    nameof(TradeDateButtonPrefix)));
            Log.Message($"{Tag} COMPAT_UI driver installed.");
        }

        private static void TradeDateWindowPrefix(object __instance)
        {
            if (!CompatibilityUiTest || !simulateTradeDateConfirm ||
                __instance == null || __instance.GetType() != tradeDateDialogType)
                return;

            insideTradeDateDialog = true;
            tradeDateButtonCalls = 0;
        }

        private static void TradeDateWindowPostfix()
        {
            insideTradeDateDialog = false;
        }

        private static bool TradeDateButtonPrefix(ref bool __result)
        {
            if (!insideTradeDateDialog || !simulateTradeDateConfirm)
                return true;

            tradeDateButtonCalls++;
            if (tradeDateButtonCalls != 3)
                return true;

            simulateTradeDateConfirm = false;
            __result = true;
            Log.Message($"{Tag} COMPAT_UI simulated Kiiro trade date confirm.");
            return false;
        }

        private static bool RunCompatibilityUiTest()
        {
            if (compatibilityUiCompleted)
                return true;

            if (tradeDateDialogType == null || tradeDateField == null ||
                tradeDateComponentProperty == null)
            {
                Fail("compatibility UI targets were not resolved");
                return false;
            }

            if (!compatibilityUiInitialized)
            {
                compatibilityUiInitialized = true;
                compatibilityUiStartedAt = Time.realtimeSinceStartup;
                initialTradeDate = ReadTradeFestivalDate();
                expectedTradeDate = (initialTradeDate + 17) % 60;
                if (expectedTradeDate < 0)
                    expectedTradeDate += 60;
                initialTradeDateLetterCount = CountTradeDateLetters();
                Log.Message($"{Tag} COMPAT_UI observing Kiiro date initial={initialTradeDate} " +
                            $"expected={expectedTradeDate} letters={initialTradeDateLetterCount}");
            }

            if (Time.realtimeSinceStartup - compatibilityUiStartedAt > 120f)
            {
                Fail($"compatibility UI timed out currentDate={ReadTradeFestivalDate()} " +
                     $"expected={expectedTradeDate} letters={CountTradeDateLetters()}");
                return false;
            }

            if (Role == "client" && !compatibilityUiIssued)
            {
                compatibilityUiIssued = true;
                try
                {
                    var constructor = tradeDateDialogType.GetConstructor(new[]
                    {
                        typeof(TaggedString),
                        typeof(string),
                        typeof(string),
                        typeof(WindowLayer)
                    });
                    var dialog = constructor?.Invoke(new object[]
                    {
                        (TaggedString)"MP compatibility UI test",
                        null,
                        "MP compatibility UI test",
                        WindowLayer.Dialog
                    }) as Window;
                    if (dialog == null)
                    {
                        Fail("could not construct Kiiro trade date dialog");
                        return false;
                    }

                    Find.WindowStack.Add(dialog);
                    AccessTools.Field(tradeDateDialogType, "quadrum")
                        ?.SetValue(dialog, (Quadrum)(expectedTradeDate / 15));
                    AccessTools.Field(tradeDateDialogType, "day")
                        ?.SetValue(dialog, expectedTradeDate % 15);
                    simulateTradeDateConfirm = true;
                    Log.Message($"{Tag} COMPAT_UI opened Kiiro trade date dialog.");
                }
                catch (Exception e)
                {
                    Fail("could not drive Kiiro trade date dialog: " + e);
                    return false;
                }
            }

            int currentDate = ReadTradeFestivalDate();
            int letterCount = CountTradeDateLetters();
            if (currentDate != expectedTradeDate ||
                letterCount <= initialTradeDateLetterCount)
                return false;

            compatibilityUiCompleted = true;
            Log.Message($"{Tag} COMPAT_UI_COMPLETE role={Role} KiiroTradeDate={currentDate} " +
                        $"matchingLettersBefore={initialTradeDateLetterCount} " +
                        $"matchingLettersAfter={letterCount}");
            return true;
        }

        private static int ReadTradeFestivalDate()
        {
            object component = tradeDateComponentProperty?.GetValue(null, null);
            object value = component == null ? null : tradeDateField?.GetValue(component);
            return value is int date ? date : -1;
        }

        private static int CountTradeDateLetters()
        {
            string title = "Kiiro.TradeFestivalSetUp_LetterTitle".Translate().ToString();
            return Find.LetterStack?.LettersListForReading.Count(letter =>
                string.Equals(letter.Label.ToString(), title, StringComparison.Ordinal)) ?? 0;
        }

        private static bool CreateAndOpenLocalCaravanSession()
        {
            try
            {
                var multiplayerAssembly = typeof(Multiplayer.Client.Multiplayer).Assembly;
                var extensionsType = multiplayerAssembly.GetType("Multiplayer.Client.Extensions", false);
                var mpCompMethod = extensionsType?.GetMethod(
                    "MpComp",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                caravanUiMapComp = mpCompMethod?.Invoke(null, new object[] { Find.CurrentMap });
                if (caravanUiMapComp == null)
                    return false;

                var createMethod = caravanUiMapComp.GetType()
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m =>
                        string.Equals(m.Name, "CreateCaravanFormingSession", StringComparison.Ordinal) &&
                        m.GetParameters().Length == 5);
                caravanUiSession = createMethod?.Invoke(
                    caravanUiMapComp,
                    new object[] { Faction.OfPlayer, false, null, false, null });
                caravanUiProxy = InvokeSessionOpenWindow(caravanUiSession);
                return caravanUiSession != null && caravanUiProxy != null;
            }
            catch (Exception e)
            {
                Log.Error($"{Tag} CARAVAN_UI local session creation failed: {e}");
                return false;
            }
        }

        private static Dialog_FormCaravan InvokeSessionOpenWindow(object session)
        {
            if (session == null)
                return null;
            var method = session.GetType().GetMethod(
                "OpenWindow",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(bool) },
                null);
            return method?.Invoke(session, new object[] { false }) as Dialog_FormCaravan;
        }

        private static void RemoveLocalCaravanSession()
        {
            if (caravanUiMapComp == null || caravanUiSession == null)
                return;
            try
            {
                var manager = caravanUiMapComp.GetType()
                    .GetField(
                        "sessionManager",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(caravanUiMapComp);
                var removeMethod = manager?.GetType()
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m =>
                        string.Equals(m.Name, "RemoveSession", StringComparison.Ordinal) &&
                        m.GetParameters().Length == 1);
                removeMethod?.Invoke(manager, new[] { caravanUiSession });
            }
            catch (Exception e)
            {
                Log.Warning($"{Tag} CARAVAN_UI local session cleanup failed: {e.Message}");
            }
        }

        private static void AdvanceCaravanUiStage(int stage)
        {
            caravanUiStage = stage;
            caravanUiStageAtRealtime = Time.realtimeSinceStartup;
        }

        private static int ReadVehicleSelectedTab()
        {
            try
            {
                var type = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType("Vehicles.Patch_FormCaravanDialog", false))
                    .FirstOrDefault(t => t != null);
                var field = type?.GetField(
                    "selectedTab",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                return field == null ? -1 : (int)field.GetValue(null);
            }
            catch
            {
                return -1;
            }
        }

        private static void SetVehicleSelectedTab(int tab)
        {
            try
            {
                var type = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType("Vehicles.Patch_FormCaravanDialog", false))
                    .FirstOrDefault(t => t != null);
                var field = type?.GetField(
                    "selectedTab",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                field?.SetValue(null, tab);
            }
            catch
            {
                // The tab state is diagnostic-only for this run.
            }
        }

        private static void ReadProxyTabInfo()
        {
            caravanMassProxyTabCount = -1;
            caravanMassProxyTabLabels = string.Empty;
            try
            {
                var field = AccessTools.Field(
                    typeof(Dialog_FormCaravan),
                    "tabsList");
                var tabs = field?.GetValue(null) as List<TabRecord>;
                if (tabs == null)
                    return;

                caravanMassProxyTabCount = tabs.Count;
                caravanMassProxyTabLabels = string.Join(
                    ",",
                    tabs.Select(t => t.label.ToString()));
            }
            catch (Exception e)
            {
                Log.Warning(
                    $"{Tag} CARAVAN_MASS_UI tab info read failed: " + e.Message);
            }
        }

        private static void InstallRigorMortisUiDriver()
        {
            var buttonText = typeof(Widgets)
                .GetMethods(BindingFlags.Static | BindingFlags.Public)
                .FirstOrDefault(method =>
                    method.Name == nameof(Widgets.ButtonText) &&
                    method.ReturnType == typeof(bool) &&
                    method.GetParameters().Length == 6 &&
                    method.GetParameters()[0].ParameterType == typeof(Rect));
            if (buttonText == null)
            {
                Log.Error($"{Tag} RIGOR_UI driver could not resolve Widgets.ButtonText.");
                return;
            }

            var harmony = new Harmony("local.mp.meowonlineshop.autotest.rigormortis");
            harmony.Patch(
                buttonText,
                prefix: new HarmonyMethod(
                    typeof(LongRunGameComponent),
                    nameof(RigorStoryButtonPrefix))
                {
                    priority = Priority.First
                });

            var optionType = AccessTools.TypeByName("QuestEditor_Library.DialogElement_Option");
            var optionDraw = optionType?
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(method =>
                    method.Name == "Draw" &&
                    method.GetParameters().Length == 2 &&
                    method.GetParameters()[0].ParameterType == typeof(float).MakeByRefType() &&
                    method.GetParameters()[1].ParameterType == typeof(Rect));
            if (optionDraw != null)
            {
                harmony.Patch(
                    optionDraw,
                    prefix: new HarmonyMethod(
                        typeof(LongRunGameComponent),
                        nameof(RigorOptionDrawPrefix)) { priority = Priority.First + 100 },
                    postfix: new HarmonyMethod(
                        typeof(LongRunGameComponent),
                        nameof(RigorOptionDrawPostfix)) { priority = Priority.Last });
            }
            else
            {
                Log.Error($"{Tag} RIGOR_UI driver could not resolve DialogElement_Option.Draw.");
            }
            Log.Message($"{Tag} RIGOR_UI driver installed.");
        }

        private static void RigorOptionDrawPrefix()
        {
            rigorOptionDrawActive = true;
        }

        private static void RigorOptionDrawPostfix()
        {
            rigorOptionDrawActive = false;
        }

        private static bool RigorStoryButtonPrefix(ref bool __result)
        {
            if (!simulateRigorStoryOption || rigorStoryWindow == null)
                return true;
            if (Find.WindowStack == null || !Find.WindowStack.IsOpen(rigorStoryWindow))
                return true;

            var windows = Find.WindowStack.Windows;
            if (windows == null || windows.Count == 0 ||
                !ReferenceEquals(windows[windows.Count - 1], rigorStoryWindow))
                return true;

            // CQF constructs its first options in nextOptions and promotes them
            // only after the first window draw. Consume the synthetic click only
            // while a real DialogElement_Option.Draw call is active.
            if (!rigorOptionDrawActive)
                return true;

            simulateRigorStoryOption = false;
            rigorStoryClickIssued = true;
            __result = true;
            Log.Message($"{Tag} RIGOR_STORY simulated real CQF option click node={rigorStoryExpectedNode}.");
            return false;
        }

        public static void SyncPrepareRigorMortisActors()
        {
            try
            {
                if (Find.CurrentMap == null)
                    throw new InvalidOperationException("current map is unavailable");

                FindRigorMortisActors();
                if (rigorZombie != null && rigorTarget != null)
                    return;

                var zombieKind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Axolotl_CommonZombie");
                var mutantDef = DefDatabase<MutantDef>.GetNamedSilentFail("RM_FlyingZombie");
                if (zombieKind == null || mutantDef == null)
                {
                    Log.Error($"{Tag} RIGOR_SETUP missing defs zombieKind={zombieKind != null} mutant={mutantDef != null}.");
                    throw new InvalidOperationException("required Rigor Mortis defs are missing");
                }

                Map map = Find.Maps
                    .Where(candidate => candidate != null)
                    .OrderBy(candidate => candidate.uniqueID)
                    .FirstOrDefault();
                if (map == null)
                    throw new InvalidOperationException("no deterministic Rigor Mortis test map is available");
                IntVec3 zombieCell = map.AllCells
                    .Where(c => c.Standable(map) && c.GetEdifice(map) == null)
                    .OrderBy(c => c.DistanceTo(map.Center))
                    .FirstOrDefault(c =>
                        GenRadial.RadialCellsAround(c, 1.5f, false)
                            .Count(adjacent =>
                                adjacent.InBounds(map) &&
                                adjacent.Standable(map) &&
                                adjacent.GetEdifice(map) == null) >= 2);
                List<IntVec3> adjacentCells = GenRadial.RadialCellsAround(zombieCell, 1.5f, false)
                    .Where(c => c.InBounds(map) && c.Standable(map) && c.GetEdifice(map) == null)
                    .Take(2)
                    .ToList();
                if (adjacentCells.Count < 2)
                    throw new InvalidOperationException("two adjacent test cells are unavailable");
                IntVec3 targetCell = adjacentCells[0];
                IntVec3 clawTargetCell = adjacentCells[1];

                rigorZombie = PawnGenerator.GeneratePawn(zombieKind, Faction.OfPlayer);
                rigorZombie.Name = new NameSingle(RigorZombieName);
                GenSpawn.Spawn(rigorZombie, zombieCell, map);
                MutantUtility.SetFreshPawnAsMutant(rigorZombie, mutantDef);
                rigorZombie.SetFaction(Faction.OfPlayer);
                if (rigorZombie.drafter != null)
                    rigorZombie.drafter.Drafted = true;
                HoldRigorPawn(rigorZombie);
                if (rigorZombie.mindState != null)
                    rigorZombie.mindState.duty = null;
                foreach (Ability ability in rigorZombie.mutant?.AllAbilitiesForReading ?? Enumerable.Empty<Ability>())
                    ability?.ResetCooldown();

                rigorTarget = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                rigorTarget.Name = new NameSingle(RigorTargetName);
                GenSpawn.Spawn(rigorTarget, targetCell, map);
                if (rigorTarget.drafter != null)
                    rigorTarget.drafter.Drafted = true;
                HoldRigorPawn(rigorTarget);

                rigorClawTarget = ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.Steel) as Building;
                if (rigorClawTarget == null)
                    throw new InvalidOperationException("could not create the claw building target");
                GenSpawn.Spawn(rigorClawTarget, clawTargetCell, map);
                Faction hostileFaction = Find.FactionManager.AllFactions
                    .FirstOrDefault(f => f != null && !f.defeated && f.HostileTo(Faction.OfPlayer));
                rigorClawTarget.SetFaction(hostileFaction);

                int abilityCount = rigorZombie.mutant?.AllAbilitiesForReading?.Count ?? 0;
                Log.Message(
                    $"{Tag} RIGOR_SETUP_COMPLETE zombie={rigorZombie.thingIDNumber} target={rigorTarget.thingIDNumber} " +
                    $"clawTarget={rigorClawTarget.thingIDNumber} map={map.uniqueID} " +
                    $"mutant={rigorZombie.mutant?.Def?.defName ?? "null"} " +
                    $"abilities={abilityCount} drafted={rigorZombie.Drafted}.");
                if (abilityCount < RigorAbilityDefs.Length)
                    throw new InvalidOperationException($"only {abilityCount} Rigor Mortis abilities were created");
            }
            catch (Exception e)
            {
                Log.Error($"{Tag} RIGOR_SETUP failed: {e}");
                throw;
            }
        }

        public static void SyncPrepareRigorStoryActor(Pawn pawn)
        {
            if (pawn == null)
                return;

            Faction storyFaction = Find.FactionManager?.AllFactionsListForReading?
                .FirstOrDefault(faction =>
                    faction != null &&
                    faction != Faction.OfPlayer &&
                    !faction.defeated);
            if (storyFaction == null)
                throw new InvalidOperationException("no non-player faction is available for story exit test");

            pawn.SetFaction(storyFaction);
            Log.Message(
                $"{Tag} RIGOR_STORY_FACTION_READY pawn={pawn.thingIDNumber} " +
                $"faction={storyFaction.def?.defName ?? "unknown"}.");
        }

        public static void SyncFinishRigorAbility(Pawn pawn)
        {
            HoldRigorPawn(pawn);
            rigorAwaitingCleanup = false;
        }

        private static void HoldRigorPawn(Pawn pawn)
        {
            if (pawn?.jobs == null)
                return;
            pawn.jobs.StopAll();
            var waitJob = JobMaker.MakeJob(JobDefOf.Wait);
            waitJob.expiryInterval = 999999;
            waitJob.playerForced = true;
            pawn.jobs.StartJob(waitJob, JobCondition.InterruptForced);
            pawn.pather?.StopDead();
        }

        private static void FindRigorMortisActors()
        {
            if (rigorZombie != null && rigorTarget != null)
                return;

            foreach (var map in Find.Maps)
            {
                if (map?.mapPawns?.AllPawns == null)
                    continue;
                foreach (var pawn in map.mapPawns.AllPawns)
                {
                    string name = pawn?.Name?.ToStringShort;
                    if (string.Equals(name, RigorZombieName, StringComparison.Ordinal))
                        rigorZombie = pawn;
                    else if (string.Equals(name, RigorTargetName, StringComparison.Ordinal))
                        rigorTarget = pawn;
                }
            }
        }

        private static bool RunRigorMortisTest()
        {
            if (rigorTestCompleted)
                return true;

            FindRigorMortisActors();
            if (rigorZombie == null || rigorTarget == null)
            {
                if (Role == "host" && !rigorSetupRequested)
                {
                    rigorSetupRequested = true;
                    rigorSetupRequestedAt = Time.realtimeSinceStartup;
                    rigorSetupSyncMethod.DoSync(null);
                    Log.Message($"{Tag} RIGOR_SETUP_REQUESTED role={Role}.");
                }
                else if (rigorSetupRequestedAt >= 0f &&
                         Time.realtimeSinceStartup - rigorSetupRequestedAt > 60f)
                {
                    Fail("Rigor Mortis synchronized actor setup timed out");
                }
                return false;
            }

            if (rigorStageStartedAt < 0f)
                rigorStageStartedAt = Time.realtimeSinceStartup;
            float rigorStageTimeout = TwoMapViewTest && AsyncTimeTest ? 600f : 180f;
            if (Time.realtimeSinceStartup - rigorStageStartedAt > rigorStageTimeout)
            {
                Fail(
                    $"Rigor Mortis stage timed out stage={rigorStage} " +
                    $"zombieSpawned={rigorZombie.Spawned} targetSpawned={rigorTarget.Spawned}");
                return false;
            }

            if (rigorStage < RigorAbilityDefs.Length)
            {
                string defName = RigorAbilityDefs[rigorStage];
                Ability ability = rigorZombie.mutant?.AllAbilitiesForReading?
                    .FirstOrDefault(a => string.Equals(a?.def?.defName, defName, StringComparison.Ordinal));
                if (ability == null)
                {
                    Fail($"Rigor Mortis ability missing def={defName} stage={rigorStage}");
                    return false;
                }

                if (ability.CooldownTicksRemaining <= 0)
                    rigorStageSawReady = true;

                if (rigorStageSawReady && ability.CooldownTicksRemaining > 0)
                {
                    Log.Message(
                        $"{Tag} RIGOR_ABILITY_COMPLETE role={Role} def={defName} " +
                        $"cooldown={ability.CooldownTicksRemaining} pawn={rigorZombie.thingIDNumber} " +
                        $"spawned={rigorZombie.Spawned} job={rigorZombie.CurJobDef?.defName ?? "null"}.");
                    rigorStage++;
                    rigorActionIssued = false;
                    rigorStageSawReady = false;
                    if (Role == "client")
                    {
                        rigorAwaitingCleanup = true;
                        rigorCleanupSyncMethod.DoSync(null, rigorZombie);
                    }
                    rigorStageStartedAt = Time.realtimeSinceStartup;
                    return false;
                }

                if (Role == "client" && !rigorAwaitingCleanup && !rigorActionIssued && rigorZombie.Spawned &&
                    rigorTarget.Spawned)
                {
                    rigorActionIssued = true;
                    try
                    {
                        var command = new Command_Ability(ability, rigorZombie);
                        command.ProcessInput(new Event { type = EventType.MouseDown });
                        if (ability.def.targetRequired)
                        {
                            LocalTargetInfo target;
                            if (string.Equals(defName, "RM_SkillMove", StringComparison.Ordinal))
                            {
                                IntVec3 cell = CellFinder.RandomClosewalkCellNear(
                                    rigorZombie.Position,
                                    rigorZombie.Map,
                                    3);
                                target = new LocalTargetInfo(cell);
                            }
                            else if (string.Equals(defName, "RM_ClawSkill", StringComparison.Ordinal))
                            {
                                target = new LocalTargetInfo(rigorClawTarget);
                            }
                            else
                            {
                                target = new LocalTargetInfo(rigorTarget);
                            }

                            bool valid = ability.verb.ValidateTarget(target, false);
                            Log.Message(
                                $"{Tag} RIGOR_TARGET_VALIDATED role={Role} def={defName} " +
                                $"valid={valid} targetThing={target.Thing?.thingIDNumber ?? 0} " +
                                $"distance={rigorZombie.Position.DistanceTo(target.Cell):F2}.");
                            if (!valid)
                            {
                                Fail($"Rigor Mortis target validation rejected def={defName}");
                                return false;
                            }
                            ability.verb.OrderForceTarget(target);
                        }

                        Log.Message(
                            $"{Tag} RIGOR_ABILITY_ISSUED role={Role} def={defName} id={ability.Id} " +
                            $"targetRequired={ability.def.targetRequired} pawn={rigorZombie.thingIDNumber}.");
                    }
                    catch (Exception e)
                    {
                        Fail($"Rigor Mortis ability issue failed def={defName}: {e}");
                    }
                }

                return false;
            }

            if (rigorStage == RigorAbilityDefs.Length)
            {
                if (Role == "client")
                {
                    if (rigorZombie.Faction == Faction.OfPlayer)
                    {
                        if (!rigorStoryFactionRequested)
                        {
                            rigorStoryFactionRequested = true;
                            rigorStoryFactionSyncMethod.DoSync(null, rigorZombie);
                            Log.Message($"{Tag} RIGOR_STORY faction change requested.");
                        }
                        return false;
                    }

                    if (rigorStoryWindow == null)
                    {
                        if (!rigorZombie.Spawned)
                        {
                            Fail("Rigor story actor left map before story test opened");
                            return false;
                        }
                        rigorStoryWindow = CreateRigorStoryWindow();
                        if (rigorStoryWindow == null)
                        {
                            Fail("could not create real Rigor Mortis CQF story window");
                            return false;
                        }
                        Find.WindowStack.Add(rigorStoryWindow);
                        rigorStoryExpectedNode = 0;
                        rigorStoryClickIssued = false;
                        simulateRigorStoryOption = true;
                        rigorStageStartedAt = Time.realtimeSinceStartup;
                        Log.Message($"{Tag} RIGOR_STORY opened real CQF window tree=RM_Dialog_01_Taoist.");
                        return false;
                    }

                    if (rigorZombie.Spawned)
                    {
                        int currentNode = ReadRigorStoryNode(rigorStoryWindow);
                        if (currentNode > rigorStoryExpectedNode)
                        {
                            Log.Message(
                                $"{Tag} RIGOR_STORY_NODE_COMPLETE role={Role} " +
                                $"from={rigorStoryExpectedNode} to={currentNode}.");
                            rigorStoryExpectedNode = currentNode;
                            rigorStoryClickIssued = false;
                            rigorStageStartedAt = Time.realtimeSinceStartup;
                            simulateRigorStoryOption = true;
                        }
                        else if (currentNode == rigorStoryExpectedNode &&
                                 !rigorStoryClickIssued &&
                                 !simulateRigorStoryOption &&
                                 Find.WindowStack.IsOpen(rigorStoryWindow))
                        {
                            simulateRigorStoryOption = true;
                        }

                        // Batch-mode RimWorld does not guarantee an OnGUI pass for a
                        // newly opened Window. If no real option Draw occurs, exercise
                        // the exact registered compatibility command against this real
                        // CQF window and its real option multicast action.
                        if (currentNode == rigorStoryExpectedNode &&
                            !rigorStoryClickIssued &&
                            Time.realtimeSinceStartup - rigorStageStartedAt > 1f)
                        {
                            DispatchRigorStoryOption(currentNode);
                        }
                    }
                }

                if (!rigorZombie.Spawned)
                {
                    rigorStage++;
                    rigorStageStartedAt = Time.realtimeSinceStartup;
                    Log.Message(
                        $"{Tag} RIGOR_STORY_COMPLETE role={Role} actorLeftMap=True " +
                        $"windowOpen={(rigorStoryWindow != null && Find.WindowStack.IsOpen(rigorStoryWindow))}.");
                }
                return false;
            }

            rigorTestCompleted = true;
            Log.Message(
                $"{Tag} RIGOR_COMPLETE role={Role} abilities={RigorAbilityDefs.Length} " +
                $"storyNodes=6 actorLeftMap={!rigorZombie.Spawned}.");
            return true;
        }

        private static void DispatchRigorStoryOption(int nodeIndex)
        {
            try
            {
                Type patchType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(assembly => assembly.GetType(
                        "MP_MeowOnlineShop.Patch_RigorMortisStoryDialogs",
                        false))
                    .FirstOrDefault(type => type != null);
                var syncField = patchType?.GetField(
                    "SyncSelectOptionMethod",
                    BindingFlags.Static | BindingFlags.NonPublic);
                var syncMethod = syncField?.GetValue(null) as ISyncMethod;
                if (syncMethod == null)
                {
                    Fail("Rigor story registered SyncSelectOption method is unavailable");
                    return;
                }

                simulateRigorStoryOption = false;
                rigorStoryClickIssued = true;
                syncMethod.DoSync(
                    null,
                    rigorZombie,
                    rigorTarget,
                    "RM_Dialog_01_Taoist",
                    -1,
                    nodeIndex,
                    0);
                Log.Message(
                    $"{Tag} RIGOR_STORY dispatched registered CQF option command node={nodeIndex}.");
            }
            catch (Exception e)
            {
                Fail($"Rigor story registered option dispatch failed: {e}");
            }
        }

        private static bool AnyRigorAbilityCasting()
        {
            var abilities = rigorZombie?.mutant?.AllAbilitiesForReading;
            return abilities != null && abilities.Any(a => a != null && a.Casting);
        }

        private static Window CreateRigorStoryWindow()
        {
            try
            {
                Type treeType = AccessTools.TypeByName("QuestEditor_Library.DialogTreeDef");
                if (treeType == null)
                    return null;
                Type databaseType = typeof(DefDatabase<>).MakeGenericType(treeType);
                MethodInfo getNamed = AccessTools.Method(
                    databaseType,
                    "GetNamedSilentFail",
                    new[] { typeof(string) });
                object tree = getNamed?.Invoke(null, new object[] { "RM_Dialog_01_Taoist" });
                MethodInfo create = AccessTools.Method(
                    treeType,
                    "CreateCQFDialog",
                    new[] { typeof(Thing), typeof(Thing), typeof(Quest) });
                return create?.Invoke(
                    tree,
                    new object[] { rigorZombie, rigorTarget, null }) as Window;
            }
            catch (Exception e)
            {
                Log.Error($"{Tag} RIGOR_STORY create failed: {e}");
                return null;
            }
        }

        private static int ReadRigorStoryNode(Window window)
        {
            if (window == null)
                return -1;
            try
            {
                object node = AccessTools.Field(window.GetType(), "curNode")?.GetValue(window);
                object value = AccessTools.Field(node?.GetType(), "index")?.GetValue(node);
                return value == null ? -1 : Convert.ToInt32(value);
            }
            catch
            {
                return -1;
            }
        }

        private static void InstallGravshipDiagnostics()
        {
            try
            {
                MethodInfo runCmds = AccessTools.Method(typeof(TickPatch), "RunCmds");
                if (runCmds != null)
                {
                    GravshipDiagnosticsHarmony.Patch(
                        runCmds,
                        prefix: new HarmonyMethod(
                            typeof(LongRunGameComponent),
                            nameof(GravshipRunCmdsPrefix)));
                }

                MethodInfo schedule = AccessTools.Method(
                    typeof(MultiplayerSession),
                    "ScheduleCommand");
                if (schedule != null)
                {
                    GravshipDiagnosticsHarmony.Patch(
                        schedule,
                        postfix: new HarmonyMethod(
                            typeof(LongRunGameComponent),
                            nameof(GravshipScheduleCommandPostfix)));
                }

                PatchGravshipLifecycle(
                    typeof(GravshipUtility),
                    "GenerateGravship",
                    nameof(GravshipGeneratePostfix),
                    new[] { typeof(Building_GravEngine) });
                PatchGravshipLifecycle(
                    typeof(WorldComponent_GravshipController),
                    "InitiateTakeoff",
                    nameof(GravshipInitiateTakeoffPostfix),
                    new[] { typeof(Building_GravEngine), typeof(PlanetTile) });
                PatchGravshipLifecycle(
                    typeof(WorldComponent_GravshipController),
                    "TakeoffEnded",
                    nameof(GravshipTakeoffEndedPostfix),
                    Type.EmptyTypes);
                PatchGravshipLifecycle(
                    typeof(GravshipUtility),
                    "TravelTo",
                    nameof(GravshipTravelToPostfix),
                    new[] { typeof(Gravship), typeof(PlanetTile), typeof(PlanetTile) });
                PatchGravshipLifecycle(
                    typeof(GravshipUtility),
                    "ArriveNewMap",
                    nameof(GravshipArriveNewMapPostfix),
                    new[] { typeof(Gravship) });
                PatchGravshipLifecycle(
                    typeof(WorldComponent_GravshipController),
                    "LandingEnded",
                    nameof(GravshipLandingEndedPostfix),
                    Type.EmptyTypes);
                PatchGravshipLifecycle(
                    typeof(GravshipUtility),
                    "AbandonMap",
                    nameof(GravshipAbandonMapPostfix),
                    new[] { typeof(Map) });

                Log.Message($"{Tag} GRAVSHIP diagnostics installed.");
            }
            catch (Exception e)
            {
                Log.Error($"{Tag} GRAVSHIP diagnostics install failed: {e}");
            }
        }

        private static void PatchGravshipLifecycle(
            Type declaringType,
            string methodName,
            string postfixName,
            Type[] parameterTypes)
        {
            MethodInfo target = AccessTools.Method(
                declaringType,
                methodName,
                parameterTypes);
            if (target == null)
            {
                Log.Warning(
                    $"{Tag} GRAVSHIP lifecycle target missing " +
                    $"{declaringType?.Name}.{methodName}.");
                return;
            }

            GravshipDiagnosticsHarmony.Patch(
                target,
                postfix: new HarmonyMethod(
                    typeof(LongRunGameComponent),
                    postfixName));
        }

        private static void GravshipRunCmdsPrefix()
        {
            if (!GravshipSyncTest ||
                Multiplayer.Client.Multiplayer.Client == null)
                return;

            try
            {
                int timer = TickPatch.Timer;
                foreach (ITickable tickable in TickPatch.AllTickables)
                {
                    if (tickable == null)
                        continue;

                    object cmds = tickable.GetType()
                        .GetProperty("Cmds")
                        ?.GetValue(tickable, null);
                    if (cmds == null)
                        continue;

                    PropertyInfo countProperty = cmds.GetType()
                        .GetProperty("Count");
                    if (countProperty == null ||
                        (int)countProperty.GetValue(cmds, null) == 0)
                        continue;

                    MethodInfo peekMethod = cmds.GetType()
                        .GetMethod("Peek", Type.EmptyTypes);
                    ScheduledCommand cmd = peekMethod?.Invoke(cmds, null)
                        as ScheduledCommand;
                    if (cmd == null)
                        continue;

                    if (cmd.ticks != timer || cmd.type != CommandType.Sync)
                        continue;

                    ITickable target = TickPatch.TickableById(cmd.mapId);
                    if (target != null)
                        continue;

                    gravshipOrphanCmdCount++;
                    Log.Error(
                        $"{Tag} GRAVSHIP_ORPHAN_CMD count={gravshipOrphanCmdCount} " +
                        $"handler={ResolveSyncHandlerName(cmd)} {cmd} " +
                        $"timer={timer} frozen={TickPatch.serverFrozen} " +
                        $"frozenAt={TickPatch.frozenAt} maps=[" +
                        string.Join(
                            ",",
                            Find.Maps.Select(m => m.uniqueID.ToString()).ToArray()) +
                        "]");
                }
            }
            catch (Exception e)
            {
                Log.Warning($"{Tag} GRAVSHIP orphan probe failed: {e.Message}");
            }
        }

        private static string ResolveSyncHandlerName(ScheduledCommand cmd)
        {
            try
            {
                if (cmd.data == null || cmd.data.Length < 4)
                    return "noData";

                int id = BitConverter.ToInt32(cmd.data, 0);
                var handlers = AccessTools
                    .Field(typeof(Sync), "handlers")
                    ?.GetValue(null) as System.Collections.IList;
                if (handlers != null && id >= 0 && id < handlers.Count)
                    return handlers[id]?.ToString() ?? $"hash={id}";
                return $"hash={id}";
            }
            catch (Exception e)
            {
                return "resolveFailed:" + e.Message;
            }
        }

        private static void GravshipScheduleCommandPostfix(
            ScheduledCommand cmd)
        {
            if (!GravshipSyncTest || cmd == null || cmd.type != CommandType.Sync)
                return;

            try
            {
                string handler = ResolveSyncHandlerName(cmd);
                if (handler.IndexOf("Grav", StringComparison.OrdinalIgnoreCase) < 0 &&
                    handler.IndexOf("Pilot", StringComparison.OrdinalIgnoreCase) < 0 &&
                    handler.IndexOf("Launch", StringComparison.OrdinalIgnoreCase) < 0)
                    return;

                Log.Message(
                    $"{Tag} GRAVSHIP_SCHEDULED handler={handler} {cmd} " +
                    $"timer={TickPatch.Timer} maps=[" +
                    string.Join(
                        ",",
                        Find.Maps.Select(m => m.uniqueID.ToString()).ToArray()) +
                    "]");
            }
            catch (Exception e)
            {
                Log.Warning($"{Tag} GRAVSHIP schedule probe failed: {e.Message}");
            }
        }

        private static void GravshipGeneratePostfix(Gravship __result)
        {
            if (!GravshipSyncTest || __result == null)
                return;
            Log.Message(
                $"{Tag} GRAVSHIP_GENERATED id={__result.ID} " +
                $"tile={__result.Tile} tick={TickPatch.Timer} " +
                $"maps=[{string.Join(",", Find.Maps.Select(m => m.uniqueID.ToString()).ToArray())}]");
        }

        private static void GravshipInitiateTakeoffPostfix(
            Building_GravEngine engine,
            PlanetTile targetTile)
        {
            if (!GravshipSyncTest || engine == null)
                return;
            Log.Message(
                $"{Tag} GRAVSHIP_TAKEOFF engine={engine.thingIDNumber} " +
                $"map={engine.Map?.uniqueID} from={engine.Map?.Tile} to={targetTile} " +
                $"tick={TickPatch.Timer}");
        }

        private static void GravshipTakeoffEndedPostfix()
        {
            if (!GravshipSyncTest)
                return;
            Log.Message(
                $"{Tag} GRAVSHIP_TAKEOFF_ENDED tick={TickPatch.Timer} " +
                $"maps=[{string.Join(",", Find.Maps.Select(m => m.uniqueID.ToString()).ToArray())}] " +
                $"frozen={TickPatch.serverFrozen}");
        }

        private static void GravshipTravelToPostfix(
            Gravship gravship,
            PlanetTile oldTile,
            PlanetTile newTile)
        {
            if (!GravshipSyncTest || gravship == null)
                return;
            Log.Message(
                $"{Tag} GRAVSHIP_TRAVEL id={gravship.ID} from={oldTile} to={newTile} " +
                $"tick={TickPatch.Timer}");
        }

        private static void GravshipArriveNewMapPostfix(Gravship gravship)
        {
            if (!GravshipSyncTest || gravship == null)
                return;
            Log.Message(
                $"{Tag} GRAVSHIP_ARRIVE_NEW_MAP id={gravship.ID} " +
                $"dest={gravship.destinationTile} tick={TickPatch.Timer} " +
                $"mapsBefore=[{string.Join(",", Find.Maps.Select(m => m.uniqueID.ToString()).ToArray())}]");
        }

        private static void GravshipLandingEndedPostfix()
        {
            if (!GravshipSyncTest)
                return;
            Log.Message(
                $"{Tag} GRAVSHIP_LANDING_ENDED tick={TickPatch.Timer} " +
                $"maps=[{string.Join(",", Find.Maps.Select(m => m.uniqueID.ToString()).ToArray())}]");
        }

        private static void GravshipAbandonMapPostfix(Map map)
        {
            if (!GravshipSyncTest || map == null)
                return;
            Log.Message(
                $"{Tag} GRAVSHIP_ABANDON_MAP map={map.uniqueID} tile={map.Tile} " +
                $"tick={TickPatch.Timer}");
        }

        public static void SyncGravshipPrepare(int engineId)
        {
            Building_GravEngine engine = FindGravshipEngineById(engineId);
            if (engine == null)
            {
                Log.Error($"{Tag} GRAVSHIP_PREPARE engine=null id={engineId}");
                return;
            }

            engine.cooldownCompleteTick = -1;
            if (engine.launchInfo == null)
            {
                engine.launchInfo = new LaunchInfo
                {
                    quality = 1f,
                    doNegativeOutcome = false
                };
            }

            try
            {
                AccessTools.Field(typeof(ResearchManager), "gravEngineInspected")
                    ?.SetValue(Find.ResearchManager, true);
            }
            catch
            {
                // The inspection flag is a convenience for the test world.
            }

            gravshipPrepareExecuted++;
            Log.Message(
                $"{Tag} GRAVSHIP_PREPARE engine={engine.thingIDNumber} " +
                $"tile={engine.Map?.Tile} map={engine.Map?.uniqueID} " +
                $"cooldown={engine.cooldownCompleteTick} fuel={engine.TotalFuel:F1} " +
                $"maxDist={engine.MaxLaunchDistance}");
        }

        public static void SyncGravshipLaunch(
            int engineId,
            PlanetTile target)
        {
            Building_GravEngine engine = FindGravshipEngineById(engineId);
            if (engine == null || !target.Valid)
            {
                Log.Error(
                    $"{Tag} GRAVSHIP_LAUNCH invalid engine={engine != null} id={engineId} target={target}");
                return;
            }

            gravshipLaunchExecuted++;
            Log.Message(
                $"{Tag} GRAVSHIP_LAUNCH engine={engine.thingIDNumber} " +
                $"from={engine.Map?.Tile} to={target} tick={TickPatch.Timer} mapsBefore=[" +
                string.Join(
                    ",",
                    Find.Maps.Select(m => m.uniqueID.ToString()).ToArray()) +
                "]");
            Find.GravshipController.InitiateTakeoff(engine, target);
        }

        private static Building_GravEngine FindGravshipEngine()
        {
            foreach (Map map in Find.Maps)
            {
                if (map == null)
                    continue;

                Building_GravEngine engine = map.listerThings.AllThings
                    .OfType<Building_GravEngine>()
                    .FirstOrDefault(e =>
                        e != null &&
                        e.Spawned &&
                        e.Faction == Faction.OfPlayer);
                if (engine != null)
                {
                    Log.Message(
                        $"{Tag} GRAVSHIP found engine id={engine.thingIDNumber} " +
                        $"map={map.uniqueID} pos={engine.Position} tile={map.Tile}");
                    return engine;
                }
            }
            return null;
        }

        private static Building_GravEngine FindGravshipEngineById(int id)
        {
            foreach (Map map in Find.Maps)
            {
                if (map == null)
                    continue;

                Building_GravEngine engine = map.listerThings.AllThings
                    .OfType<Building_GravEngine>()
                    .FirstOrDefault(e =>
                        e != null &&
                        e.thingIDNumber == id);
                if (engine != null)
                    return engine;
            }

            return null;
        }

        private static PlanetTile FindGravshipDestination(
            Building_GravEngine engine)
        {
            if (engine == null || engine.Map == null)
                return PlanetTile.Invalid;

            PlanetTile from = engine.Map.Tile;
            int maxDist = Math.Max(1, engine.MaxLaunchDistance);
            int limit = Math.Min(Find.WorldGrid.TilesCount, 60000);
            for (int i = 0; i < limit; i++)
            {
                PlanetTile candidate = new PlanetTile(i, from.Layer);
                if (!candidate.Valid || candidate.Equals(from))
                    continue;

                int distance = Find.WorldGrid.TraversalDistanceBetween(
                    from,
                    candidate);
                if (distance <= 0 || distance > maxDist)
                    continue;
                if (!GravshipUtility.TryGetPathFuelCost(
                        from,
                        candidate,
                        out float cost,
                        out _,
                        10f,
                        engine.FuelUseageFactor))
                    continue;
                if (cost > engine.TotalFuel)
                    continue;
                StringBuilder reason = new StringBuilder();
                if (!TileFinder.IsValidTileForNewSettlement(
                        candidate,
                        reason,
                        forGravship: true))
                    continue;

                return candidate;
            }

            return PlanetTile.Invalid;
        }

        private static bool RunGravshipSyncTest(int elapsed)
        {
            if (gravshipStage >= 4)
                return true;

            if (gravshipStageAtRealtime < 0f)
                gravshipStageAtRealtime = Time.realtimeSinceStartup;

            if (Time.realtimeSinceStartup - gravshipStageAtRealtime > 600f)
            {
                Fail(
                    $"gravship sync stage timed out stage={gravshipStage} " +
                    $"elapsed={elapsed} orphanCmds={gravshipOrphanCmdCount}");
                return false;
            }

            switch (gravshipStage)
            {
                case 0:
                    gravshipEngine = FindGravshipEngine();
                    if (gravshipEngine == null)
                    {
                        Log.Message($"{Tag} GRAVSHIP waiting for engine.");
                        return false;
                    }

                    gravshipPrepareDispatched++;
                    Log.Message(
                        $"{Tag} GRAVSHIP dispatch prepare " +
                        $"engine={gravshipEngine.thingIDNumber} " +
                        $"map={gravshipEngine.Map?.uniqueID}.");
                    gravshipPrepareSyncMethod.DoSync(
                        null,
                        gravshipEngine.thingIDNumber);
                    gravshipStage = 1;
                    gravshipStageAtRealtime = -1f;
                    return false;

                case 1:
                    if (gravshipPrepareExecuted < gravshipPrepareDispatched)
                        return false;

                    PlanetTile destination =
                        FindGravshipDestination(gravshipEngine);
                    if (!destination.Valid)
                    {
                        Fail("gravship destination selection failed");
                        return false;
                    }

                    gravshipTargetTile = destination.ToString();
                    gravshipBaselineMapCount = Find.Maps.Count;
                    gravshipLastMapCount = Find.Maps.Count;
                    gravshipLaunchDispatched++;
                    Log.Message(
                        $"{Tag} GRAVSHIP dispatch launch " +
                        $"engine={gravshipEngine.thingIDNumber} target={destination} " +
                        $"maps={Find.Maps.Count}.");
                    gravshipLaunchSyncMethod.DoSync(
                        null,
                        gravshipEngine.thingIDNumber,
                        destination);
                    gravshipStage = 2;
                    gravshipStageAtRealtime = -1f;
                    return false;

                case 2:
                    if (gravshipLaunchExecuted < gravshipLaunchDispatched)
                        return false;

                    Log.Message(
                        $"{Tag} GRAVSHIP launch executed; monitoring transition.");
                    gravshipStage = 3;
                    gravshipStageAtRealtime = -1f;
                    return false;

                case 3:
                    int maps = Find.Maps.Count;
                    if (maps != gravshipLastMapCount)
                    {
                        Log.Message(
                            $"{Tag} GRAVSHIP mapCountChanged " +
                            $"from={gravshipLastMapCount} to={maps} " +
                            $"tick={TickPatch.Timer} maps=[" +
                            string.Join(
                                ",",
                                Find.Maps.Select(m => m.uniqueID.ToString())
                                    .ToArray()) +
                            "] orphanCmds={gravshipOrphanCmdCount}");
                        gravshipLastMapCount = maps;
                    }

                    if (elapsed >= 12000)
                    {
                        gravshipStage = 4;
                        Log.Message(
                            $"{Tag} GRAVSHIP_TEST_DONE role={Role} " +
                            $"tick={TickPatch.Timer} elapsed={elapsed} maps={maps} " +
                            $"orphanCmds={gravshipOrphanCmdCount} target={gravshipTargetTile}");
                        return true;
                    }

                    return false;
            }

            return true;
        }

        private static void Fail(string reason)
        {
            if (terminalLogged)
                return;

            terminalLogged = true;
            terminalAtRealtime = Time.realtimeSinceStartup;
            Log.Error($"{Tag} FAILED role={Role} {reason}");
        }
    }
}
