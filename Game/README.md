# 测试原型：战斗、基地与出征

> 当前状态（2026-11-16）：最新 A/B UI 试玩包在关闭窗口时发生 UnityPlayer 原生崩溃，尚未修复；C 正式审核仍暂停。本文保留各轮历史说明，历史测试通过不代表退出路径已通过验收。最新总览见 [仓库 README](../README.md) 与 [项目上下文](../docs/context/PROJECT_CONTEXT.md)。`outputs/` 下的历史证据仅保留在原工作区，不随源码仓库上传。

本工程使用 **Unity 6000.3.28f1**。第四轮继续默认进入「战术编排与战斗」，优先加载法师技能库，保留首轮「战斗试验」「基地试验」及第二轮「出征与备战」。界面、规则测试、导出的记录调用同一份纯 C# 核心；战斗 WAIT 与基地 tick 独立，不作隐式换算。

所有玩法数字均为可替换测试值；具体待定规则见本文末尾和各模块的 `ASSUMPTIONS.md`。未修改研究资料目录的 `PROJECT_CONTEXT.md`。

## 打开与试玩

工程位置：仓库根目录下的 `Game/`。

1. 在 Unity Hub 的项目列表打开 `Game`。若列表未刷新，用「从磁盘添加项目」选择以上文件夹。
2. 确认编辑器为 **6000.3.28f1**，等待包解析与脚本编译完成。
3. 在 Project 面板打开 `Assets/Prototype/Scenes/PrototypeHub.unity`。
4. 点击顶部 Play，并切到 **Game** 标签。界面在运行时建立；未运行时场景中只有相机和入口控制器，这是预期行为。
5. 默认查看第四轮法师库战术页；顶部四个入口可切换并保留各自进度。离开战术页会暂停自动播放。首轮战斗／基地继续独立；出征入口把战斗收益交给备战公库，再把真实武器值交给下一战。

场景已生成。只有场景丢失或明确需要重新生成时，使用菜单 `Prototype > Generate Hub`；它会重建 Hub 场景，不会重置测试 JSON。手动编辑场景后，应先保存自己的副本再重建。

也可运行首轮历史 Windows 开发包（它不包含第二轮出征入口）：

`..\outputs\prototype_20261114\playable\Windows\Prototype.exe`

请保留整个 `Windows` 文件夹，包括 `Prototype_Data`、`MonoBleedingEdge`、`UnityPlayer.dll` 和字体授权文件。它是本地开发试玩包，不是已完成平衡或发布验收的游戏。

## 第四轮：MP、群体技能与状态反馈

默认配置为 `Assets/StreamingAssets/Prototype/Combat/round4_mage_library.json`，法师实际开放 **27** 项技能，仍只有 **5** 条装载容量；27 技能不是 27 个槽，满槽时应替换已有指令，或先删除再新增。该例开局法师 MP **4／30**，以第一条冥想指令检验真实回蓝。技能数、资源、效果与 WAIT 都是第四轮试玩参数；最终实跑记录见验证节。

左侧「查找技能」按实际技能目录浏览，可用中文名称筛选，仍只显示当前角色白名单中的技能。技能详情读取实际 MP 费用、基础 WAIT、效果、覆盖、`isMagic` 与 `damageType`；条件使用真实目录下拉，阈值按比例、点数、人数、WAIT、排号分别说明，不能把 WAIT 小数误读成百分比。

右侧继续使用固定 14 号三行卡。MP 从真实快照刷新，点卡可见完整姓名、标签、当前有效敏捷系数、所有状态的幅度／施加／到期与装备来源。短状态使用护、魔盾、增、易、急、缓、默、晕、弱等；过长姓名或多状态只在首行省略，HP／MP 两行完整生成，不用全局省略或缩小字号隐藏资源。

- 冥想／传蓝／群体回蓝直接显示实际目标 MP 前后及变化量；汲魔的目标扣除与施放者返还分别显示，封顶与实际回收量由核心给出。
- 群体先按完整条件与选法选锚点，再由核心扩展同队、原范围内的合法存活覆盖者。覆盖者不重复筛锚点条件，整次只付一次 MP、安排一次 WAIT。红色高亮读取实际 `targetIds` 主覆盖名单，锚点字段保留；汲魔返还给施放者的额外效果不被误作主覆盖。
- 每项效果列实际 HP／MP 前后、状态 ID、施加／到期、被移除状态与永久易伤标记变化。定身跳过显示真实 `controlReason`，沉默跳过显示对应尝试的 `reasonCode` 与原原因，再由核心选择回退指令。
- 加速／减速的当前有效系数从快照读取，执行记录分别给出基础敏捷、状态倍率与实际 WAIT；界面不重算、提前改写已预约时刻或推导状态效果。

