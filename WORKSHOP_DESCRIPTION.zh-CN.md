# [MP] 联机兼容补丁

联机交流群：432965131

面向 **RimWorld Multiplayer（ZWMultiplayer）** 的 Harmony 联机兼容补丁合集。

本模组主要为 **Perspective Shift、RJW 系列、部分种族与剧情扩展** 提供专项联机修正，重点改善操作同步、随机数确定性、Pawn 状态、工作指令、特殊移动及长时间联机稳定性。

本模组**不替代**官方的 **Multiplayer Compatibility** 整包，而是对实际使用的模组进行补充兼容。

---

## 硬性依赖

* **Multiplayer**

  * Package ID：`rwmt.Multiplayer`

---

# Perspective Shift 联机兼容

针对 **Perspective Shift** 提供以下优化：

* WASD 移动输入同步
* 移动与视角操作的确定性处理
* 防止客户端输入状态不一致
* 降低连续按键和特殊移动造成的不同步
* 特殊移动、飞行状态及联机命令同步

同时支持：

* **Milira Race 使用 Perspective Shift 时的飞行操作**
* 米莉拉飞行状态、移动输入与特殊命令同步

Steam 创意工坊：

`https://steamcommunity.com/sharedfiles/filedetails/?id=3686618980`

---

# 种族与剧情扩展兼容

## Milira Race

主要兼容内容：

* 种族核心玩法与特殊能力
* 武器模式与飞行行为
* Perspective Shift 飞行操作
* 逆重力飞船与穿梭机流程
* Pawn 上下飞船及地图切换状态
* 降低特殊移动时出现掉线或不同步的风险

附属兼容：

* **Milira Event Story Expand**

---

## Kiiro Race

主要兼容内容：

* 种族状态与特殊能力
* 剧情事件和延迟操作
* Pawn、事件与剧情流程同步
* 降低剧情触发时的不同步或掉线风险

附属兼容：

* **Kiiro Story: Events Expanded**

---

## Ratkin

主要兼容内容：

* Pawn 状态与交互
* 工作、装备和战斗行为
* 武器操作与生成流程

附属兼容：

* **Ratkin Weapons**

目前不包含鼠族异象相关内容。

---

## MoeLotl Race

主要兼容内容：

* 种族核心状态与行为
* 特殊工作、能力和交互
* 长时间联机稳定性

附属兼容：

* **MoeLotl: Rigor Mortis（七日重生）**

---

## Insect Girls

主要兼容内容：

* 种族状态与特殊能力
* Pawn 生成、工作和战斗交互
* 随机行为确定性
* 降低联机不同步和疑似掉线风险

---

# RJW 系列联机兼容

本模组为 **RJW 核心及常用扩展** 提供专项联机优化。

主要处理：

* 核心动作与长时间行为同步
* 工作下达、目标选择和完成回调
* Hediff、需求、经验与技能数据
* Pawn 关系、年龄检查及状态变化
* 动画动作链与实际游戏行为同步
* 生殖、怀孕、周期和产物状态
* 基因赋予、遗传、出生与 Pawn 生成
* 交易、事件、信件和延迟操作
* 家具 Gizmo、菜单及确认窗口
* 重复点击与重复命令防护
* 随机数、生成顺序和序列化确定性
* 长时间联机运行稳定性

## 已纳入优化范围

### 核心与动画

* **RJW Core / RimJobWorld**
* **RJW Sexperience**
* **Rimworld Animations 2.0**

### 生殖与身体状态

* **RJW Menstruation Cycle**
* **Sized Apparel for RJW**
* **RJW Cumpilation**

### 家具与玩法扩展

* **RJW Onahole**
* **RJW PE**
* **RJW SexSlaveCraft**
* **RJW Brothel Colony 1.6 Test**

### 事件与交易

* **RJW Events**
* **RJW Ero Traders**

### 基因与种族

* **RJW Genes**
* **RJW Fantasy Races**

### 关系与浪漫

* **RJW Romance Tweaks / More Options**
* **Rimder Romance PE Patch**

