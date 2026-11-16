# 第二轮基地：出征材料、公共采购与装备强化交接

2026-11-14。状态：本模块实现完成；Unity 6000.3.28f1 隔离副本的 44 项 EditMode 测试通过。公共接口、界面、场景及整包接入仍由工程会话负责。

## 范围与负责人

本轮实现独立的纯 C# 备战样例：一次预置出征结果归公共库，公库向具名居民购买私人辅料，随后消耗两种公共材料强化公营作战人员已装备的一件武器。价格、数量及强化效果来自可替换测试配置。

首轮 `BaseSession`、`default.json`、近远厨房和首餐账本继续保留。接口、界面、场景、战斗伤害换算及整包构建由工程或战斗会话接入，不在本次改动范围。

## 改动路径与调用入口

本轮新增运行时入口为 `Prototype.BaseEconomy.PreparationSession`，输入配置为 `PreparationScenario`，测试配置为 `Assets/StreamingAssets/Prototype/BaseEconomy/preparation.json`。首轮 `BaseSession` 和 `default.json` 继续独立使用。

| 调用 | 用途 |
| --- | --- |
| `ApplyExpeditionDrop(ExpeditionDrop input)` | 将一笔带标识的出征现金或矿料结算到公库 |
| `PurchaseAuxiliary(string commandId, int quantity)` | 公库向场景中的具名居民采购私人辅料 |
| `EnhanceEquippedWeapon(string commandId)` | 校验并消耗两种公共材料，强化预置已装备武器一次 |
| `GetSnapshot()` | 读取当前钱包、归属库存、累计消耗和装备值 |
| `GetSettlementReceipt(string settlementId)` | 只读查询已经提交的出征回执；未找到或不是出征结算时返回 `null` |
| `Ledger`、`ExportLedgerText()`、`ExportLedgerCsv()` | 读取有序逐笔账本或导出可重复比较的记录 |
| `Reset()` | 开一个相同配置的新实验，恢复开局并清空去重记录 |

命令返回 `PreparationCommandResult`，包含 `Success`、`IsReplay`、`Code`、`Reason` 和 `LedgerSequence`。失败原因通过结果对象返回，不会先扣除再补回。

| 路径（相对 `Game/`） | 改动 |
| --- | --- |
| `Assets/Prototype/BaseEconomy/Runtime/PreparationScenario.cs` | 可序列化本地配置及出征掉落输入，校验和深复制 |
| `Assets/Prototype/BaseEconomy/Runtime/PreparationSnapshot.cs` | 不可变快照、账本、命令结果与装备变动 |
| `Assets/Prototype/BaseEconomy/Runtime/PreparationSession.cs` | 接收掉落、公共采购、强化、去重与账本导出 |
| `Assets/StreamingAssets/Prototype/BaseEconomy/preparation.json` | 第二轮唯一运行测试配置；价格、数量、开局及装备加成 |
| `Assets/Prototype/BaseEconomy/Tests/Editor/PreparationSessionTests.cs` | 第二轮规则及首餐回归测试 |
| `Assets/Prototype/BaseEconomy/Tests/Editor/PreparationEvidenceExporter.cs` | 从真实核心会话导出账本、失败快照和最小输入输出 |
| `Assets/Prototype/BaseEconomy/README.md`、`ASSUMPTIONS.md`、`ROUND2_HANDOFF.md` | 两轮入口、临时口径和本次交接 |

新增 Unity 资产配套 `.meta`。首轮配置和运行时代码不因本次扩展改变；具体回归核对结果见实跑记录。

本模块本地 DTO 和装备读出只是接入样例，不代表工程公共接口、存档格式或标识已经冻结。

## 最小输入输出样例

配置由 Unity 调用方读取；运行时核心不做文件 IO。以下内容可放入工程侧的方法中，需引用 `System`、`System.IO`、`UnityEngine` 和 `Prototype.BaseEconomy`：

