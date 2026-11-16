# 第四轮交接：法师技能库与条件扩展

本轮交付 27 项可实际执行的法师技能、37 项条件、24 种选择方式，以及 5 组只改一项决策的实跑对照。技能库可供现有指令 GUI 加载，指令编辑与单步／自动战斗继续调用原 CombatSession。以下数值、状态公式和范围结算均为第四轮试玩假设；全部法术目前按瞬发技能验证，咏唱尚未实现。

## 工程加载与改动路径

| 路径（相对 Game） | 用途 |
| --- | --- |
| `Assets/StreamingAssets/Prototype/Combat/round4_mage_library.json` | 可直接加载的默认法师库与完整战斗；友军 ID 保持 `mage`／`ally` |
| `Assets/StreamingAssets/Prototype/Combat/round4_meditation.json` | MP 不足时先冥想的正式对照输入 |
| `Assets/StreamingAssets/Prototype/Combat/round4_meditation_risk.json` | MP 足够击杀时先冥想的风险输入 |
| `Assets/StreamingAssets/Prototype/Combat/round4_area.json` | 单体／全体法术对照输入 |
| `Assets/StreamingAssets/Prototype/Combat/round4_cleanse.json` | 及时净化／重伤再净化对照输入 |
| `Assets/StreamingAssets/Prototype/Combat/round4_control.json` | 先控制／先输出对照输入 |
| `Assets/StreamingAssets/Prototype/Combat/round4_expedition.json` | 新技能与新状态跨两场承接，场间显式经过 20 WAIT |
| `Assets/Prototype/Combat/Runtime/CombatMageSamples.cs` | 实际编辑、对照与逐技能执行探针；没有另一套战斗模拟器 |
| `Assets/Prototype/Combat/Reports/GenerateRound4Reports.ps1` | 编译实际 Runtime，读取上述 JSON，生成 17 份报告和验证文件 |
| `Assets/Prototype/Combat/Runtime/CombatSession.cs`、`CombatModels.cs`、`CombatTacticsCatalog.cs` | 核心 Agent 实现的新效果、状态、范围与目录；旧编辑 API 保留 |

完整字段、状态公式和目录口径见 [ROUND4_API_HANDOFF.md](ROUND4_API_HANDOFF.md)。本模块没有改 UI、场景、公共库接口、整包构建或共享上下文；界面与出征投影由工程会话接入。

默认 Mage 智力 40，已配 5 条指令、容量 5，每条最多 3 项条件；Ally 智力 10，已配 2 条、容量 2，每条最多 1 项条件。27 项是 Mage 学会的技能池，不能同时装成 27 条指令。所有角色保留显式白名单 `availableActionIds`＋`availableActionIdsExplicit=true`，GUI 应调用 `GetAvailableActions(unitId)`，不要将全场动作池当成该角色的技能。

所有新动作使用 `mage_` 或 `round4_` 独立 ID，同 ID 在七份新配置间定义相同，生成器逐项核对。敌人名称已改为短中文，角色 ID 和引用保持稳定。首轮、二轮和三轮的 JSON 与历史战报没有被改写。

## 第一次操作就能看到的冥想

加载 `round4_mage_library.json`，直接单步：WAIT 0 的 `mage` 执行 `mage_meditate`，MP **4→16**，费用 0，本次 WAIT **60**，下次行动时刻 60。条件为 `mage.instructions[0]`，ID `meditate_when_low`：`mp_ratio_at_most`、`subject=actor`、`threshold=0.25`，对象 self。

默认顺序为低 MP 冥想、35% HP 急救、敌人至少两名时全体火雨、火弹、法杖兜底；Ally 先用非魔法净化解除有害状态，再攻击。当前默认实跑 **WAIT 230 胜利**，Mage 剩 **38 HP／6 MP**，Ally 剩 **100 HP／8 MP**。[完整默认运行与边界记录](ROUND4_LIBRARY_RUN.json) 包含以下实际检查：

- 满 MP 时，即使将冥想条件改为总是，恢复无有效对象也会跳过并继续找下一条指令。
- MP 0 且被沉默，仍能执行非魔法冥想，MP 0→12。
- 满 MP 且被沉默，魔法指令被实际阻止，继续执行非魔法法杖敲击。
- 从同一开局逐事件 Step 与自动 RunToCompletion，终局快照和完整日志相同。

