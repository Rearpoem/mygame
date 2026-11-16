using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Prototype.BaseEconomy;
using Prototype.Combat;
using Prototype.Expedition;
using Prototype.Tactics;
using Prototype.UnityClient.Candidates.Common;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Ui = Prototype.UnityClient.PrototypeUiKit;

namespace Prototype.UnityClient
{
    /// <summary>Thin view/controller. All decisions and settlement stay in the domain sessions.</summary>
    public sealed class PrototypeHubController : MonoBehaviour
    {
        [SerializeField] private TMP_FontAsset uiFont;
        private bool initialized;
        private string combatDefaultJson;
        private string baseDefaultJson;
        private BaseScenario baseScenario;
        private decimal initialBaseCash;
        private RectTransform combatPanel;
        private RectTransform basePanel;
        private RectTransform expeditionPanel;
        private RectTransform tacticsPanel;
        private TMP_Text currentModule;
        private TMP_Text combatClock;
        private TMP_Text combatInstructions;
        private TMP_Text combatUnits;
        private TMP_Text combatFeedback;
        private TMP_Text combatLog;
        private TMP_Text baseClock;
        private TMP_Text baseCash;
        private TMP_Text baseResidents;
        private TMP_Text baseParameters;
        private TMP_Text baseFeedback;
        private TMP_Text baseLog;
        private ScrollRect combatScroll;
        private ScrollRect combatUnitsScroll;
        private ScrollRect baseScroll;
        private TMP_Text[] friendlyCells;
        private TMP_Text[] enemyCells;
        private Button[] mapCells;
        private TMP_Text[] mapLabels;
        private Button combatNav;
        private Button baseNav;
        private Button expeditionNav;
        private Button tacticsNav;
        private RectTransform pageRoot;
        private RectTransform headerRoot;
        private RectTransform navigationRoot;
        private CanvasScaler canvasScaler;

        public CombatSession CombatSession { get; private set; }
        public BaseSession BaseSession { get; private set; }
        public ExpeditionPanelController ExpeditionView { get; private set; }
        public TacticsPanelController TacticsView { get; private set; }
        public TacticsSharedController SharedTactics { get; private set; }
        public TacticsCandidateHost TacticsCandidates { get; private set; }
        public TMP_Dropdown TacticsCandidateDropdown { get; private set; }
        public TacticsWorkbenchSession TacticsSession { get { return TacticsView == null ? null : TacticsView.Workbench; } }
        public Button TacticsNavButton { get { return tacticsNav; } }
        public ExpeditionSliceSession ExpeditionSession { get { return ExpeditionView == null ? null : ExpeditionView.SliceSession; } }
        public CombatStepResult LastCombatStep { get; private set; }
        public bool IsInitialized { get { return initialized; } }
        public TMP_FontAsset UiFont { get { return uiFont; } }
        public string CurrentModule { get; private set; }
        public TMP_Text CombatLogText { get { return combatLog; } }
        public TMP_Text CombatUnitsText { get { return combatUnits; } }
        public ScrollRect CombatUnitsScroll { get { return combatUnitsScroll; } }
        public TMP_Text BaseLogText { get { return baseLog; } }
        public TMP_Text BaseCashText { get { return baseCash; } }
        public Button CombatStepButton { get; private set; }
        public Button CombatReplayButton { get; private set; }
        public Button CombatResetButton { get; private set; }
        public Button CombatDefaultsButton { get; private set; }
        public Button CombatHealUpButton { get; private set; }
        public Button CombatHealDownButton { get; private set; }
        public Button CombatApplyButton { get; private set; }
        public TMP_InputField HealingThresholdInput { get; private set; }
        public TMP_InputField AllyHpInput { get; private set; }
        public Button BaseRecruitButton { get; private set; }
        public Button BaseStepButton { get; private set; }
        public Button BaseRunToMealButton { get; private set; }
        public Button BaseResetButton { get; private set; }
        public Button BaseDefaultsButton { get; private set; }
        public Button BaseNearButton { get; private set; }
        public Button BaseFarButton { get; private set; }

        public void SetFontForScene(TMP_FontAsset font) { uiFont = font; }

        private void Start()
        {
            if (initialized) return;
            try { Initialize(uiFont); }
            catch (Exception ex)
            {
                Debug.LogException(ex, this);
                ShowStartupError(ex.Message);
            }
        }

        public void Initialize(TMP_FontAsset font, CombatScenario combat = null, BaseScenario economy = null)
        {
            if (initialized) return;
            uiFont = font != null ? font : TMP_Settings.defaultFontAsset;
            if (uiFont == null)
                throw new InvalidOperationException("未找到中文字体。请先运行 Prototype/Generate Hub。");
            combatDefaultJson = combat == null ? ReadScenario("Combat") : JsonUtility.ToJson(combat);
            baseDefaultJson = economy == null ? ReadScenario("BaseEconomy") : JsonUtility.ToJson(economy);
            CombatSession = new CombatSession(JsonUtility.FromJson<CombatScenario>(combatDefaultJson));
            baseScenario = JsonUtility.FromJson<BaseScenario>(baseDefaultJson);
            BaseSession = new BaseSession(baseScenario.Clone());
            initialBaseCash = Convert.ToDecimal(BaseSession.GetSnapshot().TotalCash, CultureInfo.InvariantCulture);
            BuildInterface();
            initialized = true;
            RefreshCombat(true);
            RefreshBase();
            ShowTactics();
        }

