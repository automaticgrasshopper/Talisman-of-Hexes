using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using UnityEngine.InputSystem;

namespace Nova
{
    /// <summary>
    /// 根据输入模式自动切换图片或GameObject的组件
    /// 使用方法：挂在Image上，配置两套图片（键盘鼠标和手柄）
    /// 扩展模式：可以切换整个GameObject而不仅仅是图片
    /// </summary>
    public class InputModeImage : MonoBehaviour
    {
        [Header("扩展模式")]
        [Tooltip("启用后切换GameObject而不是Sprite")]
        [SerializeField] private bool useGameObjectSwitch = false;

        [Header("图片配置")]
        [Tooltip("键盘鼠标模式下显示的图片")]
        [SerializeField] private Sprite keyboardMouseSprite;

        [Tooltip("手柄模式下显示的图片")]
        [SerializeField] private Sprite gamepadSprite;

        [Header("GameObject配置（扩展模式使用）")]
        [Tooltip("键盘鼠标模式下显示的GameObject")]
        [SerializeField] private GameObject keyboardMouseObject;

        [Tooltip("手柄模式下显示的GameObject")]
        [SerializeField] private GameObject gamepadObject;

        [Header("组件引用")]
        [Tooltip("目标Image组件（自动获取）")]
        [SerializeField] private Image targetImage;

        [Header("设置")]
        [Tooltip("启用时立即更新图片")]
        [SerializeField] private bool updateOnEnable = true;

        [Tooltip("延迟更新时间（秒）")]
        [SerializeField] private float updateDelay = 0.1f;

        [Tooltip("启用调试日志")]
        [SerializeField] private bool enableDebugLogs = false;

        [Header("备用图片")]
        [Tooltip("键盘鼠标模式备用图片（当主图片为空时使用）")]
        [SerializeField] private Sprite fallbackKeyboardMouseSprite;

        [Tooltip("手柄模式备用图片（当主图片为空时使用）")]
        [SerializeField] private Sprite fallbackGamepadSprite;

        [Header("备用GameObject（扩展模式使用）")]
        [Tooltip("键盘鼠标模式备用GameObject（当主GameObject为空时使用）")]
        [SerializeField] private GameObject fallbackKeyboardMouseObject;

        [Tooltip("手柄模式备用GameObject（当主GameObject为空时使用）")]
        [SerializeField] private GameObject fallbackGamepadObject;

        private bool isInitialized = false;
        private InputMode lastInputMode;

        #region Unity 生命周期
        private void Awake()
        {
            InitializeComponent();
        }

        private void OnEnable()
        {
            if (!isInitialized)
            {
                InitializeComponent();
            }

            // 注册输入模式改变事件
            if (GamePadNavigationManager.Instance != null)
            {
                GamePadNavigationManager.Instance.OnInputModeChanged += OnInputModeChanged;
            }

            if (updateOnEnable)
            {
                // 延迟更新以确保所有组件已初始化
                StartCoroutine(DelayedUpdateImage());
            }

            if (enableDebugLogs)
            {
                Debug.Log($"[InputModeImage] 组件启用，目标: {targetImage?.gameObject.name ?? "null"}，扩展模式: {useGameObjectSwitch}");
            }
        }

        private void OnDisable()
        {
            // 注销事件
            if (GamePadNavigationManager.Instance != null)
            {
                GamePadNavigationManager.Instance.OnInputModeChanged -= OnInputModeChanged;
            }

            if (enableDebugLogs)
            {
                Debug.Log($"[InputModeImage] 组件禁用");
            }
        }

        private void OnDestroy()
        {
            // 清理事件
            if (GamePadNavigationManager.Instance != null)
            {
                GamePadNavigationManager.Instance.OnInputModeChanged -= OnInputModeChanged;
            }
        }

        private void Start()
        {
            // 确保图片已更新
            if (isInitialized && updateOnEnable)
            {
                UpdateDisplayBasedOnInputMode();
            }
        }
        #endregion

