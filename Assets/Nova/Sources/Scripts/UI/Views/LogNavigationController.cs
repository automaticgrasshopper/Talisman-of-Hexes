using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Nova
{
    public class LogNavigationController : MonoBehaviour, INavigationPanel
    {
        [Header("导航设置")]
        [Tooltip("启动时自动选中最后一个条目")]
        [SerializeField] private bool autoSelectOnEnable = true;
        [Tooltip("导航输入冷却时间（秒）")]
        [SerializeField] private float navigationCooldown = 0.3f;
        [Tooltip("手柄输入优先于鼠标输入")]
        [SerializeField] private bool gamepadPriority = true;
        [Tooltip("启用调试日志")]
        [SerializeField] private bool enableDebugLogs = false;

        [Header("滚动条设置")]
        [Tooltip("LoopVerticalScrollRectWithSwitch 组件")]
        [SerializeField] private LoopVerticalScrollRectWithSwitch scrollRect;
        [Tooltip("右摇杆滚动速度")]
        [SerializeField] private float scrollSpeed = 1.5f;

        [Header("高亮显示设置")]
        [Tooltip("高亮模式")]
        [SerializeField] private HighlightMode highlightMode = HighlightMode.TextColor;
        [Tooltip("强制保持高亮状态")]
        [SerializeField] private bool forceHighlightState = true;
        [Tooltip("文本高亮颜色")]
        [SerializeField] private Color textHighlightColor = new Color(1f, 1f, 0f, 1f);
        [Tooltip("背景高亮颜色")]
        [SerializeField] private Color backgroundHighlightColor = new Color(1f, 1f, 0f, 0.3f);

        [Header("导航面板设置")]
        [Tooltip("面板名称")]
        [SerializeField] private string panelName = "LogView";
        [Tooltip("面板优先级")]
        [SerializeField] private int priority = PanelPriority.LOG;
        [Tooltip("默认选中对象")]
        [SerializeField] private GameObject defaultSelection;
        [Tooltip("使用自定义导航")]
        [SerializeField] private bool useCustomNavigation = true;

        // 高亮模式枚举
        public enum HighlightMode
        {
            TextColor,
            Background,
            Outline,
            Scale,
            Multiple
        }

        // 私有字段
        private LogController logController;
        private int currentSelectedIndex = -1;
        private EventSystem eventSystem;
        private float lastNavigationTime;
        private bool isActive = false;
        private bool usingGamepad = false;
        private bool wasGamepadConnected = false;

        // 性能优化字段
        private Coroutine refreshCoroutine;
        private int lastValidSelectedIndex = -1;
        private bool isInitialized = false;

        // 当前可见条目管理
        private List<GameObject> visibleEntries = new List<GameObject>();
        private Dictionary<GameObject, Color[]> originalTextColors = new Dictionary<GameObject, Color[]>();
        private Dictionary<GameObject, Color> originalBackgroundColors = new Dictionary<GameObject, Color>();

        // 输入状态
        private bool dpadUpPressed = false;
        private bool dpadDownPressed = false;
        private bool dpadUpProcessed = false;
        private bool dpadDownProcessed = false;

        // 新增：A键状态
        private bool aButtonPressed = false;
        private bool aButtonProcessed = false;

        // INavigationPanel 接口实现
        public string PanelName => panelName;
        public int Priority => priority;
        public bool IsActive => isActive && gameObject.activeInHierarchy;
        public bool UseCustomNavigation => useCustomNavigation;
        public GameObject DefaultSelection => defaultSelection;

        #region Unity生命周期
        private void Awake()
        {
            eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                eventSystem = FindObjectOfType<EventSystem>();
            }

            // 获取LogController引用
            logController = GetComponent<LogController>();
            if (logController == null)
            {
                logController = FindObjectOfType<LogController>();
            }

            if (!useLazyInitialization)
            {
                InitializeImmediate();
            }

            // 注册到导航管理器
            if (GamePadNavigationManager.Instance != null)
            {
                GamePadNavigationManager.Instance.RegisterPanel(this);
            }
        }

        private void Start()
        {
            if (useLazyInitialization)
            {
                StartCoroutine(InitializeDelayed());
            }

            if (autoSelectOnEnable)
            {
                StartCoroutine(SelectLastEntryDelayed());
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

            if (!isInitialized)
            {
                StartCoroutine(InitializeDelayed());
            }

            if (autoSelectOnEnable)
            {
                StartCoroutine(SelectLastEntryDelayed());
            }

            StartPeriodicRefresh();
        }

        private void OnDisable()
        {
            isActive = false;
            usingGamepad = false;

            RestoreAllHighlights();
            StopPeriodicRefresh();
        }

        private void OnDestroy()
        {
            // 从导航管理器注销
            if (GamePadNavigationManager.Instance != null)
            {
                GamePadNavigationManager.Instance.UnregisterPanel(this);
            }

            RestoreAllHighlights();
            StopPeriodicRefresh();
        }
        #endregion

        #region 初始化方法
        /// <summary>
        /// 立即初始化
        /// </summary>
        private void InitializeImmediate()
        {
            if (isInitialized) return;

            RefreshVisibleEntries();
            isInitialized = true;

            if (enableDebugLogs)
            {
                Debug.Log($"[LogNavigationController] 立即初始化完成");
            }
        }

        /// <summary>
        /// 延迟初始化
        /// </summary>
        private IEnumerator InitializeDelayed()
        {
            if (isInitialized) yield break;

            yield return null; // 等待一帧
            RefreshVisibleEntries();
            isInitialized = true;

            if (enableDebugLogs)
            {
                Debug.Log($"[LogNavigationController] 延迟初始化完成");
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
                Debug.Log($"[LogNavigationController] 面板激活");
            }

            isActive = true;
            usingGamepad = true;

            // 重置导航状态
            lastNavigationTime = 0f;
            dpadUpProcessed = false;
            dpadDownProcessed = false;
            aButtonProcessed = false;

            // 延迟设置选中对象，确保UI布局完成
            StartCoroutine(DelayedActivation());
        }

        private IEnumerator DelayedActivation()
        {
            yield return null; // 等待一帧，确保UI布局完成

            if (!isInitialized)
            {
                RefreshVisibleEntries();
                isInitialized = true;
            }

            // 设置初始选中
            if (visibleEntries.Count > 0)
            {
                if (lastValidSelectedIndex >= 0 && lastValidSelectedIndex < visibleEntries.Count)
                {
                    SelectLogEntry(lastValidSelectedIndex);
                }
                else
                {
                    SelectLastEntry();
                }
            }

            StartPeriodicRefresh();
        }

        /// <summary>
        /// 面板停用时的处理
        /// </summary>
        public void OnPanelDeactivated()
        {
            if (enableDebugLogs)
            {
                Debug.Log($"[LogNavigationController] 面板停用");
            }

            isActive = false;
            usingGamepad = false;

            // 保存最后选中的索引
            if (currentSelectedIndex >= 0 && currentSelectedIndex < visibleEntries.Count)
            {
                lastValidSelectedIndex = currentSelectedIndex;
            }

            RestoreAllHighlights();
            StopPeriodicRefresh();
        }

        /// <summary>
        /// 处理自定义导航逻辑 - 简化版本，只处理十字键
        /// </summary>
        public void HandleCustomNavigation()
        {
            if (!isActive || !usingGamepad) return;

            CheckGamepadConnection();
            CheckInputMode();

            // 只处理十字键导航输入
            CheckDPadNavigationInput();

            // 处理右摇杆滚动
            CheckScrollbarControl();

            // 处理A键确认输入
            CheckAButtonInput();

            // 强制保持高亮状态
            if (forceHighlightState && usingGamepad && currentSelectedIndex >= 0 &&
                currentSelectedIndex < visibleEntries.Count && visibleEntries[currentSelectedIndex] != null)
            {
                SetEntryHighlight(visibleEntries[currentSelectedIndex], true);
            }
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
            if (Gamepad.current != null)
            {
                Vector2 dpad = Gamepad.current.dpad.ReadValue();

                // 检测十字键按下状态
                bool newDpadUp = dpad.y > 0.5f;
                bool newDpadDown = dpad.y < -0.5f;

                // 检测按钮按下（从未按到按下的瞬间）
                if (newDpadUp && !dpadUpPressed)
                {
                    dpadUpPressed = true;
                    dpadUpProcessed = false;
                }
                else if (!newDpadUp)
                {
                    dpadUpPressed = false;
                    dpadUpProcessed = false;
                }

                if (newDpadDown && !dpadDownPressed)
                {
                    dpadDownPressed = true;
                    dpadDownProcessed = false;
                }
                else if (!newDpadDown)
                {
                    dpadDownPressed = false;
                    dpadDownProcessed = false;
                }

                // 新增：检测A键按下状态
                bool newAButton = Gamepad.current.aButton.isPressed;
                if (newAButton && !aButtonPressed)
                {
                    aButtonPressed = true;
                    aButtonProcessed = false;
                }
                else if (!newAButton)
                {
                    aButtonPressed = false;
                    aButtonProcessed = false;
                }
            }
        }

        /// <summary>
        /// 检查十字键导航输入 - 简化版本
        /// </summary>
        private void CheckDPadNavigationInput()
        {
            if (!usingGamepad) return;

            bool canNavigate = Time.unscaledTime - lastNavigationTime > navigationCooldown;
            if (!canNavigate) return;

            // 处理十字键上
            if (dpadUpPressed && !dpadUpProcessed)
            {
                HandleVerticalNavigation(true);
                lastNavigationTime = Time.unscaledTime;
                dpadUpProcessed = true;
            }
            // 处理十字键下
            else if (dpadDownPressed && !dpadDownProcessed)
            {
                HandleVerticalNavigation(false);
                lastNavigationTime = Time.unscaledTime;
                dpadDownProcessed = true;
            }
        }

        /// <summary>
        /// 新增：检查A键输入
        /// </summary>
        private void CheckAButtonInput()
        {
            if (!usingGamepad) return;

            // 处理A键按下
            if (aButtonPressed && !aButtonProcessed)
            {
                HandleAButtonPress();
                aButtonProcessed = true;
            }
        }

        /// <summary>
        /// 新增：处理A键按下事件 - 回到节点
        /// </summary>
        private void HandleAButtonPress()
        {
            if (currentSelectedIndex < 0 || currentSelectedIndex >= visibleEntries.Count)
            {
                if (enableDebugLogs)
                {
                    Debug.LogWarning($"[LogNavigationController] 无法处理A键按下：当前选中索引无效 {currentSelectedIndex}");
                }
                return;
            }

            GameObject selectedEntry = visibleEntries[currentSelectedIndex];
            if (selectedEntry == null)
            {
                if (enableDebugLogs)
                {
                    Debug.LogWarning($"[LogNavigationController] 无法处理A键按下：选中条目为null");
                }
                return;
            }

            // 查找GoBackButton
            Transform goBackButtonTransform = selectedEntry.transform.Find("Text/GoBackButton");
            if (goBackButtonTransform == null)
            {
                if (enableDebugLogs)
                {
                    Debug.LogWarning($"[LogNavigationController] 无法处理A键按下：未找到GoBackButton");
                }
                return;
            }

            Button goBackButton = goBackButtonTransform.GetComponent<Button>();
            if (goBackButton == null)
            {
                if (enableDebugLogs)
                {
                    Debug.LogWarning($"[LogNavigationController] 无法处理A键按下：GoBackButton上未找到Button组件");
                }
                return;
            }

            if (goBackButton.gameObject.activeInHierarchy && goBackButton.interactable)
            {
                goBackButton.onClick.Invoke();
                if (enableDebugLogs)
                {
                    Debug.Log($"[LogNavigationController] 手柄A键触发回到节点: {currentSelectedIndex}");
                }
            }
            else
            {
                if (enableDebugLogs)
                {
                    Debug.LogWarning($"[LogNavigationController] 无法处理A键按下：GoBackButton未激活或不可交互");
                }
            }
        }

        /// <summary>
        /// 处理滚动条控制
        /// </summary>
        private void CheckScrollbarControl()
        {
            if (scrollRect == null) return;

            // 使用右摇杆控制滚动
            if (Gamepad.current != null)
            {
                Vector2 rightStick = Gamepad.current.rightStick.ReadValue();

                // 添加死区，避免微小移动
                if (Mathf.Abs(rightStick.y) > 0.2f)
                {
                    // 直接设置滚动位置
                    float currentPos = scrollRect.verticalNormalizedPosition;
                    float scrollDelta = -rightStick.y * scrollSpeed * Time.unscaledDeltaTime;
                    float newPos = Mathf.Clamp01(currentPos + scrollDelta);

                    scrollRect.verticalNormalizedPosition = newPos;

                    // 滚动后刷新可见条目
                    if (Time.frameCount % 5 == 0) // 每5帧刷新一次，避免性能问题
                    {
                        RefreshVisibleEntries();
                        UpdateSelectionAfterScroll();
                    }
                }
            }
        }

        /// <summary>
        /// 处理垂直导航
        /// </summary>
        private void HandleVerticalNavigation(bool moveUp)
        {
            if (visibleEntries.Count == 0) return;

            int newIndex = currentSelectedIndex;

            // 确保索引有效
            if (newIndex < 0 || newIndex >= visibleEntries.Count)
            {
                newIndex = moveUp ? visibleEntries.Count - 1 : 0;
            }

            // 计算新索引 - 在当前可见页面内循环
            if (moveUp)
            {
                newIndex--;
                if (newIndex < 0)
                {
                    newIndex = visibleEntries.Count - 1;
                }
            }
            else
            {
                newIndex++;
                if (newIndex >= visibleEntries.Count)
                {
                    newIndex = 0;
                }
            }

            SelectLogEntry(newIndex);

            if (enableDebugLogs)
            {
                Debug.Log($"[LogNavigationController] 导航: {(moveUp ? "上" : "下")}, 从 {currentSelectedIndex} 到 {newIndex}");
            }
        }
        #endregion

        #region 条目选择和高亮管理
        /// <summary>
        /// 选择日志条目
        /// </summary>
        public void SelectLogEntry(int index)
        {
            if (index < 0 || index >= visibleEntries.Count || visibleEntries[index] == null)
            {
                if (enableDebugLogs)
                {
                    Debug.LogWarning($"[LogNavigationController] 尝试选择无效的日志索引: {index}");
                }
                return;
            }

            // 取消之前的高亮
            if (currentSelectedIndex >= 0 && currentSelectedIndex < visibleEntries.Count)
            {
                SetEntryHighlight(visibleEntries[currentSelectedIndex], false);
            }

            // 设置新的高亮
            currentSelectedIndex = index;
            lastValidSelectedIndex = index;
            SetEntryHighlight(visibleEntries[currentSelectedIndex], true);

            // 确保选中的条目在视图中（滚动调整）
            EnsureEntryVisible(index);

            if (enableDebugLogs)
            {
                Debug.Log($"[LogNavigationController] 选中日志条目: {index}");
            }
        }

        /// <summary>
        /// 选择最后一个条目
        /// </summary>
        public void SelectLastEntry()
        {
            if (visibleEntries.Count > 0)
            {
                SelectLogEntry(visibleEntries.Count - 1);
            }
        }

        /// <summary>
        /// 确保条目在视图中可见
        /// </summary>
        private void EnsureEntryVisible(int index)
        {
            if (scrollRect == null) return;
            if (index < 0 || index >= visibleEntries.Count) return;

            // 这里可以添加逻辑来确保选中的条目在视图中央
            // 由于我们使用可见条目管理，选中的条目应该已经在视图中
        }

        /// <summary>
        /// 滚动后更新选择
        /// </summary>
        private void UpdateSelectionAfterScroll()
        {
            if (currentSelectedIndex >= 0 && currentSelectedIndex < visibleEntries.Count)
            {
                // 重新应用高亮，确保状态正确
                SetEntryHighlight(visibleEntries[currentSelectedIndex], true);
            }
            else if (visibleEntries.Count > 0)
            {
                // 如果当前选择无效，选择第一个可见条目
                SelectLogEntry(0);
            }
        }
        #endregion

        #region 可见条目管理
        /// <summary>
        /// 刷新可见条目列表
        /// </summary>
        public void RefreshVisibleEntries()
        {
            // 恢复所有高亮
            RestoreAllHighlights();

            visibleEntries.Clear();
            originalTextColors.Clear();
            originalBackgroundColors.Clear();

            // 获取当前可见的条目
            if (scrollRect != null && scrollRect.content != null)
            {
                for (int i = 0; i < scrollRect.content.childCount; i++)
                {
                    GameObject child = scrollRect.content.GetChild(i).gameObject;
                    if (child.activeInHierarchy && child.GetComponent<LogEntryController>() != null)
                    {
                        visibleEntries.Add(child);
                        PrepareEntryForHighlight(child);
                    }
                }
            }

            if (enableDebugLogs)
            {
                Debug.Log($"[LogNavigationController] 刷新可见条目，找到 {visibleEntries.Count} 个条目");
            }

            // 重置选中索引
            if (currentSelectedIndex >= visibleEntries.Count ||
                (currentSelectedIndex >= 0 && visibleEntries[currentSelectedIndex] == null))
            {
                currentSelectedIndex = -1;
                lastValidSelectedIndex = -1;
            }
        }

        /// <summary>
        /// 为条目准备高亮效果
        /// </summary>
        private void PrepareEntryForHighlight(GameObject entry)
        {
            if (entry == null) return;

            // 保存原始文本颜色
            if (!originalTextColors.ContainsKey(entry))
            {
                List<Color> textColors = new List<Color>();

                // 收集普通Text组件
                Text[] texts = entry.GetComponentsInChildren<Text>(true);
                foreach (Text text in texts)
                {
                    if (text != null)
                    {
                        textColors.Add(text.color);
                    }
                }

                // 收集TextMeshPro组件
                TextMeshProUGUI[] tmpTexts = entry.GetComponentsInChildren<TextMeshProUGUI>(true);
                foreach (TextMeshProUGUI tmp in tmpTexts)
                {
                    if (tmp != null)
                    {
                        textColors.Add(tmp.color);
                    }
                }

                originalTextColors[entry] = textColors.ToArray();
            }

            // 保存原始背景颜色
            if (!originalBackgroundColors.ContainsKey(entry))
            {
                Image bg = entry.GetComponent<Image>();
                if (bg != null)
                {
                    originalBackgroundColors[entry] = bg.color;
                }
            }
        }

        /// <summary>
        /// 设置条目高亮状态
        /// </summary>
        private void SetEntryHighlight(GameObject entry, bool highlight)
        {
            if (entry == null) return;

            switch (highlightMode)
            {
                case HighlightMode.TextColor:
                    SetTextColorHighlight(entry, highlight);
                    break;
                case HighlightMode.Background:
                    SetBackgroundHighlight(entry, highlight);
                    break;
                case HighlightMode.Multiple:
                    SetTextColorHighlight(entry, highlight);
                    SetBackgroundHighlight(entry, highlight);
                    break;
                default:
                    SetTextColorHighlight(entry, highlight);
                    break;
            }
        }

        /// <summary>
        /// 设置文本颜色高亮
        /// </summary>
        private void SetTextColorHighlight(GameObject entry, bool highlight)
        {
            if (entry == null) return;

            if (highlight)
            {
                // 设置文本高亮颜色
                Text[] texts = entry.GetComponentsInChildren<Text>(true);
                foreach (Text text in texts)
                {
                    if (text != null)
                    {
                        text.color = textHighlightColor;
                    }
                }

                TextMeshProUGUI[] tmpTexts = entry.GetComponentsInChildren<TextMeshProUGUI>(true);
                foreach (TextMeshProUGUI tmp in tmpTexts)
                {
                    if (tmp != null)
                    {
                        tmp.color = textHighlightColor;
                    }
                }
            }
            else
            {
                // 恢复文本原始颜色
                if (originalTextColors.ContainsKey(entry))
                {
                    Color[] originalColors = originalTextColors[entry];
                    int colorIndex = 0;

                    Text[] texts = entry.GetComponentsInChildren<Text>(true);
                    foreach (Text text in texts)
                    {
                        if (text != null && colorIndex < originalColors.Length)
                        {
                            text.color = originalColors[colorIndex++];
                        }
                    }

                    TextMeshProUGUI[] tmpTexts = entry.GetComponentsInChildren<TextMeshProUGUI>(true);
                    foreach (TextMeshProUGUI tmp in tmpTexts)
                    {
                        if (tmp != null && colorIndex < originalColors.Length)
                        {
                            tmp.color = originalColors[colorIndex++];
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 设置背景高亮
        /// </summary>
        private void SetBackgroundHighlight(GameObject entry, bool highlight)
        {
            if (entry == null) return;

            Image bg = entry.GetComponent<Image>();
            if (bg != null)
            {
                if (highlight)
                {
                    bg.color = backgroundHighlightColor;
                }
                else if (originalBackgroundColors.ContainsKey(entry))
                {
                    bg.color = originalBackgroundColors[entry];
                }
            }
        }

        /// <summary>
        /// 恢复所有高亮效果
        /// </summary>
        private void RestoreAllHighlights()
        {
            foreach (GameObject entry in visibleEntries)
            {
                if (entry != null)
                {
                    SetEntryHighlight(entry, false);
                }
            }
        }
        #endregion

        #region 输入模式管理
        /// <summary>
        /// 检查游戏手柄连接状态
        /// </summary>
        private void CheckGamepadConnection()
        {
            bool isGamepadConnected = Gamepad.current != null;

            if (isGamepadConnected != wasGamepadConnected)
            {
                wasGamepadConnected = isGamepadConnected;

                if (isGamepadConnected && gamepadPriority)
                {
                    usingGamepad = true;
                    OnGamepadActivated();
                }
                else if (!isGamepadConnected)
                {
                    usingGamepad = false;
                    OnGamepadDeactivated();
                }
            }
            else if (isGamepadConnected && !usingGamepad && gamepadPriority)
            {
                usingGamepad = true;
                OnGamepadActivated();
            }
        }

        /// <summary>
        /// 检查输入模式
        /// </summary>
        private void CheckInputMode()
        {
            // 只在每几帧检查一次，减少性能开销
            if (Time.frameCount % 3 != 0) return;

            // 检查鼠标输入
            if (gamepadPriority && usingGamepad && Mouse.current != null)
            {
                Vector2 mouseDelta = Mouse.current.delta.ReadValue();
                if (mouseDelta.magnitude > 0.1f || Mouse.current.leftButton.wasPressedThisFrame)
                {
                    if (enableDebugLogs)
                    {
                        Debug.Log($"[LogNavigationController] 检测到鼠标输入，切换到键鼠模式");
                    }
                    usingGamepad = false;
                    OnGamepadDeactivated();
                }
            }

            // 检查手柄输入
            if (gamepadPriority && !usingGamepad && Gamepad.current != null)
            {
                Vector2 dpad = Gamepad.current.dpad.ReadValue();
                bool anyButtonPressed = Gamepad.current.aButton.wasPressedThisFrame ||
                                      Gamepad.current.bButton.wasPressedThisFrame ||
                                      Gamepad.current.xButton.wasPressedThisFrame ||
                                      Gamepad.current.yButton.wasPressedThisFrame;

                if (dpad != Vector2.zero || anyButtonPressed)
                {
                    if (enableDebugLogs)
                    {
                        Debug.Log($"[LogNavigationController] 检测到手柄输入，切换到手柄模式");
                    }
                    usingGamepad = true;
                    OnGamepadActivated();
                }
            }
        }

        /// <summary>
        /// 手柄模式激活
        /// </summary>
        private void OnGamepadActivated()
        {
            if (!isActive) return;

            Cursor.visible = false;

            if (lastValidSelectedIndex >= 0 && lastValidSelectedIndex < visibleEntries.Count)
            {
                SelectLogEntry(lastValidSelectedIndex);
            }
            else if (visibleEntries.Count > 0)
            {
                SelectLastEntry();
            }

            if (enableDebugLogs)
            {
                Debug.Log($"[LogNavigationController] 切换到手柄模式");
            }
        }

        /// <summary>
        /// 手柄模式停用
        /// </summary>
        private void OnGamepadDeactivated()
        {
            Cursor.visible = true;

            if (currentSelectedIndex >= 0 && currentSelectedIndex < visibleEntries.Count)
            {
                lastValidSelectedIndex = currentSelectedIndex;
            }

            if (currentSelectedIndex >= 0 && currentSelectedIndex < visibleEntries.Count)
            {
                SetEntryHighlight(visibleEntries[currentSelectedIndex], false);
                currentSelectedIndex = -1;
            }

            // 重置输入状态
            dpadUpPressed = false;
            dpadDownPressed = false;
            dpadUpProcessed = false;
            dpadDownProcessed = false;
            aButtonPressed = false;
            aButtonProcessed = false;

            if (enableDebugLogs)
            {
                Debug.Log($"[LogNavigationController] 切换到键鼠模式");
            }
        }
        #endregion

        #region 协程管理
        /// <summary>
        /// 延迟选择最后一个条目
        /// </summary>
        private IEnumerator SelectLastEntryDelayed()
        {
            yield return new WaitForEndOfFrame();
            yield return new WaitForSeconds(0.1f);

            if (visibleEntries.Count > 0)
            {
                SelectLastEntry();
            }
        }

        /// <summary>
        /// 启动定期刷新
        /// </summary>
        private void StartPeriodicRefresh()
        {
            StopPeriodicRefresh();
            refreshCoroutine = StartCoroutine(PeriodicRefreshCoroutine());
        }

        /// <summary>
        /// 停止定期刷新
        /// </summary>
        private void StopPeriodicRefresh()
        {
            if (refreshCoroutine != null)
            {
                StopCoroutine(refreshCoroutine);
                refreshCoroutine = null;
            }
        }

        /// <summary>
        /// 定期刷新协程
        /// </summary>
        private IEnumerator PeriodicRefreshCoroutine()
        {
            WaitForSeconds waitInterval = new WaitForSeconds(1f); // 每秒刷新一次

            while (isActive)
            {
                yield return waitInterval;

                // 刷新可见条目
                RefreshVisibleEntries();

                // 如果当前选择无效但有条目，重新选择
                if (currentSelectedIndex == -1 && visibleEntries.Count > 0)
                {
                    SelectLastEntry();
                }
            }
        }
        #endregion

        #region 公共API
        /// <summary>
        /// 获取当前选中的日志条目索引
        /// </summary>
        public int GetCurrentSelectedIndex()
        {
            return currentSelectedIndex;
        }

        /// <summary>
        /// 获取当前选中的日志条目
        /// </summary>
        public GameObject GetCurrentSelectedEntry()
        {
            if (currentSelectedIndex >= 0 && currentSelectedIndex < visibleEntries.Count)
            {
                return visibleEntries[currentSelectedIndex];
            }
            return null;
        }

        /// <summary>
        /// 设置高亮模式
        /// </summary>
        public void SetHighlightMode(HighlightMode mode)
        {
            if (highlightMode != mode)
            {
                highlightMode = mode;
                RefreshVisibleEntries();

                if (currentSelectedIndex >= 0 && currentSelectedIndex < visibleEntries.Count)
                {
                    SetEntryHighlight(visibleEntries[currentSelectedIndex], true);
                }
            }
        }

        /// <summary>
        /// 手动刷新可见条目
        /// </summary>
        [ContextMenu("手动刷新可见条目")]
        public void ManualRefreshVisibleEntries()
        {
            RefreshVisibleEntries();

            if (usingGamepad && visibleEntries.Count > 0 && currentSelectedIndex == -1)
            {
                SelectLastEntry();
            }
        }

        /// <summary>
        /// 强制重新选中最后有效的条目
        /// </summary>
        public void ForceReselectLastValid()
        {
            if (lastValidSelectedIndex >= 0 && lastValidSelectedIndex < visibleEntries.Count)
            {
                SelectLogEntry(lastValidSelectedIndex);
            }
            else if (visibleEntries.Count > 0)
            {
                SelectLastEntry();
            }
        }

        /// <summary>
        /// 手动设置滚动条组件
        /// </summary>
        public void SetScrollRect(LoopVerticalScrollRectWithSwitch newScrollRect)
        {
            scrollRect = newScrollRect;
            RefreshVisibleEntries();
        }

        /// <summary>
        /// 获取可见条目总数
        /// </summary>
        public int GetVisibleEntryCount()
        {
            return visibleEntries.Count;
        }

        /// <summary>
        /// 检查是否已初始化
        /// </summary>
        public bool IsInitialized()
        {
            return isInitialized;
        }

        /// <summary>
        /// 新增：手动触发A键功能（用于测试）
        /// </summary>
        [ContextMenu("测试A键功能")]
        public void TestAButtonFunction()
        {
            if (currentSelectedIndex >= 0 && currentSelectedIndex < visibleEntries.Count)
            {
                HandleAButtonPress();
            }
            else
            {
                Debug.LogWarning($"[LogNavigationController] 无法测试A键功能：没有选中的条目");
            }
        }
        #endregion

        #region 调试方法
        [ContextMenu("打印当前状态")]
        public void PrintCurrentStatus()
        {
            Debug.Log($"[LogNavigationController] 状态报告:");
            Debug.Log($"- 可见条目总数: {visibleEntries.Count}");
            Debug.Log($"- 当前选中索引: {currentSelectedIndex}");
            Debug.Log($"- 最后有效索引: {lastValidSelectedIndex}");
            Debug.Log($"- 活动状态: {isActive}");
            Debug.Log($"- 使用手柄: {usingGamepad}");
            Debug.Log($"- 高亮模式: {highlightMode}");
            Debug.Log($"- 滚动条组件: {(scrollRect != null ? "已分配" : "未分配")}");
            Debug.Log($"- 滚动条位置: {(scrollRect != null ? scrollRect.verticalNormalizedPosition.ToString("F2") : "N/A")}");
            Debug.Log($"- 初始化状态: {isInitialized}");
            Debug.Log($"- A键状态: 按下={aButtonPressed}, 已处理={aButtonProcessed}");

            for (int i = Mathf.Max(0, currentSelectedIndex - 2); i < Mathf.Min(visibleEntries.Count, currentSelectedIndex + 3); i++)
            {
                if (i >= 0 && i < visibleEntries.Count)
                {
                    string status = (i == currentSelectedIndex) ? "[选中]" : "";
                    Debug.Log($"- 可见条目 {i}: {visibleEntries[i]?.name ?? "null"} {status}");
                }
            }
        }

        [ContextMenu("选中最后一个条目")]
        public void DebugSelectLast()
        {
            if (visibleEntries.Count > 0)
            {
                SelectLastEntry();
            }
        }
        #endregion

        // 配置
        private bool useLazyInitialization = true;
    }
}