## 27 项技能及其试玩参数

表中范围仍受指令的 scope 限制；“全体”指锚点同队且处于该 scope 内的合法存活对象，不会跨阵营自动扩展。费用／WAIT 为基础值；敏捷和有效状态可以改变实际 WAIT。除冥想、紧急吐纳和法杖敲击以外，表中技能均 `isMagic=true`，被沉默阻止。减伤、增伤和削弱均为直接数值；持续时间以固定全局 WAIT 计。

| 动作 ID／显示名 | 真实用途／数值 | 范围 | MP 费用 | 基础 WAIT | 被沉默阻止 |
| --- | --- | --- | ---: | ---: | --- |
| `mage_meditate` 冥想 | 恢复目标 12 MP，封顶；适合自己持续补给 | 单体 | 0 | 60 | 否 |
| `mage_quick_breath` 紧急吐纳 | 恢复 4 MP；较快、每 WAIT 回复效率更低 | 单体 | 0 | 25 | 否 |
| `mage_mana_transfer` 魔力转移 | 消耗自己 8 MP，恢复目标 8 MP | 单体 | 8 | 35 | 是 |
| `mage_mana_drain` 汲取魔力 | 从目标扣最多 8 MP，按实扣量给施放者返还并封顶 | 单体 | 0 | 45 | 是 |
| `mage_staff` 法杖敲击 | 8 物理伤害，无 MP 后备 | 单体 | 0 | 40 | 否 |
| `mage_firebolt` 火弹 | 28 魔法伤害 | 单体 | 8 | 40 | 是 |
| `mage_arcane_lance` 奥术长矛 | 45 魔法伤害，较高费用与 WAIT | 单体 | 16 | 70 | 是 |
| `mage_chain_lightning` 整排雷击 | 每个覆盖者 16 魔法伤害 | 同排 | 12 | 55 | 是 |
| `mage_fire_rain` 全体火雨 | 每个覆盖者 14 魔法伤害 | 全体 | 14 | 65 | 是 |
| `mage_piercing_ray` 纵列射线 | 每个覆盖者 20 魔法伤害 | 同列 | 10 | 55 | 是 |
| `mage_first_aid` 急救 | 恢复 30 HP | 单体 | 6 | 30 | 是 |
| `mage_greater_heal` 大治疗 | 恢复 60 HP | 单体 | 12 | 60 | 是 |
| `mage_group_heal` 群体恢复 | 每个覆盖者恢复 20 HP | 全体 | 12 | 55 | 是 |
| `mage_stone_skin` 石肤护盾 | guard 6，持续 80 WAIT | 单体 | 4 | 25 | 是 |
| `mage_mass_guard` 群体护盾 | guard 4，持续 100 WAIT | 全体 | 12 | 55 | 是 |
| `mage_magic_barrier` 魔法屏障 | magic_guard 10，持续 100 WAIT，只减魔法伤害 | 单体 | 5 | 30 | 是 |
| `mage_empower` 魔力增幅 | empowered 10，持续 100 WAIT，增加伤害输出 | 单体 | 6 | 35 | 是 |
| `mage_focus_mark` 易伤标记 | vulnerable，持续 150 WAIT，供条件／目标优先识别 | 单体 | 3 | 25 | 是 |
| `mage_cleanse` 净化 | 清除全部有害状态及旧永久易伤标记 | 单体 | 4 | 25 | 是 |
| `mage_group_cleanse` 广域净化 | 同上，对全部合法覆盖者生效 | 全体 | 12 | 60 | 是 |
| `mage_dispel` 驱散 | 清除全部有益状态 | 单体 | 5 | 35 | 是 |
| `mage_freeze` 冰缚 | stunned，持续 70 WAIT，阻止主动行动 | 单体 | 8 | 25 | 是 |
| `mage_silence` 沉默 | silenced，持续 80 WAIT，阻止魔法行动 | 单体 | 6 | 35 | 是 |
| `mage_weaken` 虚弱 | weakened 8，持续 100 WAIT，减少伤害输出 | 单体 | 6 | 35 | 是 |
| `mage_haste` 加速 | haste 25，持续 100 WAIT，后续 WAIT 倍率 0.8 | 单体 | 6 | 35 | 是 |
| `mage_slow` 减速 | slow 25，持续 100 WAIT，后续 WAIT 倍率 1.25 | 单体 | 6 | 35 | 是 |
| `mage_mass_weaken` 群体虚弱 | weakened 5，持续 80 WAIT | 全体 | 14 | 55 | 是 |

