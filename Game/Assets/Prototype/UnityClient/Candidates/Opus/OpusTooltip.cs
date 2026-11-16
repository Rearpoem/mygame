using UnityEngine;
using UnityEngine.EventSystems;

namespace Prototype.UnityClient.Candidates.Opus
{
    public sealed class OpusTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private OpusCandidateView owner;
        private string value;
        private float entered;
        private bool waiting;
        internal void Configure(OpusCandidateView view, string text) { owner = view; value = text; }
        public void OnPointerEnter(PointerEventData data) { entered = Time.unscaledTime; waiting = true; }
        public void OnPointerExit(PointerEventData data) { waiting = false; owner?.HideTooltip(); }
        private void OnDisable() { waiting = false; owner?.HideTooltip(); }
        private void Update()
        {
            if (!waiting || owner == null || Time.unscaledTime - entered < .4f) return;
            waiting = false; owner.DisplayTooltip(value, (RectTransform)transform);
        }
    }
}