        private static string ReadScenario(string module)
        {
            var path = Path.Combine(Application.streamingAssetsPath, "Prototype", module, "default.json");
            if (!File.Exists(path)) throw new FileNotFoundException("缺少测试配置：" + path);
            return File.ReadAllText(path, Encoding.UTF8);
        }

        private RectTransform CreateCanvas()
        {
            var canvasRect = Ui.Rect("PrototypeCanvas", transform);
            var canvas = canvasRect.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 0;
            canvasScaler = canvasRect.gameObject.AddComponent<CanvasScaler>();
            canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasScaler.referenceResolution = new Vector2(1280, 900);
            canvasScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            canvasScaler.matchWidthOrHeight = .5f;
            canvasRect.gameObject.AddComponent<GraphicRaycaster>();
            if (EventSystem.current == null)
            {
                var events = new GameObject("PrototypeEventSystem", typeof(EventSystem));
                events.transform.SetParent(transform, false);
                events.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
            var background = Ui.Image("Backdrop", canvasRect, Ui.Background);
            Ui.Stretch(background.rectTransform);
            var root = Ui.Column("Page", background.transform, 10, 22);
            pageRoot = root;
            Ui.Stretch(root);
            return root;
        }

        private void BuildInterface()
        {
            var root = CreateCanvas();
            var header = Ui.Row("PersistentHeader", root, 20);
            headerRoot = header;
            Ui.FixedHeight(header.gameObject, 62);
            var titleBlock = Ui.Column("TitleBlock", header, 3);
            Ui.Flexible(titleBlock.gameObject);
            var title = Ui.Text("Title", titleBlock, uiFont, "条件指令 × 基地经营", 29);
            Ui.FixedHeight(title.gameObject, 36);
            var sub = Ui.Text("Subtitle", titleBlock, uiFont,
                "第四轮  /  法师技能库、MP 与群体状态效果", 16, Ui.Muted);
            Ui.FixedHeight(sub.gameObject, 23);
            currentModule = Ui.Text("CurrentModule", header, uiFont, "", 18, Ui.Accent);
            currentModule.alignment = TextAlignmentOptions.MidlineRight;
            Ui.Width(currentModule.gameObject, 290);

            var nav = Ui.Row("ModuleNavigation", root, 10);
            navigationRoot = nav;
            Ui.FixedHeight(nav.gameObject, 44);
            tacticsNav = Ui.Button("Nav_Tactics", nav, uiFont, "战术编排与战斗", ShowTactics, 208, true);
            combatNav = Ui.Button("Nav_Combat", nav, uiFont, "战斗试验", ShowCombat, 160, true);
            baseNav = Ui.Button("Nav_Base", nav, uiFont, "基地试验", ShowBase, 160, true);
            expeditionNav = Ui.Button("Nav_Expedition", nav, uiFont, "出征与备战", ShowExpedition, 174, true);
            TacticsCandidateDropdown = TacticsUiControls.Dropdown("Tactics_CandidateSelector", nav, uiFont, 200);
            var explanation = Ui.Text("IndependentClockHint", nav, uiFont,
                "切换保留进度 · 离开战术页自动暂停", 15, Ui.Muted);
            explanation.alignment = TextAlignmentOptions.MidlineLeft;
            explanation.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.Flexible(explanation.gameObject);

            var body = Ui.Rect("ModuleBody", root);
            Ui.Flexible(body.gameObject);
            tacticsPanel = Ui.Rect("TacticsPanel", body);
            Ui.Stretch(tacticsPanel);
            var legacyTactics = Ui.Column("TacticsLegacyPanel", tacticsPanel, 8, 14, Ui.Panel);
            Ui.Stretch(legacyTactics);
            combatPanel = Ui.Column("CombatPanel", body, 10, 14, Ui.Panel);
            Ui.Stretch(combatPanel);
            basePanel = Ui.Column("BasePanel", body, 10, 14, Ui.Panel);
            Ui.Stretch(basePanel);
            expeditionPanel = Ui.Column("ExpeditionPanel", body, 9, 14, Ui.Panel);
            Ui.Stretch(expeditionPanel);
            BuildCombatPanel();
            BuildBasePanel();
            ExpeditionView = expeditionPanel.gameObject.AddComponent<ExpeditionPanelController>();
            ExpeditionView.Initialize(uiFont);
            TacticsView = legacyTactics.gameObject.AddComponent<TacticsPanelController>();
            TacticsView.ExpeditionReceiver = ReceiveTacticsForExpedition;
            TacticsView.Initialize(uiFont);
            SharedTactics = tacticsPanel.gameObject.AddComponent<TacticsSharedController>();
            SharedTactics.Initialize(TacticsView);
            TacticsCandidates = tacticsPanel.gameObject.AddComponent<TacticsCandidateHost>();
            TacticsCandidates.CandidateChanged += _ => ApplyShellLayout();
            TacticsCandidates.Initialize(uiFont, SharedTactics, legacyTactics, TacticsCandidateDropdown);
        }

        private CombatEditResult ReceiveTacticsForExpedition(CombatScenario scenario)
        {
            if (ExpeditionView == null)
                return new CombatEditResult { success = false, message = "出征入口尚未准备好，战术草稿保持不变。" };
            CombatEditResult result = ExpeditionView.ReceiveTactics(scenario);
            if (result.success) ShowExpedition();
            return result;
        }

        private void BuildCombatPanel()
        {
            var actions = Ui.Row("CombatActions", combatPanel);
            Ui.FixedHeight(actions.gameObject, 43);
            CombatStepButton = Ui.Button("Combat_Step", actions, uiFont, "推进一个行动", StepCombat, 138, true);
            CombatReplayButton = Ui.Button("Combat_ReplayFirst", actions, uiFont, "重放首步", ReplayFirstCombat, 116);
            CombatResetButton = Ui.Button("Combat_Reset", actions, uiFont, "重置当前选择", ResetCombat, 142);
            CombatDefaultsButton = Ui.Button("Combat_Defaults", actions, uiFont, "恢复默认", RestoreCombatDefaults, 116);
            combatClock = Ui.Text("CombatClock", actions, uiFont, "", 17, Ui.Accent);
            combatClock.alignment = TextAlignmentOptions.MidlineRight;
            Ui.Flexible(combatClock.gameObject);

            var settings = Ui.Row("CombatSettings", combatPanel, 8);
            Ui.FixedHeight(settings.gameObject, 42);
            Ui.Label("ThresholdLabel", settings, uiFont, "急救 HP 阈值 %", 145);
            HealingThresholdInput = Ui.Input("Combat_HealingThreshold", settings, uiFont, 76);
            Ui.Label("AllyHpLabel", settings, uiFont, "队友开局 HP", 124);
            AllyHpInput = Ui.Input("Combat_AllyHp", settings, uiFont, 76);
            AllyHpInput.contentType = TMP_InputField.ContentType.IntegerNumber;
            CombatApplyButton = Ui.Button("Combat_ApplySettings", settings, uiFont, "应用并重置", ApplyCombatSettings, 132);
            var hint = Ui.Text("CombatSettingsHint", settings, uiFont, "参数来自测试 JSON", 15, Ui.Muted);
            hint.alignment = TextAlignmentOptions.MidlineRight;
            Ui.Flexible(hint.gameObject);

            var details = Ui.Row("CombatDetails", combatPanel, 12);
            Ui.Flexible(details.gameObject);
            var state = Ui.Column("CombatState", details, 8, 10, Ui.Card);
            Ui.Flexible(state.gameObject, 1, 1);
            var instructionHeader = Ui.Row("InstructionsHeading", state);
            Ui.FixedHeight(instructionHeader.gameObject, 35);
            var instructionTitle = Ui.Text("InstructionTitle", instructionHeader, uiFont, "法师 · 指令优先级", 18, Ui.Accent);
            instructionTitle.alignment = TextAlignmentOptions.MidlineLeft;
            Ui.Flexible(instructionTitle.gameObject);
            CombatHealUpButton = Ui.Button("Combat_HealUp", instructionHeader, uiFont, "急救 ↑", () => MoveHealing(-1), 86);
            CombatHealDownButton = Ui.Button("Combat_HealDown", instructionHeader, uiFont, "急救 ↓", () => MoveHealing(1), 86);
            combatInstructions = Ui.Text("CombatInstructions", state, uiFont, "", 17);
            Ui.FixedHeight(combatInstructions.gameObject, 136);
            var formationHeader = Ui.Text("FormationHeading", state, uiFont, "实时站位  /  每方独立 3 × 3", 17, Ui.Accent);
            Ui.FixedHeight(formationHeader.gameObject, 27);
            var formations = Ui.Row("Formations", state, 9);
            Ui.FixedHeight(formations.gameObject, 164);
            friendlyCells = BuildFormation("FriendlyFormation", "己方  /  上前排 · 下后排", formations);
            enemyCells = BuildFormation("EnemyFormation", "敌方  /  上前排 · 下后排", formations);
            combatUnits = Ui.ScrollText("CombatUnits", state, uiFont, out combatUnitsScroll, 16);
            combatUnits.textWrappingMode = TextWrappingModes.NoWrap;
            // Keep the SDF glyph fringe inside the scroll viewport's mask on every edge.
            combatUnits.margin = new Vector4(4, 4, 4, 4);

            var logColumn = Ui.Column("CombatLogColumn", details, 8, 10, Ui.Card);
            Ui.Flexible(logColumn.gameObject, 1, 1);
            var logHeading = Ui.Text("CombatLogHeading", logColumn, uiFont, "行动轨迹 · 候选、跳过与最终效果", 18, Ui.Accent);
            Ui.FixedHeight(logHeading.gameObject, 30);
            combatLog = Ui.ScrollText("CombatLog", logColumn, uiFont, out combatScroll, 17);
            var scrollHint = Ui.Text("CombatLogHint", logColumn, uiFont, "鼠标滚轮查看完整日志；重放首步使用当前指令与开局。", 14, Ui.Muted);
            Ui.FixedHeight(scrollHint.gameObject, 24);

            combatFeedback = Ui.Text("CombatFeedback", combatPanel, uiFont, "", 16, Ui.Good);
            Ui.FixedHeight(combatFeedback.gameObject, 46);
        }

        private TMP_Text[] BuildFormation(string name, string heading, Transform parent)
        {
            var column = Ui.Column(name, parent, 4);
            Ui.Flexible(column.gameObject);
            var title = Ui.Text(name + "Title", column, uiFont, heading, 14, Ui.Muted);
            Ui.FixedHeight(title.gameObject, 22);
            var board = Ui.Rect(name + "Grid", column);
            Ui.Flexible(board.gameObject);
            var grid = board.gameObject.AddComponent<GridLayoutGroup>();
            grid.spacing = new Vector2(3, 3);
            var sizer = board.gameObject.AddComponent<PrototypeMapGridSizer>();
            sizer.columns = 3;
            sizer.rows = 3;
            var cells = new TMP_Text[9];
            for (var i = 0; i < cells.Length; i++)
            {
                var cell = Ui.Image(name + "Cell_" + i, board, Ui.Background);
                var label = Ui.Text("Occupants", cell.transform, uiFont, "", 15);
                Ui.Stretch(label.rectTransform, 2, 1, 2, 1);
                label.alignment = TextAlignmentOptions.Center;
                label.enableAutoSizing = true;
                label.fontSizeMin = 11;
                label.fontSizeMax = 15;
                cells[i] = label;
            }
            return cells;
        }

        private void BuildBasePanel()
        {
            var actions = Ui.Row("BaseActions", basePanel);
            Ui.FixedHeight(actions.gameObject, 43);
            BaseRecruitButton = Ui.Button("Base_Recruit", actions, uiFont, "招募厨师", RecruitCook, 120, true);
            BaseStepButton = Ui.Button("Base_Step", actions, uiFont, "推进一步", StepBase, 112);
            BaseRunToMealButton = Ui.Button("Base_RunToMeal", actions, uiFont, "跑到首餐", RunBaseToMeal, 112);
            BaseResetButton = Ui.Button("Base_Reset", actions, uiFont, "重置同一开局", ResetBase, 142);
            BaseDefaultsButton = Ui.Button("Base_Defaults", actions, uiFont, "恢复默认", RestoreBaseDefaults, 116);
            baseClock = Ui.Text("BaseClock", actions, uiFont, "", 17, Ui.Accent);
            baseClock.alignment = TextAlignmentOptions.MidlineRight;
            Ui.Flexible(baseClock.gameObject);

            var locations = Ui.Row("KitchenLocationChoices", basePanel);
            Ui.FixedHeight(locations.gameObject, 40);
            Ui.Label("KitchenChoiceLabel", locations, uiFont, "预置厨房开局", 133);
            BaseNearButton = Ui.Button("Base_NearKitchen", locations, uiFont, "近处", () => SelectKitchen(baseScenario.nearKitchenPosition), 128);
            BaseFarButton = Ui.Button("Base_FarKitchen", locations, uiFont, "远处", () => SelectKitchen(baseScenario.farKitchenPosition), 128);
            var locationHint = Ui.Text("KitchenLocationHint", locations, uiFont,
                "改位置即重开；这是预置设施对照，建设费用另做。", 15, Ui.Muted);
            locationHint.alignment = TextAlignmentOptions.MidlineLeft;
            Ui.Flexible(locationHint.gameObject);

            baseCash = Ui.Text("BaseCash", basePanel, uiFont, "", 20, Ui.Good);
            Ui.FixedHeight(baseCash.gameObject, 56);

            var details = Ui.Row("BaseDetails", basePanel, 12);
            Ui.Flexible(details.gameObject);
            var state = Ui.Column("BaseState", details, 8, 10, Ui.Card);
            Ui.Flexible(state.gameObject);
            var mapHint = Ui.Text("MapHeading", state, uiFont, "俯视地图 · 点击空格选择预置厨房的开局位置", 17, Ui.Accent);
            Ui.FixedHeight(mapHint.gameObject, 28);
            var board = Ui.Rect("BaseMap", state);
            Ui.FixedHeight(board.gameObject, 232);
            var grid = board.gameObject.AddComponent<GridLayoutGroup>();
            grid.spacing = new Vector2(3, 3);
            var sizer = board.gameObject.AddComponent<PrototypeMapGridSizer>();
            sizer.columns = baseScenario.mapWidth;
            sizer.rows = baseScenario.mapHeight;
            mapCells = new Button[baseScenario.mapWidth * baseScenario.mapHeight];
            mapLabels = new TMP_Text[mapCells.Length];
            for (var y = baseScenario.mapHeight - 1; y >= 0; y--)
            for (var x = 0; x < baseScenario.mapWidth; x++)
            {
                var px = x;
                var py = y;
                var index = y * baseScenario.mapWidth + x;
                var button = Ui.Button("Base_Map_" + x + "_" + y, board, uiFont, "", () => SelectKitchen(new GridPosition(px, py)));
                var element = button.GetComponent<LayoutElement>();
                if (element != null) Destroy(element);
                var label = button.GetComponentInChildren<TextMeshProUGUI>();
                label.fontSizeMin = 10;
                label.fontSizeMax = 14;
                label.textWrappingMode = TextWrappingModes.Normal;
                mapCells[index] = button;
                mapLabels[index] = label;
            }
            baseResidents = Ui.ScrollText("BaseResidents", state, uiFont, out _, 16);

            var logColumn = Ui.Column("BaseLogColumn", details, 8, 10, Ui.Card);
            Ui.Flexible(logColumn.gameObject);
            var ledgerHeading = Ui.Text("LedgerHeading", logColumn, uiFont, "账本 · 每笔转账双方与物品归属", 18, Ui.Accent);
            Ui.FixedHeight(ledgerHeading.gameObject, 30);
            baseLog = Ui.ScrollText("BaseLedger", logColumn, uiFont, out baseScroll, 16);
            baseParameters = Ui.Text("BaseTestParameters", logColumn, uiFont, "", 14, Ui.Muted);
            Ui.FixedHeight(baseParameters.gameObject, 103);

            baseFeedback = Ui.Text("BaseFeedback", basePanel, uiFont, "", 16, Ui.Good);
            Ui.FixedHeight(baseFeedback.gameObject, 43);
        }

        public void ShowCombat()
        {
            if (combatPanel == null) return;
            combatPanel.gameObject.SetActive(true);
            basePanel.gameObject.SetActive(false);
            expeditionPanel.gameObject.SetActive(false);
            tacticsPanel.gameObject.SetActive(false);
            CurrentModule = "Combat";
            currentModule.text = "当前入口：战斗  /  WAIT 时间轴";
            combatNav.interactable = false;
            baseNav.interactable = true;
            expeditionNav.interactable = true;
            tacticsNav.interactable = true;
            ApplyShellLayout();
        }

        public void ShowBase()
        {
            if (basePanel == null) return;
            combatPanel.gameObject.SetActive(false);
            basePanel.gameObject.SetActive(true);
            expeditionPanel.gameObject.SetActive(false);
            tacticsPanel.gameObject.SetActive(false);
            CurrentModule = "BaseEconomy";
            currentModule.text = "当前入口：基地  /  独立 tick";
            combatNav.interactable = true;
            baseNav.interactable = false;
            expeditionNav.interactable = true;
            tacticsNav.interactable = true;
            ApplyShellLayout();
        }

        public void ShowExpedition()
        {
            if (expeditionPanel == null) return;
            combatPanel.gameObject.SetActive(false);
            basePanel.gameObject.SetActive(false);
            expeditionPanel.gameObject.SetActive(true);
            tacticsPanel.gameObject.SetActive(false);
            CurrentModule = "Expedition";
            currentModule.text = "当前入口：出征与备战";
            combatNav.interactable = true;
            baseNav.interactable = true;
            expeditionNav.interactable = false;
            tacticsNav.interactable = true;
            ApplyShellLayout();
        }

        public void ShowTactics()
        {
            if (tacticsPanel == null) return;
            combatPanel.gameObject.SetActive(false);
            basePanel.gameObject.SetActive(false);
            expeditionPanel.gameObject.SetActive(false);
            tacticsPanel.gameObject.SetActive(true);
            CurrentModule = "Tactics";
            currentModule.text = "当前入口：战术编排与战斗";
            combatNav.interactable = true;
            baseNav.interactable = true;
            expeditionNav.interactable = true;
            tacticsNav.interactable = false;
            ApplyShellLayout();
        }

        private void ApplyShellLayout()
        {
            if (pageRoot == null || headerRoot == null || navigationRoot == null) return;
            var modern = CurrentModule == "Tactics" && TacticsCandidates != null && TacticsCandidates.IsModernCandidate;
            headerRoot.gameObject.SetActive(!modern);
            var pageLayout = pageRoot.GetComponent<VerticalLayoutGroup>();
            pageLayout.padding = modern ? new RectOffset(0, 0, 0, 0) : new RectOffset(22, 22, 22, 22);
            pageLayout.spacing = modern ? 0 : 10;
            var navLayout = navigationRoot.GetComponent<HorizontalLayoutGroup>();
            navLayout.padding = modern ? new RectOffset(16, 16, 0, 0) : new RectOffset(0, 0, 0, 0);
            Ui.FixedHeight(navigationRoot.gameObject, modern ? 40 : 44);
            if (TacticsCandidateDropdown != null) TacticsCandidateDropdown.gameObject.SetActive(CurrentModule == "Tactics");
            ApplyCanvasMatch();
        }

        private void LateUpdate()
        {
            // Presentation scaling only. The shared controller remains the sole playback clock.
            ApplyCanvasMatch();
        }

        private void ApplyCanvasMatch()
        {
            if (canvasScaler == null) return;
            var opus = CurrentModule == "Tactics" && TacticsCandidates != null
                && TacticsCandidates.CurrentCandidateId == "opus";
            var match = opus ? (Screen.width / (float)Math.Max(1, Screen.height) < 1280f / 900f ? 0f : 1f) : .5f;
            if (!Mathf.Approximately(canvasScaler.matchWidthOrHeight, match))
                canvasScaler.matchWidthOrHeight = match;
        }

        private int HealingInstructionIndex()
        {
            var mage = CombatSession.GetScenario().units.FirstOrDefault(unit => unit.id == "mage");
            return mage == null ? -1 : Array.FindIndex(mage.instructions, instruction => instruction.actionId == "first_aid");
        }

        private void MoveHealing(int direction)
        {
            var from = HealingInstructionIndex();
            if (from < 0) return;
            var result = CombatSession.MoveInstruction("mage", from, from + direction);
            if (result.success)
            {
                CombatSession.Reset();
                LastCombatStep = null;
                RefreshCombat(true);
                SetCombatFeedback("已调整急救优先级，并按当前选择重置。点「重放首步」查看差异。", false);
            }
            else SetCombatFeedback(result.message, true);
        }

        private void ApplyCombatSettings()
        {
            if (!double.TryParse(HealingThresholdInput.text, NumberStyles.Float, CultureInfo.InvariantCulture, out var threshold)
                || double.IsNaN(threshold) || double.IsInfinity(threshold) || threshold < 0 || threshold > 100)
            {
                SetCombatFeedback("治疗阈值请输入 0 至 100 的百分比。", true);
                return;
            }
            var ally = CombatSession.GetSnapshot().units.FirstOrDefault(unit => unit.id == "ally");
            if (ally == null || !int.TryParse(AllyHpInput.text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hp)
                || hp < 0 || hp > ally.maxHp)
            {
                SetCombatFeedback("队友开局 HP 请输入 0 至 " + (ally == null ? "最大 HP" : ally.maxHp.ToString()) + " 的整数。", true);
                return;
            }
            var instruction = HealingInstructionIndex();
            if (instruction < 0)
            {
                SetCombatFeedback("当前配置没有可编辑的急救指令。", true);
                return;
            }
            var conditionEdit = CombatSession.SetConditionThreshold("mage", instruction, 0, threshold / 100);
            if (!conditionEdit.success) { SetCombatFeedback(conditionEdit.message, true); return; }
            var hpEdit = CombatSession.SetInitialHp("ally", hp);
            if (!hpEdit.success) { SetCombatFeedback(hpEdit.message, true); return; }
            CombatSession.Reset();
            LastCombatStep = null;
            RefreshCombat(true);
            SetCombatFeedback("已应用阈值与队友开局 HP；重置保留这些选择。", false);
        }

        private void StepCombat()
        {
            LastCombatStep = CombatSession.Step();
            RefreshCombat(false);
            var step = LastCombatStep;
            if (string.Equals(step.status, "Blocked", StringComparison.OrdinalIgnoreCase))
            {
                SetCombatFeedback("时间轴已阻塞：" + step.message + "；查看日志中的逐条尝试原因。", true);
                return;
            }
            var action = string.IsNullOrEmpty(step.actionName) ? step.message : step.actorName + " → " + step.actionName + " → " + step.targetName;
            SetCombatFeedback(action + "\n基础 WAIT " + Number(step.baseWait) + " × 敏捷系数 "
                + Number(step.agilityFactor) + " = " + Number(step.actualWait) + "；下次行动 t=" + Number(step.nextActionTime),
                string.Equals(step.status, "Blocked", StringComparison.OrdinalIgnoreCase));
        }

        private void ReplayFirstCombat()
        {
            CombatSession.Reset();
            LastCombatStep = null;
            StepCombat();
        }

        private void ResetCombat()
        {
            CombatSession.Reset();
            LastCombatStep = null;
            RefreshCombat(true);
            SetCombatFeedback("已回到开局；保留当前指令顺序、治疗阈值与队友开局 HP。", false);
        }

        private void RestoreCombatDefaults()
        {
            CombatSession = new CombatSession(JsonUtility.FromJson<CombatScenario>(combatDefaultJson));
            LastCombatStep = null;
            RefreshCombat(true);
            SetCombatFeedback("已恢复启动时加载的测试 JSON 默认配置。", false);
        }

        private void RefreshCombat(bool refreshInputs)
        {
            var snapshot = CombatSession.GetSnapshot();
            var next = snapshot.units.FirstOrDefault(unit => unit.id == snapshot.nextActorId);
            combatClock.text = "t = " + Number(snapshot.time) + "  /  行动 " + snapshot.completedStepCount
                + "\n下一位：" + (next == null ? "无" : next.name);
            var mage = snapshot.units.FirstOrDefault(unit => unit.id == "mage");
            if (mage != null)
            {
                var instructions = new StringBuilder();
                instructions.Append("智力 ").Append(mage.intelligence).Append("  ·  指令容量 ").Append(mage.instructionCapacity)
                    .Append("  ·  每条条件项容量 ").Append(mage.conditionTermCapacity).AppendLine();
                for (var i = 0; i < mage.instructions.Length; i++)
                    instructions.Append(i + 1).Append(". ").Append(mage.instructions[i].display)
                        .Append(mage.instructions[i].capacityAllowed ? "" : " [容量不足]").AppendLine();
                combatInstructions.text = instructions.ToString();
                var healingIndex = HealingInstructionIndex();
                CombatHealUpButton.interactable = healingIndex > 0;
                CombatHealDownButton.interactable = healingIndex >= 0 && healingIndex < mage.instructions.Length - 1;
                HealingThresholdInput.interactable = healingIndex >= 0;
                CombatApplyButton.interactable = healingIndex >= 0;
            }
            else combatInstructions.text = "测试配置中未找到法师 mage。";

            for (var i = 0; i < 9; i++)
            {
                friendlyCells[i].text = "·";
                enemyCells[i].text = "·";
                friendlyCells[i].color = Ui.Muted;
                enemyCells[i].color = Ui.Muted;
            }
            var units = new StringBuilder();
            for (var unitIndex = 0; unitIndex < snapshot.units.Length; unitIndex++)
            {
                var unit = snapshot.units[unitIndex];
                var friendly = mage != null && unit.team == mage.team;
                if (unit.row >= 0 && unit.row < 3 && unit.column >= 0 && unit.column < 3)
                {
                    var cell = (friendly ? friendlyCells : enemyCells)[unit.row * 3 + unit.column];
                    cell.text = unit.name + (unit.hp <= 0 ? "\n倒下" : "");
                    cell.color = friendly ? Ui.Good : Ui.Bad;
                }
                units.Append(unit.name).Append("  HP ").Append(unit.hp).Append('/').Append(unit.maxHp)
                    .Append("  MP ").Append(unit.mp).Append('/').Append(unit.maxMp)
                    .Append("  WAIT→").Append(Number(unit.nextActionTime))
                    .Append("  容量").Append(unit.instructionCapacity).Append("  智力").Append(unit.intelligence);
                if (unitIndex < snapshot.units.Length - 1) units.AppendLine();
            }
            combatUnits.text = units.ToString();
            combatUnitsScroll.verticalNormalizedPosition = 1;
            combatLog.text = snapshot.completedStepCount == 0
                ? "尚未执行行动。\n\n先预测法师会救谁或攻击谁，再点「推进一个行动」。\n\n每条指令依次读取：\n条件表达式｜对象范围｜选择方式｜行动\n\n上、下调整急救优先级后重放首步，对比最终对象与效果。"
                : CombatSession.ExportLogText();
            if (refreshInputs)
            {
                var scenario = CombatSession.GetScenario();
                var mageConfig = scenario.units.FirstOrDefault(unit => unit.id == "mage");
                var firstAid = mageConfig == null ? null : mageConfig.instructions.FirstOrDefault(instruction => instruction.actionId == "first_aid");
                if (firstAid != null && firstAid.condition.terms.Length > 0)
                    HealingThresholdInput.SetTextWithoutNotify(Number(firstAid.condition.terms[0].threshold * 100));
                var allyConfig = scenario.units.FirstOrDefault(unit => unit.id == "ally");
                if (allyConfig != null) AllyHpInput.SetTextWithoutNotify(allyConfig.hp.ToString(CultureInfo.InvariantCulture));
            }
            if (snapshot.blocked) SetCombatFeedback("时间轴已阻塞：" + snapshot.blockReason, true);
            else if (snapshot.completedStepCount == 0) SetCombatFeedback("测试参数只用于观察条件指令，不表示已完成平衡。", false);
            combatScroll.verticalNormalizedPosition = 0;
        }

        private void SetCombatFeedback(string message, bool error)
        {
            combatFeedback.text = message;
            combatFeedback.color = error ? Ui.Bad : Ui.Good;
        }

        private void SelectKitchen(GridPosition position)
        {
            if (!baseScenario.IsKitchenPositionAvailable(position))
            {
                SetBaseFeedback("请选择地图内的空格；供粮者和顾客的开局格不能放厨房。", true);
                return;
            }
            baseScenario = baseScenario.WithKitchen(position);
            BaseSession = new BaseSession(baseScenario.Clone());
            RefreshBase();
            SetBaseFeedback("已改为预置厨房 " + Position(position) + " 的新开局；请招募厨师。位置对照未收建设费。", false);
        }

        private void RecruitCook()
        {
            var result = BaseSession.RecruitCook();
            RefreshBase();
            SetBaseFeedback(result.Success ? "招募款从公库转入厨师个人钱包；这笔转账不增加总现金。" : result.Reason, !result.Success);
        }

        private void StepBase()
        {
            BaseSession.Step();
            RefreshBase();
        }

        private void RunBaseToMeal()
        {
            BaseSession.RunUntilFirstMeal(baseScenario.runUntilFirstMealMaxTicks);
            RefreshBase();
            if (!BaseSession.GetSnapshot().IsComplete)
                SetBaseFeedback("尚未完成首餐；请查看工作/阻塞原因。运行上限来自测试配置。", true);
        }

        private void ResetBase()
        {
            BaseSession.Reset();
            RefreshBase();
            SetBaseFeedback("已重置同一开局，保留厨房位置 " + Position(baseScenario.kitchenPosition) + "。", false);
        }

        private void RestoreBaseDefaults()
        {
            baseScenario = JsonUtility.FromJson<BaseScenario>(baseDefaultJson);
            BaseSession = new BaseSession(baseScenario.Clone());
            RefreshBase();
            SetBaseFeedback("已恢复启动时加载的测试 JSON 默认开局。", false);
        }

        private void RefreshBase()
        {
            var snapshot = BaseSession.GetSnapshot();
            baseClock.text = "tick = " + snapshot.Tick + (snapshot.IsComplete ? "\n首餐：tick " + snapshot.FirstMealTick : "\n首餐尚未售出");
            var total = Convert.ToDecimal(snapshot.TotalCash, CultureInfo.InvariantCulture);
            baseCash.text = "公库 " + snapshot.TreasuryCash + "    供粮者 " + snapshot.SupplierCash
                + "    厨师 " + snapshot.CookCash + "    顾客 " + snapshot.CustomerCash
                + "\n现金合计 " + snapshot.TotalCash + "  /  开局基准 " + initialBaseCash + (total == initialBaseCash ? "  ·  守恒" : "  ·  合计已变化");
            baseCash.color = total == initialBaseCash ? Ui.Good : Ui.Bad;
            BaseRecruitButton.interactable = !snapshot.IsCookRecruited;
            BaseStepButton.interactable = snapshot.IsCookRecruited && !snapshot.IsComplete;
            BaseRunToMealButton.interactable = snapshot.IsCookRecruited && !snapshot.IsComplete;
            BaseNearButton.GetComponentInChildren<TMP_Text>().text = "近处 " + Position(baseScenario.nearKitchenPosition);
            BaseFarButton.GetComponentInChildren<TMP_Text>().text = "远处 " + Position(baseScenario.farKitchenPosition);
            BaseNearButton.interactable = !SamePosition(snapshot.KitchenPosition, baseScenario.nearKitchenPosition);
            BaseFarButton.interactable = !SamePosition(snapshot.KitchenPosition, baseScenario.farKitchenPosition);
            for (var y = 0; y < baseScenario.mapHeight; y++)
            for (var x = 0; x < baseScenario.mapWidth; x++)
            {
                var index = y * baseScenario.mapWidth + x;
                var pos = new GridPosition(x, y);
                var label = new StringBuilder();
                var occupied = false;
                if (SamePosition(pos, snapshot.KitchenPosition)) { label.Append("厨房"); occupied = true; }
                if (SamePosition(pos, snapshot.SupplierPosition)) { label.Append(label.Length > 0 ? "\n供粮" : "供粮"); occupied = true; }
                if (snapshot.IsCookRecruited && SamePosition(pos, snapshot.CookPosition)) { label.Append(label.Length > 0 ? "\n厨师" : "厨师"); occupied = true; }
                if (SamePosition(pos, snapshot.CustomerPosition)) { label.Append(label.Length > 0 ? "\n顾客" : "顾客"); occupied = true; }
                if (label.Length == 0) label.Append(x).Append(',').Append(y);
                mapLabels[index].text = label.ToString();
                mapLabels[index].color = occupied ? Ui.Ink : Ui.Muted;
                mapCells[index].targetGraphic.color = SamePosition(pos, snapshot.KitchenPosition)
                    ? new Color32(109, 81, 39, 255) : occupied ? new Color32(49, 82, 78, 255) : Ui.Background;
                mapCells[index].interactable = !occupied && baseScenario.IsKitchenPositionAvailable(pos);
            }
            baseResidents.text = "三人位置与工作\n"
                + "供粮者 " + Position(snapshot.SupplierPosition) + " · " + snapshot.SupplierActivity
                + "\n  原因：" + Reason(snapshot.SupplierBlockedReason)
                + "\n厨师 " + (snapshot.IsCookRecruited ? Position(snapshot.CookPosition) : "待招募") + " · " + snapshot.CookActivity
                + "\n  原因：" + Reason(snapshot.CookBlockedReason)
                + "\n  已走 " + snapshot.CookTravelTiles + " 格 / 加工剩 " + snapshot.ProductionTicksRemaining + " tick"
                + "\n顾客 " + Position(snapshot.CustomerPosition) + " · " + snapshot.CustomerActivity
                + "\n  原因：" + Reason(snapshot.CustomerBlockedReason)
                + "\n  已走 " + snapshot.CustomerTravelTiles + " 格"
                + "\n\n物资归属\n供粮者：粮 " + snapshot.SupplierGrain
                + "  /  厨师：粮 " + snapshot.CookGrain + "、餐 " + snapshot.CookMeals
                + "\n顾客已消费：餐 " + snapshot.MealsConsumed;
            baseLog.text = BaseSession.ExportLedgerText();
            if (string.IsNullOrWhiteSpace(baseLog.text)) baseLog.text = "尚无交易。先招募厨师，再推进或运行到首餐。";
            baseParameters.text = "临时测试值：招募 " + baseScenario.recruitmentGrant + " / 粮价 " + baseScenario.grainUnitPrice
                + " / 餐价 " + baseScenario.mealUnitPrice + "\n移动 " + baseScenario.movementTicksPerTile
                + " tick/格；加工 " + baseScenario.productionTicks + " tick"
                + "\n税 " + baseScenario.taxAmount + " / 福利 " + baseScenario.welfareAmount + " / 外部现金流 " + baseScenario.externalCashFlow
                + "\n本轮只验证招募、私人物流生产与首餐交易。";
            if (snapshot.IsComplete)
                SetBaseFeedback("首餐于 tick " + snapshot.FirstMealTick + " 售出并消费；账本保留完整付款双方与事后余额。", false);
            else if (!snapshot.IsCookRecruited)
                SetBaseFeedback("预置厨房 " + Position(snapshot.KitchenPosition) + "；先招募厨师。此时推进不会让时钟空转。", false);
            else SetBaseFeedback("居民按模型自行取料、加工和购餐；查看地图与账本追踪过程。", false);
            baseScroll.verticalNormalizedPosition = 0;
        }

        private void SetBaseFeedback(string message, bool error)
        {
            baseFeedback.text = message;
            baseFeedback.color = error ? Ui.Bad : Ui.Good;
        }

        private static string Reason(string reason) { return string.IsNullOrWhiteSpace(reason) ? "无阻塞" : reason; }
        private static bool SamePosition(GridPosition a, GridPosition b) { return a.x == b.x && a.y == b.y; }
        private static string Position(GridPosition position) { return "(" + position.x + "," + position.y + ")"; }
        private static string Number(double number) { return number.ToString("0.##", CultureInfo.InvariantCulture); }

        private void ShowStartupError(string message)
        {
            if (transform.Find("PrototypeCanvas") != null) return;
            var root = CreateCanvas();
            var heading = Ui.Text("StartupErrorTitle", root, uiFont, "原型加载失败", 30, Ui.Bad);
            Ui.FixedHeight(heading.gameObject, 55);
            Ui.Text("StartupError", root, uiFont, message + "\n\n请运行 Prototype/Generate Hub 并检查各入口的 StreamingAssets 测试 JSON。", 21);
        }
    }
}