单步、逐 Step 自动、暂停与本场重放沿用第三轮路径。历史事件只切换解释和高亮，棋盘保持当前真实状态；历史 HP／MP／状态以前后记录为准。「单项对照」应核对新 MP 与状态效果的实际分歧，导出保留完整结构化效果及已请求对照，不为导出补跑战斗。

战例下拉保留第三轮，并新增冥想收益与 WAIT 风险、群攻、净化、控制等第四轮配置。新的动作仍为瞬发试玩；咏唱、随机命中、AP／PP、持续伤害和复活不在本轮范围。

## 第三轮保留：编排战术并逐步观看

战术页左侧编辑四段规则，右侧显示双方独立 3×3、HP/MP、短状态和近时刻 WAIT 队列。卡片固定字号，金边标行动者，红色标目标；点击卡片可看完整状态到期、下次行动时刻、装备来源及已提交指令。

战例下拉包含「第三轮：自由编辑四段指令」「预留MP救人：只改强攻的自身MP比例门槛」「标记后集火：只改队友目标选择方式」。切换战例会载入该战例的新工作台；可先导出当前编辑，再切换。

1. 选择角色，看该角色的智力、指令容量、每条条件项容量与真实可用技能。法师与低智力队友的目录和容量分别读取核心，不因全场存在某个技能就自动向所有角色开放。
2. 选已有规则编辑，或点「新建空白」逐项选择条件、对象范围、选择方式与行动，再「新增到末尾」。修改已有规则用「应用替换」；删除、上移、下移也由核心校验。空白表单不替玩家填入隐含规则。
3. 条件默认读取同一个候选目标；选择施放者上下文时会明确读取施放者。三项 AND／OR 混合必须选择括号分组，不使用隐藏运算优先级。超容量、无效范围或不可用技能会显示拒绝原因，原草稿与本场不变。
4. 点击「应用并开战」，才从当前草稿的初始 HP/MP、状态和种子创建本场，并开始自动播放。之后继续编辑左侧只改变草稿，右侧本场仍使用已提交配置。
5. 要单步先点「暂停」，再用「单步」看一个实际事件；也可设置 0.05–5 秒间隔后「自动播放」。每个间隔只调用一次同核心 Step，再更新棋盘；「暂停」及切换入口都会停止后续推进。
6. 从事件列表选一条，滚动查看真实 attempts、逐候选条件与可执行性、前一条为什么跳过、最终对象与效果、同刻裁决。历史事件不会倒转当前棋盘，HP 前后值以该事件的实际效果为准。
7. 右侧「重放本场」与顶部「同开局重放」恢复上次提交的完整开局；未应用草稿不会混入重放。要让新草稿参与战斗，再点「应用并开战」。「单项对照」针对加载战例与单项编辑，实际运行两侧并记录首个真实行动／效果分歧；不会用日志措辞差异替代行动分歧。

发送战术到出征时，由当前 `ExpeditionSliceSession.ApplyTactics` 原子接收友方指令与所需可用动作，保留该营地现有钱包、采购、强化和去重记录。已经创建出征时拒绝接收，仍留在战术页显示真实原因。新战术完整编辑回到战术页；旧出征页的急救快捷控件可能不适用于新增技能。

「导出输入与战报」使用已有 `--prototype-report-dir` 根路径下的唯一 `tactics_*` 子目录。`loaded_scenario.json` 是加载开局，`tactics_input.json` 是当前已提交到工作台的编辑战术，`committed_scenario.json` 是本场已应用配置，`instruction_form.json` 保留尚未提交的表单输入；`session_result.json`、`tactics_report.txt`／`battle_report.txt` 和 `catalog_snapshot.json` 保存完整实际事件、已请求的对照与目录。导出只读，不自动开战、不补跑尚未点击的对照。当前依旧没有跨进程存档；未提交草稿与上场配置的区别会保存在导出中。

## 战斗：只换顺序，重放第一步

默认开局是法师满血且先行动，队友 HP 20/100，两名敌人分别为 35/100、80/100。