> RJW 各版本之间可能存在程序集、方法签名及依赖差异。补丁会通过类型和方法检测尝试安全启用；目标结构不匹配时，将停用对应补丁，而不是强制修改未知版本。

---

# 飞船与穿梭机稳定性

主要处理：

* 逆重力飞船操作导致的掉线
* 穿梭机起飞、降落及地图切换异常
* Pawn 上下飞船后的状态不一致
* 地图、载具、容器及世界对象写入顺序
* 连续操作造成的重复命令或无效状态

---

# 其他兼容内容

本模组还包含少量针对以下内容的联机修正：

* 喵呜网店与 Meow Framework 交互
* Milira 武器模式
* Rigor Mortis
* 部分 Gizmo、右键菜单与目标选择
* UI 回调及重复点击防护
* 随机生成顺序确定性
* 多地图 TPS 调度
* 中优先级警报 UI 降频

其中喵呜网店相关修正包括商品买卖、订单、通信台、交互窗口及随机结果同步。

---

# 内置 TPS 优化

本模组默认启用确定性的多地图调度，以减少非主要地图的持续性能消耗。

主要特点：

* 所有客户端使用相同调度规则
* 不依赖本地帧率或真实时间
* 多地图之间公平轮换
* 保留战斗地图与关键状态更新
* 避免客户端 Tick 频率不一致

请勿与以下类型的优化同时使用：

* 时间膨胀
* Tick 节流
* 地图休眠
* 全局 Tick 重写
* Pawn Tick 频率重写
* 其他多人 TPS 调度模组

否则可能造成逻辑延迟、不同步、Desync 或掉线。

---

# 警报 UI 降频

3.0.18 加入了可配置的中优先级警报检查降频。

该功能只减少部分 UI 重复计算，不修改实际游戏状态。高优先级警报、同步命令执行及极速模拟期间仍保留原版行为。

检测到方法签名变化或其他补丁冲突时，会自动回退原版逻辑。

> MissileGirl / RocketMan 本体仍不兼容 Multiplayer。本功能不代表已实现 RocketMan 的联机兼容。

---

# 加载顺序

请将本模组置于：

1. **Multiplayer 之后**
2. 需要兼容的 **Perspective Shift、RJW、种族及剧情模组之后**
3. 使用喵呜网店时，置于 **Meow Framework 与喵呜网店本体之后**

推荐原则：

> 被兼容模组在前，本兼容补丁在后。

---

# 使用前确认

* 当前支持 **RimWorld 1.6**
* 主机与客户端使用相同的模组列表和版本
* RJW 程序集来自同一套整合或发布来源
* 未同时启用同类 Tick、TPS 或时间膨胀优化
* 加载顺序符合相关模组要求

---

# 问题排查

发生冲突、掉线或不同步时，请优先检查：

1. Multiplayer 与各模组版本是否一致
2. 加载顺序是否正确
3. 是否存在重复的 Harmony 兼容补丁
4. 是否启用了其他 Tick 或 TPS 优化
5. RJW 程序集是否来自不同来源
6. 模组更新后是否修改了方法签名
7. 关闭特定附属模组后是否仍能稳定复现

本模组包含大量按类型检测的可选兼容补丁。未安装对应模组时，启动日志中可能出现较多黄色警告，这是正常现象。

反馈问题时请提供：

* 完整模组列表
* `Player.log`
* Multiplayer 日志
* Desync 日志或同步追踪文件
* 问题发生前的具体操作
* 是否能够稳定复现
* 主机与客户端文件是否完全一致

---

# 兼容性说明

本模组旨在改善特定整合环境下的多人体验，但无法保证所有模组组合和版本均不会发生不同步。

实际兼容效果仍可能受到模组版本、加载顺序、程序集差异及其他 Harmony 补丁影响。

当补丁无法安全应用时，会优先停止对应修正或回退原版行为，以降低存档损坏和严重联机不同步的风险。

---

**作者：尹怨怨**
github：https://github.com/KuiYiyinRua/MP-Race-Compatibility-Plus