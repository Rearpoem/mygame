# 第四轮战斗模块：法师技能、条件指令与自动出征

本模块让玩家配置的队伍自动完成战斗，并带着实际 HP、MP、未到期状态和剩余正常 WAIT 进入下一场。单步、自动、对照报告和出征都调用同一个 `Prototype.Combat.CombatSession.Step()`；没有第二份演示结算。`Runtime/` 仅引用常规 `System`，程序集保留 `noEngineReferences: true`。

所有数字均为试玩参数，未完成平衡；各轮口径见 [ASSUMPTIONS.md](ASSUMPTIONS.md)。共享上下文、公共接口、界面、场景与整包构建由对应会话负责，本模块没有修改它们。本文记录实际 Runtime、规则测试与调用；GUI 与整包由工程统一接入验收。

## 第四轮：可执行的法师技能与战局条件

`round4_mage_library.json` 提供法师技能库，技能多于可装指令槽；40智力仍为5条指令／3项条件，10智力为2条／1项。`mage_`动作包含冥想、吐纳、传蓝、汲魔、单／排／列／全体魔攻、治疗、防护、增伤、削弱、加速、减速、净化、驱散、沉默与定身。技能是否可装仍以角色真实白名单为准；新增技能不绕过容量、资源或编辑校验。

四段指令与第三轮编辑API保持。条件目录扩展至37项，包括HP／MP绝对值与缺口、增益／减益／控制、状态kind、角色tags、同队／敌对存活人数与伤员数、同排人数、状态／预约剩余WAIT及是否为施放者。选择方式24项，包括MP值／缺口、增益／受控软优先、智力、预约时刻与当前速度。每项仍逐候选读取；显式actor与1／2／3项AND／OR括号的语义不变。详细ID和字段见 [ROUND4_API_HANDOFF.md](Reports/ROUND4_API_HANDOFF.md)。

`CombatTacticsOption` 的新字段 `category/defaultValue/thresholdUnit` 为兼容性补充：value条件使用合法defaultValue；只有thresholdUnit为ratio才按百分比显示，points／count／wait／row分别为点数／人数／WAIT／排号。`integerThreshold`单独约束整数，不能用它推断百分比。新增 `GetStatusKinds/GetTargetModes/GetDamageTypes/GetEffectKinds` 均返回独立副本。

| 效果 | 当前实际结算 |
| --- | --- |
| restore_mp | 恢复实际MP并封顶；满MP或maxMp=0不合法，整条无收益则跳过，不扣费／WAIT |
| drain_mp | 扣目标实际MP再返还施放者，返还封顶，溢出销毁；禁止自吸，目标无MP时跳过 |
| cleanse／dispel | 分别清除有害／有益状态；净化也清除旧永久易伤测试标记，无可清除内容时跳过 |
| heal／damage／apply_status | 延续原结算；支持技能定义的single／row／column／all范围 |

群体技能先逐候选判断完整表达式并选锚点，再覆盖锚点同team、原scope内存活且对此效果合法的成员。锚点条件不重复筛每个覆盖成员；例如低血锚点所在排的其他伤员也接受行疗。self范围不会扩大成全队；满HP／满MP等按效果资格过滤。群体只支付一次MP与WAIT，覆盖名单在扣费前计算。旧`targetId`仍是锚点，`targetIds[]`与候选`affectedTargetIds[]`记录实际主覆盖；汲魔返还施放者另列effect。

状态新增magic_guard／weakened／haste／slow／silenced／stunned，连同原guard／empowered／vulnerable共9类。magic_guard只减magic伤害；weakened减少伤害输出，不减少治疗／回蓝。速度系数为原agilityFactor乘`(100+slow总点数)/(100+haste总点数)`，按行动提交前状态计算本次WAIT，不修改已经预约的时刻，状态到期仍用固定WAIT。沉默仅阻止`isMagic=true`的行动；`damageType=legacy/physical/magic`独立描述伤害，缺省保留legacy。定身阻止主动行动并休眠，到期／净化可唤醒，尚余正常WAIT不被取消。

新MP／状态effects保留真实HP前后，并增加MP前后、状态施加／到期、移除状态ID和永久易伤前后字段，避免旧报告把恢复MP误读为死亡。step的施放者MP前后仍表示整次行动；新baseAgilityFactor／statusWaitFactor解释最终WAIT系数。tags与当前effectiveAgilityFactor可从角色快照读取。

工程合并同ID技能时使用权威静态入口 `CombatSession.ActionDefinitionsEquivalent(a,b)`，需要独立规范副本时调用 `NormalizeActionDefinition(action)`。它们只统一可选字符串null／空、缺省single与legacy，其他真实参数差异保持不同；比较不代替合法性校验，也不改输入。旧Clone／日志及技能白名单presence语义保留。

本轮仍为瞬发技能，没有实现咏唱准备、随机命中／暴击、闪避反击、DoT、复活、后撤、AP／PP或所有职业。六项基础属性与百分比MP费用仍待后续设计。

**第四轮实际局部验证：Unity 6000.3.28f1／Test Framework 1.6.0，242／242 Combat EditMode通过，0失败、0跳过，退出码0。** 其中旧111项行为回归保留，新增131项；复制91个实际Runtime／测试／配置／meta／版本文件到独立工程并记录复制时SHA，其中42个代码／测试／配置／版本内容在测试前后SHA一致。meta允许Unity导入规范化，不用于导入后的内容稳定判定。没有启动原Game或删除锁，没有执行全工程测试、GUI、PlayMode或构建。

旧测试仅有一处必要适配：`CombatTacticsTests.CatalogEntries...`从固定状态值`test_mark`改读目录合法`defaultValue`，供新增状态kind条件使用；原编辑、求值及副本断言保留，其他五份旧测试源码与第三轮SHA一致。首次真实Unity为242／241／1，失败来自新增`is_not_actor`测试误把另一队友作为反例；只改为排除actor，继续断言选中队友a，未改运行时或配置。首次XML／日志／输入清单均保留。

证据：[第四轮验证摘要](Tests/Results/ROUND4_UNITY_VERIFICATION.json)、[实际NUnit XML](Tests/Results/ROUND4_COMBAT_EDITMODE.xml)、[最终源与副本SHA清单](Tests/Results/ROUND4_SOURCE_MANIFEST.json)、[首次失败XML](Tests/Results/ROUND4_INITIAL_FAILED_EDITMODE.xml)、[验收矩阵](Tests/ROUND4_ACCEPTANCE_MATRIX.md)。27技能逐项执行、5组单变量战例、冥想风险、两场承接与旧强化等另经最终Runtime独立生成，**17／17产物逐文件SHA一致**：[报告重放哈希](Tests/Results/ROUND4_REPORT_REPLAY_HASHES.json)。本轮调用、参数与全部战报见 [ROUND4_HANDOFF.md](Reports/ROUND4_HANDOFF.md)。

## 第三轮：四段指令编辑

四段表达仍是 `条件表达式｜对象范围｜选择方式｜行动`。`CombatSession` 提供以下实例方法，索引均从 0 开始：

```csharp
CombatEditResult InsertInstruction(string unitId, int index, CombatInstruction instruction);
CombatEditResult ReplaceInstruction(string unitId, int index, CombatInstruction instruction);
CombatEditResult RemoveInstruction(string unitId, int index);
CombatEditResult MoveInstruction(string unitId, int fromIndex, int toIndex);
CombatActionConfig[] GetAvailableActions(string unitId);
```

成功编辑立即作用于后续选择，成为 Reset 的重放基线，不自行回血、回蓝或重排已有 WAIT；错误通过 `success/message/code` 返回且不改变配置、快照或历史。返回附带 `instructionCapacity/conditionTermCapacity`。Insert 不能新增到容量之外，Replace 必须使用有效槽位且符合条件项容量；旧配置的超额指令继续显示禁用，Move 可将旧规则调入有效槽，Remove 可删除超额项。

角色新增 `availableActionIds` 和 `availableActionIdsExplicit`。非空白名单只开放该角色技能；旧 JSON 的缺省标记 false 配合 null／空数组时，从其原指令引用的已知且已实现行动推导。明确没有技能使用标记 true 配合空数组（true＋null也规范为空）。这保留 Unity JsonUtility 把原始 null 数组写成空数组后的旧 DTO 往返语义。会话导出的 GetScenario 固化真实列表并置标记 true，删除指令不会忘记技能。

`GetAvailableActions` 返回独立副本，不因当前 MP 不足隐藏技能；未知角色返回空数组。全场 `actions[]` 包含敌技，不能用它直接替代友军技能目录。

`CombatTacticsCatalog.GetConditions/GetScopes/GetSelectors/GetOperations/GetGroupings/GetSubjects()` 提供真实能力目录的独立 DTO，含 label、说明、阈值范围／默认值／整数要求及状态 ID 输入提示。不会把尚未实现的咏唱、后撤或即时反击当作可用动作。

条件新增 `subject/value`：subject 缺省或 `target` 读取当前候选；显式 `actor` 才读取施放者。HP／MP 比例阈值均为 0–1，`has_status/lacks_status` 的 value 为状态 ID，`row_is/row_at_most/row_at_least` 的 threshold 为 0／1／2（前／中／后）。支持 HP／MP 的 ≤、≥、易伤、指定状态有无和站位条件。

兼容旧 `all/any`。三项混合条件使用 `operation=mixed`、两个 `operators`（and／or）及 `grouping`：left 表示 `(A op0 B) op1 C`，right 表示 `A op0 (B op1 C)`。每个候选独立判断完整表达式，actor 项也占一个智力条件项，不把不同队员的真值拼成真。

选择方式包含配置顺序、HP 绝对值／比例最高最低、MP 比例最高最低、前排／后排优先、易伤优先。软优先只在合法且通过条件的候选中排序，没找到偏好对象时仍可选择其他候选；同值最终按配置顺序。最大 MP 为 0 的比例暂按 0 处理。

第三轮配置：`round3_tactics.json` 为 GUI 编辑素材；`round3_mp_reserve.json` 仅改施放者 MP 门槛，`round3_focus_mark.json` 仅改队友目标选择方式。新动作采用 `tactics_` 前缀，与第二轮同名旧动作的参数隔离；MP 例使用独立 `tactics_quick_attack` 表达与普通攻击不同的 WAIT。状态 ID 仍按实际状态配置读取。真实 API 细节见 [ROUND3_API_HANDOFF.md](Reports/ROUND3_API_HANDOFF.md)。

**第三轮实际局部验证：Unity 6000.3.28f1／Test Framework 1.6.0，111／111 Combat EditMode 通过，0 失败、0 跳过，退出码 0。** 包括原 53 项回归和新增 58 项；原四份测试源码保持第二轮 SHA 一致，旧 raw JsonUtility 往返断言未删除。使用独立验证副本，57 个实际代码／测试／配置／meta 文件记录源与副本 SHA，测试前后运行内容一致；没有占用工程的原 Game，也没有进行 GUI／PlayMode／整包验收。

首次 107 项中有 1 项失败，发现旧 null 技能数组经 Unity 序列化成为空数组会丢技能；Runtime 增加 presence 标记修复，新增四种往返回归后重跑全部通过。首次失败 XML、日志和输入清单保留。证据见 [第三轮验证摘要](Tests/Results/ROUND3_UNITY_VERIFICATION.json)、[实际 NUnit XML](Tests/Results/ROUND3_COMBAT_EDITMODE.xml)、[最终输入清单](Tests/Results/ROUND3_SOURCE_MANIFEST.json)、[首次失败 XML](Tests/Results/ROUND3_INITIAL_FAILED_EDITMODE.xml)。

另用最终 Runtime 独立复跑第三轮生成器，7 份产物与交付报告逐文件 SHA 完全一致：[独立报告核对](Tests/Results/ROUND3_REPORT_REPLAY_HASHES.json)。预留 MP、标记集火的首次实际分歧仍为 WAIT 40／15，默认急救阈值对照为 WAIT 30；第二轮携带与强化结果继续通过。第二轮 53 项历史证据保留在后文。

