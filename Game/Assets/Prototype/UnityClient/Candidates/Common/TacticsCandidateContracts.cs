using System;
using Prototype.Combat;
using Prototype.Tactics;
using TMPro;
using UnityEngine;

namespace Prototype.UnityClient.Candidates.Common
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class TacticsCandidateAttribute : Attribute
    {
        public string Id { get; }
        public string Label { get; }
        public TacticsCandidateAttribute(string id, string label) { Id = id; Label = label; }
    }

    /// <summary>A presentation only. Never creates a workbench, combat session or playback loop.</summary>
    public abstract class TacticsCandidateView : MonoBehaviour
    {
        public abstract void Initialize(TMP_FontAsset font, TacticsSharedController shared);
        public abstract void RefreshView();
    }

    /// <summary>Read-only presentation copies. Raw form strings deliberately include invalid input.</summary>
    public sealed class TacticsCandidateState
    {
        public TacticsWorkbenchSnapshot workbench;
        public TacticsFormInput form;
        public TacticsCatalogSnapshot catalog;
        public CombatSnapshot battle;
        public CombatStepResult[] history;
        public CombatActionConfig[] visibleActions;
        public int matchingActionCount;
        public bool selectedActionRetained;
        public bool isAutoPlaying;
        public double intervalSeconds;
        public int selectedEventIndex;
        public bool followLatest;
        public string inspectedUnitId;
        public string highlightedActorId;
        public string[] highlightedTargetIds;
        public string selectedEffectsSummary;
        public string revisionText;
        public string feedback;
        public string reasonText;
        public string queueText;
        public string actionMatchText;
        public string actionDetailsText;
        public string comparisonText;
        public bool isComparisonVisible;
        public TacticsComparisonResult lastComparison;
        public CombatEditResult lastEdit;
        public CombatEditResult lastApply;
        public CombatEditResult lastSend;
        public string exportDirectory;
        public string exportError;
        public string lastError;
    }
}