        #region 初始化
        /// <summary>
        /// 初始化组件
        /// </summary>
        private void InitializeComponent()
        {
            if (useGameObjectSwitch)
            {
                // 扩展模式：检查GameObject配置
                if (keyboardMouseObject == null && fallbackKeyboardMouseObject == null)
                {
                    Debug.LogWarning($"[InputModeImage] {gameObject.name} 扩展模式未配置键盘鼠标GameObject");
                }

                if (gamepadObject == null && fallbackGamepadObject == null)
                {
                    Debug.LogWarning($"[InputModeImage] {gameObject.name} 扩展模式未配置手柄GameObject");
                }

                // 初始隐藏所有GameObject，后续会根据输入模式显示对应的
                SetAllGameObjectsActive(false);
            }
            else
            {
                // 原有模式：自动获取Image组件
                if (targetImage == null)
                {
                    targetImage = GetComponent<Image>();
                }

                if (targetImage == null)
                {
                    Debug.LogError($"[InputModeImage] 在 {gameObject.name} 上找不到Image组件！");
                    return;
                }

                // 检查图片配置
                if (keyboardMouseSprite == null && fallbackKeyboardMouseSprite == null)
                {
                    Debug.LogWarning($"[InputModeImage] {gameObject.name} 未配置键盘鼠标图片");
                }

                if (gamepadSprite == null && fallbackGamepadSprite == null)
                {
                    Debug.LogWarning($"[InputModeImage] {gameObject.name} 未配置手柄图片");
                }
            }

            isInitialized = true;

            if (enableDebugLogs)
            {
                Debug.Log($"[InputModeImage] 初始化完成: {gameObject.name}, 扩展模式: {useGameObjectSwitch}");
            }
        }

        /// <summary>
        /// 设置所有GameObject为非激活状态
        /// </summary>
        private void SetAllGameObjectsActive(bool active)
        {
            if (keyboardMouseObject != null) keyboardMouseObject.SetActive(active);
            if (gamepadObject != null) gamepadObject.SetActive(active);
            if (fallbackKeyboardMouseObject != null) fallbackKeyboardMouseObject.SetActive(active);
            if (fallbackGamepadObject != null) fallbackGamepadObject.SetActive(active);
        }
        #endregion

        #region 显示更新
        /// <summary>
        /// 输入模式改变事件处理
        /// </summary>
        private void OnInputModeChanged(InputMode newMode)
        {
            if (!isInitialized || !isActiveAndEnabled) return;

            if (enableDebugLogs)
            {
                Debug.Log($"[InputModeImage] 输入模式改变: {lastInputMode} -> {newMode}, 扩展模式: {useGameObjectSwitch}");
            }

            lastInputMode = newMode;
            UpdateDisplayBasedOnInputMode();
        }

        /// <summary>
        /// 根据当前输入模式更新显示
        /// </summary>
        private void UpdateDisplayBasedOnInputMode()
        {
            if (!isInitialized) return;

            if (useGameObjectSwitch)
            {
                UpdateGameObjectBasedOnInputMode();
            }
            else
            {
                UpdateImageBasedOnInputMode();
            }
        }

        /// <summary>
        /// 更新图片显示（原有模式）
        /// </summary>
        private void UpdateImageBasedOnInputMode()
        {
            if (targetImage == null) return;

            InputMode currentMode = GetCurrentInputMode();
            Sprite newSprite = GetSpriteForInputMode(currentMode);

            if (newSprite != null && newSprite != targetImage.sprite)
            {
                targetImage.sprite = newSprite;

                if (enableDebugLogs)
                {
                    Debug.Log($"[InputModeImage] 更新图片: {gameObject.name} -> {currentMode}");
                }
            }
            else if (newSprite == null)
            {
                if (enableDebugLogs)
                {
                    Debug.LogWarning($"[InputModeImage] 找不到对应输入模式的图片: {currentMode}");
                }
            }
        }

