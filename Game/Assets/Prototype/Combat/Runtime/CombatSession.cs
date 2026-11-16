using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Prototype.Combat
{
    /// <summary>
    /// Deterministic combat rules. Single-step, automatic play and expeditions use this same session.
    /// All temporary numerical/targeting assumptions are documented beside the module.
    /// </summary>
    public sealed class CombatSession
    {
        private sealed class UnitState
        {
            public CombatUnitConfig config;
            public int hp;
            public int mp;
            public double readyAt;
            public bool dormant;
            public bool baseVulnerable;
            public readonly List<CombatStatusConfig> statuses = new List<CombatStatusConfig>();
        }

        private readonly CombatScenario scenario;
        private readonly List<UnitState> units = new List<UnitState>();
        private readonly List<CombatStepResult> history = new List<CombatStepResult>();
        private double time;
        private int completedSteps;
        private bool finished;
        private string outcome = string.Empty, winnerTeam = string.Empty, stopReason = string.Empty;
        private double battleStartTime, waitBudget;
        private int eventBudget;
        private readonly HashSet<string> seenStates = new HashSet<string>(StringComparer.Ordinal);
        private CombatSnapshot baselineCarry;
        private string baselineCarryTeam;
        private double baselineGap;

        public CombatSession(CombatScenario config)
        {
            ValidateScenario(config);
            scenario = CloneScenario(config);
            NormalizeLegacyActionAvailability();
            Reset();
        }

        public CombatScenario GetScenario()
        {
            return CloneScenario(scenario);
        }

        public CombatSession(CombatScenario config, CombatSnapshot previous, string carryTeam, double betweenWait) : this(config)
        {
            ValidateCarry(previous, carryTeam, betweenWait);
            baselineCarry = CopySnapshot(previous);
            baselineCarryTeam = carryTeam;
            baselineGap = betweenWait;
            Reset();
        }

        /// <summary>Keep edited configuration; restore initial HP/MP/positions/times and clear history.</summary>
        public void Reset()
        {
            units.Clear();
            foreach (CombatUnitConfig unit in scenario.units)
            {
                var state = new UnitState { config = unit, hp = unit.hp, mp = unit.mp, readyAt = unit.initialReadyTime,
                    baseVulnerable = unit.vulnerable };
                units.Add(state);
            }
            history.Clear();
            time = battleStartTime = baselineCarry == null ? 0 : baselineCarry.time + baselineGap;
            foreach (UnitState state in units)
            {
                state.readyAt += time;
                if (state.config.statuses != null) foreach (CombatStatusConfig status in state.config.statuses)
                {
                    var copy = CopyStatus(status); copy.appliedAt += time; copy.expiresAt += time;
                    if (copy.expiresAt > time) state.statuses.Add(copy);
                }
                if (baselineCarry != null && state.config.team == baselineCarryTeam)
                {
                    CombatUnitSnapshot carried = FindSnapshot(baselineCarry, state.config.id);
                    state.hp = carried.hp; state.mp = carried.mp; state.readyAt = Math.Max(time, carried.nextActionTime);
                    state.baseVulnerable = carried.baseVulnerable;
                    state.statuses.Clear();
                    if (carried.statuses != null) foreach (CombatStatusConfig status in carried.statuses)
                        if (status.expiresAt > time) state.statuses.Add(CopyStatus(status));
                }
            }
            completedSteps = 0;
            ClearBlock();
            finished = false; outcome = winnerTeam = stopReason = string.Empty;
            eventBudget = scenario.maxEvents > 0 ? scenario.maxEvents : 10000;
            waitBudget = scenario.maxWait > 0 ? scenario.maxWait : 100000;
            seenStates.Clear(); seenStates.Add(StateKey()); CheckVictory();
        }

        public CombatEditResult MoveInstruction(string unitId, int fromIndex, int toIndex)
        {
            CombatUnitConfig unit = FindConfig(unitId);
            if (unit == null) return EditFor(null, false, "找不到角色：" + unitId, "UnknownUnit");
            if (fromIndex < 0 || toIndex < 0 || fromIndex >= unit.instructions.Length || toIndex >= unit.instructions.Length)
                return EditFor(unit, false, "指令序号超出范围（序号从 0 开始）。", "InvalidIndex");
            CombatInstruction moved = unit.instructions[fromIndex];
            if (fromIndex < toIndex)
                Array.Copy(unit.instructions, fromIndex + 1, unit.instructions, fromIndex, toIndex - fromIndex);
            else if (fromIndex > toIndex)
                Array.Copy(unit.instructions, toIndex, unit.instructions, toIndex + 1, fromIndex - toIndex);
            unit.instructions[toIndex] = moved;
            ClearBlock();
            return EditFor(unit, true, "已调整优先级；重置可从同一开局重放。", "Ok");
        }

        public CombatEditResult InsertInstruction(string unitId, int index, CombatInstruction instruction)
        {
            CombatUnitConfig unit = FindConfig(unitId);
            if (unit == null) return EditFor(null, false, "找不到角色：" + unitId, "UnknownUnit");
            if (index < 0 || index > unit.instructions.Length) return EditFor(unit, false, "插入序号超出范围。", "InvalidIndex");
            if (unit.instructions.Length >= InstructionCapacity(unit.intelligence))
                return EditFor(unit, false, "智力提供的指令容量已满；请替换或删除现有规则。", "InstructionCapacityExceeded");
            CombatEditResult error = ValidateInstructionEdit(unit, instruction, -1);
            if (error != null) return error;
            var rules = new CombatInstruction[unit.instructions.Length + 1];
            Array.Copy(unit.instructions, 0, rules, 0, index);
            rules[index] = CopyInstruction(instruction);
            Array.Copy(unit.instructions, index, rules, index + 1, unit.instructions.Length - index);
            unit.instructions = rules; ClearBlock();
            return EditFor(unit, true, "已插入指令；后续选择与重置重放使用此配置。", "Ok");
        }

        public CombatEditResult ReplaceInstruction(string unitId, int index, CombatInstruction instruction)
        {
            CombatUnitConfig unit = FindConfig(unitId);
            if (unit == null) return EditFor(null, false, "找不到角色：" + unitId, "UnknownUnit");
            if (index < 0 || index >= unit.instructions.Length) return EditFor(unit, false, "替换序号超出范围。", "InvalidIndex");
            if (index >= InstructionCapacity(unit.intelligence)) return EditFor(unit, false, "该位置超出智力指令容量，请先调入有效槽位或删除。", "InstructionCapacityExceeded");
            CombatEditResult error = ValidateInstructionEdit(unit, instruction, index);
            if (error != null) return error;
            unit.instructions[index] = CopyInstruction(instruction); ClearBlock();
            return EditFor(unit, true, "已替换指令；后续选择与重置重放使用此配置。", "Ok");
        }

        public CombatEditResult RemoveInstruction(string unitId, int index)
        {
            CombatUnitConfig unit = FindConfig(unitId);
            if (unit == null) return EditFor(null, false, "找不到角色：" + unitId, "UnknownUnit");
            if (index < 0 || index >= unit.instructions.Length) return EditFor(unit, false, "删除序号超出范围。", "InvalidIndex");
            var rules = new CombatInstruction[unit.instructions.Length - 1];
            Array.Copy(unit.instructions, 0, rules, 0, index);
            Array.Copy(unit.instructions, index + 1, rules, index, unit.instructions.Length - index - 1);
            unit.instructions = rules; ClearBlock();
            return EditFor(unit, true, "已删除指令；角色已学技能目录保持不变。", "Ok");
        }

        public CombatActionConfig[] GetAvailableActions(string unitId)
        {
            CombatUnitConfig unit = FindConfig(unitId);
            var actions = new List<CombatActionConfig>();
            if (unit != null) foreach (CombatActionConfig action in scenario.actions)
                if (IsActionAvailable(unit, action.id) && SupportedEffect(action.effectKind)) actions.Add(CopyAction(action));
            return actions.ToArray();
        }

        public CombatEditResult SetConditionThreshold(string unitId, int instructionIndex, int termIndex, double value)
        {
            CombatUnitConfig unit = FindConfig(unitId);
            if (unit == null) return Edit(false, "找不到角色：" + unitId);
            if (instructionIndex < 0 || instructionIndex >= unit.instructions.Length)
                return Edit(false, "指令序号超出范围。");
            CombatInstruction instruction = unit.instructions[instructionIndex];
            CombatCondition condition = instruction == null ? null : instruction.condition;
            if (condition == null || condition.terms == null || termIndex < 0 || termIndex >= condition.terms.Length)
                return Edit(false, "条件项序号超出范围。");
            CombatConditionTerm term = condition.terms[termIndex];
            if (term == null || !IsRatioCondition(term.kind)) return Edit(false, "本条件没有可编辑的 HP/MP 比例阈值。");
            if (!Finite(value) || value < 0 || value > 1) return Edit(false, "HP/MP 比例阈值必须在 0 到 1 之间。");
            term.threshold = value;
            ClearBlock();
            return Edit(true, "已修改阈值；重置可从同一开局重放。");
        }

        /// <summary>Change the reset baseline only. Call Reset to apply it to the current encounter.</summary>
        public CombatEditResult SetInitialHp(string unitId, int hp)
        {
            CombatUnitConfig unit = FindConfig(unitId);
            if (unit == null) return Edit(false, "找不到角色：" + unitId);
            if (hp < 0 || hp > unit.maxHp) return Edit(false, "开局 HP 必须在 0 与最大 HP 之间。");
            unit.hp = hp;
            return Edit(true, "已修改开局 HP；请重置应用。");
        }

        public CombatSnapshot GetSnapshot()
        {
            UnitState next;
            bool tied;
            FindNextActor(out next, out tied);
            var snapshots = new CombatUnitSnapshot[units.Count];
            for (int i = 0; i < units.Count; i++)
            {
                UnitState state = units[i];
                CombatUnitConfig unit = state.config;
                int capacity = InstructionCapacity(unit.intelligence);
                int conditionCapacity = ConditionCapacity(unit.intelligence);
                var instructions = new CombatInstructionSnapshot[unit.instructions.Length];
                for (int j = 0; j < instructions.Length; j++)
                {
                    CombatInstruction rule = unit.instructions[j];
                    CombatEditResult issue = ValidateInstructionEdit(unit, rule, j);
                    CombatActionConfig configuredAction = rule == null ? null : FindAction(rule.actionId);
                    instructions[j] = new CombatInstructionSnapshot
                    {
                        id = rule == null ? string.Empty : rule.id,
                        display = Display(rule),
                        capacityAllowed = j < capacity && rule != null && rule.condition != null &&
                            rule.condition.terms != null && rule.condition.terms.Length > 0 && rule.condition.terms.Length <= conditionCapacity,
                        actionAvailable = configuredAction != null && SupportedEffect(configuredAction.effectKind) && IsActionAvailable(unit, configuredAction.id),
                        validationReason = j >= capacity ? "超出智力提供的指令容量。" : issue == null ? string.Empty : issue.message
                    };
                }
                snapshots[i] = new CombatUnitSnapshot
                {
                    id = unit.id, name = unit.name, team = unit.team, row = unit.row, column = unit.column,
                    hp = state.hp, maxHp = unit.maxHp, mp = state.mp, maxMp = unit.maxMp,
                    intelligence = unit.intelligence, vulnerable = state.baseVulnerable || HasStatus(state, "vulnerable"),
                    baseVulnerable = state.baseVulnerable,
                    instructionCapacity = capacity, conditionTermCapacity = conditionCapacity,
                    nextActionTime = state.readyAt, instructions = instructions,
                    statuses = CopyStatuses(state.statuses.ToArray()), dormant = state.dormant,
                    equipmentDamageBonus = unit.equipmentDamageBonus, equipmentSourceId = unit.equipmentSourceId,
                    tags = CopyStrings(unit.tags) ?? Array.Empty<string>(), effectiveAgilityFactor = EffectiveAgilityFactor(state)
                };
            }
            double nextTime = Math.Min(next == null ? double.MaxValue : next.readyAt, NextExpiry());
            bool expiryFirst = NextExpiry() <= (next == null ? double.MaxValue : next.readyAt);
            return new CombatSnapshot
            {
                scenarioId = scenario.id, parameterNotice = scenario.parameterNotice,
                seed = scenario.seed, randomUsed = false, time = time, units = snapshots,
                nextActorId = finished || next == null || expiryFirst ? string.Empty : next.config.id,
                nextActionTime = finished || nextTime == double.MaxValue ? time : nextTime, pendingTimeTie = !finished && !expiryFirst && tied,
                blocked = finished && (outcome == "Stalemate" || outcome == "Stopped"), blockReason = stopReason, completedStepCount = completedSteps,
                finished = finished, outcome = outcome, winnerTeam = winnerTeam, stopReason = stopReason, battleStartTime = battleStartTime
            };
        }

        public CombatStepResult[] GetHistory()
        {
            var copy = new CombatStepResult[history.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = CloneStep(history[i]);
            return copy;
        }

        public CombatRunResult RunToCompletion(int maxSteps = 10000, double maxWait = 100000)
        {
            if (maxSteps < 1 || !Finite(maxWait) || maxWait <= 0) throw new ArgumentException("运行预算必须为正且有限。");
            if (!finished)
            {
                eventBudget = Math.Min(eventBudget, maxSteps); waitBudget = Math.Min(waitBudget, maxWait);
                while (!finished) Step();
            }
            return new CombatRunResult { outcome = outcome, winnerTeam = winnerTeam, stopReason = stopReason,
                time = time, executedActionCount = completedSteps, snapshot = GetSnapshot(), steps = GetHistory() };
        }

        public CombatStepResult Step()
        {
            if (finished) return TerminalStep();
            if (history.Count >= eventBudget) { Finish("Stopped", "", "EventBudgetExceeded"); return TerminalStep(); }
            UnitState actor; bool tied; FindNextActor(out actor, out tied);
            double expiry = NextExpiry();
            double eventTime = Math.Min(actor == null ? double.MaxValue : actor.readyAt, expiry);
            if (eventTime == double.MaxValue) { Finish("Stalemate", "", "NoExecutableActionsOrFutureEvents"); return TerminalStep(); }
            if (eventTime - battleStartTime > waitBudget) { Finish("Stopped", "", "WaitBudgetExceeded"); return TerminalStep(); }
            time = eventTime;
            if (expiry <= (actor == null ? double.MaxValue : actor.readyAt)) return ExpireStatuses();
            CombatStepResult result = NewStep("Skipped", actor);
            result.timeTieResolved = tied;
            result.tieBreakReason = tied ? "同刻按场景 units 数组顺序，先配置者先行动。" : "";
            if (HasStatus(actor, "stunned"))
            {
                actor.dormant = true; result.controlReason = "Stunned";
                result.message = "定身状态阻止主动行动；休眠至状态到期或净化，不扣费、不取消尚余正常WAIT。";
                return Record(result);
            }
            var attempts = new List<CombatInstructionAttempt>();
            for (int index = 0; index < actor.config.instructions.Length; index++)
            {
                CombatInstruction instruction = actor.config.instructions[index];
                var attempt = new CombatInstructionAttempt { instructionIndex = index,
                    instructionId = instruction == null ? "" : instruction.id, display = Display(instruction),
                    outcome = "Skipped", reason = "", reasonCode = "", candidates = Array.Empty<CombatCandidateTrace>() };
                attempts.Add(attempt);
                UnitState target; CombatActionConfig action;
                if (!TryPrepare(actor, instruction, index, attempt, out target, out action)) continue;
                double factor = EffectiveAgilityFactor(actor);
                double wait = action.baseWait * factor;
                double ready = time + wait;
                if (!Finite(wait) || wait <= 0 || !Finite(ready) || ready <= time)
                { attempt.reason = "WAIT 无法推进到有限后续时刻；本条无消耗与效果。"; continue; }
                if (action.effectKind == "apply_status" && (!Finite(time + action.statusDuration) || time + action.statusDuration <= time))
                { attempt.reason = "状态到期时刻无效；本条无消耗与效果。"; continue; }
                List<UnitState> affected = AffectedTargets(actor, target, instruction, action);
                if (affected.Count == 0) { attempt.reason = "无合法覆盖者；本条无消耗与效果。"; continue; }
                int actorMpBefore = actor.mp;
                actor.mp -= action.mpCost;
                bool changed = actor.mp != actorMpBefore;
                var effects = new List<CombatEffect>();
                foreach (UnitState member in affected) changed |= ApplyEffect(actor, member, action, effects);
                actor.readyAt = ready; completedSteps++;
                attempt.outcome = "Selected"; attempt.reasonCode = "Selected";
                attempt.reason = "同一候选整式成立；选锚点 " + target.config.name + "，实际覆盖[" + string.Join(",", UnitIds(affected)) + "]。";
                result.status = "Executed"; result.attempts = attempts.ToArray(); result.actionId = action.id;
                result.actionName = action.displayName; result.targetId = target.config.id; result.targetName = target.config.name;
                result.targetIds = UnitIds(affected);
                result.mpCost = action.mpCost; result.mpAfter = actor.mp; result.baseWait = action.baseWait;
                result.baseAgilityFactor = actor.config.agilityFactor;
                result.statusWaitFactor = factor / actor.config.agilityFactor;
                result.agilityFactor = factor; result.actualWait = wait; result.nextActionTime = ready;
                result.equipmentDamageBonus = action.effectKind == "damage" ? actor.config.equipmentDamageBonus : 0;
                result.effects = effects.ToArray();
                result.message = actor.config.name + " → " + action.displayName + " → " + target.config.name +
                    (TargetMode(action) == "single" ? "" : "；覆盖[" + string.Join(",", result.targetIds) + "]");
                if (changed) WakeDormant();
                else { actor.dormant = true; result.message += "；无状态／资源变化，休眠至其他事件。"; }
                return Record(result);
            }
            actor.dormant = true; result.attempts = attempts.ToArray();
            foreach (CombatInstructionAttempt attempt in attempts)
                if (attempt.reasonCode == "Silenced") result.controlReason = "Silenced";
            result.message = "全部指令均不可执行；无消耗，休眠至其他行动或状态到期改变局面，继续其他角色。";
            return Record(result);
        }

        private static string TargetMode(CombatActionConfig action)
        { return string.IsNullOrEmpty(action.targetMode) ? "single" : action.targetMode; }
        private static string DamageType(CombatActionConfig action)
        { return string.IsNullOrEmpty(action.damageType) ? "legacy" : action.damageType; }
        private static string[] UnitIds(List<UnitState> selected)
        { var ids = new string[selected.Count]; for (int i=0;i<ids.Length;i++) ids[i]=selected[i].config.id; return ids; }
        private List<UnitState> AffectedTargets(UnitState actor, UnitState anchor, CombatInstruction rule, CombatActionConfig action)
        {
            var affected = new List<UnitState>(); string mode = TargetMode(action);
            foreach (UnitState candidate in units)
            {
                if (candidate.hp <= 0 || candidate.config.team != anchor.config.team || !InScope(actor,candidate,rule.scope)) continue;
                if (mode=="single" && !ReferenceEquals(candidate,anchor)) continue;
                if (mode=="row" && candidate.config.row!=anchor.config.row) continue;
                if (mode=="column" && candidate.config.column!=anchor.config.column) continue;
                if (CanReceive(actor,candidate,action)) affected.Add(candidate);
            }
            return affected;
        }
        private static bool CanReceive(UnitState actor, UnitState target, CombatActionConfig action)
        {
            if (target.hp<=0) return false;
            switch(action.effectKind)
            {
                case "heal": return target.hp<target.config.maxHp && action.magnitude>0;
                case "restore_mp": return target.config.maxMp>0 && target.mp<target.config.maxMp && action.magnitude>0;
                case "drain_mp": return !ReferenceEquals(actor,target) && target.mp>0 && action.magnitude>0;
                case "cleanse": return HasDebuff(target);
                case "dispel": return HasBuff(target);
                default: return true;
            }
        }
        private static bool Beneficial(string kind)
        { return kind=="guard" || kind=="empowered" || kind=="magic_guard" || kind=="haste"; }
        private static bool Harmful(string kind)
        { return kind=="vulnerable" || kind=="weakened" || kind=="slow" || kind=="silenced" || kind=="stunned"; }
        private static bool HasBuff(UnitState unit)
        { foreach(CombatStatusConfig status in unit.statuses) if(Beneficial(status.kind)) return true; return false; }
        private static bool HasDebuff(UnitState unit)
        { if(unit.baseVulnerable) return true; foreach(CombatStatusConfig status in unit.statuses) if(Harmful(status.kind)) return true; return false; }
        private static bool HasControl(UnitState unit)
        { return HasStatus(unit,"slow") || HasStatus(unit,"silenced") || HasStatus(unit,"stunned"); }
        private static double EffectiveAgilityFactor(UnitState unit)
        { double factor=unit.config.agilityFactor*((100.0+StatusMagnitude(unit,"slow"))/(100.0+StatusMagnitude(unit,"haste")));
            return double.IsPositiveInfinity(factor)?double.MaxValue:factor<=0?double.Epsilon:factor; }
        private static CombatEffect BeginEffect(string kind, UnitState target)
        {
            return new CombatEffect { kind=kind,targetId=target.config.id,targetName=target.config.name,
                hpBefore=target.hp,hpAfter=target.hp,mpBefore=target.mp,mpAfter=target.mp,
                baseVulnerableBefore=target.baseVulnerable,baseVulnerableAfter=target.baseVulnerable,
                removedStatusIds=Array.Empty<string>(),statusId="",description="" };
        }
        private static void EndEffect(CombatEffect effect, UnitState target)
        {
            effect.hpAfter=target.hp; effect.hpDelta=target.hp-effect.hpBefore;
            effect.mpAfter=target.mp; effect.mpDelta=target.mp-effect.mpBefore;
            effect.baseVulnerableAfter=target.baseVulnerable;
        }
        private bool ApplyEffect(UnitState actor, UnitState target, CombatActionConfig action, List<CombatEffect> effects)
        {
            CombatEffect effect=BeginEffect(action.effectKind,target);
            bool changed=false;
            switch(action.effectKind)
            {
                case "heal":
                    target.hp=(int)Math.Min(target.config.maxHp,(long)target.hp+action.magnitude); break;
                case "damage":
                    long damage=(long)action.magnitude+actor.config.equipmentDamageBonus+StatusMagnitude(actor,"empowered")-
                        StatusMagnitude(actor,"weakened")-StatusMagnitude(target,"guard")-
                        (DamageType(action)=="magic"?StatusMagnitude(target,"magic_guard"):0);
                    target.hp=(int)Math.Max(0L,(long)target.hp-Math.Max(0L,damage)); break;
                case "restore_mp":
                    target.mp=(int)Math.Min(target.config.maxMp,(long)target.mp+action.magnitude); break;
                case "drain_mp":
                    int drained=Math.Min(action.magnitude,target.mp);
                    target.mp-=drained;
                    EndEffect(effect,target);
                    effect.description=target.config.name+" MP "+Integer(effect.mpBefore)+" → "+Integer(effect.mpAfter)+"（实际汲取"+Integer(drained)+"）";
                    effects.Add(effect);
                    CombatEffect refund=BeginEffect("restore_mp",actor);
                    int returned=Math.Min(drained,actor.config.maxMp-actor.mp); actor.mp+=returned;
                    EndEffect(refund,actor);
                    refund.description=actor.config.name+" 汲魔返还MP "+Integer(refund.mpBefore)+" → "+Integer(refund.mpAfter)+
                        "；溢出销毁 "+Integer(drained-returned);
                    effects.Add(refund);
                    return drained>0;
                case "apply_status":
                    CombatStatusConfig previous=FindStatus(target,action.statusId);
                    double until=time+action.statusDuration;
                    changed=previous==null || previous.kind!=action.statusKind || previous.magnitude!=action.statusMagnitude || previous.expiresAt!=until;
                    if(previous!=null) { target.statuses.Remove(previous); effect.removedStatusIds=new[]{previous.id}; }
                    target.statuses.Add(new CombatStatusConfig { id=action.statusId,kind=action.statusKind,magnitude=action.statusMagnitude,appliedAt=time,expiresAt=until });
                    effect.statusId=action.statusId; effect.statusAppliedAt=time; effect.statusExpiresAt=until;
                    effect.description=target.config.name+" 获得 "+action.statusId+"["+action.statusKind+"]，到期WAIT "+Number(until); break;
                case "cleanse": case "dispel":
                    var removed=new List<string>();
                    for(int i=target.statuses.Count-1;i>=0;i--)
                    {
                        CombatStatusConfig status=target.statuses[i];
                        if(action.effectKind=="cleanse"?Harmful(status.kind):Beneficial(status.kind))
                        { removed.Add(status.id); target.statuses.RemoveAt(i); }
                    }
                    if(action.effectKind=="cleanse" && target.baseVulnerable) { target.baseVulnerable=false; changed=true; }
                    effect.removedStatusIds=removed.ToArray(); changed|=removed.Count>0;
                    effect.description=target.config.name+" "+(action.effectKind=="cleanse"?"净化":"驱散")+"["+string.Join(",",effect.removedStatusIds)+"]"+
                        (effect.baseVulnerableBefore!=target.baseVulnerable?"；永久易伤标记清除":""); break;
            }
            EndEffect(effect,target);
            changed|=effect.hpDelta!=0 || effect.mpDelta!=0;
            if(action.effectKind=="heal" || action.effectKind=="damage")
                effect.description=target.config.name+" HP "+Integer(effect.hpBefore)+" → "+Integer(effect.hpAfter)+" ("+
                    (effect.hpDelta>=0?"+":"")+Integer(effect.hpDelta)+")";
            if(action.effectKind=="restore_mp")
                effect.description=target.config.name+" MP "+Integer(effect.mpBefore)+" → "+Integer(effect.mpAfter)+" ("+
                    (effect.mpDelta>=0?"+":"")+Integer(effect.mpDelta)+")";
            effects.Add(effect); return changed;
        }

        /// <summary>Stable, invariant-culture export. LF newlines, no wall clock or platform-dependent data.</summary>
        public string ExportLogText()
        {
            var log = new StringBuilder();
            log.Append("COMBAT_LOG_V1\nscenario=").Append(scenario.id).Append('\n');
            log.Append("notice=").Append(scenario.parameterNotice).Append('\n');
            log.Append("seed=").Append(scenario.seed.ToString(CultureInfo.InvariantCulture)).Append("; randomUsed=false\n");
            foreach (CombatStepResult step in history)
            {
                log.Append('#').Append(Integer(step.index)).Append(" t=").Append(Number(step.time)).Append(' ')
                    .Append(step.status).Append(" actor=").Append(step.actorId).Append(" (").Append(step.actorName).Append(")\n");
                if (step.timeTieResolved) log.Append("  tie: ").Append(step.tieBreakReason).Append('\n');
                if (!string.IsNullOrEmpty(step.controlReason)) log.Append("  control=").Append(step.controlReason).Append('\n');
                foreach (CombatInstructionAttempt attempt in step.attempts)
                {
                    log.Append("  rule[").Append(Integer(attempt.instructionIndex)).Append("] ").Append(attempt.instructionId)
                        .Append(" | ").Append(attempt.display).Append(" | ").Append(attempt.outcome)
                        .Append(" | ").Append(attempt.reason).Append('\n');
                    foreach (CombatCandidateTrace candidate in attempt.candidates)
                    {
                        log.Append("    candidate=").Append(candidate.unitId).Append(" condition=")
                            .Append(candidate.passesCondition ? "true" : "false").Append(" eligible=")
                            .Append(candidate.eligible ? "true" : "false").Append(" | ").Append(candidate.reason).Append('\n');
                    }
                }
                if (step.status == "Executed")
                {
                    log.Append("  action=").Append(step.actionId).Append(" target=").Append(step.targetId)
                        .Append(" covers=[").Append(string.Join(",", step.targetIds ?? Array.Empty<string>())).Append(']')
                        .Append(" MP=").Append(Integer(step.mpBefore)).Append("->").Append(Integer(step.mpAfter))
                        .Append(" cost=").Append(Integer(step.mpCost)).Append(" equipmentBonus=").Append(Integer(step.equipmentDamageBonus)).Append('\n');
                    foreach (CombatEffect effect in step.effects) AppendEffectLog(log, effect);
                    log.Append("  WAIT=").Append(Number(step.baseWait)).Append('*').Append(Number(step.agilityFactor))
                        .Append('=').Append(Number(step.actualWait)).Append(" next=").Append(Number(step.nextActionTime)).Append('\n');
                }
                if (step.status == "StateExpired") foreach (CombatEffect effect in step.effects)
                    AppendEffectLog(log, effect);
                log.Append("  result: ").Append(step.message).Append('\n');
            }
            if (finished) log.Append("outcome=").Append(outcome).Append("; winner=").Append(winnerTeam)
                .Append("; stopReason=").Append(stopReason).Append("; time=").Append(Number(time)).Append('\n');
            return log.ToString();
        }

        private static void AppendEffectLog(StringBuilder log, CombatEffect effect)
        {
            log.Append("  effect: ").Append(effect.description).Append(" | target=").Append(effect.targetId)
                .Append(" HP=").Append(Integer(effect.hpBefore)).Append("->").Append(Integer(effect.hpAfter))
                .Append(" MP=").Append(Integer(effect.mpBefore)).Append("->").Append(Integer(effect.mpAfter));
            if (!string.IsNullOrEmpty(effect.statusId)) log.Append(" status=").Append(effect.statusId)
                .Append(" applied=").Append(Number(effect.statusAppliedAt)).Append(" expires=").Append(Number(effect.statusExpiresAt));
            if (effect.removedStatusIds != null && effect.removedStatusIds.Length > 0)
                log.Append(" removed=[").Append(string.Join(",", effect.removedStatusIds)).Append(']');
            log.Append('\n');
        }

        private CombatStepResult NewStep(string status, UnitState actor)
        {
            return new CombatStepResult { index = history.Count + 1, time = time, status = status,
                actorId = actor == null ? "" : actor.config.id, actorName = actor == null ? "" : actor.config.name,
                actionId = "", actionName = "", targetId = "", targetName = "", attempts = Array.Empty<CombatInstructionAttempt>(),
                effects = Array.Empty<CombatEffect>(), mpBefore = actor == null ? 0 : actor.mp, mpAfter = actor == null ? 0 : actor.mp,
                nextActionTime = actor == null ? time : actor.readyAt, message = "", outcome = outcome,
                winnerTeam = winnerTeam, stopReason = stopReason, tieBreakReason = "", targetIds = Array.Empty<string>(),
                controlReason = "", baseAgilityFactor = actor == null ? 0 : actor.config.agilityFactor, statusWaitFactor = 1 };
        }
        private CombatStepResult TerminalStep()
        {
            CombatStepResult result = NewStep("Finished", null);
            result.message = outcome + ": " + stopReason;
            return result;
        }
        private CombatStepResult Record(CombatStepResult result)
        {
            history.Add(result); CheckVictory();
            if (!finished)
            {
                UnitState next; bool tied; FindNextActor(out next, out tied);
                if (next == null && NextExpiry() == double.MaxValue) Finish("Stalemate", "", "NoExecutableActionsOrFutureEvents");
                else if (!seenStates.Add(StateKey())) Finish("Stalemate", "", "RepeatedDeterministicState");
            }
            result.outcome = outcome; result.winnerTeam = winnerTeam; result.stopReason = stopReason;
            return CloneStep(result);
        }
        private void CheckVictory()
        {
            var teams = new HashSet<string>(StringComparer.Ordinal);
            foreach (UnitState unit in units) if (unit.hp > 0) teams.Add(unit.config.team);
            if (teams.Count == 0) Finish("Draw", "", "AllTeamsDefeated");
            else if (teams.Count == 1)
            {
                string survivor = ""; foreach (string team in teams) survivor = team;
                string player = string.IsNullOrEmpty(scenario.playerTeam) ? scenario.units[0].team : scenario.playerTeam;
                Finish(survivor == player ? "Victory" : "Defeat", survivor, "LastTeamStanding");
            }
        }
        private void Finish(string value, string winner, string reason)
        { finished = true; outcome = value; winnerTeam = winner; stopReason = reason; }
        private double NextExpiry()
        {
            double next = double.MaxValue;
            foreach (UnitState unit in units) foreach (CombatStatusConfig status in unit.statuses)
                if (status.expiresAt < next) next = status.expiresAt;
            return next;
        }
        private CombatStepResult ExpireStatuses()
        {
            CombatStepResult result = NewStep("StateExpired", null);
            var effects = new List<CombatEffect>();
            foreach (UnitState unit in units) for (int i = unit.statuses.Count - 1; i >= 0; i--)
                if (unit.statuses[i].expiresAt <= time)
                {
                    CombatStatusConfig status = unit.statuses[i]; unit.statuses.RemoveAt(i);
                    CombatEffect effect = BeginEffect("status_expired", unit);
                    effect.statusId = status.id; effect.statusAppliedAt = status.appliedAt; effect.statusExpiresAt = status.expiresAt;
                    effect.removedStatusIds = new[] { status.id };
                    effect.description = unit.config.name + " 的 " + status.id + " 到期（固定 WAIT " + Number(time) + "）。";
                    effects.Add(effect);
                }
            result.effects = effects.ToArray(); result.message = "固定 WAIT 状态先于同刻行动到期；重新评估休眠角色。";
            WakeDormant(); return Record(result);
        }
        private void WakeDormant()
        {
            foreach (UnitState unit in units) if (unit.dormant && unit.hp > 0)
            { unit.dormant = false; unit.readyAt = Math.Max(time, unit.readyAt); }
        }
        private string StateKey()
        {
            var key = new StringBuilder();
            foreach (UnitState unit in units)
            {
                key.Append(unit.config.id).Append(':').Append(unit.hp).Append(':').Append(unit.mp).Append(':')
                    .Append(unit.baseVulnerable ? "vulnerable" : "normal").Append(':')
                    .Append(unit.hp <= 0 ? "dead" : unit.dormant ? "sleep:" + RoundTrip(Math.Max(0, unit.readyAt - time)) : RoundTrip(unit.readyAt - time));
                foreach (CombatStatusConfig status in unit.statuses) key.Append('|').Append(status.id).Append(':')
                    .Append(status.kind).Append(':').Append(status.magnitude).Append(':').Append(RoundTrip(status.expiresAt - time));
                key.Append(';');
            }
            return key.ToString();
        }
        private static int StatusMagnitude(UnitState unit, string kind)
        {
            long value = 0; foreach (CombatStatusConfig status in unit.statuses) if (status.kind == kind) value += status.magnitude;
            return (int)Math.Min(int.MaxValue, value);
        }
        private static CombatStatusConfig FindStatus(UnitState unit, string id)
        { foreach (CombatStatusConfig status in unit.statuses) if (status.id == id) return status; return null; }
        private static bool HasStatus(UnitState unit, string kind)
        { foreach (CombatStatusConfig status in unit.statuses) if (status.kind == kind) return true; return false; }
        private static CombatStatusConfig CopyStatus(CombatStatusConfig status)
        { return new CombatStatusConfig { id = status.id, kind = status.kind, magnitude = status.magnitude, appliedAt = status.appliedAt, expiresAt = status.expiresAt }; }
        private static CombatStatusConfig[] CopyStatuses(CombatStatusConfig[] source)
        {
            if (source == null) return Array.Empty<CombatStatusConfig>();
            var copy = new CombatStatusConfig[source.Length]; for (int i = 0; i < copy.Length; i++) copy[i] = CopyStatus(source[i]); return copy;
        }
        private static void ValidateStatuses(CombatStatusConfig[] statuses, string id)
        {
            if (statuses == null) return;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (CombatStatusConfig s in statuses) if (s == null || string.IsNullOrEmpty(s.id) || !ids.Add(s.id) ||
                !CombatTacticsCatalog.IsStatusSupported(s.kind) || s.magnitude < 0 ||
                !Finite(s.appliedAt) || s.appliedAt < 0 || !Finite(s.expiresAt) || s.expiresAt <= s.appliedAt)
                throw new ArgumentException("状态 ID/类型/幅度/施加与到期时刻无效：" + id);
        }
        private void ValidateCarry(CombatSnapshot previous, string team, double gap)
        {
            if (previous == null || previous.units == null || string.IsNullOrEmpty(team) || !Finite(previous.time) ||
                previous.time < 0 || !Finite(gap) || gap < 0 || !Finite(previous.time + gap))
                throw new ArgumentException("跨场快照、队伍或场间 WAIT 无效。");
            var seen = new HashSet<string>(StringComparer.Ordinal); var carriedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (CombatUnitSnapshot unit in previous.units)
            {
                if (unit == null || string.IsNullOrEmpty(unit.id) || !seen.Add(unit.id)) throw new ArgumentException("跨场快照含空或重复角色 ID。");
                if (unit.team != team) continue;
                CombatUnitConfig target = FindConfig(unit.id);
                if (target == null || target.team != team) throw new ArgumentException("跨场出现未知角色：" + unit.id);
                if (unit.maxHp != target.maxHp || unit.maxMp != target.maxMp) throw new ArgumentException("跨场最大 HP/MP 不一致，需显式处理：" + unit.id);
                if (unit.hp < 0 || unit.hp > unit.maxHp || unit.mp < 0 || unit.mp > unit.maxMp || !Finite(unit.nextActionTime) ||
                    unit.nextActionTime < 0 || unit.row != target.row || unit.column != target.column)
                    throw new ArgumentException("跨场 HP/MP/行动时刻/格位无效或与配置不一致：" + unit.id);
                ValidateStatuses(unit.statuses, unit.id);
                if (unit.statuses != null) foreach (CombatStatusConfig status in unit.statuses)
                    if (status.appliedAt > previous.time) throw new ArgumentException("跨场快照不能包含未来才施加的状态：" + unit.id);
                carriedIds.Add(unit.id);
            }
            int expected = 0;
            foreach (CombatUnitConfig unit in scenario.units) if (unit.team == team)
            { expected++; if (!carriedIds.Contains(unit.id)) throw new ArgumentException("跨场快照缺少队员，不能以满血角色补齐：" + unit.id); }
            if (expected == 0 || carriedIds.Count != expected) throw new ArgumentException("跨场队伍 ID 集合不一致。");
        }
        private static CombatUnitSnapshot FindSnapshot(CombatSnapshot snapshot, string id)
        { foreach (CombatUnitSnapshot unit in snapshot.units) if (unit.id == id) return unit; throw new ArgumentException("缺少角色：" + id); }
        public static CombatScenario CopyScenario(CombatScenario source) { return CloneScenario(source); }
        public static CombatStepResult CopyStep(CombatStepResult source) { return CloneStep(source); }
        /// <summary>Semantic comparison copy only; this does not validate an action definition.</summary>
        public static CombatActionConfig NormalizeActionDefinition(CombatActionConfig action)
        {
            if (action == null) return null;
            CombatActionConfig normalized = CopyAction(action);
            normalized.id = normalized.id ?? string.Empty;
            normalized.displayName = normalized.displayName ?? string.Empty;
            normalized.effectKind = normalized.effectKind ?? string.Empty;
            normalized.statusId = normalized.statusId ?? string.Empty;
            normalized.statusKind = normalized.statusKind ?? string.Empty;
            normalized.targetMode = TargetMode(normalized);
            normalized.damageType = DamageType(normalized);
            return normalized;
        }
        public static bool ActionDefinitionsEquivalent(CombatActionConfig a, CombatActionConfig b)
        {
            CombatActionConfig left = NormalizeActionDefinition(a), right = NormalizeActionDefinition(b);
            if (left == null || right == null) return left == null && right == null;
            return left.id == right.id && left.displayName == right.displayName && left.effectKind == right.effectKind &&
                left.magnitude == right.magnitude && left.mpCost == right.mpCost && left.baseWait == right.baseWait &&
                left.statusId == right.statusId && left.statusKind == right.statusKind && left.statusMagnitude == right.statusMagnitude &&
                left.statusDuration == right.statusDuration && left.targetMode == right.targetMode && left.isMagic == right.isMagic &&
                left.damageType == right.damageType;
        }
        public static CombatSnapshot CopySnapshot(CombatSnapshot source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var copy = new CombatSnapshot { scenarioId = source.scenarioId, parameterNotice = source.parameterNotice,
                seed = source.seed, randomUsed = source.randomUsed, time = source.time, battleStartTime = source.battleStartTime,
                nextActorId = source.nextActorId, nextActionTime = source.nextActionTime, pendingTimeTie = source.pendingTimeTie,
                blocked = source.blocked, blockReason = source.blockReason, completedStepCount = source.completedStepCount,
                finished = source.finished, outcome = source.outcome, winnerTeam = source.winnerTeam, stopReason = source.stopReason,
                units = new CombatUnitSnapshot[source.units.Length] };
            for (int i = 0; i < copy.units.Length; i++)
            {
                CombatUnitSnapshot u = source.units[i]; var rules = new CombatInstructionSnapshot[u.instructions == null ? 0 : u.instructions.Length];
                for (int j = 0; j < rules.Length; j++) { CombatInstructionSnapshot r = u.instructions[j]; rules[j] = new CombatInstructionSnapshot { id = r.id, display = r.display, capacityAllowed = r.capacityAllowed,
                    actionAvailable = r.actionAvailable, validationReason = r.validationReason }; }
                copy.units[i] = new CombatUnitSnapshot { id = u.id, name = u.name, team = u.team, row = u.row, column = u.column,
                    hp = u.hp, maxHp = u.maxHp, mp = u.mp, maxMp = u.maxMp, intelligence = u.intelligence, vulnerable = u.vulnerable,
                    baseVulnerable = u.baseVulnerable,
                    instructionCapacity = u.instructionCapacity, conditionTermCapacity = u.conditionTermCapacity,
                    nextActionTime = u.nextActionTime, instructions = rules, statuses = CopyStatuses(u.statuses), dormant = u.dormant,
                    equipmentDamageBonus = u.equipmentDamageBonus, equipmentSourceId = u.equipmentSourceId,
                    tags = CopyStrings(u.tags) ?? Array.Empty<string>(), effectiveAgilityFactor = u.effectiveAgilityFactor };
            }
            return copy;
        }

        private bool TryPrepare(UnitState actor, CombatInstruction rule, int index, CombatInstructionAttempt attempt,
            out UnitState selected, out CombatActionConfig action)
        {
            selected = null; action = null;
            if (rule == null) return Skip(attempt, "空指令。");
            if (index >= InstructionCapacity(actor.config.intelligence)) return Skip(attempt, "超出智力提供的指令容量。");
            string reason;
            if (!ValidateExpression(rule.condition, ConditionCapacity(actor.config.intelligence), out reason)) return Skip(attempt, reason);
            if (rule.scope != "self" && rule.scope != "allies" && rule.scope != "enemies") return Skip(attempt, "对象范围尚未支持：" + rule.scope);
            if (!CombatTacticsCatalog.IsSelectorSupported(rule.selector)) return Skip(attempt, "选择方式尚未支持：" + rule.selector);
            action = FindAction(rule.actionId);
            if (action == null) return Skip(attempt, "找不到行动配置：" + rule.actionId);
            if (!SupportedEffect(action.effectKind)) return Skip(attempt, "行动效果尚未实现：" + action.effectKind);
            if (!IsActionAvailable(actor.config, action.id)) return Skip(attempt, "该角色未学会或未获准配置此技能：" + action.id);
            if (action.isMagic && HasStatus(actor, "silenced")) return Skip(attempt, "沉默状态禁止此法术；可尝试后续非魔法行动。", "Silenced");
            if (actor.mp < action.mpCost) return Skip(attempt, "MP 不足：需要 " + Integer(action.mpCost) + "，当前 " + Integer(actor.mp) + "；无消耗。");
            var traces = new List<CombatCandidateTrace>();
            foreach (UnitState candidate in units)
            {
                if (!InScope(actor, candidate, rule.scope)) continue;
                var trace = new CombatCandidateTrace { unitId = candidate.config.id, unitName = candidate.config.name,
                    affectedTargetIds = Array.Empty<string>() }; traces.Add(trace);
                if (candidate.hp <= 0) { trace.reason = "HP 为 0；本场没有复活行动。"; continue; }
                trace.passesCondition = Matches(rule.condition, actor, candidate, out reason); trace.reason = reason;
                if (!trace.passesCondition) continue;
                List<UnitState> affected = AffectedTargets(actor, candidate, rule, action);
                trace.affectedTargetIds = UnitIds(affected);
                if (affected.Count == 0)
                {
                    trace.reason += action.effectKind == "heal" ? "；已满血或无合法伤员覆盖，急救无可执行效果。" :
                        action.effectKind == "restore_mp" ? "；MP已满／无MP容量，或范围内无合法缺蓝者。" :
                        action.effectKind == "drain_mp" ? "；不可自吸，且目标必须仍有MP。" : "；无可清除状态或其他合法覆盖者。";
                    continue;
                }
                trace.eligible = true;
                trace.reason += "；实际覆盖[" + string.Join(",", trace.affectedTargetIds) + "]";
                if (selected == null || PreferCandidate(candidate, selected, rule.selector)) selected = candidate;
            }
            attempt.candidates = traces.ToArray();
            if (selected == null) return Skip(attempt, "没有同一候选同时满足条件与行动可执行性，继续下一条。");
            return true;
        }

        private static bool ValidateExpression(CombatCondition expression, int capacity, out string reason)
        {
            reason = "";
            if (expression == null || expression.terms == null || expression.terms.Length == 0)
            { reason = "缺少条件；总是执行也须显式配置 always。"; return false; }
            if (expression.terms.Length > capacity || expression.terms.Length > 3)
            { reason = "本条条件项数超出当前智力开放的容量。"; return false; }
            if (expression.operation == "mixed")
            {
                if (expression.terms.Length != 3 || expression.operators == null || expression.operators.Length != 2 ||
                    (expression.grouping != "left" && expression.grouping != "right"))
                { reason = "混合条件必须有三项、两个连接符和明确的 left/right 括号。"; return false; }
                foreach (string op in expression.operators) if (op != "and" && op != "or")
                { reason = "混合条件连接符只支持 and/or。"; return false; }
            }
            else if (expression.operation != "all" && expression.operation != "any")
            { reason = "条件组合只支持 all、any 或有明确括号的 mixed。"; return false; }
            foreach (CombatConditionTerm term in expression.terms)
            {
                if (term == null || !CombatTacticsCatalog.IsConditionSupported(term.kind))
                { reason = "存在空或未实现的条件项：" + (term == null ? "" : term.kind); return false; }
                if (!string.IsNullOrEmpty(term.subject) && term.subject != "target" && term.subject != "actor")
                { reason = "条件读取来源只支持 target（默认）或 actor（施放者）。"; return false; }
                CombatTacticsOption definition = CombatTacticsCatalog.ConditionDefinition(term.kind);
                if (definition.usesThreshold && (!Finite(term.threshold) || term.threshold < definition.minThreshold || term.threshold > definition.maxThreshold ||
                    (definition.integerThreshold && Math.Floor(term.threshold) != term.threshold)))
                { reason = "条件阈值不在目录规定范围或不满足整数要求：" + definition.label; return false; }
                if (definition.usesValue && string.IsNullOrWhiteSpace(term.value))
                { reason = "本条件必须填写目录要求的值：" + definition.label; return false; }
                if ((term.kind == "has_status_kind" || term.kind == "lacks_status_kind") && !CombatTacticsCatalog.IsStatusSupported(term.value))
                { reason = "未知状态类别：" + term.value; return false; }
            }
            return true;
        }

        private bool Matches(CombatCondition expression, UnitState actor, UnitState candidate, out string reason)
        {
            var values = new bool[expression.terms.Length]; var parts = new string[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                CombatConditionTerm term = expression.terms[i];
                UnitState subject = term.subject == "actor" ? actor : candidate;
                string actual;
                bool value = EvaluateTerm(term, actor, subject, out actual);
                values[i] = value; parts[i] = TermDisplay(term) + actual + "=" + (value ? "真" : "假");
            }
            bool combined;
            if (expression.operation == "mixed")
                combined = expression.grouping == "left"
                    ? Join(Join(values[0], values[1], expression.operators[0]), values[2], expression.operators[1])
                    : Join(values[0], Join(values[1], values[2], expression.operators[1]), expression.operators[0]);
            else
            {
                combined = expression.operation == "all";
                foreach (bool value in values) combined = expression.operation == "all" ? combined && value : combined || value;
            }
            reason = ExpressionDisplay(expression, parts);
            return combined;
        }

        private bool EvaluateTerm(CombatConditionTerm term, UnitState actor, UnitState subject, out string actual)
        {
            actual="";
            int count; double remaining;
            switch(term.kind)
            {
                case "always": return true;
                case "hp_ratio_at_most": case "hp_ratio_at_least":
                    double hpRatio=(double)subject.hp/subject.config.maxHp;
                    actual="（当前 "+Percent(hpRatio)+"）";
                    return term.kind=="hp_ratio_at_most"?hpRatio<=term.threshold:hpRatio>=term.threshold;
                case "mp_ratio_at_most": case "mp_ratio_at_least":
                    double mpRatio=subject.config.maxMp==0?0:(double)subject.mp/subject.config.maxMp;
                    actual="（当前 "+Percent(mpRatio)+"）";
                    return term.kind=="mp_ratio_at_most"?mpRatio<=term.threshold:mpRatio>=term.threshold;
                case "hp_at_most": actual="（当前 "+Integer(subject.hp)+"）"; return subject.hp<=term.threshold;
                case "hp_at_least": actual="（当前 "+Integer(subject.hp)+"）"; return subject.hp>=term.threshold;
                case "mp_at_most": actual="（当前 "+Integer(subject.mp)+"）"; return subject.mp<=term.threshold;
                case "mp_at_least": actual="（当前 "+Integer(subject.mp)+"）"; return subject.mp>=term.threshold;
                case "hp_missing_at_least": count=subject.config.maxHp-subject.hp; actual="（缺口 "+Integer(count)+"）"; return count>=term.threshold;
                case "mp_missing_at_least": count=subject.config.maxMp-subject.mp; actual="（缺口 "+Integer(count)+"）"; return count>=term.threshold;
                case "vulnerable": return subject.baseVulnerable || HasStatus(subject,"vulnerable");
                case "has_status": return FindStatus(subject,term.value)!=null;
                case "lacks_status": return FindStatus(subject,term.value)==null;
                case "has_buff": return HasBuff(subject);
                case "has_debuff": return HasDebuff(subject);
                case "has_control": return HasControl(subject);
                case "has_status_kind": return HasStatus(subject,term.value) || (term.value=="vulnerable" && subject.baseVulnerable);
                case "lacks_status_kind": return !(HasStatus(subject,term.value) || (term.value=="vulnerable" && subject.baseVulnerable));
                case "has_tag": return HasTag(subject,term.value);
                case "lacks_tag": return !HasTag(subject,term.value);
                case "row_is": actual="（当前排 "+Integer(subject.config.row)+"）"; return subject.config.row==(int)term.threshold;
                case "row_at_most": actual="（当前排 "+Integer(subject.config.row)+"）"; return subject.config.row<=(int)term.threshold;
                case "row_at_least": actual="（当前排 "+Integer(subject.config.row)+"）"; return subject.config.row>=(int)term.threshold;
                case "team_alive_at_least": case "team_alive_at_most":
                    count=TeamCount(subject,false,false,false); actual="（实际 "+Integer(count)+"人）";
                    return term.kind=="team_alive_at_least"?count>=term.threshold:count<=term.threshold;
                case "opponents_alive_at_least": case "opponents_alive_at_most":
                    count=TeamCount(subject,true,false,false); actual="（实际 "+Integer(count)+"人）";
                    return term.kind=="opponents_alive_at_least"?count>=term.threshold:count<=term.threshold;
                case "team_injured_at_least":
                    count=TeamCount(subject,false,false,true); actual="（实际 "+Integer(count)+"人）"; return count>=term.threshold;
                case "row_alive_at_least":
                    count=TeamCount(subject,false,true,false); actual="（实际 "+Integer(count)+"人）"; return count>=term.threshold;
                case "row_injured_at_least":
                    count=TeamCount(subject,false,true,true); actual="（实际 "+Integer(count)+"人）"; return count>=term.threshold;
                case "status_remaining_at_most": case "status_remaining_at_least":
                    CombatStatusConfig status=FindStatus(subject,term.value);
                    if(status==null) { actual="（状态不存在）"; return false; }
                    remaining=Math.Max(0,status.expiresAt-time); actual="（剩余 "+Number(remaining)+" WAIT）";
                    return term.kind=="status_remaining_at_most"?remaining<=term.threshold:remaining>=term.threshold;
                case "wait_remaining_at_most": case "wait_remaining_at_least":
                    remaining=RemainingWait(subject); actual="（剩余 "+Number(remaining)+" WAIT）";
                    return term.kind=="wait_remaining_at_most"?remaining<=term.threshold:remaining>=term.threshold;
                case "is_actor": return ReferenceEquals(subject,actor);
                case "is_not_actor": return !ReferenceEquals(subject,actor);
                default: return false;
            }
        }
        private static bool HasTag(UnitState unit, string tag)
        { if(unit.config.tags!=null) foreach(string value in unit.config.tags) if(value==tag) return true; return false; }
        private int TeamCount(UnitState subject, bool opponents, bool rowOnly, bool injuredOnly)
        {
            int count=0;
            foreach(UnitState unit in units)
            {
                if(unit.hp<=0 || (opponents?unit.config.team==subject.config.team:unit.config.team!=subject.config.team)) continue;
                if(rowOnly && unit.config.row!=subject.config.row) continue;
                if(injuredOnly && unit.hp>=unit.config.maxHp) continue;
                count++;
            }
            return count;
        }
        private double RemainingWait(UnitState unit) { return Math.Max(0,unit.readyAt-time); }

        private static bool Join(bool a, bool b, string operation) { return operation == "and" ? a && b : a || b; }
        private static int CompareRatio(int valueA, int maxA, int valueB, int maxB)
        {
            long left = (long)valueA * (maxB == 0 ? 1 : maxB);
            long right = (long)valueB * (maxA == 0 ? 1 : maxA);
            return left < right ? -1 : left > right ? 1 : 0;
        }
        private bool PreferCandidate(UnitState a, UnitState b, string selector)
        {
            int hpRatio=CompareRatio(a.hp,a.config.maxHp,b.hp,b.config.maxHp);
            switch(selector)
            {
                case "lowest_hp_ratio": return hpRatio<0;
                case "highest_hp_ratio": return hpRatio>0;
                case "lowest_hp": return a.hp<b.hp;
                case "highest_hp": return a.hp>b.hp;
                case "lowest_mp_ratio": return CompareRatio(a.mp,a.config.maxMp,b.mp,b.config.maxMp)<0;
                case "highest_mp_ratio": return CompareRatio(a.mp,a.config.maxMp,b.mp,b.config.maxMp)>0;
                case "lowest_mp": return a.mp<b.mp;
                case "highest_mp": return a.mp>b.mp;
                case "most_hp_missing": return a.config.maxHp-a.hp>b.config.maxHp-b.hp;
                case "most_mp_missing": return a.config.maxMp-a.mp>b.config.maxMp-b.mp;
                case "front_first": return a.config.row!=b.config.row?a.config.row<b.config.row:hpRatio<0;
                case "back_first": return a.config.row!=b.config.row?a.config.row>b.config.row:hpRatio<0;
                case "vulnerable_first": return PreferFlag(a.baseVulnerable||HasStatus(a,"vulnerable"),b.baseVulnerable||HasStatus(b,"vulnerable"),hpRatio);
                case "buffed_first": return PreferFlag(HasBuff(a),HasBuff(b),hpRatio);
                case "debuffed_first": return PreferFlag(HasDebuff(a),HasDebuff(b),hpRatio);
                case "controlled_first": return PreferFlag(HasControl(a),HasControl(b),hpRatio);
                case "unprotected_first": return PreferFlag(!HasStatus(a,"guard")&&!HasStatus(a,"magic_guard"),!HasStatus(b,"guard")&&!HasStatus(b,"magic_guard"),hpRatio);
                case "highest_intelligence": return a.config.intelligence>b.config.intelligence;
                case "lowest_intelligence": return a.config.intelligence<b.config.intelligence;
                case "soonest_action": return RemainingWait(a)<RemainingWait(b);
                case "latest_action": return RemainingWait(a)>RemainingWait(b);
                case "fastest_speed": return EffectiveAgilityFactor(a)<EffectiveAgilityFactor(b);
                case "slowest_speed": return EffectiveAgilityFactor(a)>EffectiveAgilityFactor(b);
                default: return false;
            }
        }
        private static bool PreferFlag(bool a, bool b, int hpRatio) { return a!=b?a:hpRatio<0; }

        private CombatEditResult ValidateInstructionEdit(CombatUnitConfig unit, CombatInstruction rule, int replacing)
        {
            if (rule == null || string.IsNullOrWhiteSpace(rule.id))
                return EditFor(unit, false, "指令及其 ID 不能为空。", "InvalidInstruction");
            for (int i = 0; i < unit.instructions.Length; i++)
                if (i != replacing && unit.instructions[i] != null && unit.instructions[i].id == rule.id)
                    return EditFor(unit, false, "指令 ID 已存在于该角色列表。", "DuplicateInstructionId");
            if (rule.condition != null && rule.condition.terms != null && rule.condition.terms.Length > ConditionCapacity(unit.intelligence))
                return EditFor(unit, false, "本条条件项数超出当前智力开放的容量。", "ConditionCapacityExceeded");
            string reason;
            if (!ValidateExpression(rule.condition, ConditionCapacity(unit.intelligence), out reason))
                return EditFor(unit, false, reason, "InvalidCondition");
            if (rule.scope != "self" && rule.scope != "allies" && rule.scope != "enemies")
                return EditFor(unit, false, "未知对象范围。", "UnknownScope");
            if (!CombatTacticsCatalog.IsSelectorSupported(rule.selector)) return EditFor(unit, false, "未知选择方式。", "UnknownSelector");
            CombatActionConfig action = FindAction(rule.actionId);
            if (action == null || !IsActionAvailable(unit, rule.actionId))
                return EditFor(unit, false, "该技能不在角色可配置技能目录中。", "ActionUnavailable");
            if (!SupportedEffect(action.effectKind)) return EditFor(unit, false, "技能效果尚未实现。", "UnsupportedAction");
            return null;
        }
        private CombatEditResult EditFor(CombatUnitConfig unit, bool success, string message, string code)
        {
            return new CombatEditResult { success = success, message = message, code = code,
                instructionCapacity = unit == null ? 0 : InstructionCapacity(unit.intelligence),
                conditionTermCapacity = unit == null ? 0 : ConditionCapacity(unit.intelligence) };
        }
        private static bool IsRatioCondition(string kind)
        { return kind == "hp_ratio_at_most" || kind == "hp_ratio_at_least" || kind == "mp_ratio_at_most" || kind == "mp_ratio_at_least"; }
        private static bool SupportedEffect(string kind) { return CombatTacticsCatalog.IsEffectSupported(kind); }
        private bool IsActionAvailable(CombatUnitConfig unit, string actionId)
        { if (unit.availableActionIds != null) foreach (string id in unit.availableActionIds) if (id == actionId) return true; return false; }
        private void NormalizeLegacyActionAvailability()
        {
            foreach (CombatUnitConfig unit in scenario.units)
            {
                if (unit.availableActionIdsExplicit || (unit.availableActionIds != null && unit.availableActionIds.Length > 0))
                {
                    unit.availableActionIds = unit.availableActionIds ?? Array.Empty<string>();
                    unit.availableActionIdsExplicit = true;
                    continue;
                }
                var ids = new List<string>();
                foreach (CombatInstruction rule in unit.instructions)
                {
                    if (rule == null || string.IsNullOrEmpty(rule.actionId)) continue;
                    CombatActionConfig action = FindAction(rule.actionId);
                    if (action != null && SupportedEffect(action.effectKind) && !ids.Contains(action.id)) ids.Add(action.id);
                }
                unit.availableActionIds = ids.ToArray();
                unit.availableActionIdsExplicit = true;
            }
        }
        private static string[] CopyStrings(string[] source) { return source == null ? null : (string[])source.Clone(); }
        private static CombatActionConfig CopyAction(CombatActionConfig a)
        {
            return new CombatActionConfig { id = a.id, displayName = a.displayName, effectKind = a.effectKind,
                magnitude = a.magnitude, mpCost = a.mpCost, baseWait = a.baseWait, statusId = a.statusId,
                statusKind = a.statusKind, statusDuration = a.statusDuration, statusMagnitude = a.statusMagnitude,
                targetMode = a.targetMode, isMagic = a.isMagic, damageType = a.damageType };
        }
        private static CombatInstruction CopyInstruction(CombatInstruction rule)
        {
            if (rule == null) return null;
            CombatCondition condition = null;
            if (rule.condition != null)
            {
                CombatConditionTerm[] terms = rule.condition.terms == null ? null : new CombatConditionTerm[rule.condition.terms.Length];
                if (terms != null) for (int i = 0; i < terms.Length; i++)
                {
                    CombatConditionTerm term = rule.condition.terms[i];
                    if (term != null) terms[i] = new CombatConditionTerm { kind = term.kind, threshold = term.threshold, subject = term.subject, value = term.value };
                }
                condition = new CombatCondition { operation = rule.condition.operation, terms = terms, operators = CopyStrings(rule.condition.operators), grouping = rule.condition.grouping };
            }
            return new CombatInstruction { id = rule.id, label = rule.label, condition = condition, scope = rule.scope, selector = rule.selector, actionId = rule.actionId };
        }
        private static string TermDisplay(CombatConditionTerm term)
        {
            if (term == null) return "空条件";
            string label;
            switch (term.kind)
            {
                case "always": label = "总是"; break;
                case "hp_ratio_at_most": label = "HP≤" + Percent(term.threshold); break;
                case "hp_ratio_at_least": label = "HP≥" + Percent(term.threshold); break;
                case "mp_ratio_at_most": label = "MP≤" + Percent(term.threshold); break;
                case "mp_ratio_at_least": label = "MP≥" + Percent(term.threshold); break;
                case "vulnerable": label = "易伤"; break;
                case "has_status": label = "具有状态[" + term.value + "]"; break;
                case "lacks_status": label = "没有状态[" + term.value + "]"; break;
                case "row_is": label = "位于排" + Number(term.threshold); break;
                case "row_at_most": label = "排号≤" + Number(term.threshold); break;
                case "row_at_least": label = "排号≥" + Number(term.threshold); break;
                default:
                    CombatTacticsOption definition=CombatTacticsCatalog.ConditionDefinition(term.kind);
                    label=definition==null?term.kind??"未知条件":definition.label+
                        (definition.usesThreshold?Number(term.threshold):"")+(definition.usesValue?"["+term.value+"]":"");
                    break;
            }
            return (term.subject == "actor" ? "施放者" : "") + label;
        }
        private static string ExpressionDisplay(CombatCondition condition, string[] terms)
        {
            if (condition != null && condition.operation == "mixed")
            {
                if (terms.Length != 3 || condition.operators == null || condition.operators.Length != 2 ||
                    (condition.grouping != "left" && condition.grouping != "right")) return "混合条件缺少明确分组";
                string op0 = condition.operators[0] == "and" ? " AND " : " OR ";
                string op1 = condition.operators[1] == "and" ? " AND " : " OR ";
                return condition.grouping == "left" ? "(" + terms[0] + op0 + terms[1] + ")" + op1 + terms[2]
                    : terms[0] + op0 + "(" + terms[1] + op1 + terms[2] + ")";
            }
            return string.Join(condition != null && condition.operation == "any" ? " OR " : " AND ", terms);
        }

        private bool InScope(UnitState actor, UnitState candidate, string scope)
        {
            if (scope == "self") return ReferenceEquals(actor, candidate);
            if (scope == "allies") return actor.config.team == candidate.config.team &&
                (scenario.alliesIncludeSelf || !ReferenceEquals(actor, candidate));
            return actor.config.team != candidate.config.team;
        }

        private int InstructionCapacity(int intelligence)
        {
            CombatCapacityConfig capacity = scenario.capacity;
            double raw = Math.Floor(capacity.baseSlots + intelligence * capacity.slotsPerIntelligence);
            return (int)Math.Min(capacity.maximumSlots, Math.Max(capacity.minimumSlots, raw));
        }

        private int ConditionCapacity(int intelligence)
        {
            return intelligence >= scenario.capacity.thirdConditionAtIntelligence ? 3 :
                intelligence >= scenario.capacity.secondConditionAtIntelligence ? 2 : 1;
        }

        private void FindNextActor(out UnitState actor, out bool tied)
        {
            actor = null;
            tied = false;
            foreach (UnitState candidate in units)
            {
                if (candidate.hp <= 0 || candidate.dormant) continue;
                if (actor == null || candidate.readyAt < actor.readyAt) { actor = candidate; tied = false; }
                else if (candidate.readyAt == actor.readyAt) tied = true;
            }
        }

        private void ClearBlock()
        {
            if (outcome == "Stalemate") { finished = false; outcome = winnerTeam = stopReason = string.Empty; }
            WakeDormant(); seenStates.Clear(); seenStates.Add(StateKey());
        }
        private static bool Skip(CombatInstructionAttempt attempt, string reason, string code = "NotExecutable")
        { attempt.reason = reason; attempt.reasonCode = code; return false; }
        private static CombatEditResult Edit(bool success, string message) { return new CombatEditResult { success = success, message = message, code = success ? "Ok" : "InvalidEdit" }; }
        private CombatUnitConfig FindConfig(string id) { foreach (var unit in scenario.units) if (unit.id == id) return unit; return null; }
        private CombatActionConfig FindAction(string id) { foreach (var action in scenario.actions) if (action.id == id) return action; return null; }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static string Integer(int value) { return value.ToString(CultureInfo.InvariantCulture); }
        private static string Number(double value) { return value.ToString("0.###############", CultureInfo.InvariantCulture); }
        private static string RoundTrip(double value) { return value.ToString("R", CultureInfo.InvariantCulture); }
        private static string Percent(double value) { return Number(value * 100) + "%"; }

        private string Display(CombatInstruction instruction)
        {
            if (instruction == null) return "空指令";
            var terms = new List<string>();
            if (instruction.condition != null && instruction.condition.terms != null)
            {
                foreach (CombatConditionTerm term in instruction.condition.terms)
                {
                    if (term == null) terms.Add("空条件");
                    else terms.Add(TermDisplay(term));
                }
            }
            string scope = instruction.scope == "self" ? "自身" : instruction.scope == "allies" ? "己方" :
                instruction.scope == "enemies" ? "敌方" : instruction.scope;
            string selector = instruction.selector;
            foreach (CombatTacticsOption option in CombatTacticsCatalog.GetSelectors()) if (option.id == instruction.selector) selector = option.label;
            CombatActionConfig action = FindAction(instruction.actionId);
            return ExpressionDisplay(instruction.condition, terms.ToArray()) + " | " + scope + " | " + selector + " | " +
                (action == null ? instruction.actionId : action.displayName);
        }

        private static void ValidateScenario(CombatScenario config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (config.maxEvents < 0 || !Finite(config.maxWait) || config.maxWait < 0) throw new ArgumentException("运行预算不能为负或非有限值。");
            if (config.randomUsed) throw new ArgumentException("首场没有随机结算；randomUsed 必须为 false。");
            if (config.gridRows != 3 || config.gridColumns != 3) throw new ArgumentException("首轮每方站位区必须为 3×3。");
            if (config.maxUnitsPerTeam < 1 || config.maxUnitsPerTeam > 6) throw new ArgumentException("每队容量必须在 1 到 6 之间。");
            CombatCapacityConfig c = config.capacity;
            if (c == null || !Finite(c.baseSlots) || !Finite(c.slotsPerIntelligence) || c.baseSlots < 0 ||
                c.slotsPerIntelligence < 0 || c.minimumSlots < 1 || c.maximumSlots < c.minimumSlots ||
                c.secondConditionAtIntelligence < 0 || c.thirdConditionAtIntelligence < c.secondConditionAtIntelligence)
                throw new ArgumentException("智力容量测试参数无效。");
            if (config.units == null || config.units.Length == 0) throw new ArgumentException("必须配置至少一名角色。");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var cells = new HashSet<string>(StringComparer.Ordinal);
            var teamCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (CombatUnitConfig unit in config.units)
            {
                if (unit == null || string.IsNullOrEmpty(unit.id) || !ids.Add(unit.id) || string.IsNullOrEmpty(unit.team))
                    throw new ArgumentException("角色 ID 必须非空且唯一，并指定队伍。");
                if (unit.maxHp <= 0 || unit.hp < 0 || unit.hp > unit.maxHp || unit.maxMp < 0 || unit.mp < 0 || unit.mp > unit.maxMp ||
                    unit.intelligence < 0 || !Finite(unit.agilityFactor) || unit.agilityFactor <= 0 ||
                    !Finite(unit.initialReadyTime) || unit.initialReadyTime < 0 || unit.equipmentDamageBonus < 0)
                    throw new ArgumentException("角色 HP/MP/智力/敏捷系数/初始行动时刻无效：" + unit.id);
                if (unit.row < 0 || unit.row >= 3 || unit.column < 0 || unit.column >= 3 ||
                    !cells.Add(unit.team + ":" + unit.row + ":" + unit.column))
                    throw new ArgumentException("站位须在各队独立 3×3 区域内，且同队不能重叠：" + unit.id);
                int count;
                teamCounts.TryGetValue(unit.team, out count);
                teamCounts[unit.team] = count + 1;
                if (count + 1 > config.maxUnitsPerTeam) throw new ArgumentException("超过每队容量：" + unit.team);
                ValidateStatuses(unit.statuses, unit.id);
                if (unit.statuses != null) foreach (CombatStatusConfig status in unit.statuses)
                    if (status.appliedAt != 0) throw new ArgumentException("初始状态必须已在本场起点施加（相对 appliedAt=0）；未来状态激活尚未实现：" + unit.id);
            }
            if (config.actions == null) throw new ArgumentException("actions 必须是数组；可用空数组测试不可行动。");
            if (!string.IsNullOrEmpty(config.playerTeam) && !teamCounts.ContainsKey(config.playerTeam)) throw new ArgumentException("playerTeam 必须对应参战队伍。");
            ids.Clear();
            foreach (CombatActionConfig action in config.actions)
            {
                if (action == null || string.IsNullOrEmpty(action.id) || !ids.Add(action.id) || action.magnitude < 0 ||
                    action.mpCost < 0 || !Finite(action.baseWait) || action.baseWait <= 0)
                    throw new ArgumentException("行动 ID/效果量/MP费用/基础 WAIT 测试参数无效。");
                if (action.effectKind == "apply_status" && (string.IsNullOrEmpty(action.statusId) ||
                    !CombatTacticsCatalog.IsStatusSupported(action.statusKind) ||
                    action.statusMagnitude < 0 || !Finite(action.statusDuration) || action.statusDuration <= 0))
                    throw new ArgumentException("状态行动需要有效ID/类型/幅度和正持续WAIT。");
                if (TargetMode(action) != "single" && TargetMode(action) != "row" && TargetMode(action) != "column" && TargetMode(action) != "all")
                    throw new ArgumentException("行动范围只支持single/row/column/all：" + action.id);
                if (DamageType(action) != "legacy" && DamageType(action) != "physical" && DamageType(action) != "magic")
                    throw new ArgumentException("伤害类型只支持legacy/physical/magic：" + action.id);
            }
            foreach (CombatUnitConfig unit in config.units)
            {
                if (unit.availableActionIds == null) continue;
                var learned = new HashSet<string>(StringComparer.Ordinal);
                foreach (string actionId in unit.availableActionIds)
                    if (string.IsNullOrEmpty(actionId) || !learned.Add(actionId) || !ids.Contains(actionId))
                        throw new ArgumentException("角色技能白名单包含空、重复或未知行动：" + unit.id);
            }
        }

        private static CombatScenario CloneScenario(CombatScenario source)
        {
            var copy = new CombatScenario
            {
                id = source.id, title = source.title, parameterNotice = source.parameterNotice, seed = source.seed,
                randomUsed = source.randomUsed, alliesIncludeSelf = source.alliesIncludeSelf,
                maxUnitsPerTeam = source.maxUnitsPerTeam, gridRows = source.gridRows, gridColumns = source.gridColumns,
                playerTeam = source.playerTeam, maxEvents = source.maxEvents, maxWait = source.maxWait,
                capacity = new CombatCapacityConfig
                {
                    baseSlots = source.capacity.baseSlots, slotsPerIntelligence = source.capacity.slotsPerIntelligence,
                    minimumSlots = source.capacity.minimumSlots, maximumSlots = source.capacity.maximumSlots,
                    secondConditionAtIntelligence = source.capacity.secondConditionAtIntelligence,
                    thirdConditionAtIntelligence = source.capacity.thirdConditionAtIntelligence
                },
                units = new CombatUnitConfig[source.units.Length], actions = new CombatActionConfig[source.actions.Length]
            };
            for (int i = 0; i < copy.actions.Length; i++)
            {
                copy.actions[i] = CopyAction(source.actions[i]);
            }
            for (int i = 0; i < copy.units.Length; i++)
            {
                CombatUnitConfig u = source.units[i];
                var rules = new CombatInstruction[u.instructions == null ? 0 : u.instructions.Length];
                for (int j = 0; j < rules.Length; j++)
                {
                    CombatInstruction r = u.instructions[j];
                    if (r == null) continue;
                    CombatCondition condition = null;
                    if (r.condition != null)
                    {
                        CombatConditionTerm[] terms = r.condition.terms == null ? null : new CombatConditionTerm[r.condition.terms.Length];
                        if (terms != null)
                            for (int k = 0; k < terms.Length; k++)
                                if (r.condition.terms[k] != null) terms[k] = new CombatConditionTerm
                                    { kind = r.condition.terms[k].kind, threshold = r.condition.terms[k].threshold,
                                        subject = r.condition.terms[k].subject, value = r.condition.terms[k].value };
                        condition = new CombatCondition { operation = r.condition.operation, terms = terms,
                            operators = CopyStrings(r.condition.operators), grouping = r.condition.grouping };
                    }
                    rules[j] = new CombatInstruction { id = r.id, label = r.label, condition = condition,
                        scope = r.scope, selector = r.selector, actionId = r.actionId };
                }
                copy.units[i] = new CombatUnitConfig
                {
                    id = u.id, name = u.name, team = u.team, row = u.row, column = u.column,
                    maxHp = u.maxHp, hp = u.hp, maxMp = u.maxMp, mp = u.mp, intelligence = u.intelligence,
                    agilityFactor = u.agilityFactor, initialReadyTime = u.initialReadyTime, vulnerable = u.vulnerable,
                    instructions = rules, statuses = CopyStatuses(u.statuses), equipmentDamageBonus = u.equipmentDamageBonus,
                    equipmentSourceId = u.equipmentSourceId, availableActionIds = CopyStrings(u.availableActionIds),
                    availableActionIdsExplicit = u.availableActionIdsExplicit, tags = CopyStrings(u.tags)
                };
            }
            return copy;
        }

        private static CombatStepResult CloneStep(CombatStepResult source)
        {
            var copy = new CombatStepResult
            {
                index = source.index, time = source.time, status = source.status, actorId = source.actorId,
                actorName = source.actorName, actionId = source.actionId, actionName = source.actionName,
                targetId = source.targetId, targetName = source.targetName, mpCost = source.mpCost,
                mpBefore = source.mpBefore, mpAfter = source.mpAfter, baseWait = source.baseWait,
                agilityFactor = source.agilityFactor, actualWait = source.actualWait,
                nextActionTime = source.nextActionTime, message = source.message,
                outcome = source.outcome, winnerTeam = source.winnerTeam, stopReason = source.stopReason,
                timeTieResolved = source.timeTieResolved, tieBreakReason = source.tieBreakReason, equipmentDamageBonus = source.equipmentDamageBonus,
                targetIds = CopyStrings(source.targetIds) ?? Array.Empty<string>(), baseAgilityFactor = source.baseAgilityFactor,
                statusWaitFactor = source.statusWaitFactor, controlReason = source.controlReason,
                attempts = new CombatInstructionAttempt[source.attempts.Length], effects = new CombatEffect[source.effects.Length]
            };
            for (int i = 0; i < copy.attempts.Length; i++)
            {
                CombatInstructionAttempt a = source.attempts[i];
                var candidates = new CombatCandidateTrace[a.candidates.Length];
                for (int j = 0; j < candidates.Length; j++)
                {
                    CombatCandidateTrace c = a.candidates[j];
                    candidates[j] = new CombatCandidateTrace { unitId = c.unitId, unitName = c.unitName,
                        passesCondition = c.passesCondition, eligible = c.eligible, reason = c.reason,
                        affectedTargetIds = CopyStrings(c.affectedTargetIds) ?? Array.Empty<string>() };
                }
                copy.attempts[i] = new CombatInstructionAttempt { instructionIndex = a.instructionIndex,
                    instructionId = a.instructionId, display = a.display, outcome = a.outcome, reason = a.reason, reasonCode = a.reasonCode, candidates = candidates };
            }
            for (int i = 0; i < copy.effects.Length; i++)
            {
                CombatEffect e = source.effects[i];
                copy.effects[i] = new CombatEffect { kind = e.kind, targetId = e.targetId, targetName = e.targetName,
                    hpBefore = e.hpBefore, hpAfter = e.hpAfter, hpDelta = e.hpDelta, description = e.description,
                    mpBefore = e.mpBefore, mpAfter = e.mpAfter, mpDelta = e.mpDelta, statusId = e.statusId,
                    statusAppliedAt = e.statusAppliedAt, statusExpiresAt = e.statusExpiresAt,
                    removedStatusIds = CopyStrings(e.removedStatusIds) ?? Array.Empty<string>(),
                    baseVulnerableBefore = e.baseVulnerableBefore, baseVulnerableAfter = e.baseVulnerableAfter };
            }
            return copy;
        }
    }
}
