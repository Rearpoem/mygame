# 第四轮法师技能与条件扩展 API

本轮合同与核心实现已落地，全部Runtime通过Add-Type编译。核心烟测已验证冥想真实回MP及满MP回退、群体一次扣费与魔法屏障、汲魔封顶与溢出销毁、定身到期、自加速的后续WAIT、净化有害与保留增益，旧default首步与WAIT200胜利保持。正式Unity和全库样例由测试／样例Agent统一记录。旧编辑API、四段指令、1/2/3项智力容量、target缺省／actor显式、混合括号和availableActionIdsExplicit语义全部保留。只扩展Combat内部DTO、求值和瞬发效果；不实现AP/PP、随机命中、咏唱、DoT、复活或所有职业。下述新增公式为第四轮试玩假设，参考《圣兽之王》的条件类别与技能用途，不复制其资源或参数。

## 仍然使用的入口

`CombatSession.InsertInstruction/ReplaceInstruction/RemoveInstruction/MoveInstruction/GetAvailableActions`及`CombatTacticsCatalog.GetConditions/GetScopes/GetSelectors/GetOperations/GetGroupings/GetSubjects`签名不变。新技能必须出现在角色真实白名单；删除指令不忘记技能。原始旧DTO与规范化DTO的Unity往返仍保留presence标志处理。

工程投影动作定义时使用权威静态方法`CombatSession.NormalizeActionDefinition(CombatActionConfig)`及`CombatSession.ActionDefinitionsEquivalent(a,b)`。前者返回独立规范副本（null返回null），只将可选字符串null变空、targetMode空变single、damageType空变legacy；不修改Clone/GetScenario/输入对象。后者逐字段比较全部定义，包括名称、伤害、MP、WAIT、范围、isMagic与所有状态参数；physical与legacy仍不同。用于避免Unity往返引起假冲突，不能替代合法性验证，也不能吞掉同ID不同数值的真实冲突。

## Action新增字段与合法覆盖

`CombatActionConfig`新增：

| 字段 | 值与兼容语义 |
| --- | --- |
| targetMode | 缺省／空／single为原单体；row为同排；column为同列；all为同队全体 |
| isMagic | 缺省false；true时该动作被silenced阻止，与伤害类型分开 |
| damageType | 缺省／空／legacy保留旧伤害；physical／magic为显式类型；magic额外受magic_guard减伤 |

仍用单一effectKind与原magnitude/mpCost/baseWait/status字段，不引入另一套技能解释器。

先在scope内逐候选判断完整条件并按selector选锚点；再按targetMode扩展到锚点同team、仍在原scope、符合该效果合法性的存活成员，按场景顺序结算。范围成员不重复检查锚点条件。例如按HP最低者选择一排后行疗，可同时治疗同排其他伤员。self范围不会因为all模式变成全队。不存在合格覆盖者时整条跳过，无消耗和WAIT。

群体只扣一次施放者MP、只预约一次WAIT。`step.targetId`仍为锚点，新增`targetIds[]`为实际主覆盖者；候选trace新增`affectedTargetIds[]`，明确范围而非暗中换对象。汲魔的施放者返还属于额外效果记录，不放入主覆盖名单。

锚点本身可以没有收益但同范围其他成员有收益，因此targetId可能不在targetIds；例如满血锚点所在排仍有伤员。GUI应按真实targetIds/effects显示覆盖，不根据锚点推断所有受影响者。

## 真实效果

| effectKind | 结算 |
| --- | --- |
| damage | 保留原伤害＋装备＋empowered，新增减去施放者weakened；guard通用减伤，magic_guard只对magic追加减伤；伤害最低0 |
| heal | 按magnitude恢复HP并封顶；满HP不是有效覆盖者 |
| restore_mp | 按magnitude恢复目标MP并封顶；maxMp=0或满MP不是有效覆盖者；适用于冥想、传蓝、群体回蓝 |
| drain_mp | 不可自吸；扣目标min(magnitude,当前MP)，向施放者返还实际扣除量并封顶最大MP，溢出销毁；目标MP=0无效，施放者满MP仍可削减目标资源 |
| apply_status | 使用原statusId/statusKind/statusMagnitude/statusDuration；同ID替换刷新，新增状态类型见下文 |
| cleanse | 清除所有有害动态状态，并明确清除旧baseVulnerable永久测试标记；无可清除内容则无效 |
| dispel | 清除所有有益动态状态；不清除baseVulnerable；无可清除内容则无效 |

MP费用仍为显式整数，与比例条件分开。合法覆盖在扣费之前计算；例如群回蓝不会因先扣自己费用而额外把原满MP施放者加入覆盖。对敌方治疗、向友方施加伤害或负面状态继续受普通scope与白名单控制，不按阵营强行改效果。

## 状态与控制

益类：guard、empowered、magic_guard、haste。害类：vulnerable、weakened、slow、silenced、stunned。has_control识别slow／silenced／stunned。