1. 保持「急救」在第一行，点击「重放首步」。法师应对队友急救：**HP 20→65，法师 MP 30→24**。
2. 点击「急救 ↓」，把急救移到普攻之后。该操作自动恢复当前选择对应的开局。
3. 再点「重放首步」。法师应普攻低血敌人甲：**HP 35→17，队友仍为 20/100**。
4. 点「急救 ↑」移回前面，可重复第一组对照。

另一组只改一个选择的对照：先「恢复默认」，把「队友开局 HP」改为 **100**，点「应用并重置」，再「重放首步」。日志应记录急救没有合格对象，随后回退到普攻。

- 「推进一个行动」：使用当前运行状态结算下一次行动机会。
- 「重放首步」：保留当前指令顺序、治疗阈值、开局 HP，先重置再执行首步。
- 「重置当前选择」：只恢复当前选择的开局，不自动行动。
- 「恢复默认」：恢复本次启动时读取的 JSON。不会覆盖磁盘文件。
- 右侧日志可用鼠标滚轮查看，包含候选逐人判断、跳过原因、行动、目标、HP/MP 变化与 WAIT。

本场使用直接给定的效果和敏捷系数，无随机伤害、命中、暴击或闪避。种子被记录但没有参与随机抽样。同值目标暂按 JSON 中的角色顺序选择，这是测试口径；首步样例避免了平手。第二轮核心已替换首轮同刻／无动作阻塞：同刻先处理到期状态，再按 JSON 角色顺序行动；单人无指令时记录并休眠，其他人继续；无后续变化或重复局面停止为 `Stalemate`，预算停止为 `Stopped`。界面应显示实际结局和停止原因。

## 基地：从招募到第一餐

默认有预置厨房、一个供粮者和一个顾客，厨师尚未招募。厨房位置选择是在比较不同开局布局，**不是免费建造**。

1. 切到「基地试验」，选择「近处」。
2. 点击「招募厨师」：公库付出 12，厨师得到 12 启动资金。
3. 用「推进一步」观察厨师和顾客移动，或点「跑到首餐」。默认近处应在 **tick 7** 售出并消费首餐。
4. 选择「远处」，开局自动重置。再次招募并跑到首餐，应在 **tick 17** 完成。
5. 两次最终余额都应为：公库 **88**、供粮者 **16**、厨师 **16**、顾客 **10**；区别来自路程和时间。

地图上可用的格子也可用来选择预置厨房位置。切换位置会开新局；「重置同一开局」保留当前位置，「恢复默认」恢复启动时读取的位置。

默认账本可以手算：

| 事件 | 基地时刻 | 公库 | 供粮者 | 厨师 | 顾客 | 现金合计 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 开局 | 0 | 100 | 10 | 0 | 20 | 130 |
| 招募厨师，公库支付 12 | 0 | 88 | 10 | 12 | 20 | 130 |
| 厨师支付 6 现场购粮 | 2 | 88 | 16 | 6 | 20 | 130 |
| 厨师将粮加工为餐 | 7 | 88 | 16 | 6 | 20 | 130 |
| 顾客支付 10 并消费 | 7 | 88 | 16 | 16 | 10 | 130 |

加工与销售在同一个 tick 分别记账，保留餐先归厨师、再交付并消费的过程。私营加工不额外领取公库工资。重复招募或在终局继续推进，不会重复扣款或生成物品。

## 第二轮：出征、采购、强化与再战

新增入口持有一个 `ExpeditionSliceSession`。在营地调整法师指令顺序或治疗阈值，再创建唯一 ID 的出征；逐场推进和一键派遣使用同一个 `CombatExpeditionSession`，已经完成的报告不会重打。启动后本次全部场景、指令、种子与装备冻结。

默认保留急救优先时，第一场 WAIT 190 胜利，明确经过 15 WAIT，第二场从 205 开始并在 310 胜利。实际 HP/MP、未到期状态、格位与尚余行动等待沿用，不回满血／满蓝。两胜累计入公库现金 25、矿料 5，角色 XP 为法师 27、队友 25，总计 52。

接着向许禾采购淬火砂 2，每份 6；钱从公库进入许禾钱包，材料从私人归公。强化真实已装备武器消耗公共矿 3、砂 2。默认结果为公库 **113**、许禾 **32**、公共矿 **2**、砂 **0**、武器等级 **1**、攻击加成 **+4**。缺钱／缺料会显示原因，失败无部分扣款，可继续其他步骤后用同一失败命令 ID 重试。

