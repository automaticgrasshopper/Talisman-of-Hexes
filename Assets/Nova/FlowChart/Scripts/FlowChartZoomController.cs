using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Nova
{
    /// <summary>
    /// 管理流程图缩放。挂在 FlowChartView 或 ZoomPanel 上。
    /// Slider value: 10–100（整数），对应 zoom factor 0.1–1.0。
    /// 同时支持在 ScrollRect 范围内用鼠标滚轮缩放。
    /// </summary>
    public class FlowChartZoomController : MonoBehaviour
    {
        [SerializeField] private Slider slider;          // PersentSlider，range [10, 100]
        [SerializeField] private TMP_Text percentNumber;  // 显示百分比的文本
        [SerializeField] private ScrollRect scrollRect;  // 用来配合修正 content 范围
        [SerializeField] private RectTransform content;  // Content RectTransform

        [Header("鼠标滚轮缩放")]
        [Tooltip("每格滚轮对应 Slider 数值变化（slider 范围 10–100）")]
        [SerializeField] private float wheelStep = 5f;
        [Tooltip("仅在鼠标位于该范围内时响应滚轮（一般填 scrollRect.viewport）。留空则始终响应。")]
        [SerializeField] private RectTransform wheelHitRect;

        private Camera uiCamera;

        public Vector2 NominalContentSize { get; set; }  // 由 FlowChartController 在布局后填入

        public float CurrentZoom { get; private set; } = 1f;

        private void Awake()
        {
            slider.minValue = 10;
            slider.maxValue = 100;
            slider.wholeNumbers = true;
            slider.onValueChanged.AddListener(OnSliderChanged);

            // 缓存 UI 摄像机以便滚轮命中区判定
            var canvas = GetComponentInParent<Canvas>();
            uiCamera = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                ? canvas.worldCamera
                : null;

            // 若没有指定命中区，回退到 scrollRect.viewport
            if (wheelHitRect == null && scrollRect != null) wheelHitRect = scrollRect.viewport;
        }

        private void OnDestroy()
        {
            slider.onValueChanged.RemoveListener(OnSliderChanged);
        }

        private void Update()
        {
            // 仅当本组件所在 GameObject active 时才会触发 Update，关闭流程图后不响应
            if (Mouse.current == null) return;
            float scrollRaw = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Approximately(scrollRaw, 0f)) return;

            // Input System 的 scroll.y 一格通常是 120（与旧 Input.mouseScrollDelta 不同），归一化到 ±1
            float scroll = Mathf.Sign(scrollRaw);

            if (wheelHitRect != null)
            {
                Vector2 mouse = RealInput.mousePosition;
                if (float.IsPositiveInfinity(mouse.x)) return;
                if (!RectTransformUtility.RectangleContainsScreenPoint(wheelHitRect, mouse, uiCamera))
                    return;
            }

            float next = Mathf.Clamp(slider.value + scroll * wheelStep, slider.minValue, slider.maxValue);
            if (!Mathf.Approximately(next, slider.value))
                slider.value = next; // 触发 OnSliderChanged
        }

        /// <summary>每次 Show() 时调用，重置到 100%</summary>
        public void ResetToDefault()
        {
            slider.value = 100;
            // OnSliderChanged 会自动触发
        }

        private void OnSliderChanged(float value)
        {
            var zoom = Mathf.Clamp(value / 100f, 0.1f, 1f);
            CurrentZoom = zoom;
            ApplyZoom(zoom);
            UpdatePercentText(Mathf.RoundToInt(value));
        }

        private void ApplyZoom(float zoom)
        {
            if (content == null) return;
            // 只改 localScale，ScrollRect 会自动根据 scale 后的 bounds 计算滚动范围
            content.localScale = Vector3.one * zoom;
        }

        private void UpdatePercentText(int percent)
        {
            if (percentNumber == null) return;
            percentNumber.text = I18n.__("flowchart.percent.format", percent);
        }
    }
}
