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

        [Header("转角菱形 marker")]
        [Tooltip("L 转角处绘制的小菱形大小（px）。0 = 不绘制")]
        public float junctionSize = 0f;

        // ── 运行时 ─────────────────────────────────────────────────────────
        [System.NonSerialized] public Vector2 startPoint;
        [System.NonSerialized] public Vector2 endPoint;

        // 由控制器写入：竖向 riser 的 X 坐标。NaN = 不覆盖，BuildPolyline 自己取中点。
        // 对前进型边（target.col > source.col）控制器统一设为"目标列正左侧"，
        // 让进入同一列的所有边共享 riser，自然形成 bundling。
        [System.NonSerialized] public float midXOverride = float.NaN;

        private LineState _state = LineState.Visited;
        private readonly List<Vector2> _poly = new List<Vector2>();
        // L 折线的两个转角中心（midX, startY) 和 (midX, endY)；OnPopulateMesh 用来画菱形。
        private readonly List<Vector2> _corners = new List<Vector2>();

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

            // 在两个 L 转角处叠绘小菱形（state != Locked 时；Locked 视觉是虚线，不加 marker）
            if (junctionSize > 0f && _state != LineState.Locked && _corners.Count > 0)
            {
                foreach (var c in _corners)
                    DrawDiamond(vh, c, junctionSize, col);
            }
        }

        // ── 路径构建：双弧 L 形 ────────────────────────────────────────────
        private void BuildPolyline()
        {
            _poly.Clear();
            _corners.Clear();

            float dx = endPoint.x - startPoint.x;
            float dy = endPoint.y - startPoint.y;

            if (Mathf.Abs(dy) < 1f)
            {
                _poly.Add(startPoint);
                _poly.Add(endPoint);
                return;
            }

            // midX：默认走 start/end 中点；midXOverride 有效时用之（列边界 rail，由控制器写入）
            float midX = !float.IsNaN(midXOverride)
                ? midXOverride
                : startPoint.x + dx * 0.5f;
            // 转角中心（用于绘制 marker）
            _corners.Add(new Vector2(midX, startPoint.y));
            _corners.Add(new Vector2(midX, endPoint.y));

            // signX 必须分别按两段算（midX 不一定在 start/end 中点时，两段方向可能不同）
            // 但对前进型边（rail 落在 start.x 和 end.x 之间）signX 仍一致，沿用旧公式。
            float signX = dx >= 0f ? 1f : -1f;
            float signY = dy >= 0f ? 1f : -1f;

            // 圆角夹紧：两侧水平段长度的较小者 + 垂直段的一半，再减 1px 余量
            float halfXMin = Mathf.Min(
                Mathf.Abs(midX - startPoint.x),
                Mathf.Abs(endPoint.x - midX));
            float r = Mathf.Max(0f, Mathf.Min(
                cornerRadius,
                halfXMin - 1f,
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

        // 在 center 处画一个边长 size 的小菱形（4 顶点 2 三角面）。
        // UV 用 (0.5, *) / (*, 0.5) 让 marker 落在 flow shader 的中段，避免边缘条带闪烁。
        private void DrawDiamond(VertexHelper vh, Vector2 center, float size, Color32 col)
        {
            float h = size * 0.5f;
            int b = vh.currentVertCount;
            vh.AddVert(MakeVert(center + new Vector2(0f, h),  col, new Vector2(0.5f, 1f)));
            vh.AddVert(MakeVert(center + new Vector2(h, 0f),  col, new Vector2(1f, 0.5f)));
            vh.AddVert(MakeVert(center + new Vector2(0f, -h), col, new Vector2(0.5f, 0f)));
            vh.AddVert(MakeVert(center + new Vector2(-h, 0f), col, new Vector2(0f, 0.5f)));
            vh.AddTriangle(b,     b + 1, b + 2);
            vh.AddTriangle(b,     b + 2, b + 3);
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
        // OnTransformParentChanged 只在父真正"变化"时触发。控制器是先 SetParent 再 AddComponent，
        // 组件加进来时父没变化 → 事件不会触发 → 第一帧 rect 是默认 100×100，
        // 网格顶点画在错的位置（要拖一下 ScrollRect 才会触发 UI 重建、走 OnTransformParentChanged）。
        // 修复：OnEnable 时再撑满一次，覆盖默认 100×100。
        protected override void OnEnable()
        {
            base.OnEnable();
            FillParent();
        }

        protected override void OnTransformParentChanged()
        {
            base.OnTransformParentChanged();
            FillParent();
        }

        private void FillParent()
        {
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
