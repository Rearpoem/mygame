using System;
using System.Collections.Generic;
using System.Linq;
using Prototype.Combat;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using T = Prototype.UnityClient.Candidates.Hydrogen.HydrogenThemeTokens;
using Ui = Prototype.UnityClient.PrototypeUiKit;

namespace Prototype.UnityClient.Candidates.Hydrogen
{
    /// <summary>Only renders the supplied current snapshot. Selecting an old event never rewinds it.</summary>
    public sealed class HydrogenBattleBoard : MonoBehaviour
    {
        private sealed class Cell
        {
            internal Button button;
            internal Image background;
            internal Outline outline;
            internal RectTransform secondaryFrame;
            internal Image[] secondaryEdges;
            internal Image portrait;
            internal TMP_Text monogram;
            internal TMP_Text name;
            internal TMP_Text position;
            internal TMP_Text hp;
            internal TMP_Text mp;
            internal TMP_Text status;
            internal Image hpFill;
            internal Image mpFill;
            internal string unitId;
        }

        private readonly Cell[] cells = new Cell[9];
        private TMP_FontAsset font;
        private Func<string, Sprite> portraits;
        private Action<string> selectUnit;
        public Button[] CellButtons { get; private set; }
        public TMP_Text[] NameTexts { get; private set; }
        public TMP_Text[] HpTexts { get; private set; }
        public TMP_Text[] MpTexts { get; private set; }
        public TMP_Text[] StatusTexts { get; private set; }
        public string[] VisibleUnitIds { get { return cells.Select(cell => cell.unitId).ToArray(); } }

        public void Initialize(TMP_FontAsset uiFont, Func<string, Sprite> portraitResolver, Action<string> onSelectUnit)
        {
            font = uiFont;
            portraits = portraitResolver;
            selectUnit = onSelectUnit;
            var ruler = HydrogenUi.Rect("Hydrogen_RowRuler", transform);
            Ui.Stretch(ruler, 0, 0, 0, 0);
            var rows = new[] { "前排\nR0", "中排\nR1", "后排\nR2" };
            for (int row = 0; row < 3; row++)
            {
                var label = HydrogenUi.Text("Hydrogen_Row" + row, ruler, font, rows[row], 12, T.Secondary);
                label.alignment = TextAlignmentOptions.Center;
                label.rectTransform.anchorMin = new Vector2(0, 1f - (row + 1) / 3f);
                label.rectTransform.anchorMax = new Vector2(0, 1f - row / 3f);
                label.rectTransform.offsetMin = Vector2.zero;
                label.rectTransform.offsetMax = new Vector2(31, 0);
            }
            var grid = HydrogenUi.Rect("Hydrogen_True3x3Grid", transform);
            Ui.Stretch(grid, 32, 0, 0, 0);
            for (int row = 0; row < 3; row++)
                for (int column = 0; column < 3; column++)
                    BuildCell(grid, row, column);
            CellButtons = cells.Select(cell => cell.button).ToArray();
            NameTexts = cells.Select(cell => cell.name).ToArray();
            HpTexts = cells.Select(cell => cell.hp).ToArray();
            MpTexts = cells.Select(cell => cell.mp).ToArray();
            StatusTexts = cells.Select(cell => cell.status).ToArray();
        }

