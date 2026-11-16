# 第三轮战斗模块交接：四段指令编辑与战术对照

第三轮接续二轮已经试玩通过的模块。核心、样例和测试由子 Agent 分工实施，工程并行负责 GUI、场景和整包。本文只交接 Combat 内部数据与调用，不写公共库、不更改共享上下文。所有数值和新增目录口径均为本轮试玩参数。

## 文件与调用入口

| 路径（相对 Game） | 用途 |
| --- | --- |
| `Assets/Prototype/Combat/Runtime/CombatSession.cs`、`CombatModels.cs`、`CombatTacticsCatalog.cs` | 核心编辑、目录、条件与目标选择，由核心 Agent 维护 |
| `Assets/Prototype/Combat/Runtime/CombatTacticsSamples.cs` | 调用真实 ReplaceInstruction 的第三轮单字段对照，复用原 CombatComparisonReport |
| `Assets/Prototype/Combat/Runtime/CombatSampleRuns.cs` | 继续保留二轮全部入口；内部对照／配置比较供第三轮复用，因果统计只数击杀时刻及之后的行动 |
| `Assets/StreamingAssets/Prototype/Combat/round3_tactics.json` | GUI 默认素材，友军固定 `mage`、`ally`，可直接编辑并打完整场 |
| `Assets/StreamingAssets/Prototype/Combat/round3_mp_reserve.json` | 预留 MP 与不预留的同开局输入 |
| `Assets/StreamingAssets/Prototype/Combat/round3_focus_mark.json` | 标记后集火与最低 HP 优先的同开局输入 |
| `Assets/Prototype/Combat/Reports/GenerateRound3Reports.ps1` | 编译实际 Runtime，重放本轮对照、默认 GUI 案例与旧轮回归，只写新报告 |

完整签名、错误码和目录字段见 [ROUND3_API_HANDOFF.md](ROUND3_API_HANDOFF.md)。实际编辑入口属于 `CombatSession`：

```csharp
CombatEditResult InsertInstruction(string unitId, int index, CombatInstruction instruction);
CombatEditResult ReplaceInstruction(string unitId, int index, CombatInstruction instruction);
CombatEditResult RemoveInstruction(string unitId, int index);
CombatEditResult MoveInstruction(string unitId, int fromIndex, int toIndex);
CombatActionConfig[] GetAvailableActions(string unitId);
```

索引从 0 起。错误返回 `success=false`、稳定 `code` 和可显示的 `message`，不改变配置、时间、HP/MP 或历史。成功编辑影响后续选择并成为 Reset 的重放基线。每条指令仍为“条件表达式｜对象范围｜选择方式｜行动”；条件缺省读取候选，明确 `subject=actor` 才读取施放者。读取及传入 DTO 均复制。

## GUI 默认素材和动作命名

`round3_tactics.json` 中 Mage 智力 40、4 条已配指令、容量 5、每条最多 3 条件项；Ally 智力 10、1 条指令、容量 2、每条最多 1 条件项。Mage 默认依次为急救、后排未标记目标施加标记、MP≥75% 时强攻、普攻兜底。Ally 默认最低 HP 比例重斩。两个角色都有额外可配技能，删除一条指令不会删掉技能目录。

Mage 的技能目录为 `tactics_first_aid`、`tactics_basic_attack`、`tactics_power_attack`、`tactics_focus_mark`、`tactics_guard_stance`；Ally 为 `tactics_basic_attack`、`tactics_ally_strike`、`tactics_guard_stance`。GUI 应按 `session.GetAvailableActions(unitId)` 读取，不从全场动作池给友军开放敌方技能。

为保留 Unity JsonUtility 将旧 null 数组往返为 [] 的兼容性，新增 `availableActionIdsExplicit`：false＋null／[] 按旧角色原指令推导，非空列表本身视为显式，true＋null／[] 表示明确无技能。规范化 `GetScenario()` 输出标志为 true，复制时保留该标志。本轮三个 JSON 的非空白名单无需追加字段即可正确读取；新建明确空名单时应显式写 true。

所有第三轮动作均有 `tactics_` 前缀，与二轮 `first_aid`、`basic_attack` 等隔离。MP 对照的 40 WAIT 轻击单独使用 `tactics_quick_attack`，默认／集火案例的 60 WAIT 普攻使用 `tactics_basic_attack`。三个第三轮配置之间，同 ID 动作的定义完全相同，生成工具逐项检查这一点。状态 ID `focus_mark`、`guard_stance` 是状态引用，与动作 ID 分开。

