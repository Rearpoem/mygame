# 第二轮战斗模块交接

本轮在现有 `Prototype.Combat` 纯 C# 模块内扩展完整自动战斗、可重放对照与连续出征。所有新敌情、伤害、WAIT、经验和掉落均为可替换试玩参数。共享项目上下文未修改，公共接口、账本入账、界面、场景和整包构建仍由「Unity 工程与集成」会话统一接入。

## 改动路径与入口

| 路径（相对 Game） | 用途 |
| --- | --- |
| `Assets/Prototype/Combat/Runtime/CombatModels.cs` | 核心配置、状态、快照、完整战斗结果 DTO |
| `Assets/Prototype/Combat/Runtime/CombatSession.cs` | `Step()` 与 `RunToCompletion()` 共用结算；同刻排序、无动作休眠、胜负／僵局、固定 WAIT 状态与跨场构造 |
| `Assets/Prototype/Combat/Runtime/CombatExpedition.cs` | `CombatExpeditionSession.RunNextEncounter()`／`RunToEnd()`；只读结算、经验与公库应收结果 |
| `Assets/Prototype/Combat/Runtime/CombatSampleRuns.cs` | 单项配置对照、首次真实行动分歧、可读战报格式化 |
| `Assets/StreamingAssets/Prototype/Combat/default.json` | 保留首轮急救／普攻改序开局，原文件未改写 |
| `Assets/StreamingAssets/Prototype/Combat/round2_*.json` | 第二轮两种敌情、容量、队列、连续出征与装备调用配置 |
| `Assets/Prototype/Combat/Tests/EditMode/` | 实际模块 Edit Mode 回归与第二轮验收用例 |
| `Assets/Prototype/Combat/Reports/GenerateReports.ps1` | 从真实 JSON 编译实际 Runtime、复跑四组对照与三组队列，并生成下面的战报 |

Unity 仍使用 `JsonUtility.FromJson<T>()` 从配置载入 DTO，Runtime 本身不读取文件、不引用 UnityEngine。字段和数组继续与首轮消费者兼容。`CombatSession.CopyScenario/CopySnapshot/CopyStep` 提供深复制。完整临时口径见 [ASSUMPTIONS.md](../ASSUMPTIONS.md)，测试及调用概览见 [README.md](../README.md)。

## 两份正式配置对照

| 配置与战报 | 唯一变量 | 实跑结果 |
| --- | --- | --- |
| `round2_ambush.json`；[伏击对照](ROUND2_AMBUSH_REPORT.md)／[结构化记录](ROUND2_AMBUSH_REPORT.json) | 法师把第 2 条普攻移到第 1 条；技能内容、初始 HP/MP、敌人、种子和装备不变 | 进攻优先 WAIT 0 击倒伏击手，两名队员均存活；急救优先消耗 6 MP，队友 WAIT 5 阵亡，WAIT 40 才获胜 |
| `round2_pressure.json`；[持续压力对照](ROUND2_PRESSURE_REPORT.md)／[结构化记录](ROUND2_PRESSURE_REPORT.json) | 同样仅改两条指令的顺序 | 条件急救分支 WAIT 190 胜利、保留两人，法师剩 18 MP；进攻优先 WAIT 240 胜利、仅法师存活，法师剩 64 HP |

`CombatSampleRuns.ComparePriority(config)` 返回两份输入配置、两份实际 `CombatRunResult` 与 `firstDivergence`。它先验证指令仅重新排列，归一化顺序后逐字段比较其他配置；发现其他改动会抛错。首次分歧比较实际记录的时间、状态、行动者、行动、对象、MP 和效果，不因条件尝试文本不同而提前宣称分歧。每份战报列完整事件链、首个倒下者和战后 HP/MP。

容量单独隔离，不混入上述正式对照：[第三条应急指令](ROUND2_CAPACITY_REPORT.md)／[结构化记录](ROUND2_CAPACITY_REPORT.json)，配置为 `round2_capacity.json`，调用 `CompareIntelligenceCapacity(config)`。两侧只有法师智力 10→20 的差异，HP、MP、伤害、装备与三条完整规则清单一致。每条规则均仅有一个条件项，条件项数量解锁没有参与结果。低智力容量为 2，第 3 条不能执行，WAIT 5 队友阵亡、WAIT 185 全队败北；高智力容量为 3，第 3 条在 WAIT 0 急救，WAIT 190 两人存活获胜。快照与逐条尝试可供界面显示装载限制。