        private void BuildCell(RectTransform grid, int row, int column)
        {
            var image = HydrogenUi.Panel("Hydrogen_Cell_" + row + "_" + column, grid, T.Input);
            image.raycastTarget = true;
            var rect = image.rectTransform;
            rect.anchorMin = new Vector2(column / 3f, 1f - (row + 1) / 3f);
            rect.anchorMax = new Vector2((column + 1) / 3f, 1f - row / 3f);
            rect.offsetMin = new Vector2(4, 4);
            rect.offsetMax = new Vector2(-4, -4);
            var cell = new Cell { background = image, outline = image.GetComponent<Outline>() };
            cell.button = image.gameObject.AddComponent<Button>();
            cell.button.targetGraphic = image;
            cell.button.onClick.AddListener(() => { if (!string.IsNullOrEmpty(cell.unitId)) selectUnit?.Invoke(cell.unitId); });
            var portrait = HydrogenUi.Panel("Portrait", rect, Color.clear, false);
            cell.portrait = portrait;
            portrait.preserveAspect = true;
            portrait.rectTransform.anchorMin = portrait.rectTransform.anchorMax = new Vector2(1, 1);
            portrait.rectTransform.pivot = new Vector2(1, 1);
            portrait.rectTransform.anchoredPosition = new Vector2(-5, -2);
            portrait.rectTransform.sizeDelta = new Vector2(32, 32);
            cell.monogram = HydrogenUi.Text("PortraitPlaceholder", portrait.transform, font, "", 20, T.Cyan, true);
            Ui.Stretch(cell.monogram.rectTransform);
            cell.monogram.alignment = TextAlignmentOptions.Center;
            cell.name = HydrogenUi.Text("UnitName", rect, font, "空位", 15, T.Muted, true);
            // Noto's face line-height / point-size is 92.672 / 64. At 15/14/12 pt,
            // a single line needs 21.72/20.272/17.376 px before any clipping.
            // Keep real line boxes instead of relying on Overflow to rescue undersized rows.
            TopBand(cell.name.rectTransform, 5, 2, 39, 22);
            cell.position = HydrogenUi.Text("PositionAndReady", rect, font, "(" + row + "," + column + ")", 12, T.Muted, true);
            // Coordinates share the footer with status; the full next-WAIT remains in the
            // actual queue and unit inspector. This leaves the HP/MP rows independent.
            cell.position.rectTransform.anchorMin = cell.position.rectTransform.anchorMax = new Vector2(1, 0);
            cell.position.rectTransform.pivot = new Vector2(1, 0);
            cell.position.rectTransform.anchoredPosition = new Vector2(-5, 3);
            cell.position.rectTransform.sizeDelta = new Vector2(42, 20);
            cell.position.alignment = TextAlignmentOptions.MidlineRight;
            cell.hpFill = BuildBar("HpBar", rect, 38, T.Emerald);
            cell.hp = HydrogenUi.Text("ActualHP", rect, font, "", 14, T.Ink, true);
            TopBand(cell.hp.rectTransform, 6, 35, 5, 22);
            cell.hp.overflowMode = TextOverflowModes.Overflow;
            cell.mpFill = BuildBar("MpBar", rect, 60, T.Cyan);
            cell.mp = HydrogenUi.Text("ActualMP", rect, font, "", 14, T.Ink, true);
            TopBand(cell.mp.rectTransform, 6, 57, 5, 22);
            cell.mp.overflowMode = TextOverflowModes.Overflow;
            cell.status = HydrogenUi.Text("StatusesAndHighlight", rect, font, "", 12, T.Secondary, true);
            cell.status.rectTransform.anchorMin = Vector2.zero;
            cell.status.rectTransform.anchorMax = new Vector2(1, 0);
            cell.status.rectTransform.pivot = new Vector2(.5f, 0);
            cell.status.rectTransform.offsetMin = new Vector2(5, 3);
            cell.status.rectTransform.offsetMax = new Vector2(-51, 23);
            cell.status.alignment = TextAlignmentOptions.MidlineLeft;
            // A self-target keeps the actor's outer gold frame and a separate inset
            // coverage frame; a single Outline cannot communicate both identities.
            // An Outline on a transparent full quad makes opaque shifted quads when
            // useGraphicAlpha=false. Use four real edge strips and no centre Graphic.
            cell.secondaryFrame = HydrogenUi.Rect("AdditionalEventIdentity", rect);
            Ui.Stretch(cell.secondaryFrame, 2, 2, 2, 2);
            cell.secondaryEdges = new[]
            {
                FrameEdge(cell.secondaryFrame, "Top", new Vector2(0, 1), Vector2.one, new Vector2(0, -1), Vector2.zero),
                FrameEdge(cell.secondaryFrame, "Bottom", Vector2.zero, new Vector2(1, 0), Vector2.zero, new Vector2(0, 1)),
                FrameEdge(cell.secondaryFrame, "Left", Vector2.zero, new Vector2(0, 1), Vector2.zero, new Vector2(1, 0)),
                FrameEdge(cell.secondaryFrame, "Right", new Vector2(1, 0), Vector2.one, new Vector2(-1, 0), Vector2.zero)
            };
            cell.secondaryFrame.gameObject.SetActive(false);
            cells[row * 3 + column] = cell;
        }

        private static Image FrameEdge(Transform parent, string name, Vector2 from, Vector2 to,
            Vector2 lower, Vector2 upper)
        {
            var edge = HydrogenUi.Panel(name, parent, T.Amber, false);
            edge.rectTransform.anchorMin = from;
            edge.rectTransform.anchorMax = to;
            edge.rectTransform.offsetMin = lower;
            edge.rectTransform.offsetMax = upper;
            return edge;
        }

        private Image BuildBar(string name, Transform parent, float y, Color color)
        {
            var track = HydrogenUi.Panel(name, parent, T.Border, false);
            TopBand(track.rectTransform, 4, y, 4, 14);
            var fill = HydrogenUi.Panel("Fill", track.transform, new Color(color.r, color.g, color.b, .35f), false);
            Ui.Stretch(fill.rectTransform);
            return fill;
        }