所有 HP／MP 恢复封顶；汲魔不能自吸、目标 0 MP 时无效，超出施放者缺口的实扣 MP 销毁。传蓝仍可通过对象与条件自由配置；给自己传蓝只会抵消费用，不会凭空获利。易伤标记在本轮仍不附加伤害，便于单独观察条件和集火选择的作用。

每项均有实际执行证据：[逐技能验收表](ROUND4_SKILL_EXECUTION_PROOFS.md)／[完整前后快照与后续事件](ROUND4_SKILL_EXECUTION_PROOFS.json)。27 项首动作都实际 Executed，并有 HP、MP 或状态变化；群攻实际覆盖 3 人，同排／同列分别覆盖 2 人，群疗／群净化／群盾实际覆盖 2 人。探针按技能需要预置缺蓝、伤员、可净化的有害状态或可驱散的增益，首动作后继续同一个 Session：护盾实际减伤、增幅后杖击 18、虚弱后敌方 16 点法术降为 8、加速后 40→32 WAIT、减速后 50→62.5 WAIT、冰缚与沉默实际阻止行动。这些是合法能力测试，不冒充同开局平衡对照。

## 条件与选择方式的新增用处

条件仍为 1／2／3 项，智力容量与 all／any／三项 mixed 括号规则不变。37 项目录覆盖 HP／MP 比例和实值、资源缺口、好坏状态与控制、状态种类、标签、友敌存活／伤员数量、排内数量、状态剩余时长、剩余预约 WAIT 与是否本人。24 种选择方式覆盖血蓝比例／实值／缺口、状态软优先、站位、智力、行动早晚与有效速度。

可直接在 GUI 配置的例子：

- `actor MP≤25% | 自身 | 配置顺序 | 冥想`，避免误读目标 MP。
- `目标有控制状态 | 己方 | 受控制者优先 | 净化`，满血但被定身的队友也值得处理。
- `actor 敌方存活数≥2 | 敌方 | HP 比例最低 | 全体火雨`，把群攻资源用于多目标开局。
- `目标 MP≥8 | 敌方 | MP 最高 | 汲取魔力`，有实际资源可抽时才使用。
- `目标有增益 | 敌方 | 有增益者优先 | 驱散`，与削弱／直接输出形成选择。
- `目标缺少 mage_haste_state | 己方 | 智力最高 | 加速`，避免同 ID 状态无条件重复刷新。

所有条件都真实求值；value 型字段读取状态 ID／kind／tag。目录 `thresholdUnit` 明确 ratio／points／count／wait／row，仅 ratio 显示百分比。不要仅凭 `integerThreshold=false` 将 WAIT 误画成百分比。状态软优先选择器仍会回退其他合法目标，想限定目标必须同时配置对应条件。

## 五组严格单变量战例

每组使用同一初始快照、敌人、技能池、种子和装备，只编辑下表一个字段。真实 ReplaceInstruction 成功后，将声明字段恢复并逐字段比较其余配置；有额外变化直接报错。首次分歧取实际执行事件的时刻、行动者、动作、目标、MP 与效果，不以规则文字或候选尝试差异冒充行动分歧。报告同时给出完整轨迹与最终所有角色 HP／MP。

