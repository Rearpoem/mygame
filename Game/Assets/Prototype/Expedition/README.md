# 出征、公共采购与真实装备再战

本模块只做现有 `Prototype.Combat` 与 `Prototype.BaseEconomy` 的集成。逐场和一键都使用同一个 `CombatExpeditionSession`；战斗收益提交给同一个 `PreparationSession`；采购和强化继续使用基地核心。Runtime 没有 UnityEngine、文件 IO 或另一套结算。

所有数字均为第二轮可替换测试参数。实际配置由调用方读取，集成入口为 `Assets/StreamingAssets/Prototype/Expedition/default.json`，其中两条相对 StreamingAssets 路径分别指向战斗出征和基地备战配置。旧首轮 JSON、专项 Runtime 和共享 `PROJECT_CONTEXT.md` 均未被此模块改写。

## 加载与操作

```csharp
// 三份配置由 Unity 调用方用 JsonUtility.FromJson<T> 载入。
var camp = new ExpeditionSliceSession(integrationConfig, combatConfig, preparationConfig);
camp.MoveInstruction("mage", 1, 0); // 可选；会对全部场景一起应用。
camp.SetConditionThreshold("mage", 1, 0, 0.35);

SliceCommandResult started = camp.StartExpedition();
ExpeditionAdvanceResult first = camp.RunNextEncounter();
ExpeditionAdvanceResult rest = camp.RunToEnd(); // 继续同一会话，不从头模拟。
camp.PurchaseAuxiliary("purchase-001", preparationConfig.purchaseQuantity);
camp.EnhanceEquippedWeapon("enhance-001");
EquipmentComparisonReport comparison = camp.CompareEquipmentFromCurrentState();
VerificationBattleReport actualNextBattle = camp.RunVerificationBattle();

ExpeditionSliceSnapshot state = camp.GetSnapshot();
ExpeditionSliceAcceptance acceptance = camp.GetAcceptance();
// 调用方可 JsonUtility.ToJson(acceptance, true)，再自行选择导出位置。
string report = camp.ExportReportText();
string ledgerText = camp.ExportLedgerText();
string ledgerCsv = camp.ExportLedgerCsv();
```

逐场／一键不会自动调用 `StartExpedition`；界面可以在尚未开始时显式先调用一次。开始时生成 GUID 出征 ID，冻结全部场景、指令、装备输入与种子。每场真实 `resultId` 原样作为基地 `settlementId`／账本 `operationId`，不另造支付 ID。只有 `rewardsEligible` 场次入账。公库提交失败时不先发 XP；全部角色经验的加法先作溢出预检，再提交公库，然后按同一 resultId 一次性记入角色经验。

`RetrySettlement(resultId)` 只接受当前营地实际执行的结果；同笔成功重试返回 `isReplay=true`，不重复现金、物品或 XP。未成功的命令不占用 ID。查询、导出、重复已完成的一键派遣都不会重新入账；有待入账的失败场次时，终局一键入口继续明确返回 `SettlementPending`。

`PurchaseAuxiliary(commandId, quantity)` 和 `EnhanceEquippedWeapon(commandId)` 可在两场之间或结束后调用，仍由同一基地核心作原子校验。失败后可继续战斗、采购或重试，成功交易不会因后续强化失败而撤销。中途强化不回写已冻结的出征装备，而由后续真实验证再战读取。

指令改序／阈值会预检全部场景后统一应用，出征进行中明确拒绝。主线结束后的编辑作用于后续真实验证战；捕获的对照配置保持不变。`GetConfiguration/GetCombatConfiguration/GetPreparationConfiguration` 返回独立副本。

## 默认可核对结果

| 步骤 | WAIT／现金／库存／经验 |
| --- | --- |
| 第一场胜利 | WAIT 190；现金应收并入库 10；矿 2；XP 法师 11、队友 10 |
| 显式场间推进 | 15 WAIT，第二场开始 205；不恢复 HP/MP，不复活到期状态 |
| 第二场胜利 | WAIT 310；本场现金 15、矿 3、XP 16/15；累计现金 25、矿 5、XP 52（法师 27、队友 25） |
| 公共采购 | 向许禾采购淬火砂 2，每份 6；公库 125→113，许禾 20→32；砂私人→公共 |
| 强化 | 消耗公共矿 3、砂 2；剩公共矿 2、砂 0；武器等级 1、真实攻击加成 +4 |

映射在配置中明确写出：战斗 `expedition_ore` → 基地 `expedition-ore`；基地装备者 `guard-linen` 的武器实例 `guard-linen-sword-001` → 战斗队友 `ally`。本轮仅支持这一件实际已装备武器；对照的两份配置从同源复制后，只分别填写真实强化前／后加成。未知物品映射在构造前拒绝；不会通过改拼写猜测或静默丢弃。联动没有使用战斗专项独立样例的 +9。

## 两种不同的下一战

**同起点装备对照**只有在实际成功强化后才运行。主线全部胜利结束时，捕获其 WAIT 310 完整快照和冻结指令／种子；复制第二场敌情，只按集成配置把两名敌人的起始 HP 改为 40，保留 maxHP、格位和其他字段。两侧都用原完整友方 HP/MP、未到期状态与原行动时间，场间 0 WAIT。只有实际武器增伤 0→4 不同：首个真实效果分歧在 WAIT 370，同一敌人 HP 20→2／0；终局分别为 490／430。对照不改变当前队伍或任何账目。

**真实无奖励验证再战**使用当前队伍、当前武器及当前营地指令，实际推进一次 `CombatSession` 并更新当前队伍。首次默认强化后再战应在 430 结束。重复再战从上次真实结果继续，不回到 310；全部明确 `rewardsEligible=false`，不改变公库、材料或 XP。无论先后顺序，同起点对照始终读取原来捕获的 310 基准。

本轮一个营地只允许一次有奖励的两场出征。再次 `StartExpedition` 会返回 `ExistingExpedition`，不会无提示恢复模板血蓝。可继续无奖励验证，或显式 `Reset()` 恢复构造时的全部开局；Reset 同时清指令编辑、账本、XP、报告、装备强化和去重，再开始会生成新的 GUID。

## 返回值与导出

`SliceCommandResult` 有 `success,isReplay,code,reason,operationId,ledgerSequence`。推进结果含本次实际完成的 `encounters[]`、`settlements[]` 与当前完整 `report`；错误通过返回值说明，未成功的支付不会部分扣款。

`GetSnapshot()` 提供 `phase`（Camp/Expedition/Completed）、冻结状态、公库/居民/装备、当前全队快照、逐角色经验、结算回执、最近对照及最近真实再战。自有 DTO 均为 public fields + 简单数组；基地只读属性被显式映射为 `PreparationStateDto` 和逐笔 `PreparationLedgerDto`，避免 JsonUtility 导出空对象。

`GetAcceptance()` 保留实际主线完整报告、逐笔结算与账本、已点击生成的对照、全部真实验证战和最终状态。`ExportReportText()` 只格式化已有记录，含逐条件尝试、候选、行动、效果、装备来源、各角色 XP 与 settlementId，以及采购强化去向；从不为了导出自动生成模拟。所有输入和输出均深复制，外部修改报告不会改动营地。

本模块作者已编译实际 Combat/BaseEconomy/Expedition Runtime，并从真实三 JSON 跑通默认主线、装备对照、真实再战及结构化导出。Unity EditMode、PlayMode 和整包构建由集成会话统一执行，最终证据以其实际结果为准。
