# 基地原型：首餐与出征备战

本模块保留首轮“招募厨师到顾客消费第一餐”的纯 C# 闭环，并扩展第二轮“出征材料归公库、向居民采购辅料、强化已装备武器”。玩法依据为研究目录中的 `PROTOTYPE_ENGINEERING_HANDOFF.md`、`EXPEDITION_SLICE_HANDOFF.md` 与 `BASE_CONSTRUCTION_AND_ECONOMY_RULES.md`；本目录没有改写正式规则或项目上下文。

首轮测试与试玩共用 `Prototype.BaseEconomy.BaseSession`，运行时程序集不引用 UnityEngine。首餐的可编辑测试配置为 `Assets/StreamingAssets/Prototype/BaseEconomy/default.json`，所有金额、起始库存、地图坐标、每格移动时长、加工工时与运行上限都在其中。JSON 的数值是测试值，不是正式平衡方案。

第二轮使用独立会话和独立配置，接入说明、输入输出与实跑状态见 [ROUND2_HANDOFF.md](ROUND2_HANDOFF.md)。本模块仅提供本地 DTO 与装备读出样例，公共接口、界面、场景及整包构建由工程会话统一接入。

## 首轮首餐接入

```csharp
var scenario = JsonUtility.FromJson<BaseScenario>(File.ReadAllText(path));
var session = new BaseSession(scenario);
session.RecruitCook();
BaseSnapshot state = session.Step();
state = session.RunUntilFirstMeal(scenario.runUntilFirstMealMaxTicks);
string ledger = session.ExportLedgerText();

// 选择的是开局预置厨房的位置，重新开始本场；并非发出建设命令。
var farSession = new BaseSession(scenario.WithKitchen(scenario.farKitchenPosition));
```

`BaseScenario` 是 `[Serializable]` 的 public fields 数据类；调用方使用 Unity JsonUtility 加载，核心不负责读文件。`Clone()` 与 `WithKitchen(GridPosition)` 都返回独立副本。`IsKitchenPositionAvailable` 检查格子在地图内且不是供粮者/顾客初始格。状态机在构造时复制输入，外部之后修改配置不会改变当前运行或 `Reset()` 的开局。

`RecruitCook()` 返回 `BaseCommandResult.Success/Reason`。`Step()` 推进一个基地 tick；`AdvanceTicks(n)` 和 `RunUntilFirstMeal(maxTicks)` 至多推进给定数量的 tick；未招募与已完成的场次均不推进。后者不会自动招募，运行上限耗尽时返回仍未完成的快照及阻塞原因。`Reset()` 恢复该实例原始配置、时钟和账本。

`GetSnapshot()` 提供四个钱包、个人物品归属、三人位置、当前工作、阻塞原因、目的地、已走/剩余格数、下一格剩余 tick 和剩余加工 tick。`FirstMealTick=-1` 表示尚未完成。`Ledger` 是有序只读视图；事件条目保存当时全部余额及库存，后续状态不会覆写旧记录。`ExportLedgerCsv()` 与 `ExportLedgerText()` 无墙钟时间，重放可逐字比较。

## 首轮默认可手算结果

招募在 tick 0，不占移动时间。厨师与顾客都从随后第一个 tick 开始行动。每格耗时 1 tick，加工 3 tick。每个 tick 先处理厨师，再处理顾客；同 tick 可先产出再成交，账本按实际先后保留两条。

|序号|基地 tick|事件|公库|供粮者|厨师|顾客|物品归属变化|现金合计|
|---|---:|---|---:|---:|---:|---:|---|---:|
|1|0|开局|100|10|0|20|供粮者有粮 1|130|
|2|0|招募厨师|88|10|12|20|厨师入驻|130|
|3|2|厨师现场购粮|88|16|6|20|粮 1：供粮者 → 厨师|130|
|4|7|厨师加工餐食|88|16|6|20|厨师粮 -1、厨师餐 +1；无公共工资|130|
|5|7|顾客现场购餐并消费|88|16|16|10|餐 1：厨师 → 顾客 → 消费|130|

默认地图 10×5。供粮者 `(1,2)`，顾客 `(0,2)`，近厨房 `(3,2)`，远厨房 `(8,2)`。近场厨师取料往返 4 格、顾客走 3 格，首餐 tick 7；远场厨师往返 14 格、顾客走 8 格，首餐 tick 17。远场粮食交易 tick 7，返回厨房 tick 14，完成加工及消费 tick 17。两场最终钱包和物品相同。

在本场不堵路的条件下，首餐 tick = `max(2 × 厨房到供粮者曼哈顿距离 × 每格tick + 加工tick, 顾客到厨房曼哈顿距离 × 每格tick)`。路径固定先走 x 再走 y，不引入随机数。

## 首轮回归验证

在 Unity Test Runner 的 EditMode 中运行 `Prototype.BaseEconomy.Tests.BaseSessionTests`。测试读取同一份 StreamingAssets JSON，调用同一状态机，覆盖手算全链、每 tick/每条账本现金守恒、重复招募和终局幂等、三种资金不足/缺粮的交易原子性、同开局及重置重放、近远对照、配置耗时、拒绝未实现的非零现金流。Unity 工程与界面是否实际运行通过，由集成会话另行报告。

模块只读快照和账本为调试数据；没有额外的 UI 结算版本。见 `ASSUMPTIONS.md` 了解当前临时口径与尚未实现的正式规则。

## 第二轮备战接入

第二轮入口为同一命名空间下的 `PreparationSession`，配置为 `Assets/StreamingAssets/Prototype/BaseEconomy/preparation.json`。读取 JSON 后构造会话，依次调用：

```csharp
var scenario = JsonUtility.FromJson<PreparationScenario>(File.ReadAllText(path));
var session = new PreparationSession(scenario);
PreparationCommandResult drop = session.ApplyExpeditionDrop(scenario.presetExpeditionDrop);
PreparationCommandResult purchase = session.PurchaseAuxiliary("purchase-001", scenario.purchaseQuantity);
PreparationCommandResult enhance = session.EnhanceEquippedWeapon("enhance-001");
PreparationSnapshot after = session.GetSnapshot();
```

实际接入必须逐次检查 `Success`，失败时显示 `Code/Reason` 并停止当前流程；带检查、结算重放和下一战读出的完整样例见 [ROUND2_HANDOFF.md](ROUND2_HANDOFF.md)。核心不读取文件，不引用 UnityEngine，也不计算战斗伤害。

默认开局公库 100、辅料商许禾 20，许禾持有淬火砂 2；出征矿料 3 进入公库。采购 2×6 后余额为公库 88、许禾 32，合计仍为 120。强化消耗公有矿料 3 和淬火砂 2，公营战士林恩已装备的巡卫短剑强化等级 0→1，`WeaponAttackBonus` 0→4。武器所有权仍为公共，装备者不变。

事务原子性按单次命令保证；采购成功后强化失败保留已经完成的采购。成功操作 ID 在同一 session 中去重；查看回执不发奖。`Reset()` 清空去重记录，当前不提供跨存档保证。

本次使用 Unity 6000.3.28f1 的本模块隔离副本实跑，44 项 EditMode 测试全部通过（首轮 16＋第二轮 28），原工程与副本运行源码和配置哈希一致。成功账本、四组缺钱缺料结果、最小输入输出和原工程锁状态见 [ROUND2_HANDOFF.md](ROUND2_HANDOFF.md)。该结果不代表原整工程的界面、场景或构建已接入通过。