| 输入 JSON | Mage 指令 ID／索引 | 唯一改动 | 首次真实分歧 | 战果取舍 |
| --- | --- | --- | --- | --- |
| `round4_meditation.json` | `meditate_when_low`／0 | `condition.terms[0].threshold`：0→0.25 | WAIT 0：杖击→冥想，MP 4→16、花 60 WAIT，之后 WAIT 60／100 两次火弹 | 胜利 WAIT 160→100；Mage 64 HP／4 MP→76 HP／0 MP |
| `round4_meditation_risk.json` | `meditate_when_low`／0 | 同字段：0→0.35 | WAIT 0：用现有 8 MP 击杀→先冥想 | 胜利 WAIT 0→60；基线队友 20 HP，变体队友 WAIT 20 被击倒；变体 Mage 多余下 12 MP |
| `round4_area.json` | `primary_spell`／0 | `actionId`：`mage_firebolt`→`mage_fire_rain` | WAIT 0：覆盖 enemy_a 一人→enemy_a／b／c 三人，费用 8→14、WAIT 40→65 | 胜利 WAIT 80→65；Mage 同为 76 HP，剩余 MP 16→12 |
| `round4_cleanse.json` | `cleanse_control`／0 | `condition.terms[0].threshold`：0.25→1 | WAIT 0：火弹→净化被定身队友，MP 30→26 | 胜利 WAIT 150→105；队友 8→26 HP，Mage 剩余 MP 6→2 |
| `round4_control.json` | `freeze_threat`／0 | `condition.terms[0].threshold`：0.2→1 | WAIT 0：火弹→冰缚威胁，MP 30→22；敌人 WAIT 20 的行动被实际阻止 | 胜利 WAIT 40→65；队友 0→30 HP，Mage 剩余 MP 14→6 |

前两例的对象为 self，因此 target 缺省读取的也是施放者；默认 GUI 冥想规则已明确写 `subject=actor`。风险例表明“蓝不足才恢复”与“够用时先解决威胁”的时机差别，35% 并非通用推荐阈值。四个正式主例及一个风险补例分别见：

- [低 MP 冥想](ROUND4_MEDITATION_REPORT.md)／[JSON](ROUND4_MEDITATION_REPORT.json)
- [过早冥想风险](ROUND4_MEDITATION_RISK_REPORT.md)／[JSON](ROUND4_MEDITATION_RISK_REPORT.json)
- [单体与全体](ROUND4_AREA_REPORT.md)／[JSON](ROUND4_AREA_REPORT.json)
- [及时净化](ROUND4_CLEANSE_REPORT.md)／[JSON](ROUND4_CLEANSE_REPORT.json)
- [先控制威胁](ROUND4_CONTROL_REPORT.md)／[JSON](ROUND4_CONTROL_REPORT.json)

## 连战、装备与公共结算接入

[第四轮连战报告](ROUND4_EXPEDITION_REPORT.md)／[结构化结果](ROUND4_EXPEDITION_REPORT.json) 使用原 CombatExpeditionSession：第一场 WAIT 128 获胜；显式间隔 20 WAIT，第二场起点 **148**。Mage 的 **64 HP／12 MP** 原样承接，未补满。原魔法屏障到期 140，在场间消失；加速到期 180，第二场开始仍有效，随后真实到期。第二场 WAIT 252 获胜，Mage 剩 **32 HP／8 MP**，Ally **100 HP／10 MP**。

报告包含初入、前场结束、场间推进后、第二场开局和结束快照；逐场 RunNextEncounter 与批量 RunToEnd 的完整结果一致。新状态的到期时刻不在转场时刷新，已预约动作余量也继续承接。

两场各产生经验池 20、应归公库现金 5、矿料 1；本轮经验假设仍是胜利时给出场队伍全体均分，包含战后倒下者。两场合计 Mage／Ally 各应收 20 EXP，现金应收 10、矿料 2。`resultId` 为 `round4-mage-expedition-replay-001:battle:1/2`，重读报告只取副本，不推进、不重复发奖。工程在真实派遣时提供唯一 expeditionId，公共账本按 resultId 去重；模块不写公共库或存档。

沿用三轮工程投影合同：只将友方 `instructions`、`availableActionIds` 和 `availableActionIdsExplicit` 投影到真实出征配置，并补齐缺少的动作定义。动作 ID 已有时，调用核心 `CombatSession.ActionDefinitionsEquivalent(a,b)` 比较规范语义，不同定义明确拒绝；`NormalizeActionDefinition` 只规范空字符串和缺省 single／legacy，不会掩盖数值差异。不要将试玩 HP、MP、智力、装备、站位或敌情覆盖实际出征快照。

