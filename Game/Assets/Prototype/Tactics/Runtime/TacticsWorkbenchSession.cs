using System;
using System.Linq;
using System.Text;
using Prototype.Combat;

namespace Prototype.Tactics
{
    /// <summary>
    /// An isolated editor and an explicitly committed battle. All validation,
    /// selection, conditions, capacity, execution and history come from CombatSession.
    /// This adapter performs no combat settlement and has no file or Unity dependency.
    /// </summary>
    public sealed class TacticsWorkbenchSession
    {
        private readonly CombatScenario loadedScenario;
        private CombatSession editor;
        private CombatScenario committedScenario;
        private CombatSession battle;
        private int draftRevision;
        private int committedRevision;
        private CombatEditResult lastEditResult;
        private TacticsComparisonResult lastComparison;

        public TacticsWorkbenchSession(CombatScenario loaded)
        {
            if (loaded == null) throw new ArgumentNullException(nameof(loaded));
            // Let the actual engine validate before publishing any workbench state.
            new CombatSession(loaded);
            loadedScenario = CombatSession.CopyScenario(loaded);
            Reset();
        }

        public void Reset()
        {
            editor = new CombatSession(loadedScenario);
            battle = null;
            committedScenario = null;
            draftRevision = 0;
            committedRevision = -1;
            lastComparison = null;
            lastEditResult = Result(true, "Reset", "已恢复加载战例；尚未应用并开战。");
        }

        public CombatScenario GetDraftScenario() { return editor.GetScenario(); }
        public CombatSnapshot GetDraftPreview() { return editor.GetSnapshot(); }
        public CombatScenario GetCommittedScenario()
        {
            return committedScenario == null ? null : CombatSession.CopyScenario(committedScenario);
        }
        public CombatSnapshot GetBattleSnapshot() { return battle == null ? null : battle.GetSnapshot(); }
        public CombatStepResult[] GetBattleHistory() { return battle == null ? Array.Empty<CombatStepResult>() : battle.GetHistory(); }

        public TacticsCatalogSnapshot GetCatalog(string unitId)
        {
            CombatUnitSnapshot unit = editor.GetSnapshot().units.FirstOrDefault(value => value.id == unitId);
            return new TacticsCatalogSnapshot
            {
                conditions = CombatTacticsCatalog.GetConditions(), operations = CombatTacticsCatalog.GetOperations(),
                scopes = CombatTacticsCatalog.GetScopes(), selectors = CombatTacticsCatalog.GetSelectors(),
                groupings = CombatTacticsCatalog.GetGroupings(), subjects = CombatTacticsCatalog.GetSubjects(),
                statusKinds = CombatTacticsCatalog.GetStatusKinds(), targetModes = CombatTacticsCatalog.GetTargetModes(),
                damageTypes = CombatTacticsCatalog.GetDamageTypes(), effectKinds = CombatTacticsCatalog.GetEffectKinds(),
                actions = editor.GetAvailableActions(unitId), instructionCapacity = unit == null ? 0 : unit.instructionCapacity,
                conditionCapacity = unit == null ? 0 : unit.conditionTermCapacity
            };
        }

        public CombatInstruction CreateBlankInstruction()
        {
            return new CombatInstruction
            {
                id = "workbench-rule-" + Guid.NewGuid().ToString("N"), label = "新规则",
                condition = new CombatCondition { operation = "", terms = Array.Empty<CombatConditionTerm>(),
                    operators = Array.Empty<string>(), grouping = "" },
                scope = "", selector = "", actionId = ""
            };
        }

        public CombatEditResult InsertInstruction(string unitId, int index, CombatInstruction instruction)
        {
            return Edit(unitId, () => editor.InsertInstruction(unitId, index, instruction));
        }

        public CombatEditResult ReplaceInstruction(string unitId, int index, CombatInstruction instruction)
        {
            return Edit(unitId, () => editor.ReplaceInstruction(unitId, index, instruction));
        }

        public CombatEditResult RemoveInstruction(string unitId, int index)
        {
            return Edit(unitId, () => editor.RemoveInstruction(unitId, index));
        }