```csharp
// path 指向 StreamingAssets/Prototype/BaseEconomy/preparation.json。
var config = JsonUtility.FromJson<PreparationScenario>(File.ReadAllText(path));
var session = new PreparationSession(config);

// 预置结果可独立开工。实际接入时，将已批准的战斗收益映射到同一 DTO。
ExpeditionDrop approvedDrop = config.presetExpeditionDrop.Clone();
PreparationCommandResult dropResult = session.ApplyExpeditionDrop(approvedDrop);
if (!dropResult.Success)
    throw new InvalidOperationException(dropResult.Code + ": " + dropResult.Reason);

PreparationSnapshot beforePurchase = session.GetSnapshot();
PreparationCommandResult purchaseResult = session.PurchaseAuxiliary(
    "purchase-001", config.purchaseQuantity);
if (!purchaseResult.Success)
    throw new InvalidOperationException(purchaseResult.Code + ": " + purchaseResult.Reason);

PreparationSnapshot beforeEnhancement = session.GetSnapshot();
PreparationCommandResult enhancementResult = session.EnhanceEquippedWeapon("enhance-001");
if (!enhancementResult.Success)
    throw new InvalidOperationException(enhancementResult.Code + ": " + enhancementResult.Reason);

PreparationSnapshot after = session.GetSnapshot();
PreparationLedgerEntry receipt = session.GetSettlementReceipt(approvedDrop.settlementId);
PreparationCommandResult repeatedDrop = session.ApplyExpeditionDrop(approvedDrop);
// repeatedDrop.Success == true，IsReplay == true；账本和当前状态不会再次改变。
string ledger = session.ExportLedgerText();

// 下一场的工程适配层按以下身份定位装备，读取装备数据；伤害由战斗模块计算。
string equippedBy = after.CombatantId;          // guard-linen
string weaponInstance = after.WeaponInstanceId; // guard-linen-sword-001
string weaponOwner = after.WeaponOwnerId;       // public
int enhancementLevel = after.WeaponEnhancementLevel; // 1
int equipmentAttackBonus = after.WeaponAttackBonus;  // 4
```

上述 `beforePurchase` 与 `beforeEnhancement` 都是脱离后续变更的只读历史值。当前没有通用快照导入；重跑同一起点使用新 session 或 `Reset()` 并重放相同输入。对照下一战时，工程保持队伍状态、指令、敌人和种子一致，再接入装备变化。

出征输入的字段为 `settlementId`、`expeditionId`、`encounterId`、非负 `cashReward` 和显式 `items` 数组。每项 `ItemQuantity` 使用 `itemId` 与正数 `quantity`；只接受配置中的 `oreItemId`、`auxiliaryItemId`，不接受重复标识或未知物品。当前默认输入如下：

```json
{
  "settlementId": "expedition-001-encounter-001-reward",
  "expeditionId": "expedition-001",
  "encounterId": "encounter-001",
  "cashReward": 0,
  "items": [{ "itemId": "expedition-ore", "quantity": 3 }]
}
```

输入配置使用可序列化 public fields；输出快照使用只读属性，不应将其直接视作 `JsonUtility` 存档 DTO。证据导出器显式映射为 `input_output.json`，供工程选择正式适配格式。

## 可替换配置

主要测试字段：`initialTreasuryCash`、`initialResidentCash`、两类公共库存、私人辅料库存、`auxiliaryUnitPrice`、`purchaseQuantity`、`oreRequired`、`auxiliaryRequired`、`initialWeaponEnhancementLevel`、`initialWeaponAttackBonus`、`enhancementAttackBonus` 和 `presetExpeditionDrop`。角色、物品及武器标识和显示名同样来自 JSON。

修改配置后新建会话；已有会话持有构造时的独立副本，`Reset()` 仍回到该副本。未知配置版本、空标识、非法数量及非预期掉落会明确拒绝。本轮只提供可替换测试值，没有定义正式价格、成长曲线或全局物品注册表。

## 手算开局与逐笔账本

以下为第二轮可替换测试数值，不是正式价格或强化曲线；与本次实跑导出的 [成功账本 CSV](../../../../outputs/base_economy_round2_20261114/unity_run_06/Examples/success_ledger.csv) 一致。

| 阶段 | 公库现金 | 许禾现金 | 现金合计 | 公有矿料 | 公有辅料 | 许禾私有辅料 | 武器等级／攻击加成 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| 开局 | 100 | 20 | 120 | 0 | 0 | 2 | 0／0 |
| 出征矿料 3 归公库，现金奖励 0 | 100 | 20 | 120 | 3 | 0 | 2 | 0／0 |
| 公库采购辅料 2，每份 6 | 88 | 32 | 120 | 3 | 2 | 0 | 0／0 |
| 强化消耗矿料 3、辅料 2 | 88 | 32 | 120 | 0 | 0 | 0 | 1／4 |

采购付款方是公库，收款方是许禾；辅料从许禾私人持有转为公共持有。强化从公共库存实际消耗两种材料，不向私人生产者再发公共工资。`OreConsumed` 与 `AuxiliaryConsumed` 保留累计实际消耗。

