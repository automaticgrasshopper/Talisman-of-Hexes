using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace Nova
{
    /// <summary>
    /// Hover-driven side navigation menu. Mouse enters the Handle rect (or Panel rect when open)
    /// → slide out via DOTween. Mouse leaves both → slide back. Clicking an action button
    /// opens the corresponding view and force-closes the menu (with a brief reopen cooldown
    /// to avoid hover-flicker during the view-transition).
    /// </summary>
    public class SideMenuController : MonoBehaviour
    {
        [Header("Animation (anchored position of root)")]
        [SerializeField] private RectTransform root;
        [SerializeField] private Vector2 closedPosition = Vector2.zero;
        [SerializeField] private Vector2 openPosition = new Vector2(130f, 0f);
        [SerializeField] private float duration = 0.3f;
        [SerializeField] private Ease ease = Ease.OutCubic;

        [Header("Hover hit rects")]
        [Tooltip("Always-visible handle. Mouse enter here triggers Open even when closed.")]
        [SerializeField] private RectTransform handleRect;
        [Tooltip("The slide-out panel. Counted as hover area only while open.")]
        [SerializeField] private RectTransform panelRect;
        [Tooltip("打开后，panel 的命中区域向四周外扩的像素，减少误操作")]
        [SerializeField] private float panelHoverPadding = 20f;

        [Header("Action buttons (4 icons)")]
        [SerializeField] private Button btnFlowchart;
        [SerializeField] private Button btnLog;
        [SerializeField] private Button btnGallery;
        [SerializeField] private Button btnConfig;

        [Header("Audio (uses Nova UISound volume)")]
        [SerializeField] private AudioClip slideOpenSound;
        [SerializeField] private AudioClip slideCloseSound;
        [SerializeField] private AudioClip buttonClickSound;

        private const float CLICK_COOLDOWN = 0.8f;

        private ViewManager viewManager;
        private Camera uiCamera;
        private bool isOpen;
        private bool wasHover;
        private float clickCooldown;
        private Tween slideTween;

        // 视频播放 / 小游戏期间禁用侧边菜单（不弹出、按钮不响应），避免玩家在中断流程中切走视图
        private VideoController videoController;
        private PrefabLoader prefabLoader;

        /// <summary>
        /// 是否处于阻塞态：视频在播 或 prefabLoader 上挂着小游戏 prefab。
        /// </summary>
        private bool IsBlocked()
        {
            if (videoController == null) videoController = FindObjectOfType<VideoController>();
            if (videoController != null && videoController.IsVideoActive) return true;

            if (prefabLoader == null) prefabLoader = FindObjectOfType<PrefabLoader>();
            if (prefabLoader != null && !string.IsNullOrEmpty(prefabLoader.currentPrefabName)) return true;

            return false;
        }

        private void Awake()
        {
            viewManager = Utils.FindViewManager();

            var canvas = GetComponentInParent<Canvas>();
            uiCamera = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                ? canvas.worldCamera
                : null;

            if (root != null)
            {
                root.anchoredPosition = closedPosition;
            }

            if (btnFlowchart != null) btnFlowchart.onClick.AddListener(OnFlowchartClick);
            if (btnLog != null)       btnLog.onClick.AddListener(OnLogClick);
            if (btnGallery != null)   btnGallery.onClick.AddListener(OnGalleryClick);
            if (btnConfig != null)    btnConfig.onClick.AddListener(OnConfigClick);
        }

        private void OnDisable()
        {
            slideTween?.Kill();
            if (root != null) root.anchoredPosition = closedPosition;
            isOpen = false;
            wasHover = false;
            clickCooldown = 0f;
        }

        private void Update()
        {
            if (clickCooldown > 0f)
            {
                clickCooldown -= Time.unscaledDeltaTime;
                return;
            }

            // 视频 / 小游戏中：强制收起，并忽略 hover 触发的开启
            if (IsBlocked())
            {
                if (isOpen)
                {
                    wasHover = false;
                    Close();
                }
                return;
            }

            bool over = IsMouseOver();
            if (over == wasHover) return;
            wasHover = over;
            if (over) Open();
            else Close();
        }

        private bool IsMouseOver()
        {
            // 使用 Nova 的输入封装（项目启用了 Input System Package，不能用 UnityEngine.Input）
            Vector2 mouse = RealInput.mousePosition;
            if (float.IsPositiveInfinity(mouse.x)) return false; // 失焦或无鼠标
            if (handleRect != null &&
                RectTransformUtility.RectangleContainsScreenPoint(handleRect, mouse, uiCamera))
            {
                return true;
            }
            if (isOpen && panelRect != null && PanelContainsPadded(mouse))
            {
                return true;
            }
            return false;
        }

        private bool PanelContainsPadded(Vector2 screenPoint)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    panelRect, screenPoint, uiCamera, out Vector2 local))
            {
                return false;
            }
            var r = panelRect.rect;
            float p = panelHoverPadding;
            return local.x >= r.xMin - p && local.x <= r.xMax + p
                && local.y >= r.yMin - p && local.y <= r.yMax + p;
        }

        private void Open()
        {
            if (isOpen || root == null) return;
            isOpen = true;
            slideTween?.Kill();
            slideTween = root.DOAnchorPos(openPosition, duration).SetEase(ease).SetUpdate(true);
            PlaySound(slideOpenSound);
        }

        private void Close()
        {
            if (!isOpen || root == null) return;
            isOpen = false;
            slideTween?.Kill();
            slideTween = root.DOAnchorPos(closedPosition, duration).SetEase(ease).SetUpdate(true);
            PlaySound(slideCloseSound);
        }

        private void PlaySound(AudioClip clip)
        {
            if (clip == null || viewManager == null) return;
            viewManager.TryPlaySound(clip);
        }

        private void OnFlowchartClick()
        {
            if (IsBlocked()) return;
            PlaySound(buttonClickSound);
            var ctrl = viewManager != null ? viewManager.GetController<FlowChartController>() : null;
            if (ctrl != null) ctrl.Show(true, null);
            BeginCloseAfterAction();
        }

        private void OnLogClick()
        {
            if (IsBlocked()) return;
            PlaySound(buttonClickSound);
            var ctrl = viewManager != null ? viewManager.GetController<LogController>() : null;
            if (ctrl != null) ctrl.Show();
            BeginCloseAfterAction();
        }

        private void OnGalleryClick()
        {
            if (IsBlocked()) return;
            // Placeholder — see-and-hear gallery (见闻) not yet implemented.
            PlaySound(buttonClickSound);
            BeginCloseAfterAction();
        }

        private void OnConfigClick()
        {
            if (IsBlocked()) return;
            PlaySound(buttonClickSound);
            var ctrl = viewManager != null ? viewManager.GetController<ConfigViewController>() : null;
            if (ctrl != null) ctrl.Show();
            BeginCloseAfterAction();
        }

        private void BeginCloseAfterAction()
        {
            clickCooldown = CLICK_COOLDOWN;
            wasHover = false;
            Close();
        }
    }
}