        /// <summary>
        /// 更新GameObject显示（扩展模式）
        /// </summary>
        private void UpdateGameObjectBasedOnInputMode()
        {
            InputMode currentMode = GetCurrentInputMode();
            GameObject targetObject = GetGameObjectForInputMode(currentMode);
            GameObject otherObject = GetGameObjectForInputMode(currentMode == InputMode.KeyboardMouse ? InputMode.Gamepad : InputMode.KeyboardMouse);

            // 先禁用所有
            SetAllGameObjectsActive(false);

            // 启用目标GameObject
            if (targetObject != null)
            {
                targetObject.SetActive(true);

                if (enableDebugLogs)
                {
                    Debug.Log($"[InputModeImage] 更新GameObject: {gameObject.name} -> {currentMode}, 显示: {targetObject.name}");
                }
            }
            else
            {
                if (enableDebugLogs)
                {
                    Debug.LogWarning($"[InputModeImage] 找不到对应输入模式的GameObject: {currentMode}");
                }
            }
        }

        /// <summary>
        /// 获取当前输入模式
        /// </summary>
        private InputMode GetCurrentInputMode()
        {
            if (GamePadNavigationManager.Instance != null)
            {
                return GamePadNavigationManager.Instance.GetCurrentInputMode();
            }

            // 备用方案：检查是否有手柄连接
            return Gamepad.current != null ? InputMode.Gamepad : InputMode.KeyboardMouse;
        }

        /// <summary>
        /// 根据输入模式获取对应的图片
        /// </summary>
        private Sprite GetSpriteForInputMode(InputMode mode)
        {
            switch (mode)
            {
                case InputMode.KeyboardMouse:
                    return keyboardMouseSprite ?? fallbackKeyboardMouseSprite;

                case InputMode.Gamepad:
                    return gamepadSprite ?? fallbackGamepadSprite;

                default:
                    return keyboardMouseSprite ?? fallbackKeyboardMouseSprite;
            }
        }

        /// <summary>
        /// 根据输入模式获取对应的GameObject
        /// </summary>
        private GameObject GetGameObjectForInputMode(InputMode mode)
        {
            switch (mode)
            {
                case InputMode.KeyboardMouse:
                    return keyboardMouseObject ?? fallbackKeyboardMouseObject;

                case InputMode.Gamepad:
                    return gamepadObject ?? fallbackGamepadObject;

                default:
                    return keyboardMouseObject ?? fallbackKeyboardMouseObject;
            }
        }

        /// <summary>
        /// 延迟更新显示
        /// </summary>
        private IEnumerator DelayedUpdateImage()
        {
            yield return new WaitForSeconds(updateDelay);
            UpdateDisplayBasedOnInputMode();
        }
        #endregion

        #region 公共API
        /// <summary>
        /// 手动更新显示
        /// </summary>
        [ContextMenu("手动更新显示")]
        public void ManualUpdateImage()
        {
            if (!isInitialized)
            {
                InitializeComponent();
            }

            UpdateDisplayBasedOnInputMode();

            if (enableDebugLogs)
            {
                Debug.Log($"[InputModeImage] 手动更新显示: {gameObject.name}, 扩展模式: {useGameObjectSwitch}");
            }
        }

        /// <summary>
        /// 设置键盘鼠标图片
        /// </summary>
        public void SetKeyboardMouseSprite(Sprite sprite)
        {
            if (useGameObjectSwitch)
            {
                Debug.LogWarning($"[InputModeImage] 当前处于扩展模式，设置Sprite无效");
                return;
            }

            keyboardMouseSprite = sprite;
            if (GetCurrentInputMode() == InputMode.KeyboardMouse)
            {
                UpdateImageBasedOnInputMode();
            }
        }

        /// <summary>
        /// 设置手柄图片
        /// </summary>
        public void SetGamepadSprite(Sprite sprite)
        {
            if (useGameObjectSwitch)
            {
                Debug.LogWarning($"[InputModeImage] 当前处于扩展模式，设置Sprite无效");
                return;
            }

            gamepadSprite = sprite;
            if (GetCurrentInputMode() == InputMode.Gamepad)
            {
                UpdateImageBasedOnInputMode();
            }
        }

