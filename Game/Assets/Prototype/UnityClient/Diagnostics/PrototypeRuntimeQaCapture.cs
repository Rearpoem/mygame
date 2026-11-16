// Opt-in development Player QA, integrated by the shared owner; no flag means zero actions.
// Authored work source is retained in work/ui_candidates_20261115/runtime_qa.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Prototype.Combat;
using Prototype.UnityClient.Candidates.Argon;
using Prototype.UnityClient.Candidates.Common;
using Prototype.UnityClient.Candidates.Hydrogen;

using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Prototype.UnityClient.Diagnostics
{
    /// <summary>Opt-in development-player evidence capture, never a second simulator or clock.</summary>
    public sealed class PrototypeRuntimeQaCapture : MonoBehaviour
    {
        private static readonly string[] CandidateIds = { "argon", "hydrogen" };
        private readonly List<CaptureRecord> captures = new List<CaptureRecord>();
        private readonly List<OperationRecord> operations = new List<OperationRecord>();
        private readonly List<string> runtimeErrors = new List<string>();
        private PrototypeHubController hub;
        private TacticsSharedController shared;
        private string runDirectory;
        private string runId;
        private string sourceJsonPath;
        private string sourceJsonHash;
        private string expectedBuildGuid;
        private bool quitWhenDone;
        private int requestedWidth;
        private int requestedHeight;
        private Manifest manifest;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var args = Environment.GetCommandLineArgs();
            if (!args.Contains("--prototype-runtime-qa")) return;
            // Normal players do not create this component, change settings, write files or quit.
            if (!Debug.isDebugBuild || Application.isEditor || Application.isBatchMode)
            {
                Debug.LogError("Runtime QA requires an actual development Player with graphics; Editor/batch mode is not evidence.");
                if (!Application.isEditor && args.Contains("--prototype-qa-quit")) Application.Quit(2);
                return;
            }
            var root = new GameObject("PrototypeRuntimeQa_ExplicitFlagOnly");
            DontDestroyOnLoad(root);
            root.AddComponent<PrototypeRuntimeQaCapture>();
        }

        private IEnumerator Start()
        {
            // Flatten nested iterators so failures after a yield cannot masquerade as a completed run.
            var stack = new Stack<IEnumerator>();
            stack.Push(Run());
            while (stack.Count > 0)
            {
                object current = null;
                bool next = false;
                Exception failure = null;
                try { next = stack.Peek().MoveNext(); if (next) current = stack.Peek().Current; }
                catch (Exception exception) { failure = exception; }
                if (failure != null) { FinishFailure(failure); yield break; }
                if (!next) { stack.Pop(); continue; }
                var nested = current as IEnumerator;
                if (nested != null) { stack.Push(nested); continue; }
                yield return current;
            }
            manifest.status = "logic_checked_frames_captured_visual_review_pending";
            manifest.completedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            manifest.captures = captures.ToArray();
            manifest.operations = operations.ToArray();
            manifest.runtimeErrors = runtimeErrors.ToArray();
            if (runtimeErrors.Count > 0) { FinishFailure(new InvalidOperationException("Player emitted runtime errors; see runtimeErrors.")); yield break; }
            SaveManifest();
            if (quitWhenDone) Application.Quit(0);
        }

        private IEnumerator Run()
        {
            var args = Environment.GetCommandLineArgs();
            quitWhenDone = args.Contains("--prototype-qa-quit");
            runId = Required(args, "--prototype-qa-run-id");
            Guid parsedId;
            Require(Guid.TryParseExact(runId, "N", out parsedId), "QA run ID must be a new GUID in N format.");
            var outputRoot = Required(args, "--prototype-qa-output");
            Require(Path.IsPathRooted(outputRoot), "QA output root must be absolute.");
            runDirectory = Path.Combine(Path.GetFullPath(outputRoot), runId);
            Require(!Directory.Exists(runDirectory), "QA never overwrites a previous run directory.");
            Directory.CreateDirectory(runDirectory);
            expectedBuildGuid = Optional(args, "--prototype-qa-build-guid");
            var size = Required(args, "--prototype-qa-size").Split('x');
            Require(size.Length == 2 && int.TryParse(size[0], out requestedWidth) && int.TryParse(size[1], out requestedHeight), "QA size must be WIDTHxHEIGHT.");
            Require((requestedWidth == 1280 && requestedHeight == 900) || (requestedWidth == 1920 && requestedHeight == 1080), "This QA protocol has only the two authorized comparison resolutions.");
            Require(!string.IsNullOrEmpty(Application.buildGUID), "The actual new Player must expose its real build GUID.");
            Require(string.IsNullOrEmpty(expectedBuildGuid) || Application.buildGUID == expectedBuildGuid, "The launched Player differs from the optional declared build GUID.");
            Application.runInBackground = true;
            Application.logMessageReceived += RecordRuntimeError;
            Screen.SetResolution(requestedWidth, requestedHeight, FullScreenMode.Windowed);
            manifest = new Manifest
            {
                schemaVersion = 2, runId = runId, status = "running", startedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                applicationBuildGuid = Application.buildGUID, unityVersion = Application.unityVersion,
                expectedBuildGuid = expectedBuildGuid,
                clientAssemblyPath = typeof(PrototypeRuntimeQaCapture).Assembly.Location,
                clientAssemblySha256 = FileHash(typeof(PrototypeRuntimeQaCapture).Assembly.Location),
                clientAssemblyMvid = typeof(PrototypeRuntimeQaCapture).Module.ModuleVersionId.ToString("D"),
                playerDataPath = Application.dataPath, requestedWidth = requestedWidth, requestedHeight = requestedHeight,
                captureSource = "Actual development Player: WaitForEndOfFrame + ScreenCapture.CaptureScreenshotAsTexture + PNG encoding; no repaint/compositing.",
                inputCoverage = "Visible candidate Unity EventSystem.RaycastAll + pointerClickHandler dispatch; candidate switching uses a logged host command plus dropdown-center reachability probe. No OS mouse/keyboard hit testing.",
                osMouseHitTested = false, osKeyboardHitTested = false, visualReviewStatus = "Pending separate local view_image inspection of every PNG.",
                defaultNoFlagBehavior = "Bootstrap returns before creating an object or touching settings, files, sessions or exit behavior.",
                quitOnlyForExplicitQaFlag = quitWhenDone
            };
            manifest.candidateIds = CandidateIds;
            manifest.deliveryScope = "A/B only; C paused by user quota";
            manifest.pausedCandidateId = "opus";
            manifest.pausedCandidateReason = "NotRunByUserPause";
            using (var playerProcess = System.Diagnostics.Process.GetCurrentProcess())
            {
                manifest.playerPid = playerProcess.Id;
                manifest.monoReportedSessionId = playerProcess.SessionId;
                manifest.playerExecutablePath = playerProcess.MainModule.FileName;
            }
            uint desktopSession;
            if (Application.platform == RuntimePlatform.WindowsPlayer && ProcessIdToSessionId((uint)manifest.playerPid, out desktopSession))
            {
                manifest.windowsSessionId = checked((int)desktopSession);
                manifest.windowsSessionIdSource = "Win32 ProcessIdToSessionId for this Player PID; external launcher CIM remains an independent authority.";
            }
            else
            {
                manifest.windowsSessionId = -1;
                manifest.sessionIdQueryError = Marshal.GetLastWin32Error();
                manifest.windowsSessionIdSource = "Native query unavailable; do not use Mono Process.SessionId as verified desktop session. Refer to external CIM launch receipt.";
            }
            manifest.executionUser = Environment.UserDomainName + "\\" + Environment.UserName;
            SaveManifest();
            var deadline = Time.realtimeSinceStartup + 30;
            while (hub == null || !hub.IsInitialized || hub.SharedTactics == null || Screen.width != requestedWidth || Screen.height != requestedHeight)
            {
                Require(Time.realtimeSinceStartup < deadline, "Hub initialization or actual Screen size did not settle within 30 seconds.");
                hub = FindFirstObjectByType<PrototypeHubController>();
                yield return null;
            }
            shared = hub.SharedTactics;
            foreach (var candidate in CandidateIds) Require(hub.TacticsCandidates.IsAvailable(candidate), "This run requires the real registered candidate: " + candidate);
            hub.ShowTactics();
            sourceJsonPath = Path.Combine(Application.streamingAssetsPath, "Prototype", "Combat", "round4_mage_library.json");
            sourceJsonHash = FileHash(sourceJsonPath);
            var sourceConfig = JsonUtility.FromJson<CombatScenario>(File.ReadAllText(sourceJsonPath));
            // Existing public setup route, logged as setup rather than a physical UI action.
            hub.TacticsView.LoadConfiguration(File.ReadAllText(sourceJsonPath));
            Log("shared_setup", "TacticsPanelController.LoadConfiguration", "Exact round4_mage_library.json; original bytes SHA256 " + sourceJsonHash);
            shared.SelectUnit("mage");
            Log("shared_setup", "SelectUnit", "mage");
            Require(shared.GetState().workbench.draftScenario.id == sourceConfig.id && shared.GetState().workbench.draftScenario.seed == sourceConfig.seed, "Loaded scenario identity/seed mismatch.");
            manifest.loadedScenarioId = sourceConfig.id;
            manifest.seed = sourceConfig.seed;
            manifest.loadedInputPath = sourceJsonPath;
            manifest.loadedInputSha256 = sourceJsonHash;
            WriteText("source_input.json", File.ReadAllText(sourceJsonPath));

            SelectCandidate("argon");
            yield return Stabilize();
            var first = Controls();
            if (first.isBattlePage()) { Click(first.editorTab()); yield return Stabilize(); }
            Click(first.rule(0));
            yield return Stabilize();
            yield return CapturePair("01_preparation_default_rule", false, "Unchanged loaded default; existing first rule selected, complete shared form.");

            SelectCandidate("argon");
            first = Controls();
            RecordGraphicState(first.apply(), "after_candidate_activation_before_render");
            yield return Stabilize();
            RecordGraphicState(first.apply(), "after_candidate_render_before_strict_apply_probe");
            yield return CommitAndPauseAfterRender(first);
            Click(first.step());
            var meditation = hub.TacticsSession.GetBattleHistory();
            Require(meditation.Length == 1, "One visible step dispatch must make exactly one real event.");
            var action = hub.TacticsSession.GetCommittedScenario().actions.FirstOrDefault(value => value.id == meditation[0].actionId);
            Require(action != null && action.effectKind == "restore_mp" && meditation[0].effects.Any(effect => effect.targetId == "mage" && effect.mpAfter > effect.mpBefore), "Default first real event did not restore Mage MP.");
            yield return CapturePair("02_default_meditation_single_step", true, "Unchanged default, exactly one real event; MP values come from actual event/state.");
            var defaultOneStepHash = ReplayStateHash();
            SelectCandidate("hydrogen");
            yield return Stabilize();
            var replayControls = Controls();
            if (!replayControls.isBattlePage()) { Click(replayControls.battleTab()); yield return Stabilize(); }
            Click(replayControls.replay());
            Require(hub.TacticsSession.GetBattleHistory().Length == 0, "Visible B replay did not restore the committed opening.");
            yield return Stabilize();
            Click(replayControls.step());
            Require(ReplayStateHash() == defaultOneStepHash, "Visible B replay/step is not the same deterministic committed first event.");
            Log("verification_after_paired_capture", "Replay + Step", "Explicit replay verification is after, never between, the A/B paired screenshots.");

            // Controlled variant of the SAME source/seed. The existing AoE rule is moved just
            // after the resource rule; no HP/MP/enemy/skill values are manufactured.
            SelectCandidate("argon");
            yield return Stabilize();
            first = Controls();
            Click(first.editorTab());
            yield return Stabilize();
            shared.Reset();
            Log("shared_setup", "Reset", "Same loaded default; clear previous committed battle before explicit priority variant.");
            shared.SelectUnit("mage");
            yield return Stabilize();
            var draft = hub.TacticsSession.GetDraftScenario();
            var mage = draft.units.Single(value => value.id == "mage");
            var aoeIndex = Array.FindIndex(mage.instructions, rule => draft.actions.Any(value => value.id == rule.actionId && value.targetMode == "all" && value.effectKind == "damage"));
            Require(aoeIndex > 1, "No existing default AoE rule can be moved behind the resource rule.");
            var aoeId = mage.instructions[aoeIndex].actionId;
            manifest.areaVariantInstructionId = mage.instructions[aoeIndex].id;
            manifest.areaVariantActionId = aoeId;
            manifest.areaOriginalIndex = aoeIndex;
            manifest.areaTargetIndex = 1;
            Click(first.rule(aoeIndex));
            yield return Stabilize();
            while (aoeIndex > 1)
            {
                var argon = (ArgonCandidateView)hub.TacticsCandidates.ActiveView;
                Click(argon.EditorPage.MoveUpButton);
                Require(shared.GetState().lastEdit != null && shared.GetState().lastEdit.success, "Actual priority move failed.");
                aoeIndex--;
                yield return Stabilize();
            }
            var variantInput = JsonUtility.ToJson(hub.TacticsSession.GetDraftScenario(), true);
            WriteText("area_priority_variant_tactics_input.json", variantInput);
            manifest.areaVariant = "Existing " + aoeId + " rule moved upward to index 1 using visible Argon MoveUp button; original loaded source/seed unchanged. This is not the untouched default.";
            yield return CommitAndPauseAfterRender(first);
            CombatStepResult multi = null;
            var maxEvents = hub.TacticsSession.GetCommittedScenario().maxEvents;
            for (var i = 0; i < maxEvents && !hub.TacticsSession.GetBattleSnapshot().finished; i++)
            {
                Click(first.step());
                var actualHistory = hub.TacticsSession.GetBattleHistory();
                var latest = actualHistory.Length == 0 ? null : actualHistory[actualHistory.Length - 1];
                if (latest != null && latest.actionId == aoeId && latest.targetIds != null && latest.targetIds.Length > 1) { multi = latest; break; }
                yield return null;
            }
            Require(multi != null, "The actual controlled priority variant did not produce a multi-target event; no AoE screenshot is fabricated.");
            manifest.areaActualTargetCount = multi.targetIds.Length;
            yield return CapturePair("03_priority_variant_actual_multitarget", true, "Controlled priority variant, actual coverage count " + multi.targetIds.Length + "; not asserted to have three targets.");

            // A real existing export button is exercised after comparisons, with its standard
            // export schema left intact. Navigation/export must not change core state.
            SelectCandidate("hydrogen");
            yield return Stabilize();
            var last = Controls();
            if (last.isBattlePage()) { Click(last.editorTab()); yield return Stabilize(); }
            hub.TacticsView.ReportDirectoryOverride = Path.Combine(runDirectory, "official_export");
            var beforeExport = CoreHash();
            Click(((HydrogenCandidateView)hub.TacticsCandidates.ActiveView).ExportButton);
            Require(!string.IsNullOrEmpty(hub.TacticsView.LastExportDirectory) && string.IsNullOrEmpty(hub.TacticsView.LastExportError), "Existing export button failed.");
            Require(CoreHash() == beforeExport, "Export changed the actual session.");
            Require(File.ReadAllText(Path.Combine(hub.TacticsView.LastExportDirectory, "session_result.json")) == JsonUtility.ToJson(hub.TacticsSession.GetAcceptance(), true), "Export is not the actual acceptance DTO.");
            manifest.officialExportDirectory = hub.TacticsView.LastExportDirectory;
            VerifyPausedCandidateGuard();
        }

        private void VerifyPausedCandidateGuard()
        {
            var before = Invariant();
            var active = hub.TacticsCandidates.ActiveView;
            var selected = hub.TacticsCandidates.CurrentCandidateId;
            Require(!hub.TacticsCandidates.IsAvailable("opus"), "C must remain unavailable in the A/B delivery policy.");
            Require(!hub.TacticsCandidates.SelectCandidate("opus"), "The public switch command bypassed the user's C pause.");
            Require(hub.TacticsCandidates.ActiveView == active && hub.TacticsCandidates.CurrentCandidateId == selected && Invariant() == before,
                "Rejecting paused C changed the A/B view, form, battle, history or expedition ledger.");
            manifest.pausedCandidateCommandRejected = true;
            Log("paused_candidate_guard", "opus", "NotRunByUserPause: rejected at command layer; C source and historical evidence retained, no C controls or workflow executed.");
        }
        private IEnumerator CapturePair(string checkpoint, bool battlePage, string description)
        {
            shared.Pause();
            var workbench = hub.TacticsSession;
            var invariant = Invariant();
            var stateDirectory = Path.Combine(runDirectory, checkpoint);
            Directory.CreateDirectory(stateDirectory);
            WriteObject(Path.Combine(stateDirectory, "session_result.json"), workbench.GetAcceptance());
            WriteObject(Path.Combine(stateDirectory, "tactics_input.json"), workbench.GetDraftScenario());
            WriteObject(Path.Combine(stateDirectory, "instruction_form.json"), shared.GetState().form);
            File.WriteAllText(Path.Combine(stateDirectory, "battle_report.txt"), workbench.ExportReportText(), new UTF8Encoding(false));
            foreach (var id in CandidateIds)
            {
                SelectCandidate(id);
                yield return Stabilize();
                var controls = Controls();
                if (controls.isBattlePage() != battlePage) Click(battlePage ? controls.battleTab() : controls.editorTab());
                Require(controls.isBattlePage() == battlePage, "Requested visible page did not open.");
                yield return Stabilize();
                Require(ReferenceEquals(workbench, hub.TacticsSession), "Candidate switching replaced the workbench.");
                Require(Invariant() == invariant, "Candidate/page switch changed core, raw form, selected event, inspected unit or expedition ledger.");
                var state = shared.GetState();
                var before = Invariant();
                var beforeCore = CoreHash();
                var beforeForm = Hash(JsonUtility.ToJson(state.form));
                var beforeLedger = LedgerHash();
                yield return new WaitForEndOfFrame();
                var texture = ScreenCapture.CaptureScreenshotAsTexture();
                Require(texture != null && texture.width == Screen.width && texture.height == Screen.height, "Capture is not the actual Screen-sized rendered frame.");
                var png = texture.EncodeToPNG();
                var width = texture.width; var height = texture.height;
                Destroy(texture);
                var imagePath = Path.Combine(stateDirectory, id + "_" + Screen.width + "x" + Screen.height + ".png");
                File.WriteAllBytes(imagePath, png);
                var after = Invariant();
                Require(before == after && after == invariant, "Capturing a rendered frame changed actual state.");
                Require(png.LongLength <= 10L * 1024 * 1024, "Actual PNG exceeds attachment size; retain as diagnostic, do not claim review-ready.");
                var record = new CaptureRecord
                {
                    checkpoint = checkpoint, description = description, candidate = id, page = battlePage ? "battle" : "preparation",
                    file = imagePath, format = "PNG", bytes = png.LongLength, sha256 = Hash(png), width = width, height = height,
                    screenWidth = Screen.width, screenHeight = Screen.height, frame = Time.frameCount,
                    capturedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture), buildGuid = Application.buildGUID,
                    loadedScenarioId = state.workbench.draftScenario.id, seed = state.workbench.draftScenario.seed,
                    sessionId = runId, workbenchObjectIdentity = RuntimeHelpers.GetHashCode(workbench),
                    coreHashBefore = beforeCore, coreHashAfter = CoreHash(), invariantBefore = before, invariantAfter = after,
                    rawFormHash = beforeForm, rawFormHashAfter = Hash(JsonUtility.ToJson(shared.GetState().form)), ledgerHash = beforeLedger, ledgerHashAfter = LedgerHash(),
                    selectedEventIndex = state.selectedEventIndex, inspectedUnitId = state.inspectedUnitId,
                    stepCount = state.history.Length, wait = state.battle == null ? 0 : state.battle.time,
                    hasCommittedBattle = state.workbench.hasCommittedBattle,
                    hasSelectedEvent = state.selectedEventIndex >= 0 && state.selectedEventIndex < state.history.Length,
                    followLatest = state.followLatest,
                    isAutoPlaying = state.isAutoPlaying, form = state.form, battle = state.battle,
                    selectedEvent = state.selectedEventIndex >= 0 && state.selectedEventIndex < state.history.Length ? state.history[state.selectedEventIndex] : null,
                    highlightedTargetIds = state.highlightedTargetIds, displayedText = VisibleText(),
                    visualReviewStatus = "Not yet reviewed; requires separate local view_image of this exact SHA256."
                };
                captures.Add(record);
                WriteObject(Path.Combine(stateDirectory, id + "_capture.json"), record);
                SaveManifest();
            }
        }

        private IEnumerator CommitAndPauseAfterRender(ControlsForCandidate controls)
        {
            var originalInterval = shared.GetState().intervalSeconds;
            shared.SetPlaybackInterval(5); // existing legal upper bound; same single shared clock
            Log("shared_setup", "SetPlaybackInterval", "Temporary legal 5 seconds so the real newly enabled Pause can render before its strict raycast; no event is precomputed.");
            Require(shared.GetState().intervalSeconds == 5 && !shared.GetState().isAutoPlaying, "The legal pre-commit interval setup was not accepted.");
            Click(controls.apply());
            Require(shared.GetState().isAutoPlaying && hub.TacticsSession.GetBattleHistory().Length == 0, "Commit did not start the existing clock at its unchanged initial state.");
            RecordGraphicState(controls.pause(), "after_apply_before_battle_controls_render");
            yield return Stabilize(false);
            RecordGraphicState(controls.pause(), "after_battle_controls_render_before_strict_pause_probe");
            Click(controls.pause());
            Require(!shared.GetState().isAutoPlaying && hub.TacticsSession.GetBattleHistory().Length == 0, "Waiting for the real Pause control advanced the initial battle; no screenshot is accepted.");
            shared.SetPlaybackInterval(originalInterval);
            Log("shared_setup", "SetPlaybackInterval", "Restored original presentation interval " + originalInterval.ToString("R", CultureInfo.InvariantCulture) + " while paused; actual history remains empty.");
            yield return Stabilize();
        }

        private IEnumerator Stabilize(bool requirePaused = true)
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            foreach (var text in hub.TacticsCandidates.ActiveView.GetComponentsInChildren<TMP_Text>()) text.ForceMeshUpdate();
            yield return new WaitForEndOfFrame();
            yield return null;
            Canvas.ForceUpdateCanvases();
            // Views project only in safe layout frames. A first rendered layout can mark a
            // second projection dirty, so allow that projection a real rendered frame too.
            yield return new WaitForEndOfFrame();
            yield return null;
            Canvas.ForceUpdateCanvases();
            if (requirePaused) Require(!shared.GetState().isAutoPlaying, "Screenshots require the real shared playback clock to be paused.");
        }

        private void SelectCandidate(string id)
        {
            ProbeRaycast(hub.TacticsCandidateDropdown, false);
            Require(hub.TacticsCandidates.IsAvailable(id) && hub.TacticsCandidates.SelectCandidate(id), "Real candidate is unavailable: " + id);
            Log("shared_setup", "TacticsCandidateHost.SelectCandidate", id + "; no battle restart/step; global dropdown center separately probed, option click not claimed");
        }

        private ControlsForCandidate Controls()
        {
            var a = hub.TacticsCandidates.ActiveView as ArgonCandidateView;
            if (a != null) return new ControlsForCandidate { editorTab = () => a.EditorTab, battleTab = () => a.BattleTab,
                apply = () => a.ApplyButton, pause = () => a.BattlePage.PauseButton, step = () => a.BattlePage.StepButton,
                replay = () => a.ReplayButton, rule = index => a.EditorPage.RuleButtons[index], isBattlePage = () => a.IsBattlePage };
            var b = hub.TacticsCandidates.ActiveView as HydrogenCandidateView;
            if (b != null) return new ControlsForCandidate { editorTab = () => b.AssemblyTabButton, battleTab = () => b.BattleTabButton,
                    apply = () => b.ApplyBattleButton, pause = () => b.PauseButton, step = () => b.StepButton,
                    replay = () => b.ReplayButton, rule = index => b.InstructionButtons[index], isBattlePage = () => b.IsBattlePage };
            throw new InvalidOperationException("No active A/B control adapter; paused C is never substituted.");
        }

        private void Click(Button button)
        {
            Require(button != null && button.gameObject.activeInHierarchy && button.IsInteractable(), "Unity button dispatch requires the actual active/interactable candidate button.");
            Reveal(button);
            var before = shared == null ? "" : CoreHash();
            var probe = ProbeRaycast(button, true);
            var receiver = ExecuteEvents.ExecuteHierarchy(probe.hit.gameObject, probe.pointer, ExecuteEvents.pointerClickHandler);
            Require(receiver == button.gameObject, "Unity pointer click was not handled by the intended real Button: " + button.name);
            Log("Unity.RaycastAll + pointerClickHandler", button.name, "Unity pointer/listener dispatch only; OS mouse hit untested. Core hash before " + before + " / after " + (shared == null ? "" : CoreHash()));
        }

        private RaycastProbe ProbeRaycast(Selectable control, bool requireInteractable)
        {
            RecordGraphicState(control, "strict_raycast_probe");
            Require(control != null && control.gameObject.activeInHierarchy, "Raycast probe requires the real visible control.");
            if (requireInteractable) Require(control.IsInteractable(), "Raycast control is not interactable: " + control.name);
            Canvas.ForceUpdateCanvases();
            var system = EventSystem.current;
            Require(system != null, "No actual Unity EventSystem is active.");
            var rect = (RectTransform)control.transform;
            var canvas = control.GetComponentInParent<Canvas>();
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            var position = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center));
            var pointer = new PointerEventData(system) { position = position, button = PointerEventData.InputButton.Left };
            var hits = new List<RaycastResult>();
            system.RaycastAll(pointer, hits);
            var hit = hits.Count == 0 ? default(RaycastResult) : hits[0];
            var reachable = hit.gameObject != null && (hit.gameObject == control.gameObject || hit.gameObject.transform.IsChildOf(control.transform));
            Log("Unity.EventSystem.RaycastAll", control.name, "Center " + position.x.ToString("0.##", CultureInfo.InvariantCulture) + "," + position.y.ToString("0.##", CultureInfo.InvariantCulture) +
                "; first hit " + (hit.gameObject == null ? "none" : HierarchyPath(hit.gameObject.transform)) + "; intended control reachable=" + reachable + "; does not test OS coordinates/input.");
            Require(reachable, "The intended control center is blocked/missing in the actual Unity raycast: " + control.name + "; first hit " + (hit.gameObject == null ? "none" : HierarchyPath(hit.gameObject.transform)));
            return new RaycastProbe { pointer = pointer, hit = hit };
        }

        private void RecordGraphicState(Selectable control, string stage)
        {
            if (control == null) { Log("target_graphic_state", "null", stage + "; target absent"); SaveManifest(); return; }
            var graphic = control.targetGraphic;
            var canvas = graphic == null ? control.GetComponentInParent<Canvas>() : graphic.canvas;
            var registered = graphic != null && canvas != null && GraphicRegistry.GetGraphicsForCanvas(canvas).Contains(graphic);
            var rect = (RectTransform)control.transform;
            var points = new Vector3[4]; rect.GetWorldCorners(points);
            var camera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            var low = RectTransformUtility.WorldToScreenPoint(camera, points[0]);
            var high = RectTransformUtility.WorldToScreenPoint(camera, points[2]);
            var groups = control.GetComponentsInParent<CanvasGroup>(true).Select(group => HierarchyPath(group.transform) +
                " enabled=" + group.enabled + " alpha=" + group.alpha.ToString("R", CultureInfo.InvariantCulture) +
                " blocksRaycasts=" + group.blocksRaycasts + " interactable=" + group.interactable);
            Log("target_graphic_state", control.name, stage + "; path=" + HierarchyPath(control.transform) +
                "; control active=" + control.gameObject.activeInHierarchy + " enabled=" + control.enabled + " interactable=" + control.IsInteractable() +
                "; graphic=" + (graphic == null ? "none" : HierarchyPath(graphic.transform)) +
                "; depth=" + (graphic == null ? -999 : graphic.depth) + "; cull=" + (graphic != null && graphic.canvasRenderer.cull) +
                "; graphic active=" + (graphic != null && graphic.gameObject.activeInHierarchy) + " enabled=" + (graphic != null && graphic.enabled) +
                "; raycastTarget=" + (graphic != null && graphic.raycastTarget) + "; registered=" + registered +
                "; canvas=" + (canvas == null ? "none" : HierarchyPath(canvas.transform) + " enabled=" + canvas.enabled + " mode=" + canvas.renderMode) +
                "; localRect=" + rect.rect + "; screenRect=" + low + ".." + high + "; lossyScale=" + rect.lossyScale +
                "; canvasGroups=" + string.Join(" | ", groups));
            SaveManifest();
        }

        private static void Reveal(Selectable control)
        {
            Canvas.ForceUpdateCanvases();
            var scroll = control.GetComponentInParent<ScrollRect>();
            var rect = (RectTransform)control.transform;
            if (scroll != null && scroll.content != null && control.transform.IsChildOf(scroll.content))
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
                var corners = new Vector3[4]; rect.GetWorldCorners(corners);
                var low = scroll.viewport.InverseTransformPoint(corners[0]).y;
                var high = scroll.viewport.InverseTransformPoint(corners[2]).y;
                var shift = high > scroll.viewport.rect.yMax - 2 ? scroll.viewport.rect.yMax - 2 - high : low < scroll.viewport.rect.yMin + 2 ? scroll.viewport.rect.yMin + 2 - low : 0;
                scroll.content.anchoredPosition += new Vector2(0, shift);
                Canvas.ForceUpdateCanvases();
            }
            var points = new Vector3[4]; rect.GetWorldCorners(points);
            var canvas = control.GetComponentInParent<Canvas>();
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            foreach (var point in points)
            {
                var pixel = RectTransformUtility.WorldToScreenPoint(camera, point);
                Require(pixel.x >= -1 && pixel.x <= Screen.width + 1 && pixel.y >= -1 && pixel.y <= Screen.height + 1, "Actual control is outside Screen bounds: " + control.name);
            }
        }

        private TextRecord[] VisibleText()
        {
            return hub.TacticsCandidates.ActiveView.GetComponentsInChildren<TMP_Text>().Where(text => text.isActiveAndEnabled)
                .Select(text => new TextRecord { name = text.name, text = text.text, fontAsset = text.font == null ? "" : text.font.name,
                    fontSize = text.fontSize, overflowing = text.isTextOverflowing, visibleGlyphs = text.textInfo.characterInfo.Take(text.textInfo.characterCount).Count(glyph => glyph.isVisible) }).ToArray();
        }

        private string CoreHash() { return Hash(JsonUtility.ToJson(hub.TacticsSession.GetAcceptance())); }
        private string ReplayStateHash()
        {
            // An explicit replay updates its real command receipt. Compare the actual
            // committed scenario, battle and events, not the previous command's label.
            return Hash(JsonUtility.ToJson(hub.TacticsSession.GetCommittedScenario()) + "\n" +
                JsonUtility.ToJson(hub.TacticsSession.GetBattleSnapshot()) + "\n" +
                JsonUtility.ToJson(new HistoryRecord { events = hub.TacticsSession.GetBattleHistory() }));
        }
        private string LedgerHash() { return Hash(JsonUtility.ToJson(hub.ExpeditionSession.GetAcceptance())); }
        private string Invariant()
        {
            var state = shared.GetState();
            return Hash(CoreHash() + "\n" + JsonUtility.ToJson(state.form) + "\n" + state.selectedEventIndex + "\n" + state.inspectedUnitId + "\n" + state.followLatest + "\n" + LedgerHash());
        }
        private void Log(string method, string target, string detail)
        {
            operations.Add(new OperationRecord { frame = Time.frameCount, utc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture), method = method, target = target, detail = detail });
        }
        private static void Require(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
        private static string Required(string[] args, string flag)
        {
            var index = Array.IndexOf(args, flag);
            if (index < 0 || index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1])) throw new ArgumentException("Missing explicit QA argument " + flag);
            return args[index + 1];
        }
        private static string Optional(string[] args, string flag)
        {
            var index = Array.IndexOf(args, flag);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }
        private static string Hash(string value) { return Hash(Encoding.UTF8.GetBytes(value ?? "")); }
        private static string Hash(byte[] bytes)
        {
            using (var algorithm = SHA256.Create()) return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        private static string FileHash(string path)
        {
            using (var file = File.OpenRead(path)) using (var algorithm = SHA256.Create()) return BitConverter.ToString(algorithm.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
        }
        private static string HierarchyPath(Transform transform)
        {
            var parts = new List<string>();
            while (transform != null) { parts.Add(transform.name); transform = transform.parent; }
            parts.Reverse();
            return string.Join("/", parts);
        }
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);
        private void WriteText(string name, string value) { File.WriteAllText(Path.Combine(runDirectory, name), value ?? "", new UTF8Encoding(false)); }
        private static void WriteObject(string path, object value) { File.WriteAllText(path, JsonUtility.ToJson(value, true), new UTF8Encoding(false)); }
        private void SaveManifest()
        {
            if (manifest == null || string.IsNullOrEmpty(runDirectory)) return;
            manifest.captures = captures.ToArray(); manifest.operations = operations.ToArray(); manifest.runtimeErrors = runtimeErrors.ToArray();
            WriteObject(Path.Combine(runDirectory, "runtime_qa_manifest.json"), manifest);
        }
        private void RecordRuntimeError(string condition, string trace, LogType type)
        {
            if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) && runtimeErrors.Count < 50) runtimeErrors.Add(condition);
        }
        private void FinishFailure(Exception exception)
        {
            if (manifest != null) { manifest.status = "failed"; manifest.failure = exception.ToString(); manifest.completedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture); SaveManifest(); }
            Debug.LogError("Runtime QA failed: " + exception.Message);
            if (quitWhenDone) Application.Quit(2);
        }
        private void OnDestroy() { Application.logMessageReceived -= RecordRuntimeError; }

        private sealed class ControlsForCandidate
        {
            internal Func<Button> editorTab, battleTab, apply, pause, step, replay;
            internal Func<int, Button> rule;
            internal Func<bool> isBattlePage;
        }
        private sealed class RaycastProbe { internal PointerEventData pointer; internal RaycastResult hit; }
        [Serializable] private sealed class HistoryRecord { public CombatStepResult[] events; }
        [Serializable] private sealed class Manifest
        {
            public int schemaVersion, requestedWidth, requestedHeight, seed, areaActualTargetCount, areaOriginalIndex, areaTargetIndex;
            public int playerPid, windowsSessionId, monoReportedSessionId, sessionIdQueryError;
            public string windowsSessionIdSource;
            public string[] candidateIds;
            public string deliveryScope, pausedCandidateId, pausedCandidateReason;
            public bool pausedCandidateCommandRejected;
            public string playerExecutablePath, executionUser, areaVariantInstructionId, areaVariantActionId;
            public string runId, status, startedAtUtc, completedAtUtc, applicationBuildGuid, unityVersion, playerDataPath;
            public string expectedBuildGuid, clientAssemblyPath, clientAssemblySha256, clientAssemblyMvid;
            public string captureSource, inputCoverage, visualReviewStatus, defaultNoFlagBehavior;
            public bool osMouseHitTested, osKeyboardHitTested, quitOnlyForExplicitQaFlag;
            public string loadedScenarioId, loadedInputPath, loadedInputSha256, areaVariant, officialExportDirectory, failure;
            public CaptureRecord[] captures;
            public OperationRecord[] operations;
            public string[] runtimeErrors;
        }
        [Serializable] private sealed class OperationRecord { public int frame; public string utc, method, target, detail; }
        [Serializable] private sealed class TextRecord { public string name, text, fontAsset; public float fontSize; public bool overflowing; public int visibleGlyphs; }
        [Serializable] private sealed class CaptureRecord
        {
            public string checkpoint, description, candidate, page, file, format, sha256, capturedAtUtc, buildGuid, loadedScenarioId;
            public long bytes;
            public int width, height, screenWidth, screenHeight, frame, seed, workbenchObjectIdentity, selectedEventIndex, stepCount;
            public string sessionId, coreHashBefore, coreHashAfter, invariantBefore, invariantAfter, rawFormHash, rawFormHashAfter, ledgerHash, ledgerHashAfter, inspectedUnitId, visualReviewStatus;
            public bool isAutoPlaying, hasCommittedBattle, hasSelectedEvent, followLatest;
            public double wait;
            public TacticsFormInput form;
            public CombatSnapshot battle;
            public CombatStepResult selectedEvent;
            public string[] highlightedTargetIds;
            public TextRecord[] displayedText;
        }
    }
}