## 保留首轮回归

`Assets/StreamingAssets/Prototype/Combat/default.json` 保留原始样例。默认第一步：WAIT 0，法师急救队友，HP 20→65，MP 30→24，下次行动 WAIT 40。仅交换法师两条指令后：普攻敌人甲，HP 35→17，法师 MP 仍为 30，下次 WAIT 60。仅将队友开局调为满血则急救无合格目标，回退普攻。

`Reset()` 保留编辑后的顺序、阈值、开局 HP，恢复其开局基线与记录；它是重跑工具，不能用作下一场战斗的自动回血。恢复磁盘默认值需重新从 JSON 创建会话。

## 单步与完整战斗

JSON 可用 Unity `JsonUtility.FromJson<CombatScenario>` 读取。运行时不自行读文件，由工程选择加载方式。

```csharp
using Prototype.Combat;

var session = new CombatSession(config);
CombatStepResult oneEvent = session.Step(); // 行动、休眠或状态到期中的一个事件
CombatRunResult completed = session.RunToCompletion();
CombatSnapshot finalState = completed.snapshot;
CombatStepResult[] history = session.GetHistory();
string replayLog = session.ExportLogText();
```

`RunToCompletion(maxSteps, maxWait)` 只循环同一 `Step()`，返回 `outcome,winnerTeam,stopReason,time,executedActionCount,snapshot,steps`。默认累计上限为 10000 事件／本场 100000 WAIT；可由 `scenario.maxEvents/maxWait` 指定。自动入口的参数可以收紧上限，重复调用不会重新获得预算。预算中的“事件”包含成功行动、休眠和到期；`executedActionCount` 单独统计实际行动。

| 字段／状态 | 含义 |
| --- | --- |
| `snapshot.finished` | 本场已经终止 |
| `outcome` | `Victory`、`Defeat`、`Draw`、`Stalemate` 或 `Stopped` |
| `winnerTeam` | 实际存活胜方；无胜方时为空 |
| `stopReason` | 全灭、无动作／未来事件、重复局面、事件预算、WAIT 预算等具体原因 |
| `Executed` | 已按某条指令提交一次行动并扣费 |
| `Skipped` | 当前角色无可用指令，休眠，其他人继续；不会凭空增加他的 WAIT |
| `StateExpired` | 到期状态已移除；与动作同刻时先发生 |
| `Finished` | 对终态的只读返回；不会追加历史或重复消耗 |

同刻行动按 JSON `units[]` 顺序处理，日志给出 `timeTieResolved/tieBreakReason`。其他效果或到期事件唤醒休眠者时，不允许时间倒退，也不会取消其尚余正常 WAIT。这些均为第二轮试玩假设。

继续支持首轮 `MoveInstruction`、`SetConditionThreshold`、`SetInitialHp`、`GetScenario`、`GetSnapshot`、`GetHistory`、`ExportLogText`。导出／返回的 DTO 为独立副本。日志固定 LF 与 invariant culture，包含逐条候选、选择理由、效果、MP、WAIT、装备增量、同刻理由和终局，不使用墙钟时间。

## 状态与跨场调用

新增 `CombatStatusConfig { id,kind,magnitude,appliedAt,expiresAt }`。`guard` 直接减伤，`empowered` 直接增伤，`vulnerable` 供易伤条件读取。期限按施加时刻的固定全局 WAIT 计算，不按行动次数或敏捷缩放。相同状态 ID 再施加时替换并从本次施加刷新期限；不同 ID 的同类数值效果在本轮相加。完整状态规则仍待设计。

开局状态的 `appliedAt` 必须为 0，表示已经在本场开始时施加；本轮不支持未来才激活的状态配置。运行快照使用全局时刻，携带状态的施加时间不能晚于前场结束时刻。

`snapshot.vulnerable` 表示当前静态标记或动态状态合成的结果；`baseVulnerable` 单独保存首轮静态标记，跨场不会把暂时易伤固化为永久。