工程导入出征时只投影友方的 `instructions`、`availableActionIds` 和 `availableActionIdsExplicit`，并复制目标配置中缺少的动作定义；同 ID 已有不同定义应明确拒绝，不静默覆盖或使用隐藏别名。白名单和显式标志须一起复制，避免明确空名单又被当成旧配置推导。不要用实验角色的 HP、MP、智力、站位、装备或敌情覆盖真实出征快照。默认 Mage 40／Ally 10 的智力与二轮模板一致；真实资源仍由出征承接。投影后的单步、自动和批量出征继续使用同一个 CombatSession。

## 三处可直接操作的配置

| 场景 | 角色／指令 | 唯一修改字段 | 取值 | 实际第一次行动分歧 |
| --- | --- | --- | --- | --- |
| `round3_mp_reserve.json` | `mage`，`conditional_power`，指令索引 1、条件项 0 | `condition.terms[0].threshold`，保持 kind=`mp_ratio_at_least`、subject=`actor` | 0 → 0.75 | WAIT 40：再次强攻 → 零耗 MP 轻击；WAIT 0 两侧都强攻 |
| `round3_focus_mark.json` | `ally`，`ally_attack`，指令索引 0 | `selector` | `lowest_hp_ratio` → `vulnerable_first` | WAIT 15：攻击前排诱饵 → 攻击被标记的后排威胁 |
| `round3_tactics.json` 默认 GUI | `mage`，`emergency_heal`，指令索引 0、条件项 0 | `condition.terms[0].threshold`，保持 kind=`hp_ratio_at_most`、subject=`target` | 0.35 → 0.10 | WAIT 30：急救队友 → 强攻被标记敌人 |

替换例：

```csharp
CombatScenario editedCopy = session.GetScenario();
CombatUnitConfig mage = Array.Find(editedCopy.units, unit => unit.id == "mage");
CombatInstruction editedRule = mage.instructions[0];
editedRule.condition.terms[0].threshold = 0.10;
CombatEditResult edit = session.ReplaceInstruction("mage", 0, editedRule);
// 显示 edit.message；失败时会话完全保持原样。
```

## 两份正式同开局对照

[预留 MP 战报](ROUND3_MP_RESERVE_REPORT.md)／[结构化记录](ROUND3_MP_RESERVE_REPORT.json)：仅改施放者 MP 门槛，其他输入逐字段相同。第一次分歧在 WAIT 40，基线 MP 12→0，变体轻击保留 MP 12。变体随后在 WAIT 80 和 150 两次急救；基线无 MP 急救，队友 WAIT 140 阵亡。基线 WAIT 240 获胜，法师剩 50 HP、0 MP；变体 WAIT 260 获胜，法师 100 HP、0 MP，队友 10 HP。保人消耗更多战斗时间，不能把结果解读成所有敌情都应预留 MP。

[标记集火战报](ROUND3_FOCUS_MARK_REPORT.md)／[结构化记录](ROUND3_FOCUS_MARK_REPORT.json)：法师在两侧都于 WAIT 0 用真实 apply_status 将 `focus_mark`／`vulnerable` 施给后排目标，固定到期时刻为 WAIT 200。仅改 Ally 的选择方式，第一次分歧在 WAIT 15。基线后排威胁实际攻击两次，队友 WAIT 80 阵亡；变体 WAIT 75 先击倒后排威胁，它只来得及攻击一次，队友保留 12 HP。两侧 WAIT 90 获胜，法师均剩 100 HP、20 MP。易伤在本例仅用于目标识别，不暗中增加伤害。

两份报告包含完整输入配置、真实步骤与首次行动分歧。比较忽略尝试文字变化，以时间、行动者、行动、目标、MP 和效果判断行动是否真正分开。归一化声明字段后，其他配置逐字段相同；发现额外改动会抛错。击杀影响只统计对照分支在该时刻及之后的实际行动，次数为 0 时不写“阻止行动”的因果句。

## 默认 GUI 案例也有真实结果