每条账本保存 `PayerId`、`PayeeId`、`Amount`、只读 `ItemChanges`、可空 `EquipmentChange` 和当时的 `After` 快照。物品变动记录来源所有者、目标所有者及是否实际消耗；装备变动记录所有者、已装备者、武器实例、强化等级和攻击加成的前后值。CSV 另有付款方、收款方显示名列，可直接看见许禾收到 12。全文见 [成功账本 TXT](../../../../outputs/base_economy_round2_20261114/unity_run_06/Examples/success_ledger.txt)。

## 原子性与重复结算

**原子性以单次命令为边界。** 缺钱、居民缺料、任一种公共材料不足时，本次操作明确拒绝，保留调用前的现金、库存和装备值。已经成功采购后再强化失败，会保留采购付款及辅料归公的结果，不会回滚整条出征与备战流程。

失败也不追加成功账本、不新增占用操作 ID；调用方可显示 `Code` 与 `Reason`。主要失败码包括 `InsufficientCash`、`InsufficientPrivateMaterial`、`InsufficientPublicOre`、`InsufficientPublicAuxiliary`、`AlreadyEnhanced`、`DuplicateIdConflict`，以及输入非法或数值溢出的拒绝码。

出征、采购、强化的成功 ID 共用同一个 session 命名空间。同 ID、同内容重试返回成功且 `IsReplay=true`，不重复发物资、付钱、扣料或追加账本；使用另一个合法输入重用已成功 ID，而内容或操作种类不同时返回 `DuplicateIdConflict`。输入本身不合法时先返回相应输入拒绝码。一次 session 只允许成功强化一次；换新 ID 也不能突破此限制。

查询 `GetSettlementReceipt`、读取快照和导出账本均不执行发奖。回执保留出征提交时的历史快照，查看当前库存应另调用 `GetSnapshot()`。

**去重仅覆盖同一 `PreparationSession` 实例。** `Reset()`、重新构造或重启程序都没有持久化去重记录，本轮不提供存档幂等保证。实际战斗结局是否有获奖资格、公共结算标识怎样生成及跨存档去重，由工程与战斗接入层负责；本模块接收已经批准结算的输入。

## 实跑验证

本轮采用 Unity 6000.3.28f1 对相同基地源码、测试和配置的隔离测试工程实跑。原工程的首次批处理启动被许可 IPC 阻塞；未删除原工程锁文件，也未用原整工程接入或构建作为通过证据。最终测试工程位于 `work/base_economy_round2_20261114/UnityTestProject_retry`，复制当前基地模块、对应 StreamingAssets 及既有 Packages/ProjectSettings。29 份运行相关 CS、asmdef、JSON 及 meta 在运行前后与原工程逐项哈希一致，文档及其 meta 不参与代码冻结判定。

实际环境记录：首个许可阻塞进程停止后，原 `Game/Temp/UnityLockfile` 仍存在，本专项保留该文件。工程会话下次对原工程运行批处理前，应核对当时的 Editor 进程与锁状态；这项环境记录不代表基地源码测试失败，也不要求暂停其他开发。

最终有效结果目录为 `outputs/base_economy_round2_20261114/unity_run_06/`：**44 项通过、0 失败、0 跳过**，其中首轮 `BaseSessionTests` 16 项，第二轮 `PreparationSessionTests` 28 项；Unity 退出码为 0。运行前后的源码冻结与首轮基线保持，原副本一致。历次尝试保留日志及分类，最终通过结论以 `unity_run_06` 为准。

