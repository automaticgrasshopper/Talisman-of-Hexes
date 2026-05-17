using UnityEngine;
using System.Collections;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Nova
{
    public class HelperNavigationController : BaseNavigationPanel
    {
        [Header("帮助页面导航设置")]
        [Tooltip("是否完全禁用导航")]
        [SerializeField] private bool disableNavigationCompletely = true;

        private HelpViewController helpViewController;
        private bool isInitialized = false;

        // 重写面板属性
        public override string PanelName => "帮助页面";
        public override int Priority => PanelPriority.HELP;
        public override bool UseCustomNavigation => true;
        public override GameObject DefaultSelection => null;

        protected override void Start()
        {
            // 获取 HelpViewController 引用
            helpViewController = GetComponent<HelpViewController>();
            if (helpViewController == null)
            {
                helpViewController = GetComponentInParent<HelpViewController>();
            }

            // 设置面板属性
            panelName = "帮助页面";
            priority = PanelPriority.HELP;
            useCustomNavigation = true;

            defaultSelection = null;

            // 调用基类Start进行自动注册
            base.Start();

            isInitialized = true;

            if (GamePadNavigationManager.Instance != null && GamePadNavigationManager.Instance.enableDebugLogs)
            {
                Debug.Log($"[{PanelName}] 帮助页面导航控制器初始化完成");
            }
        }

        #region Unity 生命周期方法（不是重写）
        private void OnEnable()
        {
            if (!isInitialized) return;

            // 启用时立即禁用导航
            if (disableNavigationCompletely)
            {
                DisableNavigationCompletely();
            }

            if (GamePadNavigationManager.Instance != null && GamePadNavigationManager.Instance.enableDebugLogs)
            {
                Debug.Log($"[{PanelName}] 帮助页面启用，导航已禁用");
            }
        }

        private void OnDisable()
        {
            if (!isInitialized) return;

            // 禁用时恢复导航
            EnableNavigation();

            if (GamePadNavigationManager.Instance != null && GamePadNavigationManager.Instance.enableDebugLogs)
            {
                Debug.Log($"[{PanelName}] 帮助页面禁用，导航已恢复");
            }
        }

        private void Update()
        {
            if (!isInitialized) return;

            // 每帧都确保没有选中对象（额外的保护）
            if (disableNavigationCompletely && EventSystem.current != null &&
                EventSystem.current.currentSelectedGameObject != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }
        }
        #endregion

        #region INavigationPanel 接口实现
        public override void OnPanelActivated()
        {
            // 面板激活时完全禁用导航
            if (GamePadNavigationManager.Instance != null)
            {
                GamePadNavigationManager.Instance.SetIgnoreSubmit(true);
                GamePadNavigationManager.Instance.MarkUIInteracting(true);

                // 延迟清除选中状态，确保在布局完成后执行
                StartCoroutine(ClearSelectionAfterFrame());
            }

            if (GamePadNavigationManager.Instance != null && GamePadNavigationManager.Instance.enableDebugLogs)
            {
                Debug.Log($"[{PanelName}] 帮助页面激活，导航已完全禁用");
            }
        }

        public override void OnPanelDeactivated()
        {
            // 面板停用时恢复导航
            if (GamePadNavigationManager.Instance != null)
            {
                GamePadNavigationManager.Instance.SetIgnoreSubmit(false);
                GamePadNavigationManager.Instance.MarkUIInteracting(false);
            }

            if (GamePadNavigationManager.Instance != null && GamePadNavigationManager.Instance.enableDebugLogs)
            {
                Debug.Log($"[{PanelName}] 帮助页面停用，导航已恢复");
            }
        }

        public override void HandleCustomNavigation()
        {
            // 自定义导航逻辑：完全不做任何导航处理
            // 返回逻辑由 HelpViewController 的 ViewControllerBase 处理
        }

        public override bool ShouldIgnoreSubmit()
        {
            // 总是忽略确认键提交
            return true;
        }
        #endregion

        #region 清理选中状态
        private IEnumerator ClearSelectionAfterFrame()
        {
            // 等待一帧确保UI布局完成
            yield return null;

            // 清除EventSystem的选中状态
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }

            // 确保导航管理器也没有选中对象
            if (GamePadNavigationManager.Instance != null)
            {
                ClearNavigationManagerSelection();
            }
        }

        /// <summary>
        /// 强制清除导航管理器的选中状态
        /// </summary>
        private void ClearNavigationManagerSelection()
        {
            if (GamePadNavigationManager.Instance == null) return;

            try
            {
                // 使用反射强制设置私有字段
                var managerType = typeof(GamePadNavigationManager);
                var currentSelectedField = managerType.GetField("currentSelectedObject",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var lastSelectedField = managerType.GetField("lastSelectedObject",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var retryCountField = managerType.GetField("retryCount",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (currentSelectedField != null)
                {
                    currentSelectedField.SetValue(GamePadNavigationManager.Instance, null);
                }
                if (lastSelectedField != null)
                {
                    lastSelectedField.SetValue(GamePadNavigationManager.Instance, null);
                }
                if (retryCountField != null)
                {
                    retryCountField.SetValue(GamePadNavigationManager.Instance, 0);
                }

                // 重置保护状态
                GamePadNavigationManager.Instance.ResetProtectionState();
            }
            catch (System.Exception e)
            {
                if (GamePadNavigationManager.Instance.enableDebugLogs)
                {
                    Debug.LogWarning($"[{PanelName}] 清除导航管理器状态失败: {e.Message}");
                }
            }
        }
        #endregion

        #region 重写基类方法以确保纯净性
        /// <summary>
        /// 重写收集Selectables方法，确保不收集任何可交互元素
        /// </summary>
        protected override void CollectSelectables()
        {
            // 完全清空收集的列表
            collectedSelectables.Clear();

            if (GamePadNavigationManager.Instance != null && GamePadNavigationManager.Instance.enableDebugLogs)
            {
                Debug.Log($"[{PanelName}] 帮助页面已清空所有可交互元素");
            }
        }

        /// <summary>
        /// 重写验证方法，确保所有元素都被视为无效
        /// </summary>
        protected override bool IsSelectableValid(Selectable selectable)
        {
            // 帮助页面中所有Selectable都视为无效
            return false;
        }

        /// <summary>
        /// 重写可交互检查，确保所有元素都不可交互
        /// </summary>
        protected override bool IsSelectableInteractable(Selectable selectable)
        {
            // 帮助页面中所有Selectable都不可交互
            return false;
        }
        #endregion

        #region 公共API
        /// <summary>
        /// 完全禁用导航（用于外部调用）
        /// </summary>
        public void DisableNavigationCompletely()
        {
            disableNavigationCompletely = true;

            if (GamePadNavigationManager.Instance != null)
            {
                GamePadNavigationManager.Instance.SetIgnoreSubmit(true);
                GamePadNavigationManager.Instance.MarkUIInteracting(true);

                if (EventSystem.current != null)
                {
                    EventSystem.current.SetSelectedGameObject(null);
                }

                // 强制清除导航管理器状态
                ClearNavigationManagerSelection();
            }
        }

        /// <summary>
        /// 恢复导航（用于外部调用）
        /// </summary>
        public void EnableNavigation()
        {
            disableNavigationCompletely = false;

            if (GamePadNavigationManager.Instance != null)
            {
                GamePadNavigationManager.Instance.SetIgnoreSubmit(false);
                GamePadNavigationManager.Instance.MarkUIInteracting(false);
            }
        }

        /// <summary>
        /// 获取当前导航禁用状态
        /// </summary>
        public bool GetNavigationDisabledState()
        {
            return disableNavigationCompletely;
        }

        /// <summary>
        /// 手动触发面板激活（用于外部调用）
        /// </summary>
        public void ManualActivatePanel()
        {
            OnPanelActivated();
        }

        /// <summary>
        /// 手动触发面板停用（用于外部调用）
        /// </summary>
        public void ManualDeactivatePanel()
        {
            OnPanelDeactivated();
        }
        #endregion

        #region 调试方法
        [ContextMenu("打印导航状态")]
        public void PrintNavigationStatus()
        {
            Debug.Log($"[{PanelName}] 导航状态报告:");
            Debug.Log($"- 初始化: {isInitialized}");
            Debug.Log($"- 完全禁用导航: {disableNavigationCompletely}");
            Debug.Log($"- 当前选中对象: {EventSystem.current?.currentSelectedGameObject?.name ?? "null"}");
            Debug.Log($"- 导航管理器实例: {GamePadNavigationManager.Instance != null}");

            if (GamePadNavigationManager.Instance != null)
            {
                Debug.Log($"- 忽略提交: {GamePadNavigationManager.Instance.ShouldIgnoreSubmit()}");
                Debug.Log($"- 使用手柄: {GamePadNavigationManager.Instance.IsUsingGamepad()}");
            }
        }

        [ContextMenu("强制禁用导航")]
        public void ForceDisableNavigation()
        {
            DisableNavigationCompletely();
            Debug.Log($"[{PanelName}] 已强制禁用导航");
        }

        [ContextMenu("强制恢复导航")]
        public void ForceEnableNavigation()
        {
            EnableNavigation();
            Debug.Log($"[{PanelName}] 已强制恢复导航");
        }
        #endregion
    }
}
