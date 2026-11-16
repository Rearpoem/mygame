using UnityEngine;
using UnityEngine.UI;

namespace Prototype.UnityClient
{
    [RequireComponent(typeof(GridLayoutGroup))]
    public sealed class PrototypeMapGridSizer : MonoBehaviour
    {
        public int columns = 1;
        public int rows = 1;

        private void OnRectTransformDimensionsChange() { Resize(); }
        private void OnEnable() { Resize(); }
        private void LateUpdate() { Resize(); }

        private void Resize()
        {
            var grid = GetComponent<GridLayoutGroup>();
            var rect = (RectTransform)transform;
            if (grid == null || columns < 1 || rows < 1) return;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;
            grid.cellSize = new Vector2(
                Mathf.Max(1, (rect.rect.width - grid.spacing.x * (columns - 1)) / columns),
                Mathf.Max(1, (rect.rect.height - grid.spacing.y * (rows - 1)) / rows));
        }
    }
}
