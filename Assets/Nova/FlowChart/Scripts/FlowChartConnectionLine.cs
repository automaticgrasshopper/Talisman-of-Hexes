using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Nova
{
    /// <summary>
    /// L 形折线 + 圆弧连线，移植自 MovieGame/ConnectionLine.cs。
    /// 挂在 Content 下的子 GameObject 上，自动撑满 Content 大小。
    /// 调用 SetLine() 后刷新网格。
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class FlowChartConnectionLine : MaskableGraphic
    {
        public enum LineState
        {
            Locked,         // 未走过：灰色虚线
            Visited,        // 已走过但不在当前金色路径：白色实线
            CurrentBranch,  // 当前金色路径：金色流光
        }

        [Header("线条样式")]
        public float lineWidth = 4f;
        [Tooltip("转角圆弧半径，0 = 硬直角（推荐），>0 为圆角")]
        public float cornerRadius = 0f;
        [Tooltip("圆弧细分段数，推荐 8–16")]
        [Range(4, 32)]
        public int cornerSegments = 10;

        [Header("未走过（灰色虚线）")]
        public Color lockedColor = new Color(0.4f, 0.4f, 0.4f, 0.6f);
        public float dashLength = 40f;
        public float gapLength = 24f;

        [Header("已走过白色实线")]
        public Color visitedColor = Color.white;

        [Header("当前分支金色流光")]
        public Color currentBranchColor = Color.white;

        // ── 运行时 ─────────────────────────────────────────────────────────
        [System.NonSerialized] public Vector2 startPoint;
        [System.NonSerialized] public Vector2 endPoint;

        private LineState _state = LineState.Visited;
        private readonly List<Vector2> _poly = new List<Vector2>();

        /// <summary>
        /// 设置连线端点和状态。
        /// state=Locked → lockedMat（虚线）
        /// state=Visited → visitedMat（白色实线）
        /// state=CurrentBranch → currentBranchMat（金色流光实线）
        /// </summary>
        public void SetLine(Vector2 start, Vector2 end, LineState state,
            Material currentBranchMat, Material visitedMat, Material lockedMat)
        {
            startPoint = start;
            endPoint = end;
            _state = state;
            switch (state)
            {
                case LineState.CurrentBranch:
                    color = currentBranchColor;
                    material = currentBranchMat != null ? currentBranchMat : defaultGraphicMaterial;
                    break;
                case LineState.Visited:
                    color = visitedColor;
                    material = visitedMat != null ? visitedMat : defaultGraphicMaterial;
                    break;
                default:
                    color = lockedColor;
                    material = lockedMat != null ? lockedMat : defaultGraphicMaterial;
                    break;
            }
            SetVerticesDirty();
        }

        // ── 网格生成 ───────────────────────────────────────────────────────
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            BuildPolyline();
            if (_poly.Count < 2) return;
            Color32 col = (Color32)color;
            if (_state == LineState.Locked)
                DrawDashed(vh, col);
            else
                DrawSolid(vh, col);
        }

        // ── 路径构建：双弧 L 形 ────────────────────────────────────────────
        private void BuildPolyline()
        {
            _poly.Clear();

            float dx = endPoint.x - startPoint.x;
            float dy = endPoint.y - startPoint.y;

            if (Mathf.Abs(dy) < 1f)
            {
                _poly.Add(startPoint);
                _poly.Add(endPoint);
                return;
            }

            float midX = startPoint.x + dx * 0.5f;
            float signX = dx >= 0f ? 1f : -1f;
            float signY = dy >= 0f ? 1f : -1f;

            float r = Mathf.Max(0f, Mathf.Min(
                cornerRadius,
                Mathf.Abs(dx) * 0.5f - 1f,
                Mathf.Abs(dy) * 0.5f - 1f));

            if (r <= 0f)
            {
                _poly.Add(startPoint);
                _poly.Add(new Vector2(midX, startPoint.y));
                _poly.Add(new Vector2(midX, endPoint.y));
                _poly.Add(endPoint);
                return;
            }

            Vector2 center1 = new Vector2(midX - signX * r, startPoint.y + signY * r);
            Vector2 arc1In  = new Vector2(midX - signX * r, startPoint.y);
            Vector2 arc1Out = new Vector2(midX,              startPoint.y + signY * r);

            Vector2 center2 = new Vector2(midX + signX * r, endPoint.y - signY * r);
            Vector2 arc2In  = new Vector2(midX,              endPoint.y - signY * r);
            Vector2 arc2Out = new Vector2(midX + signX * r,  endPoint.y);

            float a1s, a1w, a2s, a2w;
            if      (signX > 0 && signY > 0) { a1s = 270f; a1w =  90f; a2s = 180f; a2w = -90f; }
            else if (signX > 0 && signY < 0) { a1s =  90f; a1w = -90f; a2s = 180f; a2w =  90f; }
            else if (signX < 0 && signY > 0) { a1s = 270f; a1w = -90f; a2s =   0f; a2w =  90f; }
            else                             { a1s =  90f; a1w =  90f; a2s =   0f; a2w = -90f; }

            _poly.Add(startPoint);
            _poly.Add(arc1In);
            AddArcSkipFirst(center1, r, a1s, a1w);
            _poly.Add(arc2In);
            AddArcSkipFirst(center2, r, a2s, a2w);
            _poly.Add(endPoint);
        }

        private void AddArcSkipFirst(Vector2 center, float r, float startDeg, float sweepDeg)
        {
            int steps = Mathf.Max(cornerSegments, 2);
            for (int i = 1; i <= steps; i++)
            {
                float rad = (startDeg + sweepDeg * (i / (float)steps)) * Mathf.Deg2Rad;
                _poly.Add(center + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * r);
            }
        }

        // ── 实线绘制 ───────────────────────────────────────────────────────
        private void DrawSolid(VertexHelper vh, Color32 col)
        {
            float hw = lineWidth * 0.5f;
            for (int i = 0; i < _poly.Count - 1; i++)
            {
                Vector2 p0 = _poly[i];
                Vector2 p1 = _poly[i + 1];
                Vector2 dir = p1 - p0;
                if (dir.sqrMagnitude < 0.0001f) continue;
                Vector2 perp = new Vector2(-dir.y, dir.x).normalized * hw;
                AddQuad(vh, p0, p1, perp, col);
            }
        }

        // ── 虚线绘制 ───────────────────────────────────────────────────────
        private void DrawDashed(VertexHelper vh, Color32 col)
        {
            float hw = lineWidth * 0.5f;
            float accumulated = 0f;
            bool inDash = true;
            float budget = dashLength;

            for (int i = 0; i < _poly.Count - 1; i++)
            {
                Vector2 p0 = _poly[i];
                Vector2 p1 = _poly[i + 1];
                Vector2 dir = p1 - p0;
                float len = dir.magnitude;
                if (len < 0.0001f) continue;

                Vector2 unit = dir / len;
                Vector2 perp = new Vector2(-unit.y, unit.x) * hw;
                float remain = len;
                Vector2 cursor = p0;

                while (remain > 0.0001f)
                {
                    float step = Mathf.Min(remain, budget - accumulated);
                    Vector2 next = cursor + unit * step;
                    if (inDash) AddQuad(vh, cursor, next, perp, col);
                    accumulated += step;
                    remain -= step;
                    cursor = next;
                    if (accumulated >= budget - 0.0001f)
                    {
                        accumulated = 0f;
                        inDash = !inDash;
                        budget = inDash ? dashLength : gapLength;
                    }
                }
            }
        }

        // ── 工具 ──────────────────────────────────────────────────────────
        private void AddQuad(VertexHelper vh, Vector2 p0, Vector2 p1, Vector2 perp, Color32 col)
        {
            int b = vh.currentVertCount;
            vh.AddVert(MakeVert(p0 + perp, col, new Vector2(0, 1)));
            vh.AddVert(MakeVert(p0 - perp, col, new Vector2(0, 0)));
            vh.AddVert(MakeVert(p1 + perp, col, new Vector2(1, 1)));
            vh.AddVert(MakeVert(p1 - perp, col, new Vector2(1, 0)));
            vh.AddTriangle(b,     b + 1, b + 2);
            vh.AddTriangle(b + 1, b + 3, b + 2);
        }

        private static UIVertex MakeVert(Vector2 pos, Color32 col, Vector2 uv)
        {
            UIVertex v = UIVertex.simpleVert;
            v.position = pos;
            v.color = col;
            v.uv0 = uv;
            return v;
        }

        // ── 自动撑满 Content ───────────────────────────────────────────────
        protected override void OnTransformParentChanged()
        {
            base.OnTransformParentChanged();
            if (rectTransform == null) return;
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.sizeDelta = Vector2.zero;
            rectTransform.anchoredPosition = Vector2.zero;
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            SetVerticesDirty();
        }
#endif
    }
}
