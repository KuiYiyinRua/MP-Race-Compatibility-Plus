// Action bodies from the target mod's shipped 1.6 source, executed inside one MP command.
using System;
using System.Linq;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using RatkinUnderground;
using Verse;
namespace Meow.RatkinUndergroundCompatibility
{
    internal sealed class RadioActions
    {
        readonly Dialog_RKU_Radio dialog;
        public RadioActions(Dialog_RKU_Radio value) { dialog=value; }
        Thing radio=>dialog.radio;
        RKU_RadioGameComponent radioComponent=>dialog.radioComponent;
        bool isRoyalRadioMode { get=>(bool)Radio.Get(dialog,"isRoyalRadioMode"); set=>Radio.Set(dialog,"isRoyalRadioMode",value); }
        void AddMessage(string message)=>dialog.AddMessage(message);
        public void Scan()
        {

                // 触发扫描相关对话事件
                RKU_DialogueManager.TriggerDialogueEvents(dialog, "scan");
                AddMessage("RKU_StartScanningSignal".Translate());

                // 防止刷海里
                int tile = -1;
                for (int i = 0; i < 50; i++)
                {
                    int randTile = Utils.GetRadiusTiles(radio.Map.Tile, 20);
                    Tile worldTile = Find.WorldGrid[randTile];
                    if (!worldTile.WaterCovered && !worldTile.hilliness.Equals(Hilliness.Impassable))
                    {
                        tile = randTile;
                        break;
                    }
                }

                if (tile == -1)
                {
                    Log.Error("[RKU] 尝试多次后仍未在半径20内找到合适的非海洋/非不可逾越山的落地点");
                    return;
                }

                    try
                {
                    // 如果你看到这行，我跟军爷抢饭去了，回来再修
                    // 2025/9/23 别动这块了，修了一晚上，我怕
                    // worldObjectClass要使用RatkinUnderground.RKU_MapParent，走自定义逻辑进地图（实则生成地图）
                    // RKU_MapParentModExtension用于标记是否为生成地图，防止钻机遭遇的地图有多个进入方法

                    /*WorldObjectDef def = incidentMap.RandomElement();
                    WorldObject worldObject = WorldObjectMaker.MakeWorldObject(def);
                    worldObject.Tile = tile;
                    worldObject.SetFaction(Faction.OfPlayer);
                    Find.WorldObjects.Add(worldObject); Current.Game.GetComponent<RadioStore>().scanned.Add(worldObject);*/

                    List<WorldObjectDef> worldObjectDefs = DefDatabase<WorldObjectDef>.AllDefsListForReading
                                .Where(d => d.defName != null &&
                                d.defName.StartsWith("RKU_MapParent") &&
                                d.GetModExtension<RKU_MapParentModExtension>() != null)
                                .ToList();
                    if (worldObjectDefs == null || worldObjectDefs.Count == 0)
                    {
                        Log.Error("[RKU] 没有找到任何 RKU_Incident 开头的 IncidentDef，无法生成地图/世界物体。");
                        return;
                    }
                    WorldObjectDef def = worldObjectDefs.RandomElement();
                    var ext = def.GetModExtension<RKU_MapParentModExtension>();
                    if (ext == null)
                    {
                        Log.Warning($"[RKU] 选中的 IncidentDef {def.defName} 没有 RKU_MapParentModExtension；将跳过设置 spawnMap 标记。");
                    }
                    // Scanned-site identity is saved per world object.
                    Map map = Find.Maps.FirstOrDefault(m => m.Tile == tile);
                    // build parms - 使用 StorytellerUtility 获取合理默认值
                    WorldObject worldObject = WorldObjectMaker.MakeWorldObject(def);
                    worldObject.Tile = tile;
                    worldObject.SetFaction(Faction.OfPlayer);
                    Find.WorldObjects.Add(worldObject); Current.Game.GetComponent<RadioStore>().scanned.Add(worldObject);
                    // 指定 tile（许多 world 级事件会使用 parms.targetTile）
                    /*parms.faction = null;
                    parms.target = map;
                    def.Worker.TryExecute(parms);*/
                    string label = "RKU_SiteDiscoveredLabel".Translate(def.label);
                    string text = "RKU_SiteDiscoveredText".Translate(def.label);
                    LookTargets lookTargets = new LookTargets(worldObject);
                    Find.LetterStack.ReceiveLetter(label, text, LetterDefOf.PositiveEvent, lookTargets);

                    radioComponent.canScan = false;
                    radioComponent.lastScanTick = Find.TickManager.TicksGame;

                    Log.Message($"[RKU] 成功在 tile {tile} 生成世界物体：{def.defName}");
                }
                catch (Exception e)
                {
                    Log.Error($"[RKU] 扫描信号生成地图发生错误：{e}");
                }

        }
        public void Emergency()
        {

                AddMessage("RKU_EmergencyCallSent".Translate());
                var emergencyEvents = DefDatabase<RKU_DialogueEventDef>.AllDefs
                    .Where(e => e.defName.StartsWith("RKU_EmergencyCall"))
                    .ToList();

                if (emergencyEvents.Count > 0)
                {
                    RKU_DialogueEventDef randomEvent = emergencyEvents.RandomElement();
                    RKU_DialogueManager.ExecuteDialogueEvent(randomEvent, dialog);
                }

        }
        public void Support()
        {

                int conditionMet = 0;
                if (radioComponent != null)
                {
                    //敌对线
                    if (radioComponent.ralationshipGrade <= -25)
                    {
                        QuestScriptDef questDef = DefDatabase<QuestScriptDef>.GetNamed("RKU_OpportunitySite_GuerrillaCamp", false);
                        if (radioComponent.ralationshipGrade == -25 && !Find.QuestManager.QuestsListForReading.Any(o => o.root == questDef) &&
                            radioComponent.canRescue)
                        {
                            conditionMet = -2;
                            radioComponent.canRescue = false;
                            radioComponent.lastRescueTick = Find.TickManager.TicksGame;
                        }
                        else if (radioComponent.ralationshipGrade == -50)
                        {
                            // 检查事件是否已经触发过（只能触发一次）
                            string eventKey = "RKU_RatkinTunnel_Thi";
                            bool hasTriggered = radioComponent.triggeredOnceEvents != null && radioComponent.triggeredOnceEvents.Contains(eventKey);
                            if (!hasTriggered &&
                                radioComponent.canRescue)
                            {
                                conditionMet = -3;
                                radioComponent.canRescue = false;
                                radioComponent.lastRescueTick = Find.TickManager.TicksGame;
                            }
                            else
                            {
                                conditionMet = -1; // 已经触发过，切换到王国军频段但没人接
                            }
                        }
                        else if (radioComponent.ralationshipGrade == -75)
                        {
                            // 检查事件是否已经触发过（只能触发一次）
                            string eventKey = "RKU_IncidentWorker_FinalRaid";
                            bool hasTriggered = radioComponent.triggeredOnceEvents != null && radioComponent.triggeredOnceEvents.Contains(eventKey);
                            if (!hasTriggered &&
                                radioComponent.canRescue)
                            {
                                conditionMet = -4;
                                radioComponent.canRescue = false;
                                radioComponent.lastRescueTick = Find.TickManager.TicksGame;
                            }
                            else
                            {
                                conditionMet = -1; // 已经触发过，切换到王国军频段但没人接
                            }
                        }
                        else
                        {
                            conditionMet = -1;
                        }
                    }
                    else
                    {
                        //农场
                        if (radioComponent.ralationshipGrade >= 15 && radioComponent.ralationshipGrade <= 100)
                        {
                            bool hasTriggeredWarLord1 = radioComponent.triggeredOnceEvents != null && radioComponent.triggeredOnceEvents.Contains("RKU_ProvideSupport_FarmRaid");
                            if (!hasTriggeredWarLord1 &&
                                radioComponent.canRescue)
                            {
                                conditionMet = 1;
                                radioComponent.canRescue = false;
                                radioComponent.lastRescueTick = Find.TickManager.TicksGame;
                            }
                            else
                            {
                                //城堡
                                if (radioComponent.ralationshipGrade >= 30)
                                {
                                    bool hasTriggeredWarLord2 = radioComponent.triggeredOnceEvents != null && radioComponent.triggeredOnceEvents.Contains("RKU_ProvideSupport_WarLord2");
                                    if (!hasTriggeredWarLord2 &&
                                        radioComponent.canRescue)
                                    {
                                        conditionMet = 2;
                                        radioComponent.canRescue = false;
                                        radioComponent.lastRescueTick = Find.TickManager.TicksGame;
                                    }
                                    else
                                    {
                                        //炮楼
                                        if (radioComponent.ralationshipGrade >= 55)
                                        {
                                            bool hasTriggeredFarm = radioComponent.triggeredOnceEvents != null && radioComponent.triggeredOnceEvents.Contains("RKU_ProvideSupport_WarLord1");
                                            if (!hasTriggeredFarm &&
                                                radioComponent.canRescue)
                                            {
                                                conditionMet = 3;
                                                radioComponent.canRescue = false;
                                                radioComponent.lastRescueTick = Find.TickManager.TicksGame;
                                            }
                                            else
                                            {
                                                //古代设施
                                                if (radioComponent.ralationshipGrade >= 76)
                                                {
                                                    bool hasTriggeredAncient = radioComponent.triggeredOnceEvents != null && radioComponent.triggeredOnceEvents.Contains("RKU_ProvideSupport_AncientRaid");
                                                    if (!hasTriggeredAncient &&
                                                        radioComponent.canRescue)
                                                    {
                                                        conditionMet = 4;
                                                        radioComponent.canRescue = false;
                                                        radioComponent.lastRescueTick = Find.TickManager.TicksGame;
                                                    }
                                                    else
                                                    {
                                                        //工厂防御
                                                        if (radioComponent.ralationshipGrade >= 80)
                                                        {
                                                            bool hasTriggeredFactoryDefense = radioComponent.triggeredOnceEvents != null && radioComponent.triggeredOnceEvents.Contains("RKU_ProvideSupport_FactoryDefense");
                                                            if (!hasTriggeredFactoryDefense &&
                                                                radioComponent.canRescue)
                                                            {
                                                                conditionMet = 5;
                                                                radioComponent.canRescue = false;
                                                                radioComponent.lastRescueTick = Find.TickManager.TicksGame;
                                                            }
                                                            else
                                                            {
                                                                conditionMet = 0;
                                                            }
                                                        }
                                                        else
                                                        {
                                                            conditionMet = 0;
                                                        }
                                                    }
                                                }
                                                else
                                                {
                                                    conditionMet = 0;
                                                }
                                            }
                                        }
                                        else
                                        {
                                            conditionMet = 0;
                                        }
                                    }
                                }
                                else
                                {
                                    conditionMet = 0;
                                }
                            }
                        }
                        else
                        {
                            conditionMet = 0;
                        }
                    }
                }
                // 触发
                switch (conditionMet)
                {
                    case 0: // 没事，游击队情况
                        isRoyalRadioMode = false;
                        RKU_DialogueEventDef nobodyEventA = DefDatabase<RKU_DialogueEventDef>.GetNamed("RKU_StartupEventZeroA", false);
                        RKU_DialogueEventDef nobodyEventB = DefDatabase<RKU_DialogueEventDef>.GetNamed("RKU_StartupEventZeroB", false);
                        if (radioComponent.ralationshipGrade > 0)
                        {
                            RKU_DialogueManager.ExecuteDialogueEvent(nobodyEventA, dialog);
                        }
                        else
                        {
                            RKU_DialogueManager.ExecuteDialogueEvent(nobodyEventB, dialog);
                        }
                        break;
                    case -1: // 好感度小于等于-25，切换到王国军频段但没人接
                        isRoyalRadioMode = true;
                        RKU_DialogueEventDef nobodyEvent = DefDatabase<RKU_DialogueEventDef>.GetNamed("RKU_StartupEventZero", false);
                        if (nobodyEvent != null)
                        {
                            RKU_DialogueManager.ExecuteDialogueEvent(nobodyEvent, dialog);
                        }
                        break;
                    case -2: // 好感度等于-25且没有任务可接受，触发与王国军交涉对话并开启任务
                        isRoyalRadioMode = true;
                        QuestScriptDef questDef = DefDatabase<QuestScriptDef>.GetNamed("RKU_OpportunitySite_GuerrillaCamp", false);
                        if (questDef != null && !Find.QuestManager.QuestsListForReading.Any(o => o.root == questDef))
                        {
                            RKU_DialogueEventDef negotiationEvent = DefDatabase<RKU_DialogueEventDef>.GetNamed("RKU_ProvideSupport_RoyalNegotiation", false);
                            if (negotiationEvent != null)
                            {
                                RKU_DialogueManager.ExecuteDialogueEvent(negotiationEvent, dialog);
                            }
                        }
                        break;
                    case -3: // 好感度等于-50，触发对话并触发伊文事件
                        isRoyalRadioMode = true;
                        RKU_DialogueEventDef negotiationEventC = DefDatabase<RKU_DialogueEventDef>.GetNamed("RKU_ProvideSupport_RoyalNegotiationB", false);
                        if (negotiationEventC != null)
                        {
                            RKU_DialogueManager.ExecuteDialogueEvent(negotiationEventC, dialog);
                        }
                        break;
                    case -4: // 好感度等于-75，触发对话并触发血战
                        isRoyalRadioMode = true;
                        RKU_DialogueEventDef negotiationEventD = DefDatabase<RKU_DialogueEventDef>.GetNamed("RKU_ProvideSupport_RoyalNegotiationC", false);
                        if (negotiationEventD != null)
                        {
                            RKU_DialogueManager.ExecuteDialogueEvent(negotiationEventD, dialog);
                        }
                        break;

                    case 1: // 农场任务：好感度 >= 15
                        isRoyalRadioMode = false;
                        RKU_DialogueEventDef farmEvent = DefDatabase<RKU_DialogueEventDef>.GetNamed("RKU_ProvideSupport_FarmRaid", false);
                        if (farmEvent != null)
                        {
                            RKU_DialogueManager.ExecuteDialogueEvent(farmEvent, dialog);
                        }
                        break;

                    case 2: // 城堡：好感度 >= 30
                        isRoyalRadioMode = false;
                        RKU_DialogueEventDef warLordCastleEvent = DefDatabase<RKU_DialogueEventDef>.GetNamed("RKU_ProvideSupport_WarLord2", false);
                        if (warLordCastleEvent != null)
                        {
                            RKU_DialogueManager.ExecuteDialogueEvent(warLordCastleEvent, dialog);
                        }
                        break;
                    case 3: // 炮楼：好感度 >= 55
                        isRoyalRadioMode = false;
                        RKU_DialogueEventDef warLordEvent = DefDatabase<RKU_DialogueEventDef>.GetNamed("RKU_ProvideSupport_WarLord1", false);
                        if (warLordEvent != null)
                        {
                            RKU_DialogueManager.ExecuteDialogueEvent(warLordEvent, dialog);
                        }
                        break;
                    case 4: // 古代设施任务：好感度 >= 76
                        isRoyalRadioMode = false;
                        RKU_DialogueEventDef ancientEvent = DefDatabase<RKU_DialogueEventDef>.GetNamed("RKU_ProvideSupport_AncientRaid", false);
                        if (ancientEvent != null)
                        {
                            RKU_DialogueManager.ExecuteDialogueEvent(ancientEvent, dialog);
                        }
                        break;
                    case 5: // 工厂防御任务：好感度 >= 80 且农场任务已完成，触发对话并开启工厂防御任务
                        isRoyalRadioMode = false;
                        RKU_DialogueEventDef factoryDefenseEvent = DefDatabase<RKU_DialogueEventDef>.GetNamed("RKU_ProvideSupport_FactoryDefense", false);
                        if (factoryDefenseEvent != null)
                        {
                            RKU_DialogueManager.ExecuteDialogueEvent(factoryDefenseEvent, dialog);
                        }
                        break;
                    default:
                        break;
                }

        }
    }
}