        /// <summary>
        /// 设置键盘鼠标GameObject（扩展模式使用）
        /// </summary>
        public void SetKeyboardMouseObject(GameObject gameObject)
        {
            if (!useGameObjectSwitch)
            {
                Debug.LogWarning($"[InputModeImage] 当前不处于扩展模式，设置GameObject无效");
                return;
            }

            keyboardMouseObject = gameObject;
            if (GetCurrentInputMode() == InputMode.KeyboardMouse)
            {
                UpdateGameObjectBasedOnInputMode();
            }
        }

        /// <summary>
        /// 设置手柄GameObject（扩展模式使用）
        /// </summary>
        public void SetGamepadObject(GameObject gameObject)
        {
            if (!useGameObjectSwitch)
            {
                Debug.LogWarning($"[InputModeImage] 当前不处于扩展模式，设置GameObject无效");
                return;
            }

            gamepadObject = gameObject;
            if (GetCurrentInputMode() == InputMode.Gamepad)
            {
                UpdateGameObjectBasedOnInputMode();
            }
        }

        /// <summary>
        /// 设置备用键盘鼠标图片
        /// </summary>
        public void SetFallbackKeyboardMouseSprite(Sprite sprite)
        {
            if (useGameObjectSwitch)
            {
                Debug.LogWarning($"[InputModeImage] 当前处于扩展模式，设置Sprite无效");
                return;
            }

            fallbackKeyboardMouseSprite = sprite;
            if (GetCurrentInputMode() == InputMode.KeyboardMouse && keyboardMouseSprite == null)
            {
                UpdateImageBasedOnInputMode();
            }
        }

        /// <summary>
        /// 设置备用手柄图片
        /// </summary>
        public void SetFallbackGamepadSprite(Sprite sprite)
        {
            if (useGameObjectSwitch)
            {
                Debug.LogWarning($"[InputModeImage] 当前处于扩展模式，设置Sprite无效");
                return;
            }

            fallbackGamepadSprite = sprite;
            if (GetCurrentInputMode() == InputMode.Gamepad && gamepadSprite == null)
            {
                UpdateImageBasedOnInputMode();
            }
        }

        /// <summary>
        /// 设置备用键盘鼠标GameObject（扩展模式使用）
        /// </summary>
        public void SetFallbackKeyboardMouseObject(GameObject gameObject)
        {
            if (!useGameObjectSwitch)
            {
                Debug.LogWarning($"[InputModeImage] 当前不处于扩展模式，设置GameObject无效");
                return;
            }

            fallbackKeyboardMouseObject = gameObject;
            if (GetCurrentInputMode() == InputMode.KeyboardMouse && keyboardMouseObject == null)
            {
                UpdateGameObjectBasedOnInputMode();
            }
        }

        /// <summary>
        /// 设置备用手柄GameObject（扩展模式使用）
        /// </summary>
        public void SetFallbackGamepadObject(GameObject gameObject)
        {
            if (!useGameObjectSwitch)
            {
                Debug.LogWarning($"[InputModeImage] 当前不处于扩展模式，设置GameObject无效");
                return;
            }

            fallbackGamepadObject = gameObject;
            if (GetCurrentInputMode() == InputMode.Gamepad && gamepadObject == null)
            {
                UpdateGameObjectBasedOnInputMode();
            }
        }

        /// <summary>
        /// 强制切换到指定输入模式的显示
        /// </summary>
        public void ForceSetInputMode(InputMode mode)
        {
            if (useGameObjectSwitch)
            {
                GameObject targetObject = GetGameObjectForInputMode(mode);
                SetAllGameObjectsActive(false);
                if (targetObject != null)
                {
                    targetObject.SetActive(true);
                }
            }
            else
            {
                Sprite newSprite = GetSpriteForInputMode(mode);
                if (newSprite != null && targetImage != null)
                {
                    targetImage.sprite = newSprite;
                }
            }

            if (enableDebugLogs)
            {
                Debug.Log($"[InputModeImage] 强制设置显示模式: {gameObject.name} -> {mode}, 扩展模式: {useGameObjectSwitch}");
            }
        }

