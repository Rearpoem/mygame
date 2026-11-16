# 第三轮战斗指令 API 交接

接口约定和核心实现已落地，全部 Combat Runtime 通过 Add-Type 编译；编辑、错误原子性、深复制、施放者MP与三项括号烟测通过。正式 Unity 与本轮样例验收由测试／样例 Agent 统一记录。工程可以按下列字段和签名接 GUI。全部是 Combat 模块内部接口，公共存档、跨系统接口、界面及整包仍由工程统筹。未改变二轮跨场、奖励或装备语义。

## 编辑入口

```csharp
CombatEditResult InsertInstruction(string unitId, int index, CombatInstruction instruction);
CombatEditResult ReplaceInstruction(string unitId, int index, CombatInstruction instruction);
CombatEditResult RemoveInstruction(string unitId, int index);
CombatEditResult MoveInstruction(string unitId, int fromIndex, int toIndex);
CombatActionConfig[] GetAvailableActions(string unitId);
```

以上属于 `CombatSession` 实例。索引从0开始；Insert允许0到当前长度，其他索引必须落在已有列表内。成功编辑立即影响后续指令选择，也成为Reset的重放基线，不治疗、不重置时间或消费资源。传入和返回的DTO深复制。

`CombatEditResult` 保留success/message，新增稳定 `code`、`instructionCapacity`、`conditionTermCapacity`；GUI显示message，逻辑使用code。code：Ok、UnknownUnit、InvalidIndex、InstructionCapacityExceeded、ConditionCapacityExceeded、InvalidInstruction、DuplicateInstructionId、InvalidCondition、UnknownScope、UnknownSelector、ActionUnavailable、UnsupportedAction。错误不改变配置、快照、时间、HP/MP、历史或休眠状态。

旧JSON允许保有超容量列表，超额项仍显示禁用并被运行时跳过。Insert不能新增到容量之外；Replace只能替换有效槽位并满足条件容量；Move保持既有列表长度，可把原列表中的规则调入有效槽位；Remove总能删除合法索引处的规则，包括修正旧超载。

## 技能可用范围

`CombatUnitConfig.availableActionIds`为角色技能白名单，`availableActionIdsExplicit`为白名单存在标志，解决Unity JsonUtility将旧null数组写成[]的兼容差异：

| 输入名单 | availableActionIdsExplicit | 含义 |
| --- | --- | --- |
| null或[] | false／旧JSON缺省 | 从该角色原指令引用的已知、已实现行动推导 |
| 非空名单 | true或false | 使用该显式名单 |
| null或[] | true | 明确没有可配技能；null规范为空数组 |

会话复制中全部角色都会规范为具体名单与`availableActionIdsExplicit=true`，`GetScenario()`可直接导出，`CopyScenario()`保留标志。推导只使用该角色原指令，不开放全场actions池，因此不会泄漏敌方专有技能；删除指令不删除技能目录。新技能必须同时存在于场景actions与角色白名单中。工程投影友方战术时须同时复制名单和标志，特别是明确空名单不可只复制[]。旧raw DTO的JsonUtility往返仍按旧格式推导，原回归保留。

`GetAvailableActions(unitId)`返回上述真实可配置的`CombatActionConfig[]`副本，保留id/displayName/费用/WAIT/效果字段；不因当前MP不足隐藏技能。未知角色返回空数组。插入不可用技能返回ActionUnavailable。

技能ID属于场景数据。第三轮新配置使用`tactics_`前缀，例如`tactics_first_aid`、`tactics_basic_attack`、`tactics_power_attack`、`tactics_focus_mark`，避免与第二轮同名不同参数混用。目录与编辑接口不硬编码这些ID；工程投影配置时保留旧定义，冲突须明确拒绝。

## 条件和明确括号

兼容原来的`operation=all/any`与`terms[]`。新增字段：

```csharp
CombatCondition { operation, terms, operators /* string[] */, grouping /* string */ }
CombatConditionTerm { kind, threshold, subject /* string */, value /* string */ }
```

`subject`缺省或target读取当前候选，actor显式读取施放者；都占一个智力条件项。普通HP/MP/状态/站位不会暗中改读施放者。value用于状态ID。

三项混用时operation=mixed，operators恰好两个，取值and/or；grouping必须为left或right：left=`(A op0 B) op1 C`，right=`A op0 (B op1 C)`。一条最多三项并受角色智力限制，逐候选求整个表达式，不跨不同队员凑真；不采用隐藏运算优先级。

## GUI目录

`CombatTacticsCatalog.GetConditions/GetScopes/GetSelectors/GetOperations/GetGroupings/GetSubjects()`各返回`CombatTacticsOption[]`深独立DTO。条目有id、label、description、usesThreshold、minThreshold、maxThreshold、defaultThreshold、integerThreshold、usesValue、valueHint。技能目录按角色调用session.GetAvailableActions(unitId)，不取全场动作池。

条件kind：always、hp_ratio_at_most、hp_ratio_at_least、mp_ratio_at_most、mp_ratio_at_least、vulnerable、has_status、lacks_status、row_is、row_at_most、row_at_least。比例0到1；row整数0/1/2对应前/中/后；has_status/lacks_status的value为状态ID。

scope：self/allies/enemies。selector：configured_order、lowest_hp_ratio、highest_hp_ratio、lowest_hp、highest_hp、lowest_mp_ratio、highest_mp_ratio、front_first、back_first、vulnerable_first。后两种排位优先与易伤优先为软偏好，再按HP比例最低及配置顺序平手。最大MP=0时MP比例按0处理。

角色快照仍提供instructionCapacity/conditionTermCapacity。每条指令快照新增actionAvailable和validationReason，分别给出技能是否真实可配、规则当前不能执行的配置解释；capacityAllowed继续单独表示智力槽位与条件项容量。nextActionTime表示最近事件时刻，若状态到期先于或等于行动，nextActorId为空，先处理StateExpired；不可把空nextActorId当作已终局，终局看finished/outcome/stopReason。

预留MP示例：攻击规则使用`kind=mp_ratio_at_least,subject=actor,threshold=0.75`，根据施放者MP决定是否继续耗费MP攻击；急救仍可配置候选HP条件。标记集火示例：apply_status施加kind=vulnerable/id=focus_mark，队友用vulnerable_first优先攻击该对象。沿用二轮真实apply_status，不引入未经实现的效果类型。

## 第三轮试玩假设

新增目录选法、旧配置技能推导、actor条件、0MP比例与括号字段都是本轮可替换实现口径。无新增随机；同输入同日志；每次主动机会只提交一条行动。咏唱、暴击／闪避、后撤和完整阵型仍不在本轮范围，不能把目录扩展登记成这些系统已经完成。
