using TMPro;
using UnityEngine;

namespace Nova
{
    /// <summary>
    /// 视频字幕投影样式（独立于全局 TMP 共享材质）。
    /// 挂在视频字幕 prefab 的 TMP_Text GameObject 上，运行时通过 tmp.fontMaterial
    /// 拿到 per-instance 材质副本来设置 Underlay（TMP 内建投影），不影响其它文本框。
    /// 在 Editor 下也实时生效（[ExecuteAlways] + OnValidate）。
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(TMP_Text))]
    [DisallowMultipleComponent]
    public class VideoSubtitleStyle : MonoBehaviour
    {
        [Header("投影开关")]
        [Tooltip("关闭则禁用 UNDERLAY_ON 关键字，TMP 完全不渲染投影")]
        public bool enableShadow = true;

        [Header("投影颜色")]
        [Tooltip("投影颜色（含 alpha=不透明度，类似 PS 投影的不透明度）")]
        public Color shadowColor = new Color(0f, 0f, 0f, 0.85f);

        [Header("投影位置")]
        [Tooltip("X 偏移（正值=向右），TMP 单位约为像素 / 字号比例")]
        [Range(-1f, 1f)] public float offsetX = 0.6f;
        [Tooltip("Y 偏移（正值=向上，PS 习惯一般用负值=向下偏）")]
        [Range(-1f, 1f)] public float offsetY = -0.6f;

        [Header("投影大小 / 柔度")]
        [Tooltip("投影膨胀（类似 PS 投影里的「扩展」）。\n正值=投影变粗变实，负值=变细变瘦")]
        [Range(-1f, 1f)] public float dilate = 0.2f;

        [Tooltip("投影柔化（类似 PS 投影里的「大小」=模糊半径）。\n0=硬边阴影，1=最柔散")]
        [Range(0f, 1f)] public float softness = 0.25f;

        private TMP_Text tmp;
        private Material instancedMaterial;

        private const string KEYWORD = "UNDERLAY_ON";
        private static readonly int ID_UnderlayColor = Shader.PropertyToID("_UnderlayColor");
        private static readonly int ID_UnderlayOffsetX = Shader.PropertyToID("_UnderlayOffsetX");
        private static readonly int ID_UnderlayOffsetY = Shader.PropertyToID("_UnderlayOffsetY");
        private static readonly int ID_UnderlayDilate = Shader.PropertyToID("_UnderlayDilate");
        private static readonly int ID_UnderlaySoftness = Shader.PropertyToID("_UnderlaySoftness");

        private void OnEnable()
        {
            Apply();
        }

        private void OnValidate()
        {
            // Inspector 调整时实时刷新（Editor 也走 Apply，因为 ExecuteAlways）
            if (isActiveAndEnabled) Apply();
        }

        private void EnsureRefs()
        {
            if (tmp == null) tmp = GetComponent<TMP_Text>();
            if (tmp == null) return;

            // 关键：用 fontMaterial 而不是 fontSharedMaterial。
            // 第一次访问 fontMaterial 时 TMP 会克隆一份独立材质并替换 renderer 上的引用，
            // 之后改它就只影响这一个 TMP，全局共享材质不受影响。
            if (instancedMaterial != tmp.fontMaterial)
            {
                instancedMaterial = tmp.fontMaterial;
            }
        }

        /// <summary>
        /// 应用当前参数到 per-instance 材质上。Inspector 改值时自动调用。
        /// 外部也可以在运行时主动调（比如根据视频/字号自适应阴影大小）。
        /// </summary>
        public void Apply()
        {
            EnsureRefs();
            if (instancedMaterial == null) return;

            if (enableShadow)
            {
                instancedMaterial.EnableKeyword(KEYWORD);
                instancedMaterial.SetColor(ID_UnderlayColor, shadowColor);
                instancedMaterial.SetFloat(ID_UnderlayOffsetX, offsetX);
                instancedMaterial.SetFloat(ID_UnderlayOffsetY, offsetY);
                instancedMaterial.SetFloat(ID_UnderlayDilate, dilate);
                instancedMaterial.SetFloat(ID_UnderlaySoftness, softness);
            }
            else
            {
                instancedMaterial.DisableKeyword(KEYWORD);
            }
        }
    }
}