## 单步与自动、队列停止原因

```csharp
var session = new CombatSession(config);
CombatStepResult next = session.Step();
// 自动继续同一个 session，内部仍调用 Step。
CombatRunResult complete = session.RunToCompletion();
```

`round2_time_tie.json`、`round2_one_idle.json`、`round2_stalemate.json` 是持久化重放样例。[队列战报](ROUND2_QUEUE_REPORT.md)／[结构化结果](ROUND2_QUEUE_REPORT.json)记录同刻的裁决理由、一人无可执行指令仍由其他人完成战斗，以及双方无法改变局面时的明确停止原因。报告生成器分别全程单步和自动推进这三个实际配置，并核对最终快照和完整文本日志相同。

同刻排序、休眠唤醒、状态到期优先和防止无进展的停止策略均按第二轮试玩假设实施，详见模块假设表。`Victory/Defeat/Draw/Stalemate/Stopped` 与 `winnerTeam/stopReason` 应由集成界面展示；僵局和预算停止不得展示为胜利。

## 连续两场与奖励应收

配置：`round2_expedition.json`；[连续两场战报](ROUND2_EXPEDITION_REPORT.md)／[完整快照与事件](ROUND2_EXPEDITION_REPORT.json)。

```csharp
CombatExpeditionConfig config = JsonUtility.FromJson<CombatExpeditionConfig>(json);
var expedition = new CombatExpeditionSession(config);
CombatEncounterReport first = expedition.RunNextEncounter();
CombatEncounterReport second = expedition.RunNextEncounter();
CombatExpeditionReport viewed = expedition.GetReport();

// 同一起点重新实例化；与上面的逐场结果完全一致。
CombatExpeditionReport batched = new CombatExpeditionSession(config).RunToEnd();
```

- 第一场从 WAIT 0 开始，WAIT 190 胜利；法师 HP 100/100、MP 18/30，队友 HP 62/100、MP 10/10。
- 明确经过 **15 WAIT**，第二场从 WAIT 205 开始。HP/MP 不恢复；短易伤在 WAIT 200 到期，第二场不会从模板复活它。`camp_guard` 原到期 260、`camp_empower` 原到期 400，按原绝对时刻保留。队友原定行动时刻 250 也继续保留。
- 第二场 WAIT 310 胜利；法师 HP 100/100、MP 18/30，队友 HP 42/100、MP 10/10。队友护卫状态在 WAIT 260 真实到期并产生记录，法师增伤状态仍有效。
- 第一场经验池 21，法师应收 11、队友 10；第二场经验池 31，法师应收 16、队友 15。总计 52。试玩假设为胜利时全体出场队员均分，包含本场倒下者，余数按队伍配置顺序分配。
- 第一场公库应收现金 10、`expedition_ore`×2；第二场现金 15、矿料×3。合计现金 25、矿料 5。物品标识与数量可由内容／工程替换。
- 任一场败北、平局、僵局或预算停止，立即停止本次出征，该场不产生经验或掉落，已获胜场次的应收结果保留。`CompletedAllEncounters` 表示全部请求场次已完成。

每场报告含 `expeditionId/encounterId/encounterIndex/resultId`、前场结束／场间推进后／本场开局／本场结束快照、实际事件、结局、奖励资格和停止原因。`afterIntermission` 是实际跨场构造后抽出的携带队伍视图，不使用另一份状态推进算法。

`resultId = expeditionId + ":battle:" + 场次序号`。每次真实派遣由工程给唯一出征 ID；使用相同 ID 重跑只用于同一实验。`GetReport()`、重复 `RunToEnd()` 和终局后 `RunNextEncounter()` 都不会再次战斗或发奖。返回值为副本。**本模块只返回应入公库和应分配经验，真正入账和按 resultId 去重由工程账本完成。**

为避免第二场开始才报错，出征构造会预检所有场次携带队伍的 ID 集合、maxHP/maxMP 和格位一致。缺省 `scenario.playerTeam` 在复制配置中规范为 `carryTeam`，因此将敌人排在 JSON 数组首位不会反转出征视角；显式配置的不同视角会被拒绝，输入对象不被修改。

## 装备强化读取样例

