using System;
using Prototype.UnityClient.Candidates.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Prototype.UnityClient.Candidates.Argon
{
    [TacticsCandidate("argon", "A · 石枢星盘")]
    public sealed class ArgonCandidateView : TacticsCandidateView
    {
        private TacticsSharedController shared;
        private TMP_FontAsset font;
        private RectTransform surface;
        private RectTransform editorRoot;
        private RectTransform battleRoot;
        private RectTransform toolsRoot;
        private RectTransform comparisonRoot;
        private RectTransform wideTools;
        private Button drawerCompare;
        private Button drawerSend;
        private Button drawerExport;
        private Button drawerReset;
        private Button wideCompare;
        private Button wideSend;
        private Button wideExport;
        private Button wideReset;
        private TMP_Text threeStates;
        private TMP_Text footer;
        private TMP_Text toolFeedback;
        private TMP_Text comparisonText;
        private bool battlePage;
        private bool refreshing;
        private bool initialized;
        private bool layoutRefreshPending;

        public ArgonEditorPage EditorPage { get; private set; }
        public ArgonBattlePage BattlePage { get; private set; }
        public TMP_Dropdown ScenarioDropdown { get; private set; }
        public Button EditorTab { get; private set; }
        public Button BattleTab { get; private set; }
        public Button ApplyButton { get; private set; }
        public Button ReplayButton { get; private set; }
        public Button ToolsButton { get; private set; }
        public Button CompareButton { get; private set; }
        public Button SendExpeditionButton { get; private set; }
        public Button ExportButton { get; private set; }
        public Button ResetButton { get; private set; }
        public Button CloseComparisonButton { get; private set; }
        public TMP_Text RevisionText { get { return threeStates; } }
        public TMP_Text FeedbackText { get { return footer; } }
        public TMP_Text ComparisonText { get { return comparisonText; } }
        public bool IsBattlePage { get { return battlePage; } }
        public bool IsToolsVisible { get { return toolsRoot != null && toolsRoot.gameObject.activeSelf; } }

        public override void Initialize(TMP_FontAsset value, TacticsSharedController controller)
        {
            if (initialized) return;
            font = value;
            shared = controller;
            surface = ArgonUi.Image("Argon_StoneAstrolabe", transform, ArgonTheme.Canvas).rectTransform;
            BuildHeader();
            editorRoot = ArgonUi.Rect("Argon_PreBattlePage", surface);
            ArgonUi.Fill(editorRoot, 12, 64, 12, 36);
            EditorPage = editorRoot.gameObject.AddComponent<ArgonEditorPage>();
            EditorPage.Initialize(font, shared);
            battleRoot = ArgonUi.Rect("Argon_CombatPlaybackPage", surface);
            ArgonUi.Fill(battleRoot, 12, 64, 12, 36);
            BattlePage = battleRoot.gameObject.AddComponent<ArgonBattlePage>();
            BattlePage.Initialize(font, shared);
            footer = ArgonUi.Text("Argon_Footer", surface, font, "", 12, ArgonTheme.Muted);
            var footerRect = footer.rectTransform;
            footerRect.anchorMin = Vector2.zero; footerRect.anchorMax = new Vector2(1, 0);
            footerRect.offsetMin = new Vector2(12, 3); footerRect.offsetMax = new Vector2(-12, 29);
            footer.alignment = TextAlignmentOptions.MidlineLeft;
            footer.textWrappingMode = TextWrappingModes.NoWrap;
            footer.overflowMode = TextOverflowModes.Ellipsis;
            BuildTools();
            BuildComparison();
            initialized = true;
            FitSurface();
            SetPage(false, false);
        }

        private void BuildHeader()
        {
            var header = ArgonUi.Row("Argon_WorkbenchControlBand", surface, 8);
            header.anchorMin = new Vector2(0, 1); header.anchorMax = Vector2.one; header.pivot = new Vector2(.5f, 1);
            header.offsetMin = new Vector2(12, -58); header.offsetMax = new Vector2(-12, -2);
            var cases = ArgonUi.Column("ScenarioBlock", header, 2);
            ArgonUi.Size(cases.gameObject, 260, 56);
            var title = ArgonUi.Text("ArgonTitle", cases, font, "石枢星盘 · 战例", 14, ArgonTheme.Brass, true);
            ArgonUi.Size(title.gameObject, -1, 20, 1);
            ScenarioDropdown = ArgonUi.Dropdown("Argon_Scenario", cases, font, -1, 32);
            ArgonUi.Options(ScenarioDropdown, shared.CaseTitles, shared.SelectedCaseIndex);
            ScenarioDropdown.onValueChanged.AddListener(index =>
            {
                if (refreshing) return;
                shared.Pause();
                shared.SelectCase(index);
                SetPage(false, false);
            });
            var tabs = ArgonUi.Row("PageTabs", header, 4);
            ArgonUi.Size(tabs.gameObject, 310, 56);
            EditorTab = ArgonUi.Button("Argon_EditorTab", tabs, font, "① 战前指令编排", () => SetPage(false), -1, 44);
            BattleTab = ArgonUi.Button("Argon_BattleTab", tabs, font, "② 战斗推演播放", () => SetPage(true), -1, 44);
            threeStates = ArgonUi.Text("Argon_ThreeStateContract", header, font, "", 12, ArgonTheme.Secondary);
            threeStates.alignment = TextAlignmentOptions.MidlineLeft;
            ArgonUi.Size(threeStates.gameObject, -1, 56, 1);
            wideTools = ArgonUi.Row("Argon_WideScreenTools", header, 4);
            ArgonUi.Size(wideTools.gameObject, 326, 56);
            wideCompare = ArgonUi.Button("Argon_WideCompare", wideTools, font, "单项对照", OpenComparison, 78, 38);
            wideSend = ArgonUi.Button("Argon_WideSend", wideTools, font, "发送出征", () => shared.SendToExpedition(), 78, 38);
            wideExport = ArgonUi.Button("Argon_WideExport", wideTools, font, "导出战报", () => shared.Export(), 78, 38);
            wideReset = ArgonUi.Button("Argon_WideReset", wideTools, font, "重置", ResetWorkbench, 80, 38);
            wideTools.gameObject.SetActive(false);
            var commands = ArgonUi.Row("MainCommands", header, 6);
            ArgonUi.Size(commands.gameObject, 332, 56);
            ApplyButton = ArgonUi.Button("Argon_Apply", commands, font, "应用并开战", () =>
            {
                if (shared.CommitBattle()) SetPage(true, false);
            }, 148, 38, true);
            ReplayButton = ArgonUi.Button("Argon_Replay", commands, font, "同开局重放", () => { shared.Replay(); SetPage(true, false); }, 104, 38);
            ToolsButton = ArgonUi.Button("Argon_Tools", commands, font, "工具", () =>
            {
                toolsRoot.gameObject.SetActive(!toolsRoot.gameObject.activeSelf);
                RefreshView();
            }, 68, 38);
        }

        private void BuildTools()
        {
            var content = ArgonUi.Frame("Argon_SecondaryTools", surface, ArgonTheme.Brass);
            toolsRoot = (RectTransform)content.parent;
            toolsRoot.anchorMin = toolsRoot.anchorMax = Vector2.one;
            toolsRoot.pivot = Vector2.one;
            toolsRoot.anchoredPosition = new Vector2(-12, -62);
            toolsRoot.sizeDelta = new Vector2(430, 362);
            var column = ArgonUi.Column("ToolsLayout", content, 7);
            ArgonUi.Fill(column);
            var heading = ArgonUi.Text("Heading", column, font, "战术工具 · 同一工作台会话", 16, ArgonTheme.Brass, true);
            ArgonUi.Size(heading.gameObject, -1, 27, 1);
            drawerCompare = ArgonUi.Button("Argon_Compare", column, font, "单项对照 · 基准 vs 当前草稿", OpenComparison, -1, 34);
            drawerSend = ArgonUi.Button("Argon_SendExpedition", column, font, "发送战术及所需动作到出征", () => shared.SendToExpedition(), -1, 34);
            drawerExport = ArgonUi.Button("Argon_Export", column, font, "导出输入与实际战报", () => shared.Export(), -1, 34);
            drawerReset = ArgonUi.Button("Argon_Reset", column, font, "重置本战例 · 恢复加载开局", ResetWorkbench, -1, 34);
            CompareButton = drawerCompare;
            SendExpeditionButton = drawerSend;
            ExportButton = drawerExport;
            ResetButton = drawerReset;
            ScrollRect scroll;
            toolFeedback = ArgonUi.Readout("Argon_ToolFeedback", column, font, out scroll);
            toolsRoot.gameObject.SetActive(false);
        }

        private void BuildComparison()
        {
            comparisonRoot = ArgonUi.Image("Argon_ComparisonModal", surface, new Color(0, 0, 0, .65f), true).rectTransform;
            ArgonUi.Fill(comparisonRoot);
            var content = ArgonUi.Frame("Argon_ComparisonDrawer", comparisonRoot, ArgonTheme.Brass);
            ArgonUi.Fill((RectTransform)content.parent, 96, 98, 96, 72);
            var column = ArgonUi.Column("ComparisonLayout", content, 8);
            ArgonUi.Fill(column);
            var heading = ArgonUi.Row("ComparisonHeading", column, 8);
            ArgonUi.Size(heading.gameObject, -1, 32, 1);
            var title = ArgonUi.Text("Heading", heading, font, "单项对照 · 独立计算，不推进当前棋盘", 16, ArgonTheme.Brass, true);
            ArgonUi.Size(title.gameObject, -1, 32, 1);
            CloseComparisonButton = ArgonUi.Button("Argon_CloseComparison", heading, font, "返回当前页", () =>
            {
                comparisonRoot.gameObject.SetActive(false); shared.ShowBattle(); RefreshView();
            }, 122, 32);
            ScrollRect scroll;
            comparisonText = ArgonUi.Readout("Argon_ComparisonResults", column, font, out scroll);
            comparisonRoot.gameObject.SetActive(false);
        }

        public override void RefreshView()
        {
            if (!initialized || refreshing || shared == null) return;
            if (CanvasUpdateRegistry.IsRebuildingLayout() || CanvasUpdateRegistry.IsRebuildingGraphics())
            {
                layoutRefreshPending = true;
                return;
            }
            refreshing = true;
            try
            {
                var state = shared.GetState();
                FitSurface();
                var wide = surface.rect.width > 1500;
                if (wideTools.gameObject.activeSelf != wide) wideTools.gameObject.SetActive(wide);
                CompareButton = wide ? wideCompare : drawerCompare;
                SendExpeditionButton = wide ? wideSend : drawerSend;
                ExportButton = wide ? wideExport : drawerExport;
                ResetButton = wide ? wideReset : drawerReset;
                ScenarioDropdown.SetValueWithoutNotify(shared.SelectedCaseIndex);
                ScenarioDropdown.RefreshShownValue();
                var caseTitles = shared.CaseTitles;
                var fullCaseTitle = shared.SelectedCaseIndex >= 0 && shared.SelectedCaseIndex < caseTitles.Length ? caseTitles[shared.SelectedCaseIndex] : "战例";
                // Only the closed caption is compact. The actual options and tool
                // details retain the complete title and original selection indices.
                ScenarioDropdown.captionText.text = CompactCaseTitle(fullCaseTitle);
                var snapshot = state.workbench;
                threeStates.text = "表单：" + (state.form != null && state.form.hasUnsubmittedChanges ? "未写入槽位 *" : "已同步") +
                    "\n草稿 v" + (snapshot == null ? 0 : snapshot.draftRevision) + (snapshot != null && snapshot.hasUncommittedChanges ? " · 待提交" : "") +
                    "  →  上场 " + (snapshot == null || !snapshot.hasCommittedBattle ? "尚未开战" : "v" + snapshot.committedRevision);
                threeStates.color = state.form != null && state.form.hasUnsubmittedChanges ? ArgonTheme.Warning : ArgonTheme.Secondary;
                ReplayButton.interactable = state.battle != null;
                ArgonUi.Selected(EditorTab, !battlePage);
                ArgonUi.Selected(BattleTab, battlePage);
                EditorPage.Refresh(state);
                BattlePage.Refresh(state);
                var scenario = snapshot == null ? null : snapshot.draftScenario;
                var source = scenario == null ? "" : scenario.id + " · seed " + scenario.seed;
                footer.text = string.IsNullOrWhiteSpace(state.feedback) ? source + "  |  表单 → 指令槽草稿 → 已提交战斗；切换候选保留同一状态。" : state.feedback;
                toolFeedback.text = fullCaseTitle + "\n" + source + "\n\n" + (state.feedback ?? "") +
                    (string.IsNullOrEmpty(state.exportDirectory) ? "" : "\n\n实际导出目录：\n" + state.exportDirectory) +
                    (string.IsNullOrEmpty(state.exportError) ? "" : "\n导出失败：" + state.exportError) +
                    "\n\n对照不改变当前战斗。发送出征只投影战术及必要动作，保留真实出征属性、装备与账本。";
                comparisonText.text = state.comparisonText;
                comparisonRoot.gameObject.SetActive(state.isComparisonVisible);
            }
            finally { refreshing = false; }
        }

        private void SetPage(bool showBattle, bool pauseLeaving = true)
        {
            if (pauseLeaving && !showBattle) shared.Pause();
            battlePage = showBattle;
            editorRoot.gameObject.SetActive(!showBattle);
            battleRoot.gameObject.SetActive(showBattle);
            if (initialized) RefreshView();
        }

        private void OpenComparison()
        {
            if (!shared.Compare()) return;
            comparisonRoot.gameObject.SetActive(true);
            toolsRoot.gameObject.SetActive(false);
            RefreshView();
        }

        private void ResetWorkbench()
        {
            shared.Reset();
            toolsRoot.gameObject.SetActive(false);
            comparisonRoot.gameObject.SetActive(false);
            SetPage(false, false);
        }

        private static string CompactCaseTitle(string title)
        {
            if (string.IsNullOrEmpty(title)) return "选择战例";
            var separator = title.IndexOf('：');
            if (separator < 0) separator = title.IndexOf(':');
            return separator > 0 ? title.Substring(0, separator).Trim() : title;
        }

        // Keep the authored 860 logical-pixel workbench height at wide-screen CanvasScaler
        // ratios. This is a presentation transform, not a font-size reduction: at 1080p the
        // resulting physical 14px body is larger than at the 900p design reference.
        private void FitSurface()
        {
            if (surface == null) return;
            var rect = ((RectTransform)transform).rect;
            if (rect.width < 1 || rect.height < 1) return;
            var scale = Mathf.Min(1, rect.height / 860f);
            surface.anchorMin = surface.anchorMax = new Vector2(0, 1);
            surface.pivot = new Vector2(0, 1);
            surface.anchoredPosition = Vector2.zero;
            surface.localScale = new Vector3(scale, scale, 1);
            surface.sizeDelta = new Vector2(rect.width / scale, rect.height / scale);
        }

        private void OnRectTransformDimensionsChange() { layoutRefreshPending = true; }

        private void LateUpdate()
        {
            if (!layoutRefreshPending || !initialized || !gameObject.activeInHierarchy ||
                CanvasUpdateRegistry.IsRebuildingLayout() || CanvasUpdateRegistry.IsRebuildingGraphics()) return;
            layoutRefreshPending = false;
            FitSurface();
            RefreshView();
        }
    }
}
