using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Nova
{
    public class ChoicesNavigationPanel : MonoBehaviour, INavigationPanel
    {
        [Header("导航设置")]
        [Tooltip("导航输入冷却时间（秒）")]
        [SerializeField] private float navigationCooldown = 0.3f;
        [Tooltip("启用调试日志")]
        [SerializeField] private bool enableDebugLogs = false;

        [Header("面板设置")]
        [Tooltip("面板名称")]
        [SerializeField] private string panelName = "ChoicesPanel";
        [Tooltip("面板优先级")]
        [SerializeField] private int priority = PanelPriority.CHOICES;
        [Tooltip("使用自定义导航")]
        [SerializeField] private bool useCustomNavigation = true;

        // 私有字段
        private ChoicesController choicesController;
        private int currentSelectedIndex = -1;
        private float lastNavigationTime;
        private bool isActive = false;
        private bool usingGamepad = false;
        private bool isFirstNavigation = true; // 新增：标记是否是第一次导航

        // 按钮管理
        private List<Button> choiceButtons = new List<Button>();
        private Button firstButton;
        private bool buttonsNeedRefresh = true;
        private bool isPanelVisible = false;

        // 新增：视觉状态管理
        private bool hasUserNavigated = false; // 用户是否已经进行了导航操作
        private InputMode currentInputMode = InputMode.KeyboardMouse; // 跟踪当前输入模式

        // 新增：UI隐藏状态管理
        private bool wasPanelHidden = false; // 标记面板是否曾经被隐藏
        private int lastSelectedIndexBeforeHide = -1; // 隐藏前选中的索引

        // INavigationPanel 接口实现
        public string PanelName => panelName;
        public int Priority => priority;
        public bool IsActive => isActive && gameObject.activeInHierarchy;
        public bool UseCustomNavigation => useCustomNavigation;
        public GameObject DefaultSelection => firstButton != null ? firstButton.gameObject : null;

        #region Unity生命周期
        private void Awake()
        {
            choicesController = GetComponent<ChoicesController>();
            if (choicesController == null)
            {
                choicesController = FindObjectOfType<ChoicesController>();
            }

            // 注册到导航管理器
            if (GamePadNavigationManager.Instance != null)
            {
                GamePadNavigationManager.Instance.RegisterPanel(this);
                // 订阅输入模式变化事件
                GamePadNavigationManager.Instance.OnInputModeChanged += OnInputModeChanged;
            }
        }

        private void Start()
        {
            // 立即开始监控按钮变化
            if (choicesController != null)
            {
                StartCoroutine(MonitorChoiceButtons());
            }
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

            // 获取当前输入模式
            if (GamePadNavigationManager.Instance != null)
            {
                currentInputMode = GamePadNavigationManager.Instance.GetCurrentInputMode();
                usingGamepad = (currentInputMode == InputMode.Gamepad);
            }

            // 刷新按钮
            RefreshChoiceButtons();

            // 新增：如果面板曾经被隐藏，恢复之前的导航状态
            if (wasPanelHidden)
            {
                if (enableDebugLogs)
                {
                    Debug.Log($"[ChoicesNavigationPanel] 面板重新激活，恢复导航状态");
                }

                // 恢复之前的导航状态，让用户立即可以导航
                hasUserNavigated = true;
                isFirstNavigation = false;

                // 尝试恢复之前选中的索引
                if (lastSelectedIndexBeforeHide >= 0 && lastSelectedIndexBeforeHide < choiceButtons.Count)
                {
                    if (choiceButtons[lastSelectedIndexBeforeHide] != null &&
                        choiceButtons[lastSelectedIndexBeforeHide].gameObject.activeInHierarchy &&
                        choiceButtons[lastSelectedIndexBeforeHide].interactable)
                    {
                        SelectChoiceButton(lastSelectedIndexBeforeHide);
                    }
                    else
                    {
                        // 如果之前的索引无效，选择第一个有效按钮
                        ForceSelectFirstButton();
                    }
                }
                else if (choiceButtons.Count > 0)
                {
                    // 如果没有之前的选中索引，选择第一个
                    ForceSelectFirstButton();
                }

                wasPanelHidden = false;
                lastSelectedIndexBeforeHide = -1;
            }
            else
            {
                // 正常激活：重置导航状态
                isFirstNavigation = true;
                hasUserNavigated = false;
            }

            // 只有手柄模式下才清除视觉选中状态
            if (usingGamepad && !hasUserNavigated)
            {
                ClearAllVisualSelections();
            }
        }

        private void OnDisable()
        {
            // 新增：记录隐藏前的状态
            if (isActive && isPanelVisible)
            {
                wasPanelHidden = true;
                lastSelectedIndexBeforeHide = currentSelectedIndex;

                if (enableDebugLogs)
                {
                    Debug.Log($"[ChoicesNavigationPanel] 面板隐藏，保存状态: 索引={currentSelectedIndex}");
                }
            }

            isActive = false;
            usingGamepad = false;
            currentSelectedIndex = -1;
            isPanelVisible = false;

            // 新增：不清除用户导航标志，这样重新激活时可以立即响应
            // 只在完全停用时重置这些标志
            if (!wasPanelHidden)
            {
                isFirstNavigation = true;
                hasUserNavigated = false;
            }

            // 清除所有视觉选中状态（所有模式都需要）
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
                Debug.Log($"[ChoicesNavigationPanel] 输入模式变化: {newMode}, 使用手柄: {usingGamepad}");
            }

            // 如果从手柄模式切换到键鼠模式
            if (wasUsingGamepad && !usingGamepad)
            {
                // 清除手柄模式的选中状态，恢复键鼠模式的行为
                ClearAllVisualSelections();
                // 注意：不重置用户导航标志，这样切换回手柄时可以立即导航
            }
            // 如果从键鼠模式切换到手柄模式
            else if (!wasUsingGamepad && usingGamepad)
            {
                // 准备手柄导航，如果用户已经导航过，立即恢复选中状态
                if (hasUserNavigated && choiceButtons.Count > 0)
                {
                    if (currentSelectedIndex >= 0 && currentSelectedIndex < choiceButtons.Count &&
                        choiceButtons[currentSelectedIndex] != null &&
                        choiceButtons[currentSelectedIndex].gameObject.activeInHierarchy &&
                        choiceButtons[currentSelectedIndex].interactable)
                    {
                        SelectChoiceButton(currentSelectedIndex);
                    }
                    else
                    {
                        ForceSelectFirstButton();
                    }
                }
                else
                {
                    ClearAllVisualSelections();
                }
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
                Debug.Log($"[ChoicesNavigationPanel] 面板激活");
            }

            isActive = true;

            // 更新输入模式
            if (GamePadNavigationManager.Instance != null)
            {
                currentInputMode = GamePadNavigationManager.Instance.GetCurrentInputMode();
                usingGamepad = (currentInputMode == InputMode.Gamepad);
            }

            isPanelVisible = true;

            // 重置导航冷却时间
            lastNavigationTime = 0f;

            // 刷新按钮
            RefreshChoiceButtons();

            // 如果使用手柄且用户已经导航过，立即恢复选中状态
            if (usingGamepad && hasUserNavigated && choiceButtons.Count > 0)
            {
                if (currentSelectedIndex >= 0 && currentSelectedIndex < choiceButtons.Count &&
                    choiceButtons[currentSelectedIndex] != null &&
                    choiceButtons[currentSelectedIndex].gameObject.activeInHierarchy &&
                    choiceButtons[currentSelectedIndex].interactable)
                {
                    SelectChoiceButton(currentSelectedIndex);
                }
                else
                {
                    ForceSelectFirstButton();
                }
            }
            else if (usingGamepad)
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
                Debug.Log($"[ChoicesNavigationPanel] 面板停用");
            }

            // 记录停用前的状态
            wasPanelHidden = true;
            lastSelectedIndexBeforeHide = currentSelectedIndex;

            isActive = false;
            usingGamepad = false;
            currentSelectedIndex = -1;
            isPanelVisible = false;

            // 注意：不完全重置导航状态，这样重新激活时可以立即响应
            // 只在完全销毁时才重置这些标志

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
                RefreshChoiceButtons();
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
            if (choiceButtons.Count == 0) return;

            int newIndex = currentSelectedIndex;

            // 如果是第一次导航，选中第一个选项
            if (isFirstNavigation)
            {
                isFirstNavigation = false;
                hasUserNavigated = true; // 标记用户已经开始导航
                newIndex = 0;

                if (enableDebugLogs)
                {
                    Debug.Log($"[ChoicesNavigationPanel] 第一次导航，选中第一个选项");
                }
            }
            else
            {
                // 如果当前没有选中或选中无效，从第一个开始
                if (newIndex < 0 || newIndex >= choiceButtons.Count || choiceButtons[newIndex] == null)
                {
                    newIndex = 0;
                }

                // 计算新索引 - 在当前选择按钮内循环
                if (moveUp)
                {
                    newIndex--;
                    if (newIndex < 0)
                    {
                        newIndex = choiceButtons.Count - 1;
                    }
                }
                else
                {
                    newIndex++;
                    if (newIndex >= choiceButtons.Count)
                    {
                        newIndex = 0;
                    }
                }
            }

            SelectChoiceButton(newIndex);

            if (enableDebugLogs)
            {
                Debug.Log($"[ChoicesNavigationPanel] 导航: {(moveUp ? "上" : "下")}, 从 {currentSelectedIndex} 到 {newIndex}");
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
                if (currentSelectedIndex < choiceButtons.Count && choiceButtons[currentSelectedIndex] != null)
                {
                    var currentButton = choiceButtons[currentSelectedIndex];
                    if (currentButton.gameObject.activeInHierarchy && currentButton.interactable)
                    {
                        // 检查EventSystem的当前选中对象
                        var eventSystem = EventSystem.current;
                        if (eventSystem != null && eventSystem.currentSelectedGameObject != currentButton.gameObject)
                        {
                            if (enableDebugLogs)
                            {
                                Debug.Log($"[ChoicesNavigationPanel] 检测到失焦，重新选中按钮: {currentSelectedIndex}");
                            }

                            // 重新选中当前按钮
                            SelectChoiceButton(currentSelectedIndex);
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
            else if (usingGamepad && choiceButtons.Count > 0 && hasUserNavigated)
            {
                // 没有当前选中，回到第一个
                ForceRefreshAndSelectFirst();
            }
            else if (usingGamepad)
            {
                // 没有按钮但应该刷新
                RefreshChoiceButtons();
            }
        }
        #endregion

        #region 按钮管理
        /// <summary>
        /// 刷新选择按钮列表
        /// </summary>
        public void RefreshChoiceButtons()
        {
            choiceButtons.Clear();
            firstButton = null;

            if (choicesController != null && choicesController.gameObject.activeInHierarchy)
            {
                // 获取所有子对象中的按钮
                Button[] buttons = GetComponentsInChildren<Button>(true);
                foreach (var button in buttons)
                {
                    if (button != null && button.gameObject.activeInHierarchy && button.interactable)
                    {
                        choiceButtons.Add(button);
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

            // 新增：只有手柄模式下且用户还没有开始导航才清除视觉选中状态
            if (usingGamepad && !hasUserNavigated)
            {
                ClearAllVisualSelections();
            }

            if (enableDebugLogs)
            {
                Debug.Log($"[ChoicesNavigationPanel] 刷新选择按钮，找到 {choiceButtons.Count} 个按钮, 用户已导航: {hasUserNavigated}, 使用手柄: {usingGamepad}");
                for (int i = 0; i < choiceButtons.Count; i++)
                {
                    Debug.Log($"  - 按钮 {i}: {choiceButtons[i]?.gameObject.name ?? "null"} (活跃: {choiceButtons[i]?.gameObject.activeInHierarchy}, 可交互: {choiceButtons[i]?.interactable})");
                }
            }
        }

        /// <summary>
        /// 选择指定索引的选择按钮
        /// </summary>
        public void SelectChoiceButton(int index)
        {
            if (index < 0 || index >= choiceButtons.Count || choiceButtons[index] == null)
            {
                if (enableDebugLogs)
                {
                    Debug.LogWarning($"[ChoicesNavigationPanel] 尝试选择无效的按钮索引: {index}, 按钮总数: {choiceButtons.Count}");
                }
                return;
            }

            var button = choiceButtons[index];
            if (button == null || !button.gameObject.activeInHierarchy || !button.interactable)
            {
                if (enableDebugLogs)
                {
                    Debug.LogWarning($"[ChoicesNavigationPanel] 按钮无效: {index}, 活跃: {button?.gameObject.activeInHierarchy}, 可交互: {button?.interactable}");
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
                Debug.Log($"[ChoicesNavigationPanel] 选中按钮: {index} - {button.gameObject.name}");
            }
        }

        /// <summary>
        /// 强制选择第一个按钮
        /// </summary>
        public void ForceSelectFirstButton()
        {
            if (choiceButtons.Count > 0)
            {
                SelectChoiceButton(0);
                isFirstNavigation = false; // 标记已经进行过导航
                hasUserNavigated = true; // 标记用户已经开始导航
            }
            else
            {
                // 如果没有按钮，尝试刷新
                RefreshChoiceButtons();
                if (choiceButtons.Count > 0)
                {
                    SelectChoiceButton(0);
                    isFirstNavigation = false; // 标记已经进行过导航
                    hasUserNavigated = true; // 标记用户已经开始导航
                }
            }
        }

        /// <summary>
        /// 强制刷新并选择第一个按钮
        /// </summary>
        public void ForceRefreshAndSelectFirst()
        {
            RefreshChoiceButtons();
            if (choiceButtons.Count > 0)
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
            foreach (var button in choiceButtons)
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
                                Debug.LogWarning($"[ChoicesNavigationPanel] 清除按钮状态失败: {e.Message}");
                            }
                        }
                    }
                }
            }

            if (enableDebugLogs)
            {
                Debug.Log($"[ChoicesNavigationPanel] 清除所有视觉选中状态");
            }
        }
        #endregion

        #region 协程管理
        /// <summary>
        /// 监控选择按钮变化的协程
        /// </summary>
        private IEnumerator MonitorChoiceButtons()
        {
            int lastButtonCount = 0;
            bool lastPanelVisible = false;

            while (true)
            {
                yield return new WaitForSeconds(0.1f); // 缩短检查间隔到0.1秒

                // 检查面板可见性变化
                bool currentPanelVisible = IsPanelActuallyVisible();
                if (currentPanelVisible != lastPanelVisible)
                {
                    buttonsNeedRefresh = true;
                    lastPanelVisible = currentPanelVisible;

                    if (enableDebugLogs)
                    {
                        Debug.Log($"[ChoicesNavigationPanel] 面板可见性变化: {lastPanelVisible} -> {currentPanelVisible}");
                    }
                }

                // 检查按钮数量变化
                int currentButtonCount = GetCurrentButtonCount();

                // 如果按钮数量发生变化或需要刷新，刷新按钮列表
                if (currentButtonCount != lastButtonCount || buttonsNeedRefresh)
                {
                    RefreshChoiceButtons();

                    // 如果使用手柄且面板激活且用户已经开始导航，立即选中第一个按钮
                    if (usingGamepad && isActive && choiceButtons.Count > 0 && hasUserNavigated)
                    {
                        // 只有在当前没有有效选中时才强制选择第一个
                        if (currentSelectedIndex < 0 || currentSelectedIndex >= choiceButtons.Count ||
                            choiceButtons[currentSelectedIndex] == null ||
                            !choiceButtons[currentSelectedIndex].gameObject.activeInHierarchy ||
                            !choiceButtons[currentSelectedIndex].interactable)
                        {
                            ForceSelectFirstButton();
                        }
                    }

                    lastButtonCount = currentButtonCount;

                    if (enableDebugLogs)
                    {
                        Debug.Log($"[ChoicesNavigationPanel] 检测到按钮数量变化: {lastButtonCount} -> {currentButtonCount}");
                    }
                }
            }
        }

        /// <summary>
        /// 获取当前按钮数量
        /// </summary>
        private int GetCurrentButtonCount()
        {
            if (choicesController == null || !choicesController.gameObject.activeInHierarchy)
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
        /// 检查面板是否实际可见
        /// </summary>
        private bool IsPanelActuallyVisible()
        {
            if (choicesController == null) return false;

            // 检查是否有任何活跃的选择按钮
            return choicesController.activeChoiceCount > 0;
        }

        /// <summary>
        /// 延迟选择第一个按钮
        /// </summary>
        private IEnumerator DelayedSelectFirst()
        {
            yield return null; // 等待一帧
            if (choiceButtons.Count > 0 && choiceButtons[0] != null)
            {
                SelectChoiceButton(0);
                isFirstNavigation = false; // 标记已经进行过导航
                hasUserNavigated = true; // 标记用户已经开始导航
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
            if (currentSelectedIndex >= 0 && currentSelectedIndex < choiceButtons.Count)
            {
                return choiceButtons[currentSelectedIndex];
            }
            return null;
        }

        /// <summary>
        /// 手动刷新按钮列表
        /// </summary>
        [ContextMenu("手动刷新按钮列表")]
        public void ManualRefreshButtons()
        {
            RefreshChoiceButtons();
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
        /// 重置第一次导航标志（用于特殊情况需要立即选中的情况）
        /// </summary>
        public void ResetFirstNavigationFlag()
        {
            isFirstNavigation = true;
            hasUserNavigated = false;
        }

        /// <summary>
        /// 清除所有视觉选中状态（公共方法）
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

        /// <summary>
        /// 新增：强制设置导航状态为已开始（用于UI隐藏再激活的情况）
        /// </summary>
        public void ForceSetNavigationStarted()
        {
            hasUserNavigated = true;
            isFirstNavigation = false;

            if (enableDebugLogs)
            {
                Debug.Log($"[ChoicesNavigationPanel] 强制设置导航状态为已开始");
            }
        }
        #endregion

        #region 调试方法
        [ContextMenu("打印当前状态")]
        public void PrintCurrentStatus()
        {
            Debug.Log($"[ChoicesNavigationPanel] 状态报告:");
            Debug.Log($"- 按钮总数: {choiceButtons.Count}");
            Debug.Log($"- 当前选中索引: {currentSelectedIndex}");
            Debug.Log($"- 活动状态: {isActive}");
            Debug.Log($"- 使用手柄: {usingGamepad}");
            Debug.Log($"- 输入模式: {currentInputMode}");
            Debug.Log($"- 需要刷新: {buttonsNeedRefresh}");
            Debug.Log($"- 面板可见: {isPanelVisible}");
            Debug.Log($"- 第一次导航: {isFirstNavigation}");
            Debug.Log($"- 用户已导航: {hasUserNavigated}");
            Debug.Log($"- 面板曾经隐藏: {wasPanelHidden}");
            Debug.Log($"- 隐藏前索引: {lastSelectedIndexBeforeHide}");
            Debug.Log($"- ChoicesController: {(choicesController != null ? "已找到" : "未找到")}");
            Debug.Log($"- 活动选择数量: {(choicesController != null ? choicesController.activeChoiceCount : 0)}");

            for (int i = 0; i < choiceButtons.Count; i++)
            {
                string status = (i == currentSelectedIndex) ? "[选中]" : "";
                Debug.Log($"- 按钮 {i}: {choiceButtons[i]?.gameObject.name ?? "null"} {status} (活跃: {choiceButtons[i]?.gameObject.activeInHierarchy}, 可交互: {choiceButtons[i]?.interactable})");
            }
        }
        #endregion
    }
}
