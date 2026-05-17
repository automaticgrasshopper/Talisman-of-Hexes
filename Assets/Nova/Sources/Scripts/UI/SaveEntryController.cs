using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Nova
{
    public class SaveEntryController : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private Text idText;
        private TextOutline idTextOutline;
        private Text dateText;

        private GameObject latest;
        private Button thumbnailButton;
        private Image thumbnailImage;
        private Sprite defaultThumbnailSprite;
        private Button deleteButton;

        private Color saveTextColor;
        private Color saveTextOutlineColor;
        private Color loadTextColor;
        private Color loadTextOutlineColor;

        private bool deleteButtonEnabled;

        private UnityAction<PointerEventData> onPointerEnter;
        private UnityAction<PointerEventData> onPointerExit;

        [Header("高亮设置")]
        [Tooltip("高亮时使用的材质")]
        [SerializeField] private Material highlightMaterial;

        [Tooltip("高亮动画速度")]
        [SerializeField] private float highlightAnimationSpeed = 0.5f;

        [Tooltip("是否启用高亮动画")]
        [SerializeField] private bool enableHighlightAnimation = true;

        [Tooltip("鼠标优先模式")]
        [SerializeField] private bool mousePriorityMode = true;

        private Material defaultMaterial;
        private bool isHighlighted = false;
        private bool isMouseHovered = false;
        private float highlightIntensity = 0f;
        private Material currentHighlightMaterial;

        // 高亮材质属性名称
        private static readonly int HighlightIntensityProperty = Shader.PropertyToID("_HighlightIntensity");
        private static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");
        private static readonly int HighlightColorProperty = Shader.PropertyToID("_HighlightColor");

        // 高亮状态枚举
        public enum HighlightState
        {
            None,
            Gamepad,
            Mouse,
            Both
        }

        private HighlightState currentHighlightState = HighlightState.None;

        private void Awake()
        {
            var container = transform.Find("Container");
            var header = container.Find("Header");
            idText = header.Find("Id").GetComponent<Text>();
            idTextOutline = header.Find("Id").GetComponent<TextOutline>();
            dateText = header.Find("Date").GetComponent<Text>();
            latest = container.Find("Latest").gameObject;
            thumbnailButton = container.GetComponent<Button>();
            thumbnailImage = container.Find("Image").GetComponent<Image>();
            defaultThumbnailSprite = thumbnailImage.sprite;
            deleteButton = transform.Find("DeleteButton").GetComponent<Button>();

            // 保存默认材质
            defaultMaterial = thumbnailImage.material;

            // 创建高亮材质的实例，避免共享材质
            if (highlightMaterial != null)
            {
                currentHighlightMaterial = new Material(highlightMaterial);
                // 设置默认的高亮强度为0
                currentHighlightMaterial.SetFloat(HighlightIntensityProperty, 0f);
            }

            ColorUtility.TryParseHtmlString("#33FF33FF", out saveTextColor);
            ColorUtility.TryParseHtmlString("#66FF6643", out saveTextOutlineColor);
            ColorUtility.TryParseHtmlString("#FF3333FF", out loadTextColor);
            ColorUtility.TryParseHtmlString("#FF666643", out loadTextOutlineColor);
        }

        private void Update()
        {
            UpdateHighlightAnimation();
        }

        private void UpdateHighlightAnimation()
        {
            if (currentHighlightMaterial == null) return;

            // 计算目标高亮强度
            float targetIntensity = CalculateTargetIntensity();

            if (enableHighlightAnimation)
            {
                // 使用动画过渡
                highlightIntensity = Mathf.Lerp(highlightIntensity, targetIntensity, Time.unscaledDeltaTime * highlightAnimationSpeed);

                // 如果接近目标值，直接设置
                if (Mathf.Abs(highlightIntensity - targetIntensity) < 0.01f)
                {
                    highlightIntensity = targetIntensity;
                }
            }
            else
            {
                // 无动画，直接设置
                highlightIntensity = targetIntensity;
            }

            // 应用高亮强度
            currentHighlightMaterial.SetFloat(HighlightIntensityProperty, highlightIntensity);

            // 管理材质切换
            UpdateMaterialState();
        }

        /// <summary>
        /// 计算目标高亮强度（即时响应逻辑）
        /// </summary>
        private float CalculateTargetIntensity()
        {
            // 更新高亮状态
            UpdateHighlightState();

            // 根据状态返回相应的强度
            switch (currentHighlightState)
            {
                case HighlightState.Mouse:
                    return 1.0f; // 鼠标悬停时完全高亮
                case HighlightState.Gamepad:
                    return 1.0f; // 手柄选中时完全高亮
                case HighlightState.Both:
                    return 1.0f; // 两者都有时完全高亮
                case HighlightState.None:
                default:
                    return 0.0f; // 无高亮
            }
        }

        /// <summary>
        /// 更新高亮状态（即时响应逻辑）
        /// </summary>
        private void UpdateHighlightState()
        {
            if (isMouseHovered)
            {
                // 鼠标悬停时，鼠标优先
                currentHighlightState = HighlightState.Mouse;
            }
            else if (isHighlighted)
            {
                // 没有鼠标悬停，但手柄选中
                currentHighlightState = HighlightState.Gamepad;
            }
            else
            {
                // 都没有
                currentHighlightState = HighlightState.None;
            }
        }

        /// <summary>
        /// 更新材质状态
        /// </summary>
        private void UpdateMaterialState()
        {
            // 如果有高亮（任何类型），使用高亮材质
            if (currentHighlightState != HighlightState.None && highlightIntensity > 0.01f)
            {
                if (thumbnailImage.material != currentHighlightMaterial)
                {
                    thumbnailImage.material = currentHighlightMaterial;
                }
            }
            // 如果没有高亮，切换回默认材质
            else if (thumbnailImage.material != defaultMaterial)
            {
                thumbnailImage.material = defaultMaterial;
            }
        }

        public SaveViewMode mode
        {
            set
            {
                idText.color = value == SaveViewMode.Save ? saveTextColor : loadTextColor;
                idTextOutline.effectColor = value == SaveViewMode.Save ? saveTextOutlineColor : loadTextOutlineColor;

                // 根据模式更新高亮颜色
                UpdateHighlightColors(value);
            }
        }

        private void UpdateHighlightColors(SaveViewMode viewMode)
        {
            if (currentHighlightMaterial == null) return;

            Color baseColor = viewMode == SaveViewMode.Save ? saveTextColor : loadTextColor;
            Color highlightColor = viewMode == SaveViewMode.Save ?
                new Color(saveTextColor.r * 1.5f, saveTextColor.g * 1.5f, saveTextColor.b * 1.5f, 1f) :
                new Color(loadTextColor.r * 1.5f, loadTextColor.g * 1.5f, loadTextColor.b * 1.5f, 1f);

            currentHighlightMaterial.SetColor(BaseColorProperty, baseColor);
            currentHighlightMaterial.SetColor(HighlightColorProperty, highlightColor);
        }

        private static void InitButton(Button button, UnityAction onClick)
        {
            button.onClick.RemoveAllListeners();
            if (onClick != null)
            {
                button.onClick.AddListener(onClick);
                button.interactable = true;
            }
            else
            {
                button.interactable = false;
            }
        }

        public void InitAsPreview(Sprite newThumbnailSprite, UnityAction onThumbnailButtonClicked)
        {
            idText.text = "--";
            dateText.gameObject.SetActive(false);

            latest.SetActive(false);
            deleteButtonEnabled = false;
            HideDeleteButton();
            thumbnailImage.sprite = newThumbnailSprite == null ? defaultThumbnailSprite : newThumbnailSprite;

            // 预览条目不使用高亮
            SetHighlight(false);
            SetMouseHover(false);

            InitButton(deleteButton, null);
            InitButton(thumbnailButton, onThumbnailButtonClicked);
            onPointerEnter = null;
            onPointerExit = null;
        }

        public void Init(string newIDText, string newDateText, bool isLatest, Sprite newThumbnailSprite,
            UnityAction onDeleteButtonClicked, UnityAction onThumbnailButtonClicked,
            UnityAction<PointerEventData> onThumbnailButtonEnter, UnityAction<PointerEventData> onThumbnailButtonExit)
        {
            idText.text = newIDText;
            dateText.gameObject.SetActive(true);
            dateText.text = newDateText;

            if (newThumbnailSprite == null)
            {
                latest.SetActive(false);
                deleteButtonEnabled = false;
                HideDeleteButton();
                thumbnailImage.sprite = defaultThumbnailSprite;
            }
            else
            {
                latest.SetActive(isLatest);
                deleteButtonEnabled = true;
                HideDeleteButton();
                thumbnailImage.sprite = newThumbnailSprite;
            }

            InitButton(deleteButton, onDeleteButtonClicked);
            InitButton(thumbnailButton, onThumbnailButtonClicked);
            onPointerEnter = onThumbnailButtonEnter;
            onPointerExit = onThumbnailButtonExit;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            onPointerEnter?.Invoke(eventData);
            SetMouseHover(true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            onPointerExit?.Invoke(eventData);
            SetMouseHover(false);
        }

        public void ShowDeleteButton()
        {
            //删除按钮
             if (deleteButtonEnabled)
            {
                deleteButton.gameObject.SetActive(true);
             }
        }

        public void HideDeleteButton()
        {
            deleteButton.gameObject.SetActive(false);
        }

        /// <summary>
        /// 设置手柄导航高亮
        /// </summary>
        public void SetHighlight(bool highlight)
        {
            isHighlighted = highlight;

            // 立即更新状态
            UpdateHighlightState();

            // 如果不启用动画，立即更新材质
            if (!enableHighlightAnimation)
            {
                ForceHighlight(IsHighlighted());
            }
        }

        /// <summary>
        /// 设置鼠标悬停状态
        /// </summary>
        public void SetMouseHover(bool hover)
        {
            isMouseHovered = hover;

            // 立即更新状态
            UpdateHighlightState();

            // 如果不启用动画，立即更新材质
            if (!enableHighlightAnimation)
            {
                ForceHighlight(IsHighlighted());
            }
        }

        /// <summary>
        /// 获取当前高亮状态
        /// </summary>
        public bool IsHighlighted()
        {
            return currentHighlightState != HighlightState.None;
        }

        /// <summary>
        /// 获取详细的高亮状态
        /// </summary>
        public HighlightState GetHighlightState()
        {
            return currentHighlightState;
        }

        /// <summary>
        /// 设置高亮材质（可在运行时修改）
        /// </summary>
        public void SetHighlightMaterial(Material newHighlightMaterial)
        {
            if (newHighlightMaterial != null)
            {
                currentHighlightMaterial = new Material(newHighlightMaterial);
                // 重置高亮强度
                currentHighlightMaterial.SetFloat(HighlightIntensityProperty, 0f);

                // 如果当前需要高亮，立即应用
                if (IsHighlighted())
                {
                    ForceHighlight(true);
                }
            }
        }

        /// <summary>
        /// 获取当前使用的高亮材质
        /// </summary>
        public Material GetHighlightMaterial()
        {
            return currentHighlightMaterial;
        }

        /// <summary>
        /// 强制立即更新高亮状态（无动画）
        /// </summary>
        public void ForceHighlight(bool highlight)
        {
            if (currentHighlightMaterial != null)
            {
                thumbnailImage.material = currentHighlightMaterial;
                currentHighlightMaterial.SetFloat(HighlightIntensityProperty, highlight ? 1f : 0f);
                highlightIntensity = highlight ? 1f : 0f;
            }
            else if (!highlight)
            {
                thumbnailImage.material = defaultMaterial;
                highlightIntensity = 0f;
            }
        }

        /// <summary>
        /// 设置高亮动画速度
        /// </summary>
        public void SetHighlightAnimationSpeed(float speed)
        {
            highlightAnimationSpeed = Mathf.Max(0.1f, speed);
        }

        /// <summary>
        /// 启用或禁用高亮动画
        /// </summary>
        public void SetHighlightAnimationEnabled(bool enabled)
        {
            enableHighlightAnimation = enabled;

            // 如果禁用动画，立即设置最终状态
            if (!enabled)
            {
                ForceHighlight(IsHighlighted());
            }
        }

        /// <summary>
        /// 设置鼠标优先模式
        /// </summary>
        public void SetMousePriorityMode(bool enabled)
        {
            mousePriorityMode = enabled;

            // 立即更新状态
            UpdateHighlightState();

            if (!enableHighlightAnimation)
            {
                ForceHighlight(IsHighlighted());
            }
        }

        /// <summary>
        /// 立即更新高亮状态（用于外部强制刷新）
        /// </summary>
        public void RefreshHighlightImmediate()
        {
            UpdateHighlightState();
            ForceHighlight(IsHighlighted());
        }

        private void OnDestroy()
        {
            // 清理创建的高亮材质实例
            if (currentHighlightMaterial != null && currentHighlightMaterial != highlightMaterial)
            {
                DestroyImmediate(currentHighlightMaterial);
            }
        }
    }
}
