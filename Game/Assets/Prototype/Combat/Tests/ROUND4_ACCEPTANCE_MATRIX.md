# 第四轮法师与指令扩展验收矩阵

本文件先固定可观察的验收问题。具体新增字段、技能与状态以本轮核心交接为准，测试参数不作为正式数值。目录项数量不单独构成通过条件；每类能力至少有真正执行、回退或选择不同对象的证据。共享上下文不在本任务中修改。

| 类别 | 实际验证内容 | 核心证据 |
| --- | --- | --- |
| 新条件 | 构造能区分候选的 HP／MP、状态、兵种／标签、站位、范围人数或战局输入；逐候选求完整表达式 | 选择对象／行动确实变化；失败条件回退；actor／target不串读 |
| 新选择方式 | 给出至少两个合法但排序不同的候选；软优先不突破硬过滤、范围与存活限制 | 实际 targetId、逐候选轨迹、效果对象 |
| 冥想 | 低 MP 恢复；上限夹取；满 MP／没有 MP 容量无效果时跳过；不扣未执行行动的资源 | mpBefore／mpAfter、effects、actualWait与下次行动时刻 |
| 资源循环 | 低 MP 冥想后能施法；资源与效果没有进展的循环有明确停止原因 | 同一Step／自动轨迹；有限停止或预算停止，不伪装胜利 |
| 群体技能 | 条件只筛锚点，再按锚点同team、原scope、存活／效果资格扩展；single／row／column／all，一次行动只扣一次费用 | effects覆盖对象集合、每人前后值、总费用和WAIT |
| 状态与控制 | 本轮实际支持的施加、刷新、净化／驱散、控制、到期与伤害类型 | 真实时序及受影响对象，不通过目录描述假设已实现 |
| 连续战 | 实际HP／MP、有效状态与固定WAIT承接；若实现咏唱则增加准备／释放与跨场口径 | 第二场开局来自第一场结局；场间WAIT显式，无自动恢复 |
| 编辑与容量 | 合法编辑影响选择；非法编辑原子；白名单／presence、1／2／3条件与混合括号保持 | 原三轮111回归及新增真实编辑用例 |
| 可重放性 | 相同输入、配置和种子；单步／自动与独立报告生成 | 实际快照／日志一致；独立报告逐文件或语义核对 |
| Unity兼容 | 同版本独立工程复制实际代码、测试、配置与meta，避免占用原Game | 源／副本SHA清单、真实NUnit XML、准确范围与失败记录 |

## GUI兼容观察项

- 条件、选法与角色技能菜单继续从真实目录和白名单取得，保留 `availableActionIdsExplicit`。
- 新条件如要求状态／标签／枚举值，目录需要给出合法输入提示或默认值，GUI不能只显示无含义的字符串框。
- `thresholdUnit=ratio`才按百分比显示，points／count／wait／row按各自单位；integerThreshold只说明整数约束。采用defaultValue时旧缺省保持兼容。
- 群体行动可能有多条 effects；恢复MP、净化和控制不能只显示成HP变化。查看空nextActorId也不能代替终局字段。
- 锚点自身可能不在targetIds中；高亮与效果显示应取真实覆盖名单。isMagic与damageType分开显示／投影，缺省single／legacy等价比较调用权威API。
- 工程投影新技能时应复制新增效果／范围／伤害类型等字段并检查同ID定义冲突；不把实验角色状态覆盖到真实出征。
- 公共接口、UI、场景、PlayMode与整包构建由工程会话验收；局部Combat测试通过不代表这些已通过。

## 实际验收结果

Unity 6000.3.28f1／Test Framework 1.6.0独立副本实跑：**242／242通过，0失败、0跳过、退出0**。91个实际源码／配置／测试／meta／版本文件有复制时源与副本SHA清单，其中42个非meta运行内容在执行前后一致；meta允许Unity导入规范化。原111项行为回归全部通过；新增131项如下。

| 新增fixture | 实际用例数 | 真实结算证据 |
| --- | ---: | --- |
| CombatConditionExpansionTests | 57 | 全部26种新增条件逐对象过滤、14种新选法选出不同对象、actor计数、固定时间差、软优先与非法编辑原子性 |
| CombatManaExpansionTests | 12 | 冥想真实MP／上限／空跳过／WAIT、冥想后施法、无进展循环停止、汲魔实际扣除／返还／溢出／自吸非法 |
| CombatAreaExpansionTests | 10 | 四种范围、锚点条件不重筛覆盖者、同team／scope／活着／收益过滤、一次费用、锚点可不在真实覆盖、复制与重放 |
| CombatStatusExpansionTests | 28 | 状态清除／刷新／到期、真实HPMP与移除ID副本、沉默／定身、加减速／固定期限／提交前采样、物魔伤害／装备及跨场 |
| CombatActionDefinitionTests | 15 | Unity原始动作往返与可选缺省等价；13类真实参数差异保持冲突，规范副本不改源 |
| CombatMageSamplesTests | 9 | 五组实际JSON单变量、单步／自动同态、技能库与27技能逐项执行、新两场及报告只读、跨轮动作定义一致 |

唯一旧源码适配：CombatTacticsTests.CatalogEntries从固定test_mark改读目录defaultValue，使新增状态kind有合法枚举输入；保留原有编辑／求值／副本断言。其余五份旧测试与第三轮SHA一致，不能将本轮表述为“旧111源码全不变”。

首次真实运行242／241／1，失败为新增is_not_actor测试把另一队友b当反例；按合同应排除actor，原选中a／真与新增actor／假两侧断言保留。只改新增测试一行，没有改Runtime／配置。首次XML／日志／清单均保留。

最终Runtime独立复跑第四轮报告生成器，27技能／5组对照／两场承接／旧强化等通过；17／17交付产物逐文件SHA一致。[真实XML](Results/ROUND4_COMBAT_EDITMODE.xml)、[验证摘要与复跑入口](Results/ROUND4_UNITY_VERIFICATION.json)、[最终源清单](Results/ROUND4_SOURCE_MANIFEST.json)、[独立报告SHA](Results/ROUND4_REPORT_REPLAY_HASHES.json)、[首次失败XML](Results/ROUND4_INITIAL_FAILED_EDITMODE.xml)。验证范围仅Combat模块，不代替工程GUI／PlayMode／全工程测试／整包构建。
