using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Nova
{
    /// <summary>
    /// 倒计时条 shader 特效驱动：把 Slider.value（1→0）映射成 _Urgency（0→1），
    /// 让 TimeSliderFill 材质在剩余时间少时变红、加快脉冲。同时把数字 TMP_Text
    /// 颜色按危险度做暖→红插值。
    /// 挂在 TimeSlider 根节点；Fill image 引用 TimeSliderFill.mat。
    /// </summary>
    public class TimeSliderFX : MonoBehaviour
    {
        [Tooltip("剩余比例阈值：低于此值才开始 ramp urgency，0.5 = 剩 50% 时开始紧张")]
        public float urgencyStart = 0.5f;

        [Tooltip("文字暖色（充足时间）")]
        public Color textCalmColor = new Color(0.96f, 0.82f, 0.40f, 1f);

        [Tooltip("文字危险色（接近超时）")]
        public Color textDangerColor = new Color(1f, 0.36f, 0.22f, 1f);

        [SerializeField] private Slider slider;
        [SerializeField] private Image  fillImage;
        [SerializeField] private TMP_Text label;

        private Material matInstance;
        private static readonly int UrgencyID = Shader.PropertyToID("_Urgency");

        private void Awake()
        {
            if (slider    == null) slider    = GetComponentInChildren<Slider>(true);
            if (fillImage == null && slider != null && slider.fillRect != null)
                fillImage = slider.fillRect.GetComponent<Image>();
            if (label     == null) label     = GetComponentInChildren<TMP_Text>(true);

            // 实例化材质，避免修改共享资源
            if (fillImage != null && fillImage.material != null)
            {
                matInstance = new Material(fillImage.material);
                fillImage.material = matInstance;
            }
        }

        private void Update()
        {
            if (slider == null) return;
            float v = Mathf.Clamp01(slider.value);                  // 1 = 满，0 = 超时
            // remaining=1 时 urgency=0，remaining 跌到 urgencyStart 时开始 0→1 升高
            float urgency = (v >= urgencyStart) ? 0f
                          : 1f - (v / Mathf.Max(urgencyStart, 0.001f));

            if (matInstance != null)
                matInstance.SetFloat(UrgencyID, urgency);

            if (label != null)
                label.color = Color.Lerp(textCalmColor, textDangerColor, urgency);
        }

        private void OnDestroy()
        {
            if (matInstance != null) Destroy(matInstance);
        }
    }
}