- magic_guard：只对magic伤害增加直接减伤；旧guard仍减全部本轮伤害，旧装备增伤也继续作用于damage。
- weakened：直接减少输出伤害，不减少治疗或回MP。
- haste／slow：magnitude为非负速度点，后续动作WAIT倍率为`(100+slow总量)/(100+haste总量)`，再乘原agilityFactor。例haste25→0.8，slow25→1.25；同量抵消。只影响之后动作，不追溯已有预约，状态期限不缩放。
- silenced：仅阻止isMagic动作，指令失败后可回退非魔法法杖或非魔法冥想。
- stunned：不能执行主动动作，记录Skipped/controlReason=Stunned并休眠；到期／净化等状态变化可唤醒，保留尚余正常WAIT。

所有状态仍为已施加且固定全局WAIT到期，不实现未来激活或持续伤害tick。初始appliedAt必须0，跨场不得晚于源快照time；场间只按显式WAIT过滤已过期状态。

本次动作的WAIT在扣费和施加效果前采样完整系数。给自己加速或给自己净化减速，均不会改变它自己这次动作的WAIT，只会影响之后动作；该采样时点也是本轮明确假设。

## 效果与事件记录

事件种类仍为Executed／Skipped／StateExpired／Finished。`CombatEffect`新增mpBefore/mpAfter/mpDelta、statusId/statusAppliedAt/statusExpiresAt、removedStatusIds[]、baseVulnerableBefore/baseVulnerableAfter。所有效果（包括MP与状态）都填写真实HP前后，不能让默认0被旧报告误判成死亡。step.mpBefore/mpAfter仍是施放者整次行动前后MP；汲魔另有目标扣除和施放者返还两类效果。

`step.agilityFactor`为此次完整有效系数，新增baseAgilityFactor/statusWaitFactor可分解；`controlReason`提供Stunned等控制原因。指令attempt新增reasonCode，沉默阻止为Silenced。角色快照新增tags[]与effectiveAgilityFactor。

## 条件目录：37项

复用`CombatConditionTerm(kind,threshold,subject,value)`。subject缺省target读候选，actor显式读施放者，每项照常占一个条件槽。

- 原11项：always、hp_ratio_at_most、hp_ratio_at_least、mp_ratio_at_most、mp_ratio_at_least、vulnerable、has_status、lacks_status、row_is、row_at_most、row_at_least。
- 资源绝对值：hp_at_most、hp_at_least、mp_at_most、mp_at_least；缺口：hp_missing_at_least、mp_missing_at_least。threshold为非负整数。
- 状态类别：has_buff、has_debuff、has_control、has_status_kind、lacks_status_kind；后两者value为上述真实状态kind，目录提供合法defaultValue。
- 标签：has_tag、lacks_tag，value为角色tags中的字符串；旧角色无tags等于空集合。
- 数量：team_alive_at_least、team_alive_at_most、opponents_alive_at_least、opponents_alive_at_most、team_injured_at_least、row_alive_at_least、row_injured_at_least。相对所读subject所属队伍／当前排计数，含自身存活者；opponents指其他team。要读取施放者敌人数，显式subject=actor。threshold为非负整数。
- 时间：status_remaining_at_most、status_remaining_at_least（value为状态ID；状态不存在时两者均false），wait_remaining_at_most、wait_remaining_at_least（读取max(0,readyAt-now)，不代表该人必可行动）。threshold为非负WAIT。
- 关系：is_actor、is_not_actor，判断所读subject是否为施放者，可用于排除给自己传蓝。

目录`CombatTacticsOption`新增category/defaultValue/thresholdUnit，原字段不变；value型目录给合法默认值。thresholdUnit明确为ratio／points／count／wait／row，只有ratio按百分比显示，不能根据integerThreshold=false猜百分比；整数要求仍单独读取integerThreshold。新增`GetStatusKinds/GetTargetModes/GetDamageTypes/GetEffectKinds`供能力说明使用，均返回独立DTO。

## 选择器：24项

保留原10项。新增lowest_mp／highest_mp、most_hp_missing／most_mp_missing、buffed_first／debuffed_first／controlled_first／unprotected_first、highest_intelligence／lowest_intelligence、soonest_action／latest_action、fastest_speed／slowest_speed。

资源按实际值或缺口排序。状态类是软偏好，无偏好目标时仍可选其他合法候选；unprotected指无guard与magic_guard。状态、排位优先之后按HP比例最低，再按配置顺序。soonest/latest读取剩余预约WAIT，fastest/slowest按当前完整有效敏捷系数（越小越快）排序，同值按配置顺序。

## 法师库与验证范围

样例由配置Agent使用mage_动作ID提供约24项或以上：单／排／列／全魔攻、单／群治疗、冥想与快速吐纳、传蓝与汲魔、单／群净化与驱散、标记、防护、魔法屏障、增伤、削弱、加速、减速、沉默与定身。数量不替代实跑，每个目录项必须有真实效果；所有法术当前称瞬发试玩，不宣传咏唱完成。保留旧三轮配置、白名单presence、跨场与装备结算及111项回归，正式Unity由测试Agent在独立副本统一执行。