| 证据 | 内容 |
| --- | --- |
| [最终汇总 verification.json](../../../../outputs/base_economy_round2_20261114/verification.json) | 有效测试、29 份源码与副本核对、首轮基线、四份历史账本比较和历次尝试分类 |
| [EditMode.xml](../../../../outputs/base_economy_round2_20261114/unity_run_06/EditMode.xml) | Unity Test Runner 的 44 项实际结果 |
| [EditMode.log](../../../../outputs/base_economy_round2_20261114/unity_run_06/EditMode.log) | 最终测试启动与执行日志 |
| [verification.json](../../../../outputs/base_economy_round2_20261114/unity_run_06/verification.json) | 环境、过滤范围、退出码、计数及源码一致性 |
| [input_output.json](../../../../outputs/base_economy_round2_20261114/unity_run_06/Examples/input_output.json) | 实际出征、采购、强化输入及各阶段输出；下一战装备读出 |
| [failure_cases.json](../../../../outputs/base_economy_round2_20261114/unity_run_06/Examples/failure_cases.json) | 四组拒绝结果、逐字段前后快照及账本不变标志 |
| [近厨房回归 CSV](../../../../outputs/base_economy_round2_20261114/unity_run_06/Examples/base_near_regression.csv)、[远厨房回归 CSV](../../../../outputs/base_economy_round2_20261114/unity_run_06/Examples/base_far_regression.csv) | 首餐回归：近 tick 7、远 tick 17，原有现金与归属结果 |
| [近厨房回归 TXT](../../../../outputs/base_economy_round2_20261114/unity_run_06/Examples/base_near_regression.txt)、[远厨房回归 TXT](../../../../outputs/base_economy_round2_20261114/unity_run_06/Examples/base_far_regression.txt) | 原首餐文字账本的逐字重放 |
| [首轮源码基线核对](../../../../outputs/base_economy_round2_20261114/unity_run_06/first_round_baseline_verification.json) | 首轮 5 个文件的哈希保持不变 |
| [本次配置快照目录](../../../../outputs/base_economy_round2_20261114/unity_run_06/Examples/config_used/) | 实跑的 `default.json` 与 `preparation.json` 副本，不是第二套运行参数 |

近远厨房的 CSV、TXT 共四份，与首轮历史账本的文本逐字一致。历史导出带 UTF-8 BOM，本次导出不带 BOM，因此每份文件的字节长度差 3，文件字节哈希不同；此处不声明四份导出文件字节完全相同。

成功记录确认采购前后现金都是 120；强化后累计消耗矿料 3、辅料 2，武器等级为 1、装备攻击加成为 4。`input_output.json` 的 `reportReadAndReplayHadNoEffect=true`，出征回执及重放均指向原序号 2，没有重复发奖或追加账本。测试还覆盖成功命令重放、同 ID 冲突、外部现金输入、配置替换、只读历史、输入深复制及非法输入／溢出原子拒绝。

| 实跑失败样例 | 输入缺项 | 实际结果 |
| --- | --- | --- |
| [缺钱](../../../../outputs/base_economy_round2_20261114/unity_run_06/Examples/insufficient_cash.json) | 公库 11，采购需 12 | `InsufficientCash`；现金、归属库存、装备与账本均不变 |
| [居民缺辅料](../../../../outputs/base_economy_round2_20261114/unity_run_06/Examples/insufficient_private_auxiliary.json) | 私有辅料 1，采购需 2 | `InsufficientPrivateMaterial`；未付款、未交付，账本不变 |
| [公库缺矿](../../../../outputs/base_economy_round2_20261114/unity_run_06/Examples/insufficient_public_ore.json) | 矿料 2，强化需 3；已采购辅料 2 | `InsufficientPublicOre`；辅料 2 不扣，装备不变；已完成的 12 元采购保留 |
| [公库缺辅料](../../../../outputs/base_economy_round2_20261114/unity_run_06/Examples/insufficient_public_auxiliary.json) | 公有辅料 1，强化需 2；矿料 3 | `InsufficientPublicAuxiliary`；矿料 3 不扣，装备不变；已完成的 6 元采购保留 |

四组记录的 `stateUnchanged` 和 `ledgerUnchanged` 均为 `true`。这证明本模块的单次命令拒绝行为，不代表整条流程回滚。本轮没有完成工程公共接口、UI、场景、PlayMode、Windows 整包构建或第二轮联动试玩验收。

## 仍待确定的正式规则

- 正式采购价格、材料配方、强化效果与长期成长曲线。
- 公营强化是否另收费用、工时、等级限制、品质或失败机制。
- 多居民市场、物流、仓储及自主生产如何扩展。
- 出征获奖条件、战斗结算向基地的公共适配、跨会话标识与存档去重。
- 战斗读取装备变化后怎样换算伤害；本模块不保证具体几次攻击击倒敌人。
- 基地 tick、战斗 WAIT 与离线时间的关系。

## 给 Unity 工程与集成的交接说明

加载 `preparation.json` 构造 `PreparationSession`，将一次获准结算的出征结果映射为本地 `ExpeditionDrop`，再依次提交公共采购和已装备武器强化；每步检查 `Success/Code/Reason`。界面只读取快照、回执与账本，重复查看不发奖；本地成功 ID 去重只在当前会话有效。下一战按装备者和武器实例读取 `WeaponAttackBonus` 等装备数据，由战斗模块换算效果。配置、默认手算、失败边界和本次实跑证据均见上文。公共接口、场景、界面及整包构建由工程统一接入。