        /// <summary>
        /// 获取当前显示的图片类型
        /// </summary>
        public string GetCurrentSpriteType()
        {
            if (useGameObjectSwitch)
            {
                InputMode currentMode = GetCurrentInputMode();
                return currentMode == InputMode.KeyboardMouse ? "KeyboardMouse GameObject" : "Gamepad GameObject";
            }

            if (targetImage == null || targetImage.sprite == null) return "None";

            if (targetImage.sprite == keyboardMouseSprite || targetImage.sprite == fallbackKeyboardMouseSprite)
                return "KeyboardMouse";
            else if (targetImage.sprite == gamepadSprite || targetImage.sprite == fallbackGamepadSprite)
                return "Gamepad";
            else
                return "Unknown";
        }

        /// <summary>
        /// 检查组件配置状态
        /// </summary>
        [ContextMenu("检查配置状态")]
        public void CheckConfiguration()
        {
            Debug.Log($"[InputModeImage] 配置检查:");
            Debug.Log($"- 游戏对象: {gameObject.name}");
            Debug.Log($"- 扩展模式: {useGameObjectSwitch}");

            if (useGameObjectSwitch)
            {
                Debug.Log($"- 键盘鼠标GameObject: {keyboardMouseObject?.name ?? "未设置"}");
                Debug.Log($"- 手柄GameObject: {gamepadObject?.name ?? "未设置"}");
                Debug.Log($"- 备用键盘鼠标GameObject: {fallbackKeyboardMouseObject?.name ?? "未设置"}");
                Debug.Log($"- 备用手柄GameObject: {fallbackGamepadObject?.name ?? "未设置"}");
            }
            else
            {
                Debug.Log($"- Image组件: {targetImage != null}");
                Debug.Log($"- 键盘鼠标图片: {keyboardMouseSprite?.name ?? "未设置"}");
                Debug.Log($"- 手柄图片: {gamepadSprite?.name ?? "未设置"}");
                Debug.Log($"- 备用键盘鼠标图片: {fallbackKeyboardMouseSprite?.name ?? "未设置"}");
                Debug.Log($"- 备用手柄图片: {fallbackGamepadSprite?.name ?? "未设置"}");
            }

            Debug.Log($"- 当前输入模式: {GetCurrentInputMode()}");
            Debug.Log($"- 当前显示类型: {GetCurrentSpriteType()}");
            Debug.Log($"- 初始化状态: {isInitialized}");
        }

        /// <summary>
        /// 测试切换显示
        /// </summary>
        [ContextMenu("测试切换显示")]
        public void TestSwitchSprites()
        {
            InputMode currentMode = GetCurrentInputMode();
            InputMode testMode = currentMode == InputMode.KeyboardMouse ? InputMode.Gamepad : InputMode.KeyboardMouse;

            Debug.Log($"[InputModeImage] 测试切换: {currentMode} -> {testMode}, 扩展模式: {useGameObjectSwitch}");
            ForceSetInputMode(testMode);
        }

        /// <summary>
        /// 切换扩展模式
        /// </summary>
        public void SetUseGameObjectSwitch(bool useGameObject)
        {
            if (useGameObjectSwitch != useGameObject)
            {
                useGameObjectSwitch = useGameObject;
                isInitialized = false;
                InitializeComponent();
                UpdateDisplayBasedOnInputMode();
            }
        }
        #endregion

        #region 编辑器方法
#if UNITY_EDITOR
        /// <summary>
        /// 在编辑器中快速配置组件
        /// </summary>
        [ContextMenu("快速配置")]
        private void QuickSetup()
        {
            InitializeComponent();
            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.EditorUtility.SetDirty(gameObject);
        }
#endif
        #endregion
    }
}