```csharp
CombatRunResult first = new CombatSession(firstConfig).RunToCompletion();
// 显式经过 15 WAIT；不调用 Reset，不补 HP/MP。
var next = new CombatSession(secondConfig, first.snapshot, "friendly", 15);
CombatSnapshot nextOpening = next.GetSnapshot();
CombatRunResult second = next.RunToCompletion();
```

携带队员必须同 ID 集合、同最大 HP／MP，格位需与下一份配置一致；当前保留真实状态与尚余正常 WAIT，已在场间到期的状态不重加。新敌人的初始时间与初始状态相对本场开始时刻配置。换装导致资源上限改变、增员、复活、未完成咏唱等交接语义后续另定。

## 出征结果与强化输入

```csharp
CombatExpeditionConfig expeditionConfig =
    JsonUtility.FromJson<CombatExpeditionConfig>(expeditionJson);
var expedition = new CombatExpeditionSession(expeditionConfig);
CombatEncounterReport watchedBattle = expedition.RunNextEncounter();
// 或直接跑完；两条入口共用同一场次与 CombatSession。
CombatExpeditionReport all = expedition.RunToEnd();
CombatExpeditionReport readOnlyCopy = expedition.GetReport();
```

每场提供出征与场次 ID、稳定 `resultId`、前场结束／场间推进后／本场开局与结束快照、完整轨迹、结局、停止原因、经验分配及公库应收现金和物品。仅胜利时产生本场奖励；经验池均分给本次出场队伍全体成员（含倒下者），余数按配置顺序。失败、平局、僵局或预算停止后不打下一场，已完成场次的奖励仍保留。

报告不写公库或角色经验；工程负责按 `resultId` 去重入账。每次真正派遣应赋予唯一出征 ID，实验重放不应当作新收入。`GetReport()` 与终态重复调用只读既有结算，没有再次发奖动作。

强化读取样例：工程把已装备物品的本轮伤害加值投影到 `unit.equipmentDamageBonus`，并填写 `equipmentSourceId`；战斗只读该输入，`damage` 动作计算技能基础量＋装备加值＋增伤状态－减伤状态。采购、材料扣除、强化事务和装备公共接口由工程统筹。调用例及三击变两击的实际对照见 [强化报告](Reports/ROUND2_EQUIPMENT_REPORT.md)。

## 配置与可重放报告

配置均位于 `Assets/StreamingAssets/Prototype/Combat/`：

| 文件 | 验证内容 |
| --- | --- |
| `default.json` | 首轮急救／普攻改序回归 |
| `round2_ambush.json` | 抢杀敌人，进攻优先保住队友；对照仅交换顺序 |
| `round2_pressure.json` | 持续压力，条件急救保住队友；对照仅交换顺序 |
| `round2_capacity.json` | 仅改智力 10→20，使第三条应急指令可装载；HP、MP、伤害和条件项数不变 |
| `round2_equipment.json` | 仅改装备伤害加值，普攻三击与两击 |
| `round2_expedition.json` | 连续两场，明确场间 15 WAIT，真实消耗与状态承接 |
| `round2_time_tie.json`／`round2_one_idle.json`／`round2_stalemate.json` | 同刻、个别人无指令、双方僵局 |
| `round3_tactics.json`／`round3_mp_reserve.json`／`round3_focus_mark.json` | 四段指令编辑素材、MP预留与标记集火 |
| `round4_mage_library.json` | 27技能白名单、5条指令、低MP先冥想，默认完整自动战 |
| `round4_meditation.json`／`round4_meditation_risk.json` | 合理补蓝与过早补蓝的WAIT机会成本，各只改门槛 |
| `round4_area.json`／`round4_cleanse.json`／`round4_control.json` | 群攻、及时净化与先控制，各只改一项决策 |
| `round4_expedition.json` | 新技能／新状态连续两场，明确场间20 WAIT |