旧轮真实回归另存 [ROUND4_REGRESSION_SUMMARY.json](ROUND4_REGRESSION_SUMMARY.json)：首轮急救首步 MP 30→24 保持；二轮出征两场最终 WAIT 310、现金应收 25 保持；装备强化对照仍为 WAIT 120／60；三轮预留 MP 和标记集火的首次分歧仍为 WAIT 40／15。旧历史证据保留原处。

## 调用、复跑与验证范围

原编辑 API `InsertInstruction/ReplaceInstruction/RemoveInstruction/MoveInstruction` 继续返回 `CombatEditResult(success,code,message)`，失败不更改状态。读取目录、白名单和 DTO 均返回副本。新样例调用：

```csharp
CombatComparisonReport recovery = CombatMageSamples.CompareMeditation(meditationScenario); // 0 -> .25
CombatComparisonReport risk = CombatMageSamples.CompareMeditation(riskScenario, .35);
CombatComparisonReport area = CombatMageSamples.CompareArea(areaScenario); // firebolt -> fire_rain
CombatComparisonReport cleanse = CombatMageSamples.CompareCleanse(cleanseScenario); // .25 -> 1
CombatComparisonReport control = CombatMageSamples.CompareControl(controlScenario); // .2 -> 1
CombatMageSkillProof[] proofs = CombatMageSamples.RunSkillProofs(libraryScenario);
```

生成器需要 **PowerShell 7**，本轮实跑版本 7.6.9，使用 System.Text.Json 的字段 DTO 支持：

```powershell
pwsh -NoProfile -File './Game/Assets/Prototype/Combat/Reports/GenerateRound4Reports.ps1'
# 独立目录复跑，保留交付报告：
pwsh -NoProfile -File './Game/Assets/Prototype/Combat/Reports/GenerateRound4Reports.ps1' -OutputDirectory './work/round4_independent_reports'
```

本轮样例已用冻结后的真实 Runtime 实跑通过，[生成器验证摘要](ROUND4_SAMPLE_VERIFICATION.json) 记录 5 组单变量、27 项技能实际执行、范围覆盖、真实控制、默认开局冥想、满蓝回退／沉默后备、单步与自动一致、两场承接与旧轮回归。

**实际 Unity 6000.3.28f1／Test Framework 1.6.0：Combat Edit Mode 242／242 通过，0 失败、0 跳过，退出码 0。** 保留旧轮 111 项行为回归，新增 131 项覆盖资源、范围、状态、条件／选择器、动作定义与实际法师样例。测试 Agent 在隔离工程复制最终模块执行，91 项源／副本输入 SHA 匹配，验证期间源内容未变，原 Game 工程和锁未动。

- [实际 Unity 验证摘要与复跑命令](../Tests/Results/ROUND4_UNITY_VERIFICATION.json)
- [真实 NUnit XML](../Tests/Results/ROUND4_COMBAT_EDITMODE.xml)
- [91 项输入的源／副本 SHA 清单](../Tests/Results/ROUND4_SOURCE_MANIFEST.json)
- [独立重生成 17 份产物的逐文件 SHA 核对](../Tests/Results/ROUND4_REPORT_REPLAY_HASHES.json)

测试 Agent 另行运行同一生成器，17／17 产物与交付文件逐字节一致，包含 27 技能证据、5 对照、默认战斗、连战与旧轮回归。旧轮五份测试源码 SHA 未变；旧目录遍历测试改为读取合法 `defaultValue`，以适配新增状态种类枚举，原行为断言继续保留。首次运行中一条新增 `is_not_actor` 夹具把另一名队友错误当成不合格对象；修正该测试断言后全部通过，未修改 Runtime 或 JSON 来消除失败。首次 XML、日志与输入清单也保留在 `Tests/Results/ROUND4_INITIAL_*`。

后续仍需决定咏唱／准备时间、完整命中暴击与闪避反击、属性伤害公式、状态叠加与抗性、正式技能费用／范围／持续时间、经验掉落和跨系统保存合同。本轮没有实现 DoT、复活、AP／PP，也没有把参考游戏或这些试玩参数定成最终规则。Combat 模块验证不代表 GUI、基地、Play Mode 或整包验收；相应接入仍由工程统筹。