        public CombatEditResult MoveInstruction(string unitId, int fromIndex, int toIndex)
        {
            return Edit(unitId, () => editor.MoveInstruction(unitId, fromIndex, toIndex));
        }

        public CombatEditResult GetInstructionValidation(string unitId, int index)
        {
            CombatScenario draft = editor.GetScenario();
            CombatUnitConfig unit = draft.units.FirstOrDefault(value => value.id == unitId);
            if (unit == null) return Result(false, "UnknownUnit", "找不到角色：" + unitId);
            if (index < 0 || index >= unit.instructions.Length) return Result(false, "InvalidIndex", "该指令索引不存在。");
            var preview = new CombatSession(draft);
            return TacticsCopies.Edit(preview.ReplaceInstruction(unitId, index, unit.instructions[index]));
        }

        private CombatEditResult Edit(string unitId, Func<CombatEditResult> command)
        {
            CombatScenario before = editor.GetScenario();
            CombatUnitConfig unit = before.units.FirstOrDefault(value => value.id == unitId);
            string team = string.IsNullOrEmpty(before.playerTeam) ? before.units[0].team : before.playerTeam;
            if (unit == null) return Remember(Result(false, "UnknownUnit", "找不到角色：" + unitId));
            if (unit.team != team) return Remember(Result(false, "FriendlyUnitRequired", "工作台只编辑己方指令；敌情保持加载战例。"));
            CombatEditResult edited = command();
            if (edited.success && !TacticsCopies.Equivalent(before, editor.GetScenario()))
            {
                draftRevision++;
                lastComparison = null;
            }
            return Remember(edited);
        }

        public CombatEditResult CommitBattle()
        {
            try
            {
                var candidate = new CombatSession(editor.GetScenario());
                committedScenario = candidate.GetScenario();
                battle = candidate;
                committedRevision = draftRevision;
                return Remember(Result(true, "Committed", "已按当前草稿从其初始 HP/MP、状态和种子应用并开战。后续草稿编辑不会影响本场。"));
            }
            catch (ArgumentException exception)
            {
                return Remember(Result(false, "InvalidDraft", exception.Message));
            }
        }

        public CombatStepResult StepBattle()
        {
            if (battle != null) return battle.Step();
            Remember(Result(false, "BattleNotCommitted", "请先点击应用并开战；编辑和查看预览不会偷偷推进战斗。"));
            return null;
        }

        public CombatRunResult RunBattleToEnd()
        {
            if (battle != null) return battle.RunToCompletion();
            Remember(Result(false, "BattleNotCommitted", "请先应用并开战。"));
            return null;
        }

        public void ReplayCommitted()
        {
            if (committedScenario == null)
            {
                Remember(Result(false, "BattleNotCommitted", "尚无本场已应用配置；请先应用并开战。"));
                return;
            }
            battle = new CombatSession(committedScenario);
            Remember(Result(true, "ReplayedCommitted", "已从本场已应用配置重放；未应用的编辑草稿保持原样。"));
        }

        public TacticsComparisonResult CompareWithLoaded()
        {
            var baseline = new CombatSession(loadedScenario);
            var variant = new CombatSession(editor.GetScenario());
            string kind, unitId, description, error;
            if (!TacticsComparison.TrySingleInstructionChange(baseline.GetScenario(), variant.GetScenario(),
                out kind, out unitId, out description, out error))
            {
                lastComparison = new TacticsComparisonResult { success = false, code = "SingleInstructionChangeRequired",
                    message = error, changeKind = "", changedUnitId = "" };
                return TacticsCopies.Comparison(lastComparison);
            }
            CombatScenario baselineInput = baseline.GetScenario();
            CombatScenario variantInput = variant.GetScenario();
            CombatRunResult baselineRun = baseline.RunToCompletion();
            CombatRunResult variantRun = variant.RunToCompletion();
            CombatFirstDivergence first = TacticsComparison.FirstActualDivergence(baselineRun.steps, variantRun.steps);
            lastComparison = new TacticsComparisonResult
            {
                success = true, code = "Compared", message = first == null
                    ? "两次真实战斗未发现行动或状态效果分歧；没有把尝试文字变化当成行动分歧。"
                    : "已从相同加载开局运行两次真实战斗；首次分歧按行动、时刻、对象、消耗或实际效果确定。",
                changeKind = kind, changedUnitId = unitId, baselineLogText = baseline.ExportLogText(), variantLogText = variant.ExportLogText(),
                comparison = new CombatComparisonReport
                {
                    id = loadedScenario.id + ":workbench-comparison", baselineLabel = "加载战例基准", variantLabel = "当前编辑草稿",
                    changedSetting = description, onlyDeclaredSettingChanged = true, baselineConfiguration = baselineInput,
                    variantConfiguration = variantInput, baseline = baselineRun, variant = variantRun, firstDivergence = first,
                    observableImpact = first == null ? "实际行动及效果相同。" : first.description
                }
            };
            return TacticsCopies.Comparison(lastComparison);
        }

