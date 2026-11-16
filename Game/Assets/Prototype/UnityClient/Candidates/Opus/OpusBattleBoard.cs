using System;
using System.Linq;
using Prototype.Combat;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using T = Prototype.UnityClient.Candidates.Opus.OpusTheme;
using Ui = Prototype.UnityClient.PrototypeUiKit;

namespace Prototype.UnityClient.Candidates.Opus
{
    public sealed class OpusBattleBoard : MonoBehaviour
    {
        private sealed class Cell
        {
            internal Image frame, body, portrait, hpFill, mpFill;
            internal Image[] extraCorners;
            internal TMP_Text name, ready, hp, mp, status, coords, fallback;
            internal Button button;
            internal string id;
        }
        private readonly Cell[] cells = new Cell[9];
        private Func<string, Sprite> portraits;
        private Action<string> inspect;
        private TMP_FontAsset font;
        public Button[] CellButtons { get; private set; }
        public TMP_Text[] HpTexts { get; private set; }
        public TMP_Text[] MpTexts { get; private set; }
        public TMP_Text[] StatusTexts { get; private set; }
        public void Initialize(TMP_FontAsset uiFont, bool enemy, Func<string, Sprite> portraitProvider, Action<string> onInspect)
        {
            font = uiFont; portraits = portraitProvider; inspect = onInspect;
            var ruler = OpusUi.Rect("Opus_RowLabels", transform); Ui.Stretch(ruler);
            var grid = OpusUi.Rect("Opus_ActualCoordinates", transform); Ui.Stretch(grid, 22, 0, 0, 0);
            for (int row = 0; row < 3; row++)
            {
                int visual = enemy ? 2 - row : row;
                var label = OpusUi.Text("Row" + row, ruler, font, new[] { "前", "中", "后" }[row] + "\nR" + row, 12, T.Secondary);
                label.alignment = TextAlignmentOptions.Center;
                label.rectTransform.anchorMin = new Vector2(0, 1f - (visual + 1) / 3f); label.rectTransform.anchorMax = new Vector2(0, 1f - visual / 3f);
                label.rectTransform.offsetMin = Vector2.zero; label.rectTransform.offsetMax = new Vector2(21, 0);
                for (int column = 0; column < 3; column++) BuildCell(grid, row, column, visual);
            }
            CellButtons = cells.Select(cell => cell.button).ToArray(); HpTexts = cells.Select(cell => cell.hp).ToArray();
            MpTexts = cells.Select(cell => cell.mp).ToArray(); StatusTexts = cells.Select(cell => cell.status).ToArray();
        }
        private void BuildCell(RectTransform grid, int row, int column, int visual)
        {
            var frame = OpusUi.Panel("Opus_Cell_" + row + "_" + column, grid, T.Line); frame.raycastTarget = true;
            var rect = frame.rectTransform;
            rect.anchorMin = new Vector2(column / 3f, 1f - (visual + 1) / 3f); rect.anchorMax = new Vector2((column + 1) / 3f, 1f - visual / 3f);
            rect.offsetMin = new Vector2(4, 2); rect.offsetMax = new Vector2(-4, -2);
            var cell = new Cell { frame = frame };
            cell.button = frame.gameObject.AddComponent<Button>(); cell.button.targetGraphic = frame;
            cell.button.onClick.AddListener(() => { if (cell.id != null) inspect?.Invoke(cell.id); });
            cell.body = OpusUi.Panel("StoneCard", rect, T.Panel); Ui.Stretch(cell.body.rectTransform, 2, 2, 2, 2);
            cell.portrait = OpusUi.Panel("Portrait", rect, T.Raised); OpusUi.FixedRect(cell.portrait.rectTransform, 4, 2, 24, 24); cell.portrait.preserveAspect = true;
            cell.fallback = OpusUi.Text("PortraitPlaceholder", cell.portrait.transform, font, "", 12, T.Green, true); Ui.Stretch(cell.fallback.rectTransform); cell.fallback.alignment = TextAlignmentOptions.Center;
            cell.name = OpusUi.Text("Name", rect, font, "空位", 15, T.Ink, true); OpusUi.Band(cell.name.rectTransform, 32, 2, 39, 22);
            cell.ready = OpusUi.Text("NextReady", rect, font, "", 13, T.Secondary, true); RightBand(cell.ready.rectTransform, 3, 2, 36, 22); cell.ready.alignment = TextAlignmentOptions.MidlineRight;
            cell.hpFill = Bar("HpBar", rect, 36, T.Hp); cell.mpFill = Bar("MpBar", rect, 56, T.Blue);
            cell.hp = OpusUi.Text("ActualHP", rect, font, "", 13, T.Ink, true); OpusUi.Band(cell.hp.rectTransform, 5, 27, 4, 20);
            cell.mp = OpusUi.Text("ActualMP", rect, font, "", 13, T.Ink, true); OpusUi.Band(cell.mp.rectTransform, 5, 47, 4, 20);
            cell.status = OpusUi.Text("ActualStatusAndCoverage", rect, font, "", 12, T.Secondary, true); OpusUi.Band(cell.status.rectTransform, 5, 69, 42, 20);
            cell.coords = OpusUi.Text("CoreCoordinates", rect, font, "(" + row + "," + column + ")", 12, T.Secondary, true);
            RightBand(cell.coords.rectTransform, 3, 69, 36, 20); cell.coords.alignment = TextAlignmentOptions.MidlineRight;
            cell.extraCorners = new Image[4];
            for (int i = 0; i < 4; i++)
            {
                var corner = OpusUi.Panel("AdditionalEffectCorner" + i, rect, T.Blue);
                corner.rectTransform.anchorMin = corner.rectTransform.anchorMax = new Vector2(i % 2, i / 2);
                corner.rectTransform.pivot = new Vector2(i % 2, i / 2);
                corner.rectTransform.anchoredPosition = new Vector2(i % 2 == 0 ? 2 : -2, i / 2 == 0 ? 2 : -2);
                corner.rectTransform.sizeDelta = new Vector2(12, 2); cell.extraCorners[i] = corner;
            }
            cells[row * 3 + column] = cell;
        }
        private static void RightBand(RectTransform rect, float right, float y, float width, float height)
        { rect.anchorMin = rect.anchorMax = Vector2.one; rect.pivot = Vector2.one; rect.anchoredPosition = new Vector2(-right, -y); rect.sizeDelta = new Vector2(width, height); }
        private static Image Bar(string name, RectTransform parent, float y, Color color)
        {
            var track = OpusUi.Panel(name, parent, T.Line); OpusUi.Band(track.rectTransform, 5, y, 5, 7);
            var fill = OpusUi.Panel("ActualFill", track.transform, new Color(color.r, color.g, color.b, .4f)); Ui.Stretch(fill.rectTransform); return fill;
        }
        public void Render(CombatSnapshot current, string team, CombatStepResult selected, string[] primary, bool historical)
        {
            var units = current?.units ?? Array.Empty<CombatUnitSnapshot>(); primary = primary ?? Array.Empty<string>();
            for (int i = 0; i < cells.Length; i++)
            {
                var cell = cells[i]; var unit = units.FirstOrDefault(item => item.team == team && item.row == i / 3 && item.column == i % 3);
                cell.id = unit?.id; bool present = unit != null; cell.button.interactable = present;
                foreach (var text in new[] { cell.hp, cell.mp, cell.status, cell.ready }) text.gameObject.SetActive(present);
                cell.portrait.gameObject.SetActive(present); cell.hpFill.transform.parent.gameObject.SetActive(present); cell.mpFill.transform.parent.gameObject.SetActive(present);
                foreach (var corner in cell.extraCorners) corner.gameObject.SetActive(false);
                if (!present) { cell.name.text = "空位"; cell.frame.color = T.Line; cell.body.color = T.Sunken; continue; }
                bool actor = selected?.actorId == unit.id, covered = primary.Contains(unit.id);
                var effects = (selected?.effects ?? Array.Empty<CombatEffect>()).Where(effect => effect.targetId == unit.id).ToArray();
                bool extra = effects.Length > 0 && !covered;
                var color = effects.Length == 0 ? T.Red : EffectColor(effects[0].kind);
                cell.frame.color = covered ? color : actor ? T.Amber : T.Line;
                cell.body.color = unit.hp <= 0 ? T.Top : actor ? T.Raised : T.Panel;
                foreach (var corner in cell.extraCorners) corner.gameObject.SetActive(extra);
                cell.name.text = OpusUi.Esc(unit.name); cell.name.color = actor ? T.Amber : unit.hp <= 0 ? T.Disabled : T.Ink;
                cell.name.fontStyle = unit.hp <= 0 ? FontStyles.Strikethrough : FontStyles.Bold;
                cell.ready.text = "W" + OpusUi.N(unit.nextActionTime);
                cell.hp.text = "HP " + unit.hp + "/" + unit.maxHp; cell.mp.text = "MP " + unit.mp + "/" + unit.maxMp;
                SetFill(cell.hpFill, unit.maxHp <= 0 ? 0 : (float)unit.hp / unit.maxHp); SetFill(cell.mpFill, unit.maxMp <= 0 ? 0 : (float)unit.mp / unit.maxMp);
                float ratio = unit.maxHp <= 0 ? 0 : (float)unit.hp / unit.maxHp;
                var hpColor = ratio > .5f ? T.Hp : ratio > .2f ? T.Amber : T.Red;
                cell.hpFill.color = new Color(hpColor.r, hpColor.g, hpColor.b, .4f);
                string delta = string.Join(" / ", effects.Where(effect => effect.hpDelta != 0 || effect.mpDelta != 0).Select(effect => effect.hpDelta != 0 ? Signed(effect.hpDelta) + "HP" : Signed(effect.mpDelta) + "MP"));
                cell.status.text = unit.hp <= 0 ? "倒下" : (actor ? "行动·" : "") + (covered ? "覆盖" + (selected?.targetId == unit.id ? "·锚 " : " ") : extra ? "附加 " : "")
                    + (!string.IsNullOrEmpty(delta) ? delta : OpusBattleText.ShortStatuses(unit.statuses));
                if (historical && (covered || actor || extra)) cell.status.text = "#" + selected.index + " " + cell.status.text;
                cell.status.color = covered ? color : extra ? T.Blue : T.Secondary;
                var sprite = portraits?.Invoke(unit.id); cell.portrait.sprite = sprite; cell.portrait.color = sprite == null ? T.Raised : Color.white;
                cell.fallback.gameObject.SetActive(sprite == null); cell.fallback.text = unit.name.Substring(0, 1);
            }
        }
        private static string Signed(int value) { return value > 0 ? "+" + value : value.ToString(); }
        internal static Color EffectColor(string kind) { return kind == "heal" ? T.Hp : kind == "restore_mp" || kind == "drain_mp" ? T.Blue : kind == "apply_status" || kind == "cleanse" || kind == "dispel" ? T.Purple : T.Red; }
        private static void SetFill(Image fill, float amount) { fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(amount), 1); fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero; }
    }
}