「同起点强化对照」使用主线结束时捕获的 WAIT 310 完整队伍状态和冻结指令；复制第二场敌情，只把两个敌人的起始 HP 改为 40，保留其余字段，场间为 0 WAIT。两侧只有真实装备加成 0→4 不同：WAIT 370 同一敌人 HP 20→**2／0**，终局 **490／430**。对照不消耗当前真实队伍，不产生奖励。

真实验证再战读取**当前**装备与完整队伍状态并更新队伍，默认强化后的第一次终局为 430；重复再战继续承接上次消耗。它明确没有现金、物品或 XP 奖励。之后再看同起点对照仍使用原 310 基准。联动使用 `guard-linen` 的 `guard-linen-sword-001` 映射到战斗 `ally` 的真实 +4，没有使用专项独立装备例子的 +9。

本轮营地只允许一次有奖励的两场出征；已有出征时再次创建会明确拒绝，防止无提示恢复模板队伍。全开局重置恢复加载时的配置，清除改序／阈值、报告、库存变化、XP、强化和去重，再创建才会获得新 GUID。读取报告与导出是只读操作；已点击生成的对照和每次真实验证战都会保留在完整导出中。

## 参数与重复开局

首轮配置位置：

- 战斗：`Assets/StreamingAssets/Prototype/Combat/default.json`
- 基地：`Assets/StreamingAssets/Prototype/BaseEconomy/default.json`

第二轮联动配置入口为 `Assets/StreamingAssets/Prototype/Expedition/default.json`，显式引用 `Prototype/Combat/round2_expedition.json` 与 `Prototype/BaseEconomy/preparation.json`。它还配置战斗物品 `expedition_ore` → 基地 `expedition-ore`、装备者与武器实例映射，以及对照／验证战敌方 HP 和场间 WAIT。专项 `round2_*.json` 对照与首轮配置继续独立保留。

第三轮战术开局为 `Assets/StreamingAssets/Prototype/Combat/round3_tactics.json`。可配条件、范围、选择方式、条件上下文和括号目录来自 `CombatTacticsCatalog`，角色技能来自 `CombatSession.GetAvailableActions(unitId)`；不在 UI 另建可执行规则目录。

第四轮法师库及独立战例位于同目录的 `round4_*.json`；`round4_expedition.json` 是出征 DTO，不当作单场战术开局加载。新增 `targetMode/isMagic/damageType` 与 MP／状态效果字段直接来自 Combat。当前目录扩为 37 项条件、24 项选择器，技能库存与装载容量仍分开。

战斗配置包含 `units` 的 HP/MP、智力、初始行动时刻、敏捷系数和指令，`actions` 的效果、费用和基础 WAIT，以及显式的指令容量测试参数。HP 条件阈值在 JSON 中用 **0–1 比例**，界面输入框用 **0–100 百分数**。

基地配置包含开局现金、招募测试金额、粮价、餐价、原料数、加工时间、每格移动时间、厨房近远坐标、地图大小及运行上限。`taxAmount`、`welfareAmount`、`externalCashFlow` 本轮必须为零；非零会明确拒绝，不会静默忽略。

改 JSON 后先停止 Play，再开始 Play。已经运行的会话保留启动时的配置副本；已构建程序则读取其 `Prototype_Data/StreamingAssets/Prototype/` 内随包复制的 JSON。改工程里的 JSON 不会自动更新旧试玩包。

首轮验证实际使用的 JSON 快照在 `../outputs/prototype_20261114/config_used/`。第二轮真实试玩导出的六份配置快照位于 [configuration/](../outputs/expedition_round2_20261114/player_reports/20261113_202917_038_e972edf6/configuration/)，包含战斗、集成、备战各自的 `*_loaded.json` 与 `*_current.json`。这些快照用于核对加载配置与当前选择，不是第二套运行参数源。默认样例的手算断言针对对应轮次的测试值；更换基准数字时，应一起核对相应的预期结果。

## 集成 API 与会话边界

`Prototype.Expedition.ExpeditionSliceSession` 构造接收集成、战斗出征、基地备战三份 DTO；Runtime 不读取文件或引用 UnityEngine。主要命令为 `StartExpedition`、`RunNextEncounter/RunToEnd`、`RetrySettlement(resultId)`、`PurchaseAuxiliary(commandId,quantity)`、`EnhanceEquippedWeapon(commandId)`、指令编辑、`CompareEquipmentFromCurrentState` 与 `RunVerificationBattle`。