        private bool HasUncommittedChanges()
        {
            return committedScenario == null ? draftRevision > 0 : !TacticsCopies.Equivalent(editor.GetScenario(), committedScenario);
        }

        public TacticsWorkbenchSnapshot GetSnapshot()
        {
            return new TacticsWorkbenchSnapshot
            {
                draftScenario = editor.GetScenario(), draftPreview = editor.GetSnapshot(), committedScenario = GetCommittedScenario(),
                battleSnapshot = GetBattleSnapshot(), history = GetBattleHistory(), draftRevision = draftRevision,
                committedRevision = committedRevision, hasCommittedBattle = battle != null, hasUncommittedChanges = HasUncommittedChanges(),
                lastEditResult = TacticsCopies.Edit(lastEditResult), lastComparison = TacticsCopies.Comparison(lastComparison)
            };
        }

        public TacticsWorkbenchAcceptance GetAcceptance()
        {
            return new TacticsWorkbenchAcceptance
            {
                schemaVersion = 1, loadedScenario = CombatSession.CopyScenario(loadedScenario), draftScenario = editor.GetScenario(),
                committedScenario = GetCommittedScenario(), battleSnapshot = GetBattleSnapshot(), battleHistory = GetBattleHistory(),
                draftRevision = draftRevision, committedRevision = committedRevision, hasUncommittedChanges = HasUncommittedChanges(),
                lastEditResult = TacticsCopies.Edit(lastEditResult), lastComparison = TacticsCopies.Comparison(lastComparison), reportText = ExportReportText()
            };
        }

        public string ExportReportText()
        {
            var text = new StringBuilder("战术工作台真实运行记录\n加载战例：").Append(loadedScenario.id)
                .Append("\n固定种子：").Append(loadedScenario.seed).Append("\n草稿版本：").Append(draftRevision)
                .Append("；本场已应用版本：").Append(committedRevision).Append("；有未应用修改：").Append(HasUncommittedChanges())
                .Append("\n重放使用本场已应用配置；对照基准始终为实际加载战例。全部为可替换试玩参数。\n");
            if (battle == null) text.Append("\n尚未应用并开战，没有实际行动记录。\n");
            else text.Append("\n本场实际 CombatSession 日志\n").Append(battle.ExportLogText());
            if (lastComparison != null)
            {
                text.Append("\n同开局单指令对照：").Append(lastComparison.message).Append('\n');
                if (lastComparison.success)
                {
                    text.Append(lastComparison.comparison.changedSetting).Append('\n')
                        .Append("基准结束 WAIT ").Append(lastComparison.comparison.baseline.time)
                        .Append("；草稿结束 WAIT ").Append(lastComparison.comparison.variant.time).Append('\n');
                    if (lastComparison.comparison.firstDivergence != null)
                        text.Append(lastComparison.comparison.firstDivergence.description).Append('\n');
                    text.Append("\n加载基准真实日志\n").Append(lastComparison.baselineLogText)
                        .Append("\n编辑草稿真实日志\n").Append(lastComparison.variantLogText);
                }
            }
            return text.ToString();
        }

        private CombatEditResult Remember(CombatEditResult value)
        {
            lastEditResult = TacticsCopies.Edit(value);
            return TacticsCopies.Edit(value);
        }

        private static CombatEditResult Result(bool success, string code, string message)
        {
            return new CombatEditResult { success = success, code = code, message = message };
        }
    }
}