`CombatSampleRuns.ComparePriority`、`CompareIntelligenceCapacity`、`CompareEquipment` 返回结构化配置、两次实际运行、第一处行动分歧和影响。每组报告检查除声明项外配置逐字段相同。报告入口为 [伏击对照](Reports/ROUND2_AMBUSH_REPORT.md)、[持续压力对照](Reports/ROUND2_PRESSURE_REPORT.md)、[容量对照](Reports/ROUND2_CAPACITY_REPORT.md)、[连续出征](Reports/ROUND2_EXPEDITION_REPORT.md)、[队列边界](Reports/ROUND2_QUEUE_REPORT.md)，同时保有同名 JSON。

重新生成这些报告：

```powershell
& .\Game\Assets\Prototype\Combat\Reports\GenerateReports.ps1
# 第四轮使用PowerShell 7，可指定独立输出目录而不覆盖历史报告。
& .\Game\Assets\Prototype\Combat\Reports\GenerateRound4Reports.ps1 -OutputDirectory .\work\round4_report_replay
```

该工具编译实际 Runtime、读取实际配置并运行，不生成界面、场景或整包。生成结果的验证记录为 [ROUND2_SAMPLE_VERIFICATION.json](Reports/ROUND2_SAMPLE_VERIFICATION.json)。

## 测试与工程接入

EditMode 测试位于 `Tests/EditMode/`：首轮选择与复制隔离、第二轮时间轴／跨场／装备／出征、第三轮编辑／白名单presence／混合括号继续回归；第四轮新增条件／选法的真实目标、冥想／汲魔、群体锚点、状态／速度／控制／伤害类型、动作等价及实际JSON对照。测试不复制一套战斗结算。

本次隔离验证的实际复跑工具保留在仓库`work/combat_round4_unity_validation_20261114/`：先执行`Prepare-IsolatedCombat.ps1`复制实际文件并生成SHA清单，再执行`Run-IsolatedCombatTests.ps1 -ExpectedTestCount 242`运行该独立项目。Unity正常权限许可IPC路径已实跑；源工程和隔离工程的锁均不删除。首次／最终日志与摘要分别在`Results/`、`ResultsRun02/`，稳定归档在`Tests/Results/ROUND4_*`。

工程可在已有 Unity Test Runner 运行 `Prototype.Combat.Tests`。关闭当前 Game 编辑器后，也可在仓库根目录只执行既有检查脚本的 EditMode 阶段：

```powershell
& .\tools\Run-PrototypeChecks.ps1 -Stage EditMode
```

该阶段不生成场景、不运行 PlayMode、不构建。检查脚本不会删除锁文件或抢占已有编辑器。

第二轮历史验证：**Unity 6000.3.28f1／Test Framework 1.6.0，53 项 Combat EditMode 全部通过，0 失败、0 跳过，进程退出码 0。** 当时原工程保留锁文件，因此使用隔离验证工程复制实际 Runtime、测试和配置，43 个文件逐项保存源与目标 SHA-256；执行前后实际代码／测试／配置内容一致。该次没有执行全工程 Base／UI／PlayMode，也没有构建试玩包。

结果见 [验证摘要](Tests/Results/ROUND2_UNITY_VERIFICATION.json)、[实际 NUnit XML](Tests/Results/ROUND2_COMBAT_EDITMODE.xml)、[源与副本校验清单](Tests/Results/ROUND2_SOURCE_MANIFEST.json)。各 fixture 为首轮回归 21 项、第二轮核心 18 项、出征 9 项、真实配置对照 5 项。最初沙箱调用因许可 IPC 拒绝未执行测试，原日志已保留；正常权限的隔离重试完成上述真实 Unity 测试。Add-Type 编译或报告生成结果与这份 Unity 结果分别记录。

第二轮集中交接见 [ROUND2_HANDOFF.md](Reports/ROUND2_HANDOFF.md)，第三轮编辑契约见 [ROUND3_API_HANDOFF.md](Reports/ROUND3_API_HANDOFF.md)，第四轮见 [ROUND4_HANDOFF.md](Reports/ROUND4_HANDOFF.md)。GUI、公共接入、PlayMode和整包验收由工程负责；局部Combat验证不代表这些检查已完成。