`GetSnapshot/GetAcceptance` 返回深复制；验收 DTO 使用 public fields 和数组，可直接由 JsonUtility 序列化，包含实际主线、逐笔公库与逐角色 XP 结算、采购强化账本、对照、真实再战与当前队伍。`ExportReportText/ExportLedgerText/ExportLedgerCsv` 只格式化已有记录，不为导出额外运行模拟。详细契约见 `Assets/Prototype/Expedition/README.md`。

去重范围是当前营地的同一个内存 `PreparationSession`。只有真实、获奖的战斗 resultId 可原样作为 settlementId 入账；公库失败不先记 XP，成功重试不重复任何收益。Reset 才显式清空这一实验的记录。通用跨进程存档、重启后的幂等、世界时间与离线收益均不在第二轮范围内。

第三轮的 `Prototype.Tactics.TacticsWorkbenchSession` 分开持有 editor 与 committed battle；编辑调用核心 Insert/Replace/Remove/Move，`CommitBattle` 创建本场，`StepBattle` 与 `ReplayCommitted` 供 `TacticsBattleView` 绑定。自动播放没有先 RunToCompletion 再播放的中间模拟。Hub 的 `ShowTactics` 为默认入口，其余三个入口及已验收的结算路径继续保留。

## 自动验证与记录

Unity 的 `Window > General > Test Runner` 可运行本工程的 EditMode 和 PlayMode 测试。PlayMode 测试通过真实 uGUI 按钮监听链调用会话，并检查中文和文字可见性。