[ROUND3_GUI_DEFAULT_RUN.json](ROUND3_GUI_DEFAULT_RUN.json) 记录上表第三处操作。默认急救 35%：WAIT 135 胜利，Mage 100 HP／8 MP，Ally 42 HP／10 MP。仅改为急救 10%：WAIT 75 胜利，Mage 100 HP／8 MP，Ally 12 HP／10 MP。首次分歧 WAIT 30，急救侧当时 Mage MP 20→14，强攻侧 20→8；最终总消耗相同，但治疗、击杀时机与剩余血量不同。默认配置逐事件 Step 与 RunToCompletion 的快照和完整日志也一致。

## 复跑与回归

使用 PowerShell 7 的 `pwsh` 运行；本轮实跑版本为 7.6.9，脚本使用该环境的 `System.Text.Json` 读取和保存字段 DTO。

```powershell
pwsh -NoProfile -File './Game/Assets/Prototype/Combat/Reports/GenerateRound3Reports.ps1'
```

工具读取实际三个 JSON，编译实际 Runtime，不含另一个战斗模拟器。当前实跑结果见 [ROUND3_SAMPLE_VERIFICATION.json](ROUND3_SAMPLE_VERIFICATION.json)：两份正式单变量对照可重放、默认 GUI 单字段能改变真实结果、单步／自动一致、第三轮同 ID 动作定义一致。

旧轮回归独立保留在 [ROUND3_REGRESSION_SUMMARY.json](ROUND3_REGRESSION_SUMMARY.json)：首轮 `default.json` 的急救／普攻改序仍通过；二轮出征逐场与批量同态、第一场 190＋场间 15→第二场 205／最终 310，现金应收 25；二轮装备强化三击／两击仍为 WAIT 120／60。本次没有重写二轮配置或历史战报。二轮历史 53 项 Unity 结果继续保存在 `Tests/Results/ROUND2_*`，本轮重跑结果另行归档如下。

## 实际 Unity 验证与输入核对

**Unity 6000.3.28f1／Test Framework 1.6.0：Combat Edit Mode 111／111 通过，0 失败、0 跳过，退出码 0。** 其中原四份测试共 53 项，新增 Tactics 52 项、Samples 6 项。原四份测试源码 SHA 与二轮一致，包含原样保留的 raw DTO 往返断言；没有通过放宽旧断言消除问题。

- [最终验证摘要](../Tests/Results/ROUND3_UNITY_VERIFICATION.json)
- [实际 NUnit XML](../Tests/Results/ROUND3_COMBAT_EDITMODE.xml)
- [57 个实际输入的源／副本 SHA-256 清单](../Tests/Results/ROUND3_SOURCE_MANIFEST.json)
- [独立复跑七份报告的逐文件 SHA-256 对照](../Tests/Results/ROUND3_REPORT_REPLAY_HASHES.json)

实际测试使用隔离 Unity 工程复制最终 Combat 模块、测试与配置，摘要确认源内容未变、复制内容匹配，原工程锁保持。该验证覆盖 Combat Edit Mode；GUI、Base、Play Mode 和整包构建由工程并行接入验证。本轮最初发现 Unity 将旧 null 技能数组往返为 []，已经通过 `availableActionIdsExplicit` 修复并追加四项 presence 回归；[首次失败 XML](../Tests/Results/ROUND3_INITIAL_FAILED_EDITMODE.xml)与输入清单仍保留。测试 Agent 独立运行最终报告生成工具后，七份产物与交付报告逐字节相同。

## 待定边界与工程接入

actor 条件、技能白名单、混合 AND／OR 显式分组、目录项及报错口径均按本轮试玩假设实施。MP 费用仍是配置整数，比例条件不代表已确定百分比收费。前排权重、伤害与抗性公式、咏唱、闪避即时行动、移动和完整阵型机制继续按已有未定范围处理；目录扩展不表示这些系统已经实现。

给工程：默认加载 `round3_tactics.json`，按真实目录和角色技能白名单编辑四段指令，通过 Insert／Replace／Remove／Move 提交并显示 code/message；用上表三处单项操作验收真实战果。两份正式战例可以单独切换 JSON，时间轴与战报仍读取同一 CombatSession。若导入原有出征，只投影友方指令、技能白名单及其显式标志和缺失动作定义，真实 HP／MP／状态与公库账本沿二轮路径承接；GUI、场景、整包及跨系统事务由工程统一接入。