        private static void TopBand(RectTransform rect, float left, float y, float right, float height)
        {
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(.5f, 1);
            rect.offsetMin = new Vector2(left, -y - height);
            rect.offsetMax = new Vector2(-right, -y);
        }

        public void Render(CombatSnapshot snapshot, string team, CombatStepResult selectedEvent, string[] actualPrimaryTargets = null)
        {
            var units = snapshot == null ? Array.Empty<CombatUnitSnapshot>()
                : snapshot.units.Where(unit => unit.team == team).ToArray();
            var primary = new HashSet<string>(actualPrimaryTargets ?? HydrogenBattleText.PrimaryTargets(selectedEvent), StringComparer.Ordinal);
            var additional = new HashSet<string>((selectedEvent?.effects ?? Array.Empty<CombatEffect>())
                .Where(effect => !primary.Contains(effect.targetId)).Select(effect => effect.targetId), StringComparer.Ordinal);
            for (int i = 0; i < cells.Length; i++)
            {
                var cell = cells[i];
                var unit = units.FirstOrDefault(value => value.row == i / 3 && value.column == i % 3);
                cell.unitId = unit?.id;
                bool occupied = unit != null;
                cell.button.interactable = occupied;
                cell.hp.gameObject.SetActive(occupied);
                cell.mp.gameObject.SetActive(occupied);
                cell.hpFill.transform.parent.gameObject.SetActive(occupied);
                cell.mpFill.transform.parent.gameObject.SetActive(occupied);
                cell.portrait.gameObject.SetActive(occupied);
                cell.status.gameObject.SetActive(occupied);
                cell.secondaryFrame.gameObject.SetActive(false);
                if (!occupied)
                {
                    cell.background.color = T.Input;
                    cell.outline.effectColor = T.Border;
                    cell.outline.effectDistance = new Vector2(1, -1);
                    cell.name.text = "空位";
                    cell.name.color = T.Muted;
                    cell.position.text = "(" + (i / 3) + "," + (i % 3) + ")";
                    continue;
                }
                bool actor = selectedEvent != null && selectedEvent.actorId == unit.id;
                bool affected = primary.Contains(unit.id);
                bool extra = additional.Contains(unit.id);
                cell.background.color = unit.hp <= 0 ? T.Abyss : T.Panel;
                cell.outline.effectColor = actor ? T.Gold : affected ? T.Amber : extra ? T.Cyan : T.Border;
                cell.outline.effectDistance = actor || affected || extra ? new Vector2(2, -2) : new Vector2(1, -1);
                cell.secondaryFrame.gameObject.SetActive(actor && (affected || extra));
                foreach (var edge in cell.secondaryEdges) edge.color = affected ? T.Amber : T.Cyan;
                cell.name.text = HydrogenUi.Esc(unit.name);
                cell.name.color = unit.hp <= 0 ? T.Muted : T.Ink;
                cell.position.text = "(" + unit.row + "," + unit.column + ")";
                cell.hp.text = "HP " + unit.hp + "/" + unit.maxHp;
                cell.mp.text = "MP " + unit.mp + "/" + unit.maxMp;
                SetBar(cell.hpFill, unit.maxHp <= 0 ? 0 : (float)unit.hp / unit.maxHp);
                SetBar(cell.mpFill, unit.maxMp <= 0 ? 0 : (float)unit.mp / unit.maxMp);
                var markers = new List<string>();
                if (actor) markers.Add("行动");
                if (affected) markers.Add("覆盖");
                else if (extra) markers.Add("附加");
                if (unit.hp <= 0) markers.Add("已倒下");
                else if ((unit.statuses?.Length ?? 0) > 0 || markers.Count == 0)
                    markers.Add(HydrogenBattleText.ShortStatuses(unit.statuses));
                cell.status.text = string.Join(" · ", markers);
                cell.status.color = unit.hp <= 0 ? T.Crimson : affected ? T.Amber : T.Secondary;
                var sprite = portraits?.Invoke(unit.id);
                cell.portrait.sprite = sprite;
                cell.portrait.color = sprite == null ? T.Elevated : Color.white;
                cell.monogram.gameObject.SetActive(sprite == null);
                cell.monogram.text = string.IsNullOrEmpty(unit.name) ? "?" : HydrogenUi.Esc(unit.name.Substring(0, 1));
            }
        }

        private static void SetBar(Image fill, float ratio)
        {
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(ratio), 1);
            fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
        }
    }
}