配置：`round2_equipment.json`；[强化战报](ROUND2_EQUIPMENT_REPORT.md)／[结构化记录](ROUND2_EQUIPMENT_REPORT.json)。

```csharp
CombatScenario battleInput = CombatSession.CopyScenario(template);
// resolvedBonus 由工程根据已装备武器与强化记录计算。
foreach (CombatUnitConfig member in battleInput.units)
    if (member.id == "mage")
    {
        member.equipmentSourceId = equippedWeaponId;
        member.equipmentDamageBonus = resolvedBonus;
    }
var nextBattle = new CombatSession(battleInput);
// 若要保留已出征的 HP/MP/状态，使用同一个输入加携带快照：
var continuedBattle = new CombatSession(battleInput, previousBattleEnd, "friendly", betweenWait);
```

战斗侧本轮只接收已解析的增伤数值和来源 ID，不读取基地库存、强化配方或存档。核心对 `damage` 叠加该数值，逐行动记录 `equipmentDamageBonus`；它不提高治疗量，也不改变 HP/MP 上限。样例仅将增伤 0→9，同一敌人从普攻 18×3 击倒，变为 27×2 击倒；结束时刻 120→60，队员剩余 HP 84→92。装备来源、其他队伍状态、指令、敌人和种子全部相同。

## 复跑与测试证据

在项目根目录运行：

```powershell
& './Game/Assets/Prototype/Combat/Reports/GenerateReports.ps1'
```

该命令编译实际四份 Runtime 源码，读取真实配置并重新生成报告，不含另一份战斗模拟实现。实际样例验收已通过，结果见 [ROUND2_SAMPLE_VERIFICATION.json](ROUND2_SAMPLE_VERIFICATION.json)：四组单项对照、三组队列单步／自动一致、两场逐场／批量一致、真实 HP/MP 承接、15 WAIT 场间到期和报告读取纯度。父会话另用最终源码独立生成一次，13 份报告产物逐文件 SHA-256 与本目录一致。

**真实 Unity 6000.3.28f1 Combat Edit Mode 测试：53／53 通过，0 失败、0 跳过，退出码 0。** [测试 XML](../../../../../work/combat_round2_unity_validation_20261114/ResultsRun02/EditMode.xml) · [执行摘要](../../../../../work/combat_round2_unity_validation_20261114/ResultsRun02/summary.json) · [43 个模块输入文件的源／副本 SHA-256 清单](../../../../../work/combat_round2_unity_validation_20261114/source_manifest_run02.json)。

本次使用与原工程相同 Unity 版本的隔离验证工程，实际复制当前 Combat 源码、测试与配置，摘要确认 sourceContentUnchanged=true、copiedContentMatchesSource=true；原工程锁保持。验证范围是 Combat Edit Mode，Base、UI、Play Mode 和整包构建由工程后续接入验证。首轮 `default.json` 保持 SHA-256 `3F93E870F7A395984846B71642DC4FDAB6F0855839E3433BEA01C83A16033F30`。

## 仍待决定的规则

数值公式、固定同刻优先顺序的正式设计、角色休眠与唤醒、重复状态的刷新、前排选择权重、百分比 MP 费用、胜利奖励／经验分配、场间时间与世界时间的映射均是后续设计项。咏唱、暴击／闪避即时行动、移动／后撤和完整阵型效果未在本轮现有瞬时 heal/damage/apply_status 核心中实现，不能从这一轮原型推导它们已经完成。试玩事件／WAIT 预算只负责给出停止原因，不作为正式玩法的动作上限。

## 给 Unity 工程与集成

请从现有 `CombatSession.Step/RunToCompletion` 接单步与自动播放；逐场观看和批量派遣共用 `CombatExpeditionSession`，读取每场稳定 resultId 的经验与公库应收 DTO 后，由公共账本完成一次性入账。配置使用 StreamingAssets/Prototype/Combat 下的 round2 文件，保留 default.json 首轮回归。两份正式战报分别是 ROUND2_AMBUSH_REPORT.md 与 ROUND2_PRESSURE_REPORT.md，另有容量、队列、两场承接和装备读取证据。将工程解析的强化效果填入 equipmentDamageBonus/equipmentSourceId，再用携带构造进入下一场即可保持真实消耗。界面、公共接口、场景和 Windows 整包构建由工程统一接入。
