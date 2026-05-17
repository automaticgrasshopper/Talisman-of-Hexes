using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Nova
{
    public class TitleSelectionNavigationPanel : MonoBehaviour, INavigationPanel
    {
        [Header("导航设置")]
        [Tooltip("导航输入冷却时间（秒）")]
        [SerializeField] private float navigationCooldown = 0.3f;
        [Tooltip("启用调试日志")]
        [SerializeField] private bool enableDebugLogs = false;

        [Header("面板设置")]
        [Tooltip("面板名称")]
        [SerializeField] private string panelName = "TitleSelectionPanel";
        [Tooltip("面板优先级")]
        [SerializeField] private int priority = PanelPriority.SELECT_TITLE; // 最高优先级
        [Tooltip("使用自定义导航")]
        [SerializeField] private bool useCustomNavigation = true;

        // 私有字段
        private int currentSelectedIndex = -1;
        private float lastNavigationTime;
        private bool isActive = false;
        private bool usingGamepad = false;
        private bool isFirstNavigation = true;

        // 按钮管理
        private List<Button> titleButtons = new List<Button>();
        private Button firstButton;
        private bool buttonsNeedRefresh = true;
        private bool isPanelVisible = false;

        // 视觉状态管理
        private bool hasUserNavigated = false;
        private InputMode currentInputMode = InputMode.KeyboardMouse;

        // INavigationPanel 接口实现
        public string PanelName => panelName;
        public int Priority => priority;
        public bool IsActive => isActive && gameObject.activeInHierarchy;
        public bool UseCustomNavigation => useCustomNavigation;
        public GameObject DefaultSelection => firstButton != null ? firstButton.gameObject : null;

        #region Unity生命周期
        private void Awake()
        {
            // 注册到导航管理器
            if (GamePadNavigationManager.Instance != null)
            {
                GamePadNavigationManager.Instance.RegisterPanel(this);
                GamePadNavigationManager.Instance.OnInputModeChanged += OnInputModeChanged;
            }
        }

        private void Start()
        {
            // 立即开始监控按钮变化
            StartCoroutine(MonitorTitleButtons());
        }

        private void Update()
        {
            // 只在激活状态且使用手柄时处理输入
            if (!isActive || !usingGamepad) return;

            UpdateInputState();
        }

        private void OnEnable()
        {
            isActive = true;

            // 标记需要刷新按钮
            buttonsNeedRefresh = true;
            isPanelVisible = true;
            isFirstNavigation = true;
            hasUserNavigated = false;

            // 获取当前输入模式
            if (GamePadNavigationManager.Instance != null)
            {
                currentInputMode = GamePadNavigationManager.Instance.GetCurrentInputMode();
                usingGamepad = (currentInputMode == InputMode.Gamepad);
            }

            // 刷新按钮
            RefreshTitleButtons();

            // 只有手柄模式下才清除视觉选中状态
            if (usingGamepad)
            {
                ClearAllVisualSelections();
            }
        }

        private void OnDisable()
        {
            isActive = false;
            usingGamepad = false;
            currentSelectedIndex = -1;
            isPanelVisible = false;
            isFirstNavigation = true;
            hasUserNavigated = false;

            // 清除所有视觉选中状态
            ClearAllVisualSelections();
        }

        private void OnDestroy()
        {
            // 从导航管理器注销
            if (GamePadNavigationManager.Instance != null)
            {
                GamePadNavigationManager.Instance.UnregisterPanel(this);
                GamePadNavigationManager.Instance.OnInputModeChanged -= OnInputModeChanged;
            }

            StopAllCoroutines();
        }

        /// <summary>
        /// 输入模式变化时的回调
        /// </summary>
        private void OnInputModeChanged(InputMode newMode)
        {
            currentInputMode = newMode;
            bool wasUsingGamepad = usingGamepad;
            usingGamepad = (newMode == InputMode.Gamepad);

            if (enableDebugLogs)
            {
                Debug.Log($"[TitleSelectionNavigationPanel] 输入模式变化: {newMode}, 使用手柄: {usingGamepad}");
            }

            // 如果从手柄模式切换到键鼠模式
            if (wasUsingGamepad && !usingGamepad)
            {
                ClearAllVisualSelections();
                hasUserNavigated = false;
                isFirstNavigation = true;
            }
            // 如果从键鼠模式切换到手柄模式
            else if (!wasUsingGamepad && usingGamepad)
            {
                hasUserNavigated = false;
                isFirstNavigation = true;
                ClearAllVisualSelections();
            }
        }
        #endregion

        #region INavigationPanel 实现
        /// <summary>
        /// 面板激活时的处理
        /// </summary>
        public void OnPanelActivated()
        {
            if (enableDebugLogs)
            {
                Debug.Log($"[TitleSelectionNavigationPanel] 面板激活 - 最高优先级面板");
            }

            isActive = true;

            // 更新输入模式
            if (GamePadNavigationManager.Instance != null)
            {
                currentInputMode = GamePadNavigationManager.Instance.GetCurrentInputMode();
                usingGamepad = (currentInputMode == InputMode.Gamepad);
            }

            isPanelVisible = true;

            // 重置导航状态
            lastNavigationTime = 0f;
            isFirstNavigation = true;
            hasUserNavigated = false;

            // 刷新按钮
            RefreshTitleButtons();

            // 只有手柄模式下才清除视觉上的选中状态
            if (usingGamepad)
            {
                ClearAllVisualSelections();
            }
        }

        /// <summary>
        /// 面板停用时的处理
        /// </summary>
        public void OnPanelDeactivated()
        {
            if (enableDebugLogs)
            {
                Debug.Log($"[TitleSelectionNavigationPanel] 面板停用");
            }

            isActive = false;
            usingGamepad = false;
            currentSelectedIndex = -1;
            isPanelVisible = false;
            isFirstNavigation = true;
            hasUserNavigated = false;

            // 清除所有视觉选中状态
            ClearAllVisualSelections();
        }

        /// <summary>
        /// 处理自定义导航逻辑
        /// </summary>
        public void HandleCustomNavigation()
        {
            if (!isActive || !usingGamepad) return;

            // 检查失焦情况
            CheckLostFocus();

            // 处理十字键导航
            CheckDPadNavigationInput();
        }

        /// <summary>
        /// 是否忽略确认键输入
        /// </summary>
        public bool ShouldIgnoreSubmit()
        {
            return false;
        }
        #endregion

        #region 输入处理
        /// <summary>
        /// 更新输入状态
        /// </summary>
        private void UpdateInputState()
        {
            // 检查是否需要刷新按钮
            if (buttonsNeedRefresh)
            {
                RefreshTitleButtons();
            }
        }

        /// <summary>
        /// 检查十字键导航输入
        /// </summary>
        private void CheckDPadNavigationInput()
        {
            if (!usingGamepad) return;

            bool canNavigate = Time.unscaledTime - lastNavigationTime > navigationCooldown;
            if (!canNavigate) return;

            // 检查手柄输入
            if (Gamepad.current != null)
            {
                Vector2 dpad = Gamepad.current.dpad.ReadValue();

                // 处理十字键上
                if (dpad.y > 0.5f)
                {
                    HandleVerticalNavigation(true);
                    lastNavigationTime = Time.unscaledTime;
                }
                // 处理十字键下
                else if (dpad.y < -0.5f)
                {
                    HandleVerticalNavigation(false);
                    lastNavigationTime = Time.unscaledTime;
                }
            }
        }

        /// <summary>
        /// 处理垂直导航
        /// </summary>
        private void HandleVerticalNavigation(bool moveUp)
        {
            if (titleButtons.Count == 0) return;

            int newIndex = currentSelectedIndex;

            // 如果是第一次导航，选中第一个选项
            if (isFirstNavigation)
            {
                isFirstNavigation = false;
                hasUserNavigated = true;
                newIndex = 0;

                if (enableDebugLogs)
                {
                    Debug.Log($"[TitleSelectionNavigationPanel] 第一次导航，选中第一个选项");
                }
            }
            else
            {
                // 如果当前没有选中或选中无效，从第一个开始
                if (newIndex < 0 || newIndex >= titleButtons.Count || titleButtons[newIndex] == null)
                {
                    newIndex = 0;
                }

                // 计算新索引 - 在当前选择按钮内循环
                if (moveUp)
                {
                    newIndex--;
                    if (newIndex < 0)
                    {
                        newIndex = titleButtons.Count - 1;
                    }
                }
                else
                {
                    newIndex++;
                    if (newIndex >= titleButtons.Count)
                    {
                        newIndex = 0;
                    }
                }
            }

            SelectTitleButton(newIndex);

            if (enableDebugLogs)
            {
                Debug.Log($"[TitleSelectionNavigationPanel] 导航: {(moveUp ? "上" : "下")}, 从 {currentSelectedIndex} 到 {newIndex}");
            }
        }

        /// <summary>
        /// 检查失焦情况
        /// </summary>
        private void CheckLostFocus()
        {
            if (usingGamepad && currentSelectedIndex >= 0 && hasUserNavigated)
            {
                // 检查当前选中的按钮是否仍然有效
                if (currentSelectedIndex < titleButtons.Count && titleButtons[currentSelectedIndex] != null)
                {
                    var currentButton = titleButtons[currentSelectedIndex];
                    if (currentButton.gameObject.activeInHierarchy && currentButton.interactable)
                    {
                        // 检查EventSystem的当前选中对象
                        var eventSystem = EventSystem.current;
                        if (eventSystem != null && eventSystem.currentSelectedGameObject != currentButton.gameObject)
                        {
                            if (enableDebugLogs)
                            {
                                Debug.Log($"[TitleSelectionNavigationPanel] 检测到失焦，重新选中按钮: {currentSelectedIndex}");
                            }

                            // 重新选中当前按钮
                            SelectTitleButton(currentSelectedIndex);
                        }
                    }
                    else
                    {
                        // 按钮无效，回到第一个
                        ForceRefreshAndSelectFirst();
                    }
                }
                else
                {
                    // 索引无效，回到第一个
                    ForceRefreshAndSelectFirst();
                }
            }
            else if (usingGamepad && titleButtons.Count > 0 && hasUserNavigated)
            {
                // 没有当前选中，回到第一个
                ForceRefreshAndSelectFirst();
            }
            else if (usingGamepad)
            {
                // 没有按钮但应该刷新
                RefreshTitleButtons();
            }
        }
        #endregion

        #region 按钮管理
        /// <summary>
        /// 刷新标题按钮列表
        /// </summary>
        public void RefreshTitleButtons()
        {
            titleButtons.Clear();
            firstButton = null;

            if (gameObject.activeInHierarchy)
            {
                // 获取所有子对象中的按钮
                Button[] buttons = GetComponentsInChildren<Button>(true);
                foreach (var button in buttons)
                {
                    if (button != null && button.gameObject.activeInHierarchy && button.interactable)
                    {
                        titleButtons.Add(button);
                        if (firstButton == null)
                        {
                            firstButton = button;
                        }
                    }
                }
            }

            // 重置选中索引，但不自动选中
            currentSelectedIndex = -1;
            buttonsNeedRefresh = false;

            // 只有手柄模式下且用户还没有开始导航才清除视觉选中状态
            if (usingGamepad && !hasUserNavigated)
            {
                ClearAllVisualSelections();
            }

            if (enableDebugLogs)
            {
                Debug.Log($"[TitleSelectionNavigationPanel] 刷新标题按钮，找到 {titleButtons.Count} 个按钮, 用户已导航: {hasUserNavigated}, 使用手柄: {usingGamepad}");
                for (int i = 0; i < titleButtons.Count; i++)
                {
                    Debug.Log($"  - 按钮 {i}: {titleButtons[i]?.gameObject.name ?? "null"} (活跃: {titleButtons[i]?.gameObject.activeInHierarchy}, 可交互: {titleButtons[i]?.interactable})");
                }
            }
        }

        /// <summary>
        /// 选择指定索引的标题按钮
        /// </summary>
        public void SelectTitleButton(int index)
        {
            if (index < 0 || index >= titleButtons.Count || titleButtons[index] == null)
            {
                if (enableDebugLogs)
                {
                    Debug.LogWarning($"[TitleSelectionNavigationPanel] 尝试选择无效的按钮索引: {index}, 按钮总数: {titleButtons.Count}");
                }
                return;
            }

            var button = titleButtons[index];
            if (button == null || !button.gameObject.activeInHierarchy || !button.interactable)
            {
                if (enableDebugLogs)
                {
                    Debug.LogWarning($"[TitleSelectionNavigationPanel] 按钮无效: {index}, 活跃: {button?.gameObject.activeInHierarchy}, 可交互: {button?.interactable}");
                }
                return;
            }

            // 设置选中对象
            if (GamePadNavigationManager.Instance != null)
            {
                GamePadNavigationManager.Instance.SetSelectedObject(button.gameObject);
            }
            else
            {
                var eventSystem = EventSystem.current;
                if (eventSystem != null)
                {
                    eventSystem.SetSelectedGameObject(button.gameObject);
                }
            }

            currentSelectedIndex = index;

            if (enableDebugLogs)
            {
                Debug.Log($"[TitleSelectionNavigationPanel] 选中按钮: {index} - {button.gameObject.name}");
            }
        }

        /// <summary>
        /// 强制选择第一个按钮
        /// </summary>
        public void ForceSelectFirstButton()
        {
            if (titleButtons.Count > 0)
            {
                SelectTitleButton(0);
                isFirstNavigation = false;
                hasUserNavigated = true;
            }
            else
            {
                // 如果没有按钮，尝试刷新
                RefreshTitleButtons();
                if (titleButtons.Count > 0)
                {
                    SelectTitleButton(0);
                    isFirstNavigation = false;
                    hasUserNavigated = true;
                }
            }
        }

        /// <summary>
        /// 强制刷新并选择第一个按钮
        /// </summary>
        public void ForceRefreshAndSelectFirst()
        {
            RefreshTitleButtons();
            if (titleButtons.Count > 0)
            {
                // 使用协程确保在布局完成后选择
                StartCoroutine(DelayedSelectFirst());
            }
        }

        /// <summary>
        /// 清除所有按钮的视觉选中状态
        /// </summary>
        private void ClearAllVisualSelections()
        {
            var eventSystem = EventSystem.current;
            if (eventSystem != null)
            {
                // 清除事件系统的选中状态
                eventSystem.SetSelectedGameObject(null);
            }

            // 强制所有按钮退出选中状态
            foreach (var button in titleButtons)
            {
                if (button != null)
                {
                    // 强制按钮退出选中状态
                    var selectable = button as Selectable;
                    if (selectable != null)
                    {
                        // 使用反射调用DoStateTransition来强制设置为Normal状态
                        try
                        {
                            var method = typeof(Selectable).GetMethod("DoStateTransition",
                                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            if (method != null)
                            {
                                method.Invoke(selectable, new object[] { 0, true }); // 0 = Normal state
                            }
                        }
                        catch (System.Exception e)
                        {
                            if (enableDebugLogs)
                            {
                                Debug.LogWarning($"[TitleSelectionNavigationPanel] 清除按钮状态失败: {e.Message}");
                            }
                        }
                    }
                }
            }

            if (enableDebugLogs)
            {
                Debug.Log($"[TitleSelectionNavigationPanel] 清除所有视觉选中状态");
            }
        }
        #endregion

        #region 协程管理
        /// <summary>
        /// 监控标题按钮变化的协程
        /// </summary>
        private IEnumerator MonitorTitleButtons()
        {
            int lastButtonCount = 0;
            bool lastPanelVisible = false;

            while (true)
            {
                yield return new WaitForSeconds(0.1f);

                // 检查面板可见性变化
                bool currentPanelVisible = gameObject.activeInHierarchy;
                if (currentPanelVisible != lastPanelVisible)
                {
                    buttonsNeedRefresh = true;
                    lastPanelVisible = currentPanelVisible;

                    if (enableDebugLogs)
                    {
                        Debug.Log($"[TitleSelectionNavigationPanel] 面板可见性变化: {lastPanelVisible} -> {currentPanelVisible}");
                    }
                }

                // 检查按钮数量变化
                int currentButtonCount = GetCurrentButtonCount();

                // 如果按钮数量发生变化或需要刷新，刷新按钮列表
                if (currentButtonCount != lastButtonCount || buttonsNeedRefresh)
                {
                    RefreshTitleButtons();

                    // 如果使用手柄且面板激活且用户已经开始导航，立即选中第一个按钮
                    if (usingGamepad && isActive && titleButtons.Count > 0 && hasUserNavigated)
                    {
                        ForceSelectFirstButton();
                    }

                    lastButtonCount = currentButtonCount;

                    if (enableDebugLogs)
                    {
                        Debug.Log($"[TitleSelectionNavigationPanel] 检测到按钮数量变化: {lastButtonCount} -> {currentButtonCount}");
                    }
                }
            }
        }

        /// <summary>
        /// 获取当前按钮数量
        /// </summary>
        private int GetCurrentButtonCount()
        {
            if (!gameObject.activeInHierarchy)
                return 0;

            Button[] buttons = GetComponentsInChildren<Button>(true);
            int count = 0;
            foreach (var button in buttons)
            {
                if (button != null && button.gameObject.activeInHierarchy && button.interactable)
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// 延迟选择第一个按钮
        /// </summary>
        private IEnumerator DelayedSelectFirst()
        {
            yield return null;
            if (titleButtons.Count > 0 && titleButtons[0] != null)
            {
                SelectTitleButton(0);
                isFirstNavigation = false;
                hasUserNavigated = true;
            }
        }
        #endregion

        #region 公共API
        /// <summary>
        /// 获取当前选中的按钮索引
        /// </summary>
        public int GetCurrentSelectedIndex()
        {
            return currentSelectedIndex;
        }

        /// <summary>
        /// 获取当前选中的按钮
        /// </summary>
        public Button GetCurrentSelectedButton()
        {
            if (currentSelectedIndex >= 0 && currentSelectedIndex < titleButtons.Count)
            {
                return titleButtons[currentSelectedIndex];
            }
            return null;
        }

        /// <summary>
        /// 手动刷新按钮列表
        /// </summary>
        [ContextMenu("手动刷新按钮列表")]
        public void ManualRefreshButtons()
        {
            RefreshTitleButtons();
        }

        /// <summary>
        /// 强制重新选中第一个按钮
        /// </summary>
        [ContextMenu("强制选中第一个按钮")]
        public void ManualForceSelectFirst()
        {
            ForceRefreshAndSelectFirst();
        }

        /// <summary>
        /// 标记需要刷新按钮
        /// </summary>
        public void MarkButtonsNeedRefresh()
        {
            buttonsNeedRefresh = true;
        }

        /// <summary>
        /// 重置第一次导航标志
        /// </summary>
        public void ResetFirstNavigationFlag()
        {
            isFirstNavigation = true;
            hasUserNavigated = false;
        }

        /// <summary>
        /// 清除所有视觉选中状态
        /// </summary>
        [ContextMenu("清除视觉选中状态")]
        public void ManualClearVisualSelections()
        {
            ClearAllVisualSelections();
        }

        /// <summary>
        /// 获取当前输入模式
        /// </summary>
        public InputMode GetCurrentInputMode()
        {
            return currentInputMode;
        }
        #endregion

        #region 调试方法
        [ContextMenu("打印当前状态")]
        public void PrintCurrentStatus()
        {
            Debug.Log($"[TitleSelectionNavigationPanel] 状态报告:");
            Debug.Log($"- 按钮总数: {titleButtons.Count}");
            Debug.Log($"- 当前选中索引: {currentSelectedIndex}");
            Debug.Log($"- 活动状态: {isActive}");
            Debug.Log($"- 使用手柄: {usingGamepad}");
            Debug.Log($"- 输入模式: {currentInputMode}");
            Debug.Log($"- 需要刷新: {buttonsNeedRefresh}");
            Debug.Log($"- 面板可见: {isPanelVisible}");
            Debug.Log($"- 第一次导航: {isFirstNavigation}");
            Debug.Log($"- 用户已导航: {hasUserNavigated}");

            for (int i = 0; i < titleButtons.Count; i++)
            {
                string status = (i == currentSelectedIndex) ? "[选中]" : "";
                Debug.Log($"- 按钮 {i}: {titleButtons[i]?.gameObject.name ?? "null"} {status} (活跃: {titleButtons[i]?.gameObject.activeInHierarchy}, 可交互: {titleButtons[i]?.interactable})");
            }
        }
        #endregion
    }
}
