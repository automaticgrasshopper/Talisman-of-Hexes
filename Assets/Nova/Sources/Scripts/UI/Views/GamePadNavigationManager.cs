using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Nova
{
    // 面板优先级定义
    [System.Serializable]
    public class PanelPriority
    {
        public const int MAIN_MENU = 10;      // 主界面 
        public const int SETTINGS = 50;       // 设置界面
        public const int SAVE_LOAD = 100;      // 存档界面
        public const int HELP = 60;           // 帮助界面
        public const int LOG = 100;           // 回看日志界面
        public const int NOTIFICATION = 1000; // 通知界面
        public const int MINIGAME = 20;       // 小游戏界面
        public const int CHOICES = 20;        // 选择界面
        public const int SELECT_TITLE = 200;        // 选择界面
    }

    // 导航面板接口
    public interface INavigationPanel
    {
        string PanelName { get; }
        int Priority { get; }
        bool IsActive { get; }
        bool UseCustomNavigation { get; }
        GameObject DefaultSelection { get; }
        void OnPanelActivated();
        void OnPanelDeactivated();
        void HandleCustomNavigation();
        bool ShouldIgnoreSubmit();
    }

    // 输入模式
    public enum InputMode
    {
        KeyboardMouse,
        Gamepad
    }

    public class GamePadNavigationManager : MonoBehaviour
    {
        #region Singleton
        private static GamePadNavigationManager _instance;
        public static GamePadNavigationManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<GamePadNavigationManager>();
                    if (_instance == null)
                    {
                        GameObject go = new GameObject("GamePadNavigationManager");
                        _instance = go.AddComponent<GamePadNavigationManager>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }
        #endregion

        [Header("手柄导航设置")]
        [Tooltip("初始选中的UI对象")]
        [SerializeField] private GameObject firstSelectedObject;
        [Tooltip("启动时自动启用手柄导航")]
        [SerializeField] private bool autoEnableOnStart = true;
        [Tooltip("选中对象的延迟时间（秒）")]
        [SerializeField] private float selectionDelay = 0.1f;

        [Header("CanvasGroup 处理")]
        [Tooltip("跳过CanvasGroup交互性检查")]
        [SerializeField] private bool bypassCanvasGroupCheck = true;
        [Tooltip("自动启用CanvasGroup的交互性")]
        [SerializeField] private bool autoEnableCanvasGroup = true;

        [Header("高亮显示修复")]
        [Tooltip("强制保持高亮状态")]
        [SerializeField] private bool forceHighlightState = true;
        [Tooltip("使用通用高亮方法（适用于Toggle和Slider）")]
        [SerializeField] private bool useUniversalHighlight = true;

        [Header("导航系统修复")]
        [Tooltip("启用手柄导航")]
        [SerializeField] private bool enableNavigation = true;
        [Tooltip("导航输入冷却时间（秒）")]
        [SerializeField] private float navigationCooldown = 0.1f;

        [Header("布局组兼容性")]
        [Tooltip("等待布局重建完成后再设置选中对象")]
        [SerializeField] private bool waitForLayoutRebuild = true;
        [Tooltip("最大重试尝试次数")]
        [SerializeField] private int maxRetryAttempts = 3;

        [Header("输入模式设置")]
        [Tooltip("手柄输入优先于鼠标输入")]
        [SerializeField] private bool gamepadPriority = true;
        [Tooltip("启用时重置选中状态")]
        [SerializeField] private bool resetSelectionOnEnable = true;

        [Header("UI元素兼容性")]
        [Tooltip("支持Toggle组件")]
        [SerializeField] private bool supportToggle = true;
        [Tooltip("支持Slider组件")]
        [SerializeField] private bool supportSlider = true;

        [Header("防抖和快速点击保护")]
        [Tooltip("启用防抖保护，防止快速连续点击")]
        [SerializeField] private bool enableDebounceProtection = true;
        [Tooltip("防抖时间（秒）- 在此时间内重复点击被视为一次")]
        [SerializeField] private float debounceTime = 0.3f;
        [Tooltip("快速点击保护时间（秒）- 防止UI状态未稳定时重复操作")]
        [SerializeField] private float rapidClickProtectionTime = 0.5f;
        [Tooltip("强制保持选中状态的最小时间（秒）")]
        [SerializeField] private float minSelectionHoldTime = 0.2f;

        [Header("调试设置")]
        [Tooltip("启用调试日志")]
        [SerializeField] public bool enableDebugLogs = false; // 默认关闭调试日志

        // 私有字段
        private bool usingGamepad;
        private bool wasGamepadConnected;
        private GameObject currentSelectedObject;
        private EventSystem eventSystem;
        private float lastNavigationTime;
        private bool isNavigating;
        private bool isInteractingWithUI = false;
        private bool shouldIgnoreSubmit = false;
        private GameObject lastSelectedObject;

        // 防抖和快速点击保护
        private float lastInteractionTime = 0f;
        private bool isInDebouncePeriod = false;
        private bool isInRapidClickProtection = false;
        private Coroutine debounceCoroutine;
        private Coroutine rapidClickProtectionCoroutine;
        private Coroutine selectionHoldCoroutine;
        private string lastInteractedObjectName = "";

        // 面板管理
        private List<INavigationPanel> registeredPanels = new List<INavigationPanel>();
        private INavigationPanel currentActivePanel;
        private int retryCount;
        private UIType currentUIType;

        // UI类型检测
        private enum UIType { Button, Toggle, Slider, Other }

        // 新增：输入模式事件
        private InputMode currentInputMode = InputMode.KeyboardMouse;
        public System.Action<InputMode> OnInputModeChanged;

        private void Awake()
        {
            // 单例初始化
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            // 延迟获取 EventSystem，避免执行顺序问题
            StartCoroutine(DelayedAwake());
        }

        private IEnumerator DelayedAwake()
        {
            // 等待一帧，确保 EventSystem 已经初始化
            yield return null;

            eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                eventSystem = FindObjectOfType<EventSystem>();
                if (eventSystem == null)
                {
                    Debug.LogError("场景中没有 EventSystem! 请添加 EventSystem 组件");
                }
                else
                {
                    if (enableDebugLogs)
                    {
                        Debug.Log($"[GamePadNavigationManager] 找到 EventSystem: {eventSystem.gameObject.name}");
                    }
                }
            }
            else
            {
                if (enableDebugLogs)
                {
                    Debug.Log($"[GamePadNavigationManager] 使用当前 EventSystem: {eventSystem.gameObject.name}");
                }
            }

            // 初始化最后选中的对象
            lastSelectedObject = firstSelectedObject;

            // 预定义面板优先级
            InitializeDefaultPanels();
        }

        /// <summary>
        /// 初始化默认面板配置
        /// </summary>
        private void InitializeDefaultPanels()
        {
            // 这里可以预定义一些面板配置，实际使用时通过RegisterPanel动态注册
            if (enableDebugLogs)
            {
                Debug.Log("[GamePadNavigationManager] 初始化默认面板配置");
            }
        }

        private void Start()
        {
            // 检查第一个选中对象
            if (firstSelectedObject == null)
            {
                if (enableDebugLogs)
                {
                    Debug.LogWarning("[GamePadNavigationManager] 第一个选中对象未设置，将尝试自动查找");
                }

                // 尝试自动查找一个合适的默认选中对象
                firstSelectedObject = FindDefaultSelectable();

                if (firstSelectedObject != null)
                {
                    if (enableDebugLogs)
                    {
                        Debug.Log($"[GamePadNavigationManager] 自动找到默认选中对象: {firstSelectedObject.name}");
                    }
                }
                else
                {
                    if (enableDebugLogs)
                    {
                        Debug.LogWarning("[GamePadNavigationManager] 无法自动找到默认选中对象，请在Inspector中手动设置");
                    }
                }
            }

            if (autoEnableOnStart)
            {
                EnableGamepadNavigation();
            }
        }

        /// <summary>
        /// 尝试自动查找一个合适的默认选中对象
        /// </summary>
        private GameObject FindDefaultSelectable()
        {
            // 查找场景中的第一个按钮
            Button firstButton = FindObjectOfType<Button>();
            if (firstButton != null)
            {
                return firstButton.gameObject;
            }

            // 如果没有按钮，查找其他可选中对象
            Selectable firstSelectable = FindObjectOfType<Selectable>();
            if (firstSelectable != null)
            {
                return firstSelectable.gameObject;
            }

            return null;
        }

        private void OnEnable()
        {
            if (resetSelectionOnEnable)
            {
                retryCount = 0;

                if (enableDebugLogs)
                {
                    Debug.Log($"[GamePadNavigationManager] 管理器启用，准备重置选中状态");
                }

                if (autoEnableOnStart)
                {
                    StartCoroutine(DelayedEnable());
                }
            }
        }

        private void OnDisable()
        {
            DisableGamepadNavigation();
            StopAllProtectionCoroutines();
        }

        private void OnDestroy()
        {
            DisableGamepadNavigation();
            StopAllProtectionCoroutines();

            // 停止所有可能正在运行的协程
            StopAllCoroutines();

            registeredPanels.Clear();

            // 清理单例引用
            if (_instance == this)
            {
                _instance = null;
            }
        }

        #region 输入模式管理
        /// <summary>
        /// 获取当前输入模式
        /// </summary>
        public InputMode GetCurrentInputMode()
        {
            return currentInputMode;
        }

        /// <summary>
        /// 设置输入模式
        /// </summary>
        private void SetInputMode(InputMode newMode)
        {
            if (currentInputMode != newMode)
            {
                InputMode oldMode = currentInputMode;
                currentInputMode = newMode;

                if (enableDebugLogs)
                {
                    Debug.Log($"[GamePadNavigationManager] 输入模式改变: {oldMode} -> {newMode}");
                }

                // 触发输入模式改变事件
                OnInputModeChanged?.Invoke(newMode);
            }
        }

        /// <summary>
        /// 强制设置输入模式
        /// </summary>
        public void ForceSetInputMode(InputMode mode)
        {
            SetInputMode(mode);

            if (mode == InputMode.Gamepad)
            {
                usingGamepad = true;
                OnGamepadActivated();
            }
            else
            {
                usingGamepad = false;
                OnGamepadDeactivated();
            }
        }
        #endregion

        #region 面板管理
        /// <summary>
        /// 注册导航面板
        /// </summary>
        public void RegisterPanel(INavigationPanel panel)
        {
            if (panel == null) return;

            if (!registeredPanels.Contains(panel))
            {
                registeredPanels.Add(panel);

                // 按优先级排序
                registeredPanels.Sort((a, b) => b.Priority.CompareTo(a.Priority));

                if (enableDebugLogs)
                {
                    Debug.Log($"[GamePadNavigationManager] 注册面板: {panel.PanelName}, 优先级: {panel.Priority}, 总面板数: {registeredPanels.Count}");
                }

                // 如果这是第一个面板或者优先级更高，激活它
                UpdateActivePanel();
            }
        }

        /// <summary>
        /// 注销导航面板
        /// </summary>
        public void UnregisterPanel(INavigationPanel panel)
        {
            if (panel != null && registeredPanels.Contains(panel))
            {
                registeredPanels.Remove(panel);

                if (enableDebugLogs)
                {
                    Debug.Log($"[GamePadNavigationManager] 注销面板: {panel.PanelName}, 剩余面板数: {registeredPanels.Count}");
                }

                // 如果注销的是当前活动面板，更新活动面板
                if (currentActivePanel == panel)
                {
                    UpdateActivePanel();
                }
            }
        }

        /// <summary>
        /// 更新当前活动面板
        /// </summary>
        public void UpdateActivePanel()
        {
            INavigationPanel newActivePanel = null;

            // 找到优先级最高的活动面板
            foreach (var panel in registeredPanels)
            {
                if (panel.IsActive)
                {
                    newActivePanel = panel;
                    break;
                }
            }

            // 如果活动面板发生变化
            if (newActivePanel != currentActivePanel)
            {
                // 停用旧面板
                if (currentActivePanel != null)
                {
                    currentActivePanel.OnPanelDeactivated();
                    if (enableDebugLogs)
                    {
                        Debug.Log($"[GamePadNavigationManager] 停用面板: {currentActivePanel.PanelName}");
                    }
                }

                // 激活新面板
                currentActivePanel = newActivePanel;
                if (currentActivePanel != null)
                {
                    currentActivePanel.OnPanelActivated();
                    if (enableDebugLogs)
                    {
                        Debug.Log($"[GamePadNavigationManager] 激活面板: {currentActivePanel.PanelName}, 优先级: {currentActivePanel.Priority}");
                    }

                    // 延迟设置新面板的默认选中对象
                    StartCoroutine(DelayedPanelSelection());
                }
                else
                {
                    if (enableDebugLogs)
                    {
                        Debug.Log($"[GamePadNavigationManager] 没有活动面板");
                    }
                }
            }
        }

        private System.Collections.IEnumerator DelayedPanelSelection()
        {
            yield return null; // 等待一帧确保UI布局完成

            if (usingGamepad && currentActivePanel != null && currentActivePanel.DefaultSelection != null)
            {
                SetSelectedObject(currentActivePanel.DefaultSelection);
            }
        }

        /// <summary>
        /// 强制激活指定面板
        /// </summary>
        public void ActivatePanel(INavigationPanel panel)
        {
            if (panel != null && panel.IsActive)
            {
                // 暂时将该面板移到列表前面（最高优先级）
                registeredPanels.Remove(panel);
                registeredPanels.Insert(0, panel);
                UpdateActivePanel();
            }
        }

        /// <summary>
        /// 获取当前活动面板
        /// </summary>
        public INavigationPanel GetCurrentActivePanel()
        {
            return currentActivePanel;
        }

        /// <summary>
        /// 检查指定面板是否是当前活动面板
        /// </summary>
        public bool IsPanelActive(INavigationPanel panel)
        {
            return currentActivePanel == panel;
        }
        #endregion

        #region 核心导航功能
        // 全局开关：true = 整个手柄系统禁用（Update / Enable 全部 no-op）
        // 暂时不需要手柄支持，先关掉。要恢复时把这里改回 false 即可。
        private const bool GAMEPAD_SYSTEM_DISABLED = true;

        public void EnableGamepadNavigation()
        {
            if (GAMEPAD_SYSTEM_DISABLED) return;

            retryCount = 0;
            CheckGamepadConnection();

            if (enableDebugLogs)
            {
                Debug.Log($"[GamePadNavigationManager] 启用手柄导航");
            }
        }

        public void DisableGamepadNavigation()
        {
            usingGamepad = false;
            OnGamepadDeactivated();

            if (enableDebugLogs)
            {
                Debug.Log($"[GamePadNavigationManager] 禁用手柄导航");
            }
        }

        private void Update()
        {
            if (GAMEPAD_SYSTEM_DISABLED) return;

            CheckGamepadConnection();

            // 更新活动面板状态
            UpdateActivePanel();

            // 如果当前有活动面板且使用自定义导航，让面板处理自己的导航逻辑
            if (currentActivePanel != null && currentActivePanel.UseCustomNavigation)
            {
                currentActivePanel.HandleCustomNavigation();
                return;
            }

            // 否则使用默认导航逻辑
            CheckGamepadInput();

            // 如果当前没有选中对象，但应该有一个，则重新设置
            if (usingGamepad && eventSystem != null &&
                eventSystem.currentSelectedGameObject == null &&
                currentSelectedObject != null && !isNavigating && !isInteractingWithUI &&
                !isInDebouncePeriod && !isInRapidClickProtection)
            {
                if (retryCount < maxRetryAttempts)
                {
                    retryCount++;
                    SetSelectedObject(currentSelectedObject);

                    if (enableDebugLogs && retryCount > 1)
                    {
                        Debug.Log($"[GamePadNavigationManager] 重新尝试设置选中对象，尝试次数: {retryCount}");
                    }
                }
                else
                {
                    if (enableDebugLogs)
                    {
                        Debug.LogWarning($"[GamePadNavigationManager] 超过最大重试次数 ({maxRetryAttempts})，停止尝试设置选中对象");
                    }
                    retryCount = 0;
                }
            }
            else if (usingGamepad && eventSystem != null &&
                     eventSystem.currentSelectedGameObject != null)
            {
                retryCount = 0;

                // 更新当前选中对象
                GameObject newSelected = eventSystem.currentSelectedGameObject;
                if (newSelected != currentSelectedObject)
                {
                    currentSelectedObject = newSelected;

                    // 更新最后选中的对象
                    if (IsObjectSelectable(newSelected))
                    {
                        lastSelectedObject = newSelected;
                        if (enableDebugLogs)
                        {
                            Debug.Log($"[GamePadNavigationManager] 更新最后选中对象: {newSelected.name}");
                        }
                    }

                    currentUIType = GetUIType(currentSelectedObject);
                    isInteractingWithUI = false;
                    shouldIgnoreSubmit = false;
                }

                // 持续确保高亮状态（仅在非导航状态下）
                if (forceHighlightState && currentSelectedObject != null && !isNavigating)
                {
                    EnsureHighlightState(currentSelectedObject);
                }
            }

            // 检查鼠标输入，如果检测到鼠标输入且不是手柄优先模式，切换到键鼠模式
            if (gamepadPriority && usingGamepad && Mouse.current != null)
            {
                Vector2 mouseDelta = Mouse.current.delta.ReadValue();
                if (mouseDelta.magnitude > 0.1f || Mouse.current.leftButton.wasPressedThisFrame)
                {
                    if (enableDebugLogs)
                    {
                        Debug.Log($"[GamePadNavigationManager] 检测到鼠标输入，切换到键鼠模式");
                    }
                    usingGamepad = false;
                    OnGamepadDeactivated();
                }
            }
        }

        /// <summary>
        /// 检查手柄输入（导航输入和按钮输入）
        /// </summary>
        private void CheckGamepadInput()
        {
            if (Gamepad.current == null) return;

            // 检查导航输入（十字键或左摇杆）
            Vector2 dpad = Gamepad.current.dpad.ReadValue();
            Vector2 leftStick = Gamepad.current.leftStick.ReadValue();
            bool hasNavigationInput = dpad != Vector2.zero || leftStick.magnitude > 0.2f;

            // 检查按钮输入
            bool anyButtonPressed = Gamepad.current.aButton.wasPressedThisFrame ||
                                   Gamepad.current.bButton.wasPressedThisFrame ||
                                   Gamepad.current.xButton.wasPressedThisFrame ||
                                   Gamepad.current.yButton.wasPressedThisFrame ||
                                   Gamepad.current.startButton.wasPressedThisFrame ||
                                   Gamepad.current.selectButton.wasPressedThisFrame;

            bool hasAnyGamepadInput = hasNavigationInput || anyButtonPressed;

            // 检查是否在冷却时间内
            bool canNavigate = Time.unscaledTime - lastNavigationTime > navigationCooldown;

            if (hasAnyGamepadInput)
            {
                // 如果当前不是使用手柄模式但检测到手柄输入，切换到手柄模式
                if (!usingGamepad && gamepadPriority)
                {
                    usingGamepad = true;
                    OnGamepadActivated();
                }

                // 如果有任何手柄输入且当前没有选中对象，立即选中对象
                if (usingGamepad && (eventSystem.currentSelectedGameObject == null ||
                    !IsObjectSelectable(eventSystem.currentSelectedGameObject)) &&
                    !isInteractingWithUI && !isInDebouncePeriod)
                {
                    GameObject targetObject = currentActivePanel?.DefaultSelection ?? firstSelectedObject;

                    if (enableDebugLogs)
                    {
                        Debug.Log($"[GamePadNavigationManager] 检测到手柄输入，立即选中对象: {targetObject?.name ?? "未设置"}");
                    }

                    if (targetObject != null)
                    {
                        SetSelectedObject(targetObject);
                    }
                }

                // 如果是导航输入且不在冷却时间，标记为导航状态
                if (hasNavigationInput && canNavigate && enableNavigation)
                {
                    isNavigating = true;
                    lastNavigationTime = Time.unscaledTime;
                    isInteractingWithUI = false;
                    shouldIgnoreSubmit = false;

                    if (enableDebugLogs)
                    {
                        Debug.Log($"[GamePadNavigationManager] 检测到导航输入: DPad={dpad}, LeftStick={leftStick}");
                    }

                    // 设置一个短暂的导航状态，然后重置
                    StartCoroutine(ResetNavigationState());
                }

                // 检查是否在交互性UI元素上按下按钮
                if (anyButtonPressed && currentSelectedObject != null)
                {
                    // 新增：防抖检查
                    if (enableDebounceProtection && IsInDebouncePeriod(currentSelectedObject))
                    {
                        if (enableDebugLogs)
                        {
                            Debug.Log($"[GamePadNavigationManager] 防抖保护：忽略快速重复点击 {currentSelectedObject.name}");
                        }
                        return; // 在防抖期内，忽略此次点击
                    }

                    UIType uiType = GetUIType(currentSelectedObject);

                    // 对于Toggle和Slider，标记为正在交互
                    if (uiType == UIType.Toggle || uiType == UIType.Slider)
                    {
                        // 新增：开始防抖保护
                        StartDebounceProtection();

                        // 新增：开始快速点击保护
                        StartRapidClickProtection();

                        // 新增：强制保持选中状态
                        StartSelectionHoldProtection();

                        isInteractingWithUI = true;

                        // 更新最后选中的对象
                        lastSelectedObject = currentSelectedObject;

                        // 对于Slider，按下确认键时忽略提交，保持选中状态
                        if (uiType == UIType.Slider)
                        {
                            shouldIgnoreSubmit = true;
                            if (enableDebugLogs)
                            {
                                Debug.Log($"[GamePadNavigationManager] 在Slider上按下确认键，忽略提交操作，保持选中状态");
                            }
                        }

                        if (enableDebugLogs)
                        {
                            Debug.Log($"[GamePadNavigationManager] 检测到在 {uiType} 上的按钮输入，保持选中状态");
                        }
                    }
                    else if (uiType == UIType.Button)
                    {
                        // 按钮点击后也记住选中状态
                        lastSelectedObject = currentSelectedObject;
                        if (enableDebugLogs)
                        {
                            Debug.Log($"[GamePadNavigationManager] 按钮点击后记住选中状态: {currentSelectedObject.name}");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 设置选中的对象
        /// </summary>
        public void SetSelectedObject(GameObject selectedObject)
        {
            if ((isInDebouncePeriod || isInRapidClickProtection) &&
    (currentUIType == UIType.Toggle || currentUIType == UIType.Slider) &&
    selectedObject != currentSelectedObject)
            {
                if (enableDebugLogs)
                {
                    Debug.Log($"[GamePadNavigationManager] 保护期内保持当前选中对象: {currentSelectedObject?.name ?? "null"}");
                }
                return;
            }
            // 检查 EventSystem 是否存在
            if (eventSystem == null)
            {
                eventSystem = EventSystem.current;
                if (eventSystem == null)
                {
                    Debug.LogError("[GamePadNavigationManager] 无法找到 EventSystem!");
                    return;
                }
            }

            if (selectedObject != null && eventSystem != null)
            {
                if (!selectedObject.activeInHierarchy)
                {
                    if (enableDebugLogs)
                    {
                        Debug.LogWarning($"[GamePadNavigationManager] 尝试选中的对象 {selectedObject.name} 未激活，无法选中");
                    }
                    return;
                }

                // 检查UI元素类型和交互性
                UIType uiType = GetUIType(selectedObject);
                bool isInteractable = IsObjectSelectable(selectedObject);

                if (!isInteractable)
                {
                    if (enableDebugLogs)
                    {
                        Debug.LogWarning($"[GamePadNavigationManager] 对象 {selectedObject.name} 不可交互，类型: {uiType}");
                    }
                    return;
                }

                // 设置选中对象
                eventSystem.SetSelectedGameObject(selectedObject);
                currentSelectedObject = selectedObject;
                currentUIType = uiType;

                // 更新最后选中的对象
                lastSelectedObject = selectedObject;

                // 设置新对象时重置交互状态
                isInteractingWithUI = false;
                shouldIgnoreSubmit = false;

                // 根据UI类型处理选中状态
                switch (uiType)
                {
                    case UIType.Button:
                        var button = selectedObject.GetComponent<Button>();
                        if (button != null)
                        {
                            button.Select();
                            if (forceHighlightState)
                            {
                                EnsureHighlightState(selectedObject);
                            }
                        }
                        break;
                    case UIType.Toggle:
                        var toggle = selectedObject.GetComponent<Toggle>();
                        if (toggle != null)
                        {
                            toggle.Select();
                            // Toggle选中后强制保持高亮状态
                            if (forceHighlightState && useUniversalHighlight)
                            {
                                EnsureToggleHighlight(toggle);
                                // 启动协程持续确保Toggle高亮
                                StartCoroutine(KeepToggleHighlighted(toggle));
                            }
                        }
                        break;
                    case UIType.Slider:
                        var slider = selectedObject.GetComponent<Slider>();
                        if (slider != null)
                        {
                            slider.Select();
                            if (forceHighlightState && useUniversalHighlight)
                            {
                                EnsureSliderHighlight(slider);
                            }
                        }
                        break;
                    default:
                        // 对于其他Selectable类型
                        var selectable = selectedObject.GetComponent<Selectable>();
                        if (selectable != null)
                        {
                            selectable.Select();
                            if (forceHighlightState)
                            {
                                EnsureHighlightState(selectedObject);
                            }
                        }
                        break;
                }

                if (enableDebugLogs)
                {
                    Debug.Log($"[GamePadNavigationManager] 已选中对象: {selectedObject.name} (UI类型: {uiType})");
                }

                StartCoroutine(ValidateSelection(selectedObject));
            }
            else
            {
                if (enableDebugLogs)
                {
                    if (selectedObject == null)
                    {
                        Debug.LogError("[GamePadNavigationManager] selectedObject 未设置!");
                    }
                    if (eventSystem == null)
                    {
                        Debug.LogError("[GamePadNavigationManager] EventSystem 为 null!");
                    }
                }
            }
        }
        #endregion

        #region 保护机制
        /// <summary>
        /// 检查是否处于防抖期内
        /// </summary>
        private bool IsInDebouncePeriod(GameObject currentObject)
        {
            if (!enableDebounceProtection) return false;

            float timeSinceLastInteraction = Time.unscaledTime - lastInteractionTime;
            bool isSameObject = currentObject.name == lastInteractedObjectName;

            // 如果是同一个对象且在防抖时间内，则处于防抖期
            return timeSinceLastInteraction < debounceTime && isSameObject;
        }

        /// <summary>
        /// 开始防抖保护
        /// </summary>
        private void StartDebounceProtection()
        {
            if (!enableDebounceProtection) return;

            lastInteractionTime = Time.unscaledTime;
            lastInteractedObjectName = currentSelectedObject != null ? currentSelectedObject.name : "";
            isInDebouncePeriod = true;

            // 停止之前的防抖协程
            if (debounceCoroutine != null)
            {
                StopCoroutine(debounceCoroutine);
            }

            // 启动新的防抖协程
            debounceCoroutine = StartCoroutine(DebounceCoroutine());
        }

        private IEnumerator DebounceCoroutine()
        {
            yield return new WaitForSeconds(debounceTime);
            isInDebouncePeriod = false;

            if (enableDebugLogs)
            {
                Debug.Log($"[GamePadNavigationManager] 防抖保护期结束");
            }
        }

        private void StartRapidClickProtection()
        {
            isInRapidClickProtection = true;

            // 停止之前的保护协程
            if (rapidClickProtectionCoroutine != null)
            {
                StopCoroutine(rapidClickProtectionCoroutine);
            }

            // 启动新的保护协程
            rapidClickProtectionCoroutine = StartCoroutine(RapidClickProtectionCoroutine());
        }

        private IEnumerator RapidClickProtectionCoroutine()
        {
            yield return new WaitForSeconds(rapidClickProtectionTime);
            isInRapidClickProtection = false;

            if (enableDebugLogs)
            {
                Debug.Log($"[GamePadNavigationManager] 快速点击保护期结束");
            }
        }

        private void StartSelectionHoldProtection()
        {
            // 停止之前的保护协程
            if (selectionHoldCoroutine != null)
            {
                StopCoroutine(selectionHoldCoroutine);
            }

            // 启动新的保护协程
            selectionHoldCoroutine = StartCoroutine(SelectionHoldCoroutine());
        }

        private IEnumerator SelectionHoldCoroutine()
        {
            float startTime = Time.unscaledTime;

            // 在最小保持时间内，强制保持当前选中状态
            while (Time.unscaledTime - startTime < minSelectionHoldTime)
            {
                if (currentSelectedObject != null && eventSystem != null)
                {
                    // 确保当前对象保持选中状态
                    if (eventSystem.currentSelectedGameObject != currentSelectedObject)
                    {
                        eventSystem.SetSelectedGameObject(currentSelectedObject);

                        if (enableDebugLogs)
                        {
                            Debug.Log($"[GamePadNavigationManager] 强制保持选中状态: {currentSelectedObject.name}");
                        }
                    }
                }
                yield return null;
            }

            if (enableDebugLogs)
            {
                Debug.Log($"[GamePadNavigationManager] 选中状态保持保护期结束");
            }
        }

        /// <summary>
        /// 停止所有保护协程
        /// </summary>
        private void StopAllProtectionCoroutines()
        {
            if (debounceCoroutine != null)
            {
                StopCoroutine(debounceCoroutine);
                debounceCoroutine = null;
            }

            if (rapidClickProtectionCoroutine != null)
            {
                StopCoroutine(rapidClickProtectionCoroutine);
                rapidClickProtectionCoroutine = null;
            }

            if (selectionHoldCoroutine != null)
            {
                StopCoroutine(selectionHoldCoroutine);
                selectionHoldCoroutine = null;
            }
        }
        #endregion

        #region 工具方法
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

        private void OnGamepadActivated()
        {
            // 设置输入模式为手柄
            SetInputMode(InputMode.Gamepad);

            Cursor.visible = false;

            // 使用当前活动面板的默认对象
            GameObject targetObject = currentActivePanel?.DefaultSelection ?? firstSelectedObject;

            // 立即设置选中对象
            if (targetObject != null)
            {
                if (enableDebugLogs)
                {
                    Debug.Log($"[GamePadNavigationManager] 激活手柄模式，设置选中对象: {targetObject.name}");
                }

                SetSelectedObject(targetObject);
            }

            if (enableDebugLogs)
            {
                Debug.Log($"[GamePadNavigationManager] 切换到手柄模式");
            }
        }

        private void OnGamepadDeactivated()
        {
            // 设置输入模式为键鼠
            SetInputMode(InputMode.KeyboardMouse);

            Cursor.visible = true;

            if (!gamepadPriority)
            {
                if (eventSystem != null)
                {
                    eventSystem.SetSelectedGameObject(null);
                }
                currentSelectedObject = null;
            }

            retryCount = 0;
            isNavigating = false;
            isInteractingWithUI = false;
            shouldIgnoreSubmit = false;

            // 重置保护状态
            isInDebouncePeriod = false;
            isInRapidClickProtection = false;

            if (enableDebugLogs)
            {
                Debug.Log($"[GamePadNavigationManager] 切换到键鼠模式");
            }
        }

        /// <summary>
        /// 检查对象是否可选中
        /// </summary>
        private bool IsObjectSelectable(GameObject obj)
        {
            if (obj == null) return false;

            // 检查是否有任何可交互的UI组件
            var selectable = obj.GetComponent<Selectable>();
            if (selectable != null)
            {
                return selectable.interactable && selectable.isActiveAndEnabled;
            }

            // 检查Toggle
            var toggle = obj.GetComponent<Toggle>();
            if (toggle != null)
            {
                return toggle.interactable && toggle.isActiveAndEnabled;
            }

            // 检查Slider
            var slider = obj.GetComponent<Slider>();
            if (slider != null)
            {
                return slider.interactable && slider.isActiveAndEnabled;
            }

            return false;
        }

        /// <summary>
        /// 获取UI元素的类型
        /// </summary>
        private UIType GetUIType(GameObject obj)
        {
            if (obj == null) return UIType.Other;

            if (obj.GetComponent<Button>() != null) return UIType.Button;
            if (obj.GetComponent<Toggle>() != null) return UIType.Toggle;
            if (obj.GetComponent<Slider>() != null) return UIType.Slider;

            return UIType.Other;
        }

        private IEnumerator ResetNavigationState()
        {
            // 等待导航完成
            yield return new WaitForSeconds(0.1f);
            isNavigating = false;
        }

        private IEnumerator DelayedEnable()
        {
            yield return new WaitForEndOfFrame();

            CheckGamepadConnection();

            if (usingGamepad || (gamepadPriority && Gamepad.current != null))
            {
                GameObject targetObject = currentActivePanel?.DefaultSelection ?? firstSelectedObject;

                if (enableDebugLogs)
                {
                    Debug.Log($"[GamePadNavigationManager] 使用选中对象: {targetObject?.name ?? "null"}");
                }

                if (waitForLayoutRebuild)
                {
                    yield return StartCoroutine(SetSelectedAfterLayoutRebuild(targetObject));
                }
                else
                {
                    SetSelectedObject(targetObject);
                }
            }
        }

        private IEnumerator SetSelectedAfterLayoutRebuild(GameObject selectedObject)
        {
            yield return new WaitForEndOfFrame();
            yield return new WaitForSeconds(selectionDelay);

            SetSelectedObject(selectedObject);
        }

        private IEnumerator ValidateSelection(GameObject expectedSelected)
        {
            yield return new WaitForEndOfFrame();

            if (eventSystem != null && eventSystem.currentSelectedGameObject != expectedSelected)
            {
                if (enableDebugLogs)
                {
                    Debug.LogWarning($"[GamePadNavigationManager] 选中状态验证失败! 期望: {expectedSelected.name}, 实际: {eventSystem.currentSelectedGameObject?.name ?? "null"}");
                }

                if (retryCount < maxRetryAttempts)
                {
                    retryCount++;
                    SetSelectedObject(expectedSelected);
                }
            }
            else if (enableDebugLogs)
            {
                Debug.Log($"[GamePadNavigationManager] 选中状态验证成功: {expectedSelected.name}");
            }
        }
        #endregion

        #region 高亮处理
        /// <summary>
        /// 持续确保Toggle高亮状态的协程
        /// </summary>
        private IEnumerator KeepToggleHighlighted(Toggle toggle)
        {
            if (toggle == null) yield break;

            // 持续一段时间内确保Toggle保持高亮
            float duration = 2f; // 持续2秒
            float elapsed = 0f;

            while (elapsed < duration && currentSelectedObject == toggle.gameObject)
            {
                if (forceHighlightState && useUniversalHighlight)
                {
                    EnsureToggleHighlight(toggle);
                }
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (enableDebugLogs && elapsed >= duration)
            {
                Debug.Log($"[GamePadNavigationManager] Toggle持续高亮保护期结束: {toggle.gameObject.name}");
            }
        }

        /// <summary>
        /// 确保按钮显示高亮状态（适用于Button等使用SpriteSwap的组件）
        /// </summary>
        private void EnsureHighlightState(GameObject selectedObject)
        {
            var selectable = selectedObject.GetComponent<Selectable>();
            if (selectable == null) return;

            try
            {
                MethodInfo doStateTransition = typeof(Selectable).GetMethod("DoStateTransition",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                if (doStateTransition != null)
                {
                    doStateTransition.Invoke(selectable, new object[] { 1, true }); // 1 = Highlighted

                    if (enableDebugLogs)
                    {
                        Debug.Log($"[GamePadNavigationManager] 强制设置按钮高亮状态: {selectedObject.name}");
                    }
                }
            }
            catch (System.Exception e)
            {
                if (enableDebugLogs)
                {
                    Debug.LogWarning($"[GamePadNavigationManager] 强制设置按钮高亮状态失败: {e.Message}");
                }
            }
        }

        /// <summary>
        /// 确保Toggle显示高亮状态（使用ColorTint）
        /// </summary>
        private void EnsureToggleHighlight(Toggle toggle)
        {
            if (toggle == null) return;

            try
            {
                // 使用反射调用Toggle的内部状态转换方法
                MethodInfo doStateTransition = typeof(Selectable).GetMethod("DoStateTransition",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                if (doStateTransition != null)
                {
                    doStateTransition.Invoke(toggle, new object[] { 1, true }); // 1 = Highlighted

                    if (enableDebugLogs)
                    {
                        Debug.Log($"[GamePadNavigationManager] 强制设置Toggle高亮状态: {toggle.gameObject.name}");
                    }
                }
            }
            catch (System.Exception e)
            {
                if (enableDebugLogs)
                {
                    Debug.LogWarning($"[GamePadNavigationManager] 强制设置Toggle高亮状态失败: {e.Message}");
                }
            }
        }

        /// <summary>
        /// 确保Slider显示高亮状态（使用ColorTint）
        /// </summary>
        private void EnsureSliderHighlight(Slider slider)
        {
            if (slider == null) return;

            try
            {
                // 使用反射调用Slider的内部状态转换方法
                MethodInfo doStateTransition = typeof(Selectable).GetMethod("DoStateTransition",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                if (doStateTransition != null)
                {
                    doStateTransition.Invoke(slider, new object[] { 1, true }); // 1 = Highlighted

                    if (enableDebugLogs)
                    {
                        Debug.Log($"[GamePadNavigationManager] 强制设置Slider高亮状态: {slider.gameObject.name}");
                    }
                }
            }
            catch (System.Exception e)
            {
                if (enableDebugLogs)
                {
                    Debug.LogWarning($"[GamePadNavigationManager] 强制设置Slider高亮状态失败: {e.Message}");
                }
            }
        }
        #endregion

        #region 公共API
        public void SetDefaultSelectedObject(GameObject selectedObject)
        {
            firstSelectedObject = selectedObject;
            if (lastSelectedObject == null)
            {
                lastSelectedObject = selectedObject;
            }

            if (usingGamepad)
            {
                SetSelectedObject(selectedObject);
            }
        }

        public void SetSelectedObjectDelayed(GameObject selectedObject)
        {
            StartCoroutine(SetSelectedAfterLayoutRebuild(selectedObject));
        }

        public bool IsUsingGamepad()
        {
            return usingGamepad;
        }

        public void ForceSetNavigationMode(bool useGamepad)
        {
            if (useGamepad != usingGamepad)
            {
                usingGamepad = useGamepad;
                if (useGamepad)
                {
                    OnGamepadActivated();
                }
                else
                {
                    OnGamepadDeactivated();
                }
            }
        }

        /// <summary>
        /// 设置选中对象并通知当前面板
        /// </summary>
        public void SetSelectedObjectWithPanelNotify(GameObject selectedObject)
        {
            SetSelectedObject(selectedObject);

            // 通知当前活动面板更新选中状态
            if (currentActivePanel is SettingsNavigationPanel settingsPanel)
            {
                settingsPanel.UpdateCurrentSelection(selectedObject);
            }
        }

        /// <summary>
        /// 获取当前选中的游戏对象
        /// </summary>
        public GameObject GetCurrentSelectedGameObject()
        {
            return eventSystem != null ? eventSystem.currentSelectedGameObject : null;
        }

        [ContextMenu("重置选中状态")]
        public void ResetSelection()
        {
            retryCount = 0;
            isInteractingWithUI = false;
            shouldIgnoreSubmit = false;
            GameObject targetObject = currentActivePanel?.DefaultSelection ?? firstSelectedObject;

            if (targetObject != null)
            {
                SetSelectedObject(targetObject);
            }
        }

        [ContextMenu("重置到初始选中对象")]
        public void ResetToInitialSelection()
        {
            retryCount = 0;
            isInteractingWithUI = false;
            shouldIgnoreSubmit = false;
            if (firstSelectedObject != null)
            {
                SetSelectedObject(firstSelectedObject);
            }
        }

        public void MarkUIInteracting(bool interacting)
        {
            isInteractingWithUI = interacting;

            if (enableDebugLogs)
            {
                Debug.Log($"[GamePadNavigationManager] 设置UI交互状态: {interacting}");
            }
        }

        public bool ShouldIgnoreSubmit()
        {
            return shouldIgnoreSubmit || (currentActivePanel?.ShouldIgnoreSubmit() ?? false);
        }

        public void SetIgnoreSubmit(bool ignore)
        {
            shouldIgnoreSubmit = ignore;
            if (enableDebugLogs)
            {
                Debug.Log($"[GamePadNavigationManager] 设置忽略确认键状态: {ignore}");
            }
        }

        public GameObject GetLastSelectedObject()
        {
            return lastSelectedObject;
        }

        public void SetLastSelectedObject(GameObject selectedObject)
        {
            if (selectedObject != null && IsObjectSelectable(selectedObject))
            {
                lastSelectedObject = selectedObject;
                if (enableDebugLogs)
                {
                    Debug.Log($"[GamePadNavigationManager] 手动设置最后选中对象: {selectedObject.name}");
                }
            }
        }

        public void ResetToLastSelectedObject()
        {
            if (lastSelectedObject != null)
            {
                SetSelectedObject(lastSelectedObject);
            }
        }

        public void ResetProtectionState()
        {
            isInDebouncePeriod = false;
            isInRapidClickProtection = false;
            StopAllProtectionCoroutines();

            if (enableDebugLogs)
            {
                Debug.Log($"[GamePadNavigationManager] 手动重置保护状态");
            }
        }

        [ContextMenu("手动设置选中对象")]
        public void ManualSetSelectedObject()
        {
            GameObject targetObject = currentActivePanel?.DefaultSelection ?? firstSelectedObject;

            if (targetObject != null)
            {
                SetSelectedObject(targetObject);
            }
            else
            {
                Debug.LogError("[GamePadNavigationManager] 没有可用的选中对象!");
            }
        }

        [ContextMenu("强制重新设置选中对象")]
        public void ForceReselectObject()
        {
            if (lastSelectedObject != null)
            {
                retryCount = 0;
                isInteractingWithUI = false;
                shouldIgnoreSubmit = false;
                SetSelectedObject(lastSelectedObject);
            }
        }

        [ContextMenu("测试手柄输入")]
        public void TestGamepadInput()
        {
            if (Gamepad.current != null)
            {
                Vector2 dpad = Gamepad.current.dpad.ReadValue();
                Vector2 leftStick = Gamepad.current.leftStick.ReadValue();

                bool anyButtonPressed = Gamepad.current.aButton.wasPressedThisFrame ||
                                       Gamepad.current.bButton.wasPressedThisFrame ||
                                       Gamepad.current.xButton.wasPressedThisFrame ||
                                       Gamepad.current.yButton.wasPressedThisFrame;

                Debug.Log($"[GamePadNavigationManager] 手柄输入测试:");
                Debug.Log($"- DPad: {dpad}");
                Debug.Log($"- Left Stick: {leftStick}");
                Debug.Log($"- 按钮按下: {anyButtonPressed}");
                Debug.Log($"- 当前UI类型: {currentUIType}");
                Debug.Log($"- 正在交互: {isInteractingWithUI}");
                Debug.Log($"- 忽略确认键: {shouldIgnoreSubmit}");
                Debug.Log($"- 最后选中对象: {lastSelectedObject?.name ?? "null"}");
                Debug.Log($"- 当前活动面板: {currentActivePanel?.PanelName ?? "无"}");
                Debug.Log($"- 防抖保护: {isInDebouncePeriod}");
                Debug.Log($"- 快速点击保护: {isInRapidClickProtection}");
                Debug.Log($"- 当前输入模式: {currentInputMode}");
            }
            else
            {
                Debug.Log("[GamePadNavigationManager] 没有检测到手柄");
            }
        }

        [ContextMenu("打印当前状态")]
        public void PrintCurrentStatus()
        {
            Debug.Log($"[GamePadNavigationManager] 状态报告:");
            Debug.Log($"- 使用手柄: {usingGamepad}");
            Debug.Log($"- 手柄连接: {Gamepad.current != null}");
            Debug.Log($"- 默认对象: {firstSelectedObject?.name ?? "未设置"}");
            Debug.Log($"- 当前选中: {eventSystem?.currentSelectedGameObject?.name ?? "无"}");
            Debug.Log($"- 最后选中: {lastSelectedObject?.name ?? "无"}");
            if (eventSystem?.currentSelectedGameObject != null)
            {
                Debug.Log($"- 当前选中类型: {GetUIType(eventSystem.currentSelectedGameObject)}");
            }
            Debug.Log($"- 强制高亮状态: {forceHighlightState}");
            Debug.Log($"- 通用高亮: {useUniversalHighlight}");
            Debug.Log($"- 启用导航: {enableNavigation}");
            Debug.Log($"- 正在导航: {isNavigating}");
            Debug.Log($"- 重试计数: {retryCount}/{maxRetryAttempts}");
            Debug.Log($"- 手柄优先: {gamepadPriority}");
            Debug.Log($"- 当前UI类型: {currentUIType}");
            Debug.Log($"- 正在交互: {isInteractingWithUI}");
            Debug.Log($"- 忽略确认键: {shouldIgnoreSubmit}");
            Debug.Log($"- 防抖保护: {isInDebouncePeriod}");
            Debug.Log($"- 快速点击保护: {isInRapidClickProtection}");
            Debug.Log($"- 启用防抖: {enableDebounceProtection}");
            Debug.Log($"- 防抖时间: {debounceTime}");
            Debug.Log($"- 快速点击保护时间: {rapidClickProtectionTime}");
            Debug.Log($"- 最小选中保持时间: {minSelectionHoldTime}");
            Debug.Log($"- 注册面板数量: {registeredPanels.Count}");
            Debug.Log($"- 当前活动面板: {currentActivePanel?.PanelName ?? "无"}");
            Debug.Log($"- 当前输入模式: {currentInputMode}");

            // 打印所有注册面板
            foreach (var panel in registeredPanels)
            {
                Debug.Log($"  - {panel.PanelName} (优先级: {panel.Priority}, 活跃: {panel.IsActive}, 自定义导航: {panel.UseCustomNavigation})");
            }
        }
        #endregion
    }
}