批处理脚本在 `../tools/Run-PrototypeChecks.ps1`。运行前先关闭这个工程的 Unity 编辑器；脚本不会删除锁文件，也不会抢占已打开的同一工程。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-PrototypeChecks.ps1
```

在仓库根目录执行。默认依次生成场景、跑 EditMode、跑 PlayMode、导出样例、构建 Windows；也可用 `-Stage EditMode` 等单独运行。每次创建新的上海时间戳输出目录，保存日志、NUnit XML 和 `summary.json`。测试数为零、失败或缺少结果文件都不会报告成功。

首轮历史验收为 **37 项 EditMode、5 项 PlayMode**；记录位于 `../outputs/prototype_20261114/`：

- `EditMode.xml`：战斗与基地规则结果。
- `ui_verified/PlayMode.xml`：最终界面接入及名单可见性结果。
- `export/Examples/combat_heal_first.txt`、`combat_attack_first.txt`：仅改指令顺序的实跑记录。
- `export/Examples/base_near.csv`、`base_far.csv` 及对应 `.txt`：现金、物品归属与独立时刻的实际账本。
- `playable/summary.json`：交付试玩包的构建结果。
- `verification.json`：集成会话的最终核验摘要及源文件校验值。

第二轮已在**原 Game 工程、Unity 6000.3.28f1** 实跑：Generate、EditMode、PlayMode、Examples 均通过，退出码均为 0。EditMode **115／115**（战斗 53、基地 44、出征集成 18）；PlayMode **13／13**，两类测试均为 0 失败、0 跳过。

- [EditMode.xml](../outputs/expedition_round2_20261114/validation_run01/EditMode.xml)：本轮原工程规则与集成测试。
- [PlayMode.xml](../outputs/expedition_round2_20261114/validation_run01/PlayMode.xml)：本轮真实界面测试。
- [summary.json](../outputs/expedition_round2_20261114/validation_run01/summary.json)：各执行阶段摘要。

### 第二轮最终交付与真实 EXE 验收

Windows **Build 已通过，退出码 0**；交付 ZIP 已冻结。集成负责人已解压并启动实际 EXE，通过鼠标操作走通完整主线及改序首场对照。

- [Windows ZIP](../outputs/expedition_round2_20261114/package/ExpeditionPrototype-Windows.zip)
- [已验收的解压 EXE](../outputs/expedition_round2_20261114/package/Extracted/ExpeditionPrototype/Prototype.exe)

默认真实试玩导出：

- [完整战报 battle_report.txt](../outputs/expedition_round2_20261114/player_reports/20261113_202917_038_e972edf6/battle_report.txt)
- [采购强化账本 preparation_ledger.txt](../outputs/expedition_round2_20261114/player_reports/20261113_202917_038_e972edf6/preparation_ledger.txt)
- [账本 CSV](../outputs/expedition_round2_20261114/player_reports/20261113_202917_038_e972edf6/preparation_ledger.csv)
- [结构化验收 session_result.json](../outputs/expedition_round2_20261114/player_reports/20261113_202917_038_e972edf6/session_result.json)

实际默认结果为公库 **113**、许禾 **32**、矿 **2**、砂 **0**、武器 **Lv1／+4**、角色 XP 合计 **52**。同起点对照从 WAIT 310 开始，WAIT 370 同一敌人 HP 为 20→2／0，两侧终局为 490／430。真实再战承接 310→430，读取武器实例 `guard-linen-sword-001` 的真实 +4，无额外奖励；重复结算及切换页面后，账本仍为 5 笔、XP 仍为 52。

[改序后的真实 EXE 导出](../outputs/expedition_round2_20261114/player_reports/20261113_202645_146_36aa590c/)保留了另一组实际操作结果：第一场进攻优先在 WAIT 240 结束，林恩倒下；默认急救优先的第一场在 WAIT 190 结束，全员存活。首轮历史交付与 37／5 测试记录继续保留，第二轮测试计数以上方 115／13 为准。

这些是当前内存营地的试玩结果；程序仍没有通用跨进程存档或重启后的去重保证。显式全开局 Reset 会清空本次记录。

### 第三轮分阶段验证与交付

本轮在原 Game 工程、Unity 6000.3.28f1 实跑，最终结果按下列各阶段记录核对：

| 阶段 | 实际结果 | 证据 |
| --- | --- | --- |
| EditMode | **193／193**，0 失败、0 跳过；Combat 111、Base 44、旧出征集成 18、工作台 20 | [EditMode.xml](../outputs/tactics_round3_20261114/validation_run01/EditMode.xml) |
| 首次 PlayMode | **19／21**，2 失败；保留初次失败证据 | [首次 PlayMode.xml](../outputs/tactics_round3_20261114/validation_run01/PlayMode.xml) |
| 修复后 PlayMode | **21／21**，0 失败、0 跳过，退出码 0 | [最终 PlayMode.xml](../outputs/tactics_round3_20261114/ui_validation_run02/PlayMode.xml) |
| 首两轮样例回归导出 | Examples Passed，退出码 0 | [regression_examples/summary.json](../outputs/tactics_round3_20261114/regression_examples/summary.json) |
| Windows 构建 | Build Passed，退出码 0 | [build_run01/summary.json](../outputs/tactics_round3_20261114/build_run01/summary.json) |

两次测试间的 C# 改动仅为 `TacticsBattleView` 的卡片纵向留白修复，以及 `TacticsHubPlayModeTests` 按实际 JsonUtility 输出校验导出的精确序列化断言；Core 未改变。首次 [All summary](../outputs/tactics_round3_20261114/validation_run01/summary.json) 的 Failed 状态保留，不改写为通过；最终阶段分别引用以上真实记录。第二轮 115／13 与其交付包继续作为历史验收。

本轮打包与解压目标：

- [TacticsPrototype-Windows.zip](../outputs/tactics_round3_20261114/package/TacticsPrototype-Windows.zip)
- [解压目录 Prototype.exe](../outputs/tactics_round3_20261114/package/Extracted/TacticsPrototype/Prototype.exe)

### 第三轮真实解压 EXE 验收

集成负责人已从上述解压 `TacticsPrototype/Prototype.exe` 用鼠标完成实际操作：默认「应用并开战」自动运行到 **WAIT 135**，队友剩 **42 HP**；同开局重放后暂停并单步，可见急救跳过后选择标记，行动者／目标突出且实际候选原因可查看。恢复自动后得到相同结果并导出 [基准战报](../outputs/tactics_round3_20261114/player_reports/tactics_20261113_212026_128_199dba1c/tactics_report.txt)与 [基准 session_result.json](../outputs/tactics_round3_20261114/player_reports/tactics_20261113_212026_128_199dba1c/session_result.json)。

最短单项对照步骤：选择法师的第 1 条急救指令（索引 0）→滚动到条件阈值，将 **0.35 改为 0.10**→「应用替换」→「应用并开战」（开始自动播放；要单步先暂停）→「单项对照」。直接在未提交表单时点击「应用并开战」已实际验证会拒绝，保护本场不混入未提交输入。保持其他配置不变后，真实战斗结束于 **WAIT 75**、队友剩 **12 HP**；对照首次实际分歧在 **WAIT 30：急救→强攻**。

变体与对照的实际导出：

- [tactics_report.txt](../outputs/tactics_round3_20261114/player_reports/tactics_20261113_212136_543_74a30535/tactics_report.txt) · [session_result.json](../outputs/tactics_round3_20261114/player_reports/tactics_20261113_212136_543_74a30535/session_result.json)
- [加载开局 loaded_scenario.json](../outputs/tactics_round3_20261114/player_reports/tactics_20261113_212136_543_74a30535/loaded_scenario.json) · [编辑战术 tactics_input.json](../outputs/tactics_round3_20261114/player_reports/tactics_20261113_212136_543_74a30535/tactics_input.json) · [上场 committed_scenario.json](../outputs/tactics_round3_20261114/player_reports/tactics_20261113_212136_543_74a30535/committed_scenario.json)

还实际点击「发送战术到出征」，保留原第二轮开局的队友 HP 20、法师 MP 30 与钱包 100／20。换用原出征敌情后，两场均胜利，结束于 **WAIT 610**、林恩倒下，公库 **125**、矿 **5**、XP **52**。结果见 [出征实际战报](../outputs/tactics_round3_20261114/player_reports/20261113_212217_257_bbc791fb/battle_report.txt)与 [出征 session_result.json](../outputs/tactics_round3_20261114/player_reports/20261113_212217_257_bbc791fb/session_result.json)。0.10 阈值在工作台战例中节省 WAIT，同时留下更少 HP；同一战术换敌情和起始资源后不保证更好。

第三轮交付 ZIP 为 **77.6 MiB、297 文件**，Windows x64（PE Machine `0x8664`）。当时 GUI 验收前后包哈希与 **73 份源码输入**哈希均未变化，原交付包保持冻结。完整历史记录见 [DELIVERY.md](../outputs/tactics_round3_20261114/DELIVERY.md) 与 [verification.json](../outputs/tactics_round3_20261114/verification.json)；当前内存会话、无跨进程存档的边界不变。

### 第四轮验证与交付

第四轮已在**原 Game 工程、Unity 6000.3.28f1** 完整执行 All，整体 **Passed**：EditMode **329／329**、PlayMode **25／25**，两类测试均为 0 失败、0 跳过；Generate、EditMode、PlayMode、Examples、Build 每阶段退出码均为 0。

- [第四轮 All summary.json](../outputs/mage_round4_20261114/validation_run01/summary.json)
- [EditMode.xml](../outputs/mage_round4_20261114/validation_run01/EditMode.xml) · [PlayMode.xml](../outputs/mage_round4_20261114/validation_run01/PlayMode.xml)
- [MageLibraryPrototype-Windows.zip](../outputs/mage_round4_20261114/package/MageLibraryPrototype-Windows.zip)
- [解压入口 MageLibraryPrototype/Prototype.exe](../outputs/mage_round4_20261114/package/Extracted/MageLibraryPrototype/Prototype.exe)

已从上述解压 EXE 完成真实鼠标验收：中文查找冥想、应用后自动播放、暂停／重放／单步、实际替换群体技能、单项对照及发送战术到旧营地。具体操作与结果如下；完整交付记录见 [本轮 DELIVERY.md](../outputs/mage_round4_20261114/DELIVERY.md)。

| 真实 GUI 操作 | 实际结果 | 导出 |
| --- | --- | --- |
| 默认法师库查找冥想，重放并单步，再继续自动 | 冥想 MP **4→16**、WAIT **60**；最终 Victory **230**，法师 **38 HP／6 MP**、队友 **100 HP／8 MP**；目录为 27 技能／37 条件／24 选法 | [默认库记录](../outputs/mage_round4_20261114/player_reports/tactics_20261113_221112_341_054992c8/) |
| 群攻战例把 `primary_spell` 从火弹换成全体火雨，应用替换后重放单步，再单项对照 | 三敌各 **18→4 HP**，只扣 **14 MP**、安排 **65 WAIT**；实战只推进 **1** 步，对照另得基准 **80**／变体 **65**，首次分歧 **0** | [群体记录与对照](../outputs/mage_round4_20261114/player_reports/tactics_20261113_221331_776_c54717db/) |
| 回默认库，发送战术到旧第二轮营地并一键派遣 | 保留初始法师 **100 HP／30 MP**、队友 **20 HP／10 MP**及钱包 **100／20**；首动 `mage_first_aid`，两胜至 **595**，最终法师 **100 HP／18 MP**、队友 **44 HP／8 MP**；现金 **125**、矿 **5**、XP **52** | [真实出征记录](../outputs/mage_round4_20261114/player_reports/20261113_221428_513_2be6e912/) |

净化、控制、汲魔本次未逐项手点，覆盖来自自动测试。GUI 后核对 **304** 个解压文件、**90** 项冻结运行输入、**23** 个保护项均无变化，ZIP SHA 保持原值；代码、测试、配置、ZIP 与包内 `START_HERE` 未为收尾文档重写。前三轮历史证据及当前内存会话、无跨进程存档边界不变。

## 工程边界与负责人

| 路径 | 负责人 | 约定 |
| --- | --- | --- |
| `Assets/Prototype/Combat/` 及对应 StreamingAssets | 战斗专项 | 纯 C# 核心、规则测试、测试数字与假设 |
| `Assets/Prototype/BaseEconomy/` 及对应 StreamingAssets | 基地专项 | 纯 C# 核心、账本、规则测试、测试数字与假设 |
| `Assets/Prototype/Expedition/` 及对应 StreamingAssets | 出征集成 | 公共奖励／XP 去重、装备映射、同起点对照与真实再战 |
| `Assets/Prototype/Tactics/` | 战术工作台适配 | 草稿／本场分离、单项实际对照与验收 DTO；决策仍由 Combat 核心执行 |
| `Assets/Prototype/UnityClient/` | 工程/UI | 界面与命令接入；不另写结算 |
| `Assets/Prototype/Editor/`、`Scenes/`、`Art/` | 工程/UI | 场景、字体、生成与构建入口，单一负责人编辑 |
| `Packages/`、`ProjectSettings/`、公共接入与交付 | 集成负责人 | 统一版本、配置、测试调度和集成 |

场景、Prefab 和项目设置需要修改时先交接负责人；资产和 `.meta` 一起移动和提交。不要同时运行两个编辑器修改本工程。

固定依赖：URP 17.3.0、uGUI/TMP 2.0.0、Input System 1.20.1、Test Framework 1.6.0。官方旧 2D 模板的 Input System 1.12.0 与当前编辑器不兼容，已替换为支持 Unity 6 的版本。中文采用项目内的 Noto Sans CJK SC，授权和来源随构建复制到 `ThirdPartyNotices/NotoSansCJK/`。

## 待定规则与本轮口径

| 具体分歧 | 当前测试口径/处理 | 负责人 |
| --- | --- | --- |
| 智力容量的基数、系数、取整及条件解锁门槛 | JSON 中显式给定线性测试值，仅检验容量接入 | 战斗专项 |
| MP 费用、伤害/治疗公式、射程、前排倾向 | 使用固定测试效果与费用；样例显式选 HP 比例最低者 | 战斗专项 |
| 己方是否包含自己、同值目标与同刻事件次序 | 配置决定己方范围；目标同值取 JSON 顺序；同刻先到期状态再按角色顺序，这是第二轮测试口径 | 战斗专项 |
| 无可执行指令时如何推进时间 | 单人休眠、其他人继续；无变化或重复局面停止为僵局，保留轨迹 | 战斗专项 |
| 跨遭遇时间与状态承接 | 已按显式 WAIT 承接 HP/MP、未到期状态与原行动时间；正式世界时间换算未定 | 战斗专项／集成 |
| 咏唱、命中暴击、闪避即时行动 | 本轮仍未实现，不由现有瞬时技能或队列推导正式规则 | 战斗专项 |
| 成交、付款与交货的先后 | 首场在现场校验余额与库存后钱货同时交付；无预付款和在途退出 | 基地专项 |
| 招募金额公式、生产与移动耗时 | 使用交接单样例和 JSON 测试值 | 基地专项 |
| 施工收费、福利/税/阶级的日结顺序、需求优先级及离开 | 本轮不进入这些流程；预置厨房、零税福利与外部现金流 | 基地专项 |
| 战斗 WAIT 与基地 tick、挂机与离线时间换算 | 两个会话独立，不作隐式换算 | 总策划与专项 |

进一步的输入约束与逐项口径见 `Assets/Prototype/Combat/ASSUMPTIONS.md` 和 `Assets/Prototype/BaseEconomy/ASSUMPTIONS.md`。
