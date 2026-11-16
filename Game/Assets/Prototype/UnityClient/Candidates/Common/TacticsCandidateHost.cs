using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using Ui = Prototype.UnityClient.PrototypeUiKit;

namespace Prototype.UnityClient.Candidates.Common
{
    /// <summary>Switches presentation roots only. Session, raw form and clock are never recreated.</summary>
    public sealed class TacticsCandidateHost : MonoBehaviour
    {
        // Deployment policy only: retain the Opus implementation and its evidence,
        // but neither the selector nor the public command may activate it this round.
        private static readonly string[] Ids = { "argon", "hydrogen", "legacy" };
        public const string PausedCandidateNotice = "C 方案暂停；本轮提供 A/B";
        private readonly Dictionary<string, Type> types = new Dictionary<string, Type>();
        private readonly Dictionary<string, TacticsCandidateView> views = new Dictionary<string, TacticsCandidateView>();
        private TMP_FontAsset font;
        private TacticsSharedController shared;
        private RectTransform legacyRoot;
        private TMP_Dropdown selector;
        private bool switching;
        public string CurrentCandidateId { get; private set; }
        public TacticsCandidateView ActiveView { get; private set; }
        public bool IsModernCandidate { get { return CurrentCandidateId != null && CurrentCandidateId != "legacy"; } }
        public event Action<string> CandidateChanged;

        public void Initialize(TMP_FontAsset uiFont, TacticsSharedController controller, RectTransform legacy, TMP_Dropdown dropdown)
        {
            font = uiFont; shared = controller; legacyRoot = legacy; selector = dropdown;
            foreach (var type in typeof(TacticsCandidateView).Assembly.GetTypes().Where(type => !type.IsAbstract && typeof(TacticsCandidateView).IsAssignableFrom(type)))
            {
                var attribute = (TacticsCandidateAttribute)Attribute.GetCustomAttribute(type, typeof(TacticsCandidateAttribute));
                if (attribute == null) continue;
                if (types.ContainsKey(attribute.Id)) throw new InvalidOperationException("候选注册重复：" + attribute.Id);
                types.Add(attribute.Id, type);
            }
            TacticsUiControls.Options(selector, new[]
            {
                types.ContainsKey("argon") ? "A · 石枢星盘" : "A · 实现待接入",
                types.ContainsKey("hydrogen") ? "B · 星象秘库" : "B · 实现待接入",
                "旧版工作台（回退）"
            });
            selector.onValueChanged.AddListener(index => { if (!switching && index >= 0 && index < Ids.Length) SelectCandidate(Ids[index]); });
            shared.Changed += RefreshActive;
            SelectCandidate(types.ContainsKey("argon") ? "argon" : "legacy");
            shared.NotifyUser(PausedCandidateNotice, false);
        }

        public bool IsAvailable(string id)
        {
            return Array.IndexOf(Ids, id) >= 0 && (id == "legacy" || types.ContainsKey(id));
        }

        public bool SelectCandidate(string id)
        {
            if (!IsAvailable(id))
            {
                shared.NotifyUser(id == "opus" ? PausedCandidateNotice + "；已有草稿和战斗保持。" : "该候选尚未接入，已有草稿和战斗保持。", true);
                RestoreSelector();
                return false;
            }
            if (id == CurrentCandidateId) return true;
            shared.Pause();
            switching = true;
            try
            {
                if (id != "legacy" && !views.ContainsKey(id))
                {
                    var root = Ui.Rect("Candidate_" + id, transform);
                    Ui.Stretch(root);
                    root.gameObject.SetActive(false);
                    var view = (TacticsCandidateView)root.gameObject.AddComponent(types[id]);
                    view.Initialize(font, shared);
                    views.Add(id, view);
                }
                foreach (var view in views.Values) view.gameObject.SetActive(false);
                legacyRoot.gameObject.SetActive(id == "legacy");
                CurrentCandidateId = id;
                ActiveView = id == "legacy" ? null : views[id];
                if (ActiveView != null) ActiveView.gameObject.SetActive(true);
                RestoreSelector();
            }
            finally { switching = false; }
            CandidateChanged?.Invoke(id);
            RefreshActive();
            return true;
        }

        private void RestoreSelector()
        {
            var index = Array.IndexOf(Ids, CurrentCandidateId);
            selector.SetValueWithoutNotify(index < 0 ? Ids.Length - 1 : index);
            selector.RefreshShownValue();
        }
        private void RefreshActive()
        {
            if (!switching && isActiveAndEnabled && ActiveView != null) ActiveView.RefreshView();
        }
        private void OnEnable() { RefreshActive(); }
        private void OnDisable() { if (shared != null) shared.Pause(); }
        private void OnDestroy() { if (shared != null) shared.Changed -= RefreshActive; }
    }
}
