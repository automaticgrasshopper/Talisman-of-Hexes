using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Nova
{
    public enum SaveViewMode
    {
        Save,
        Load
    }

    public class SaveViewController : ViewControllerBase, INavigationPanel
    {
        [Header("存档界面设置")]
        [SerializeField] private GameObject saveEntryPrefab;
        [SerializeField] private GameObject saveEntryRowPrefab;
        [SerializeField] private int maxRow;
        [SerializeField] private int maxCol;

        // 添加主机模式布尔值
        [SerializeField] private bool hostMode = false;

        [Header("音效设置")]
        [SerializeField] private AudioClip saveActionSound;
        [SerializeField] private AudioClip loadActionSound;
        [SerializeField] private AudioClip deleteActionSound;
        [SerializeField] private AudioClip navigationSound;

        [Header("材质设置")]
        [SerializeField] private Material blurMaterial;
        [SerializeField] private Material highlightMaterial;

        [Header("导航面板设置")]
        [Tooltip("面板名称")]
        [SerializeField] private string panelName = "SaveLoad";
        [Tooltip("面板优先级")]
        [SerializeField] private int priority = PanelPriority.SAVE_LOAD;
        [Tooltip("默认选中对象")]
        [SerializeField] private GameObject defaultSelection;
        [Tooltip("使用自定义导航")]
        [SerializeField] private bool useCustomNavigation = true;

        [Header("面板引用设置")]
        [Tooltip("手动引用实际的存档界面面板")]
        [SerializeField] private GameObject actualSavePanel;

        [Header("高亮设置")]
        [Tooltip("鼠标悬停时也显示高亮")]
        [SerializeField] private bool highlightOnMouseHover = true;
        [Tooltip("高亮动画速度")]
        [SerializeField] private float highlightAnimationSpeed = 2f;

        [Header("输入模式设置")]
        [Tooltip("启用输入模式检测")]
        [SerializeField] private bool enableInputModeDetection = true;

        [Header("调试设置")]
        [SerializeField] private bool enableDebugLogs = true;

        public UnityEvent bookmarkSaved;
        public UnityEvent bookmarkLoaded;

        private GameState gameState;
        private CheckpointManager checkpointManager;

        private Button backgroundButton;
        private SaveEntryController previewEntry;
        private TextProxy thumbnailTextProxy;

        private Button saveButton;
        private Button loadButton;
        private Text saveText;
        private Text loadText;
        private CanvasGroup saveButtonCanvasGroup;

        private Button leftButton;
        private Button rightButton;
        private Text leftButtonText;
        private Text rightButtonText;
        private Text pageText;

        private Color defaultTextColor;
        private Color saveTextColor;
        private Color loadTextColor;
        private Color disabledTextColor;

        private Sprite corruptedThumbnailSprite;

        private readonly List<SaveEntryController> saveEntryControllers = new List<SaveEntryController>();
        private readonly Dictionary<int, Sprite> cachedThumbnailSprites = new Dictionary<int, Sprite>();

        private int maxSaveEntry;
        private int page = 1;
        private int maxPage = 1;

        private int _selectedSaveID = -1;
        private int _currentSelectedIndex = -1;

        private int currentSelectedIndex
        {
            get => _currentSelectedIndex;
            set
            {
                if (_currentSelectedIndex >= 0 && _currentSelectedIndex < saveEntryControllers.Count)
                {
                    saveEntryControllers[_currentSelectedIndex].SetHighlight(false);
                }

                _currentSelectedIndex = value;

                if (_currentSelectedIndex >= 0 && _currentSelectedIndex < saveEntryControllers.Count)
                {
                    saveEntryControllers[_currentSelectedIndex].SetHighlight(true);
                    int saveID = GetSaveIDFromIndex(_currentSelectedIndex);
                    if (checkpointManager.bookmarksMetadata.ContainsKey(saveID))
                    {
                        selectedSaveID = saveID;
                    }
                    else
                    {
                        selectedSaveID = -1;
                    }
                }
            }
        }

        private float navigationCooldown = 0f;
        private const float navigationCooldownTime = 0.2f;

        private int selectedSaveID
        {
            get => _selectedSaveID;
            set
            {
                this.RuntimeAssert(checkpointManager.bookmarksMetadata.ContainsKey(value) || value == -1,
                    "selectedSaveID must be a saveID with existing bookmark, or -1.");

                if (_selectedSaveID >= 0)
                {
                    SaveIDToSaveEntryController(_selectedSaveID).HideDeleteButton();
                }

                _selectedSaveID = value;

                if (value == -1)
                {
                    ShowPreviewScreen();
                }
                else
                {
                    ShowPreviewBookmark(value);
                    SaveIDToSaveEntryController(value).ShowDeleteButton();
                }
            }
        }

        private SaveViewMode saveViewMode = SaveViewMode.Save;
        private BookmarkType saveViewBookmarkType = BookmarkType.AutoSave;
        private bool fromTitle;

        private Texture2D screenTexture;
        private Sprite screenSprite;
        private DialogueDisplayData currentDialogue;

        private const string DateTimeFormat = "yyyy/MM/dd  HH:mm";

        // 输入模式管理
        private InputMode currentInputMode = InputMode.KeyboardMouse;
        private bool usingGamepad = false;

        // INavigationPanel 接口实现
        public string PanelName => panelName;
        public int Priority => priority;
        public bool IsActive => actualSavePanel != null ? actualSavePanel.activeInHierarchy : active && gameObject.activeInHierarchy;
        public bool UseCustomNavigation => useCustomNavigation;
        public GameObject DefaultSelection => defaultSelection;

        protected override void Awake()
        {
            base.Awake();

            maxSaveEntry = maxRow * maxCol;

            gameState = Utils.FindNovaController().GameState;
            checkpointManager = Utils.FindNovaController().CheckpointManager;

            if (actualSavePanel == null)
            {
                actualSavePanel = FindSavePanelInChildren();
                if (actualSavePanel == null)
                {
                    actualSavePanel = myPanel;
                }
            }

            backgroundButton = myPanel.transform.Find("Background").GetComponent<Button>();
            thumbnailTextProxy = myPanel.transform.Find("Background/Left/TextBox/Text").GetComponent<TextProxy>();

            var headerPanel = myPanel.transform.Find("Background/Right/Bottom");
            var saveButtonPanel = headerPanel.Find("SaveButton");
            var loadButtonPanel = headerPanel.Find("LoadButton");
            saveButton = saveButtonPanel.GetComponent<Button>();
            loadButton = loadButtonPanel.GetComponent<Button>();
            saveText = saveButtonPanel.GetComponentInChildren<Text>();
            loadText = loadButtonPanel.GetComponentInChildren<Text>();
            saveButtonCanvasGroup = saveButton.GetComponent<CanvasGroup>();

            var pagerPanel = headerPanel.Find("Pager");
            var leftButtonPanel = pagerPanel.Find("LeftButton");
            var rightButtonPanel = pagerPanel.Find("RightButton");
            leftButton = leftButtonPanel.GetComponent<Button>();
            rightButton = rightButtonPanel.GetComponent<Button>();
            leftButtonText = leftButtonPanel.GetComponentInChildren<Text>();
            rightButtonText = rightButtonPanel.GetComponentInChildren<Text>();
            pageText = pagerPanel.Find("PageText").GetComponent<Text>();

            ColorUtility.TryParseHtmlString("#000000FF", out defaultTextColor);
            ColorUtility.TryParseHtmlString("#33CC33FF", out saveTextColor);
            ColorUtility.TryParseHtmlString("#CC3333FF", out loadTextColor);
            ColorUtility.TryParseHtmlString("#808080FF", out disabledTextColor);

            corruptedThumbnailSprite = Utils.Texture2DToSprite(Utils.ClearTexture);

            backgroundButton.onClick.AddListener(Unselect);
            saveButton.onClick.AddListener(ShowSave);
            loadButton.onClick.AddListener(ShowLoad);
            leftButton.onClick.AddListener(PageLeft);
            rightButton.onClick.AddListener(PageRight);

            var saveEntryGrid = myPanel.transform.Find("Background/Right/Top");
            for (int rowIdx = 0; rowIdx < maxRow; ++rowIdx)
            {
                var saveEntryRow = Instantiate(saveEntryRowPrefab, saveEntryGrid.transform);
                for (int colIdx = 0; colIdx < maxCol; ++colIdx)
                {
                    var saveEntry = Instantiate(saveEntryPrefab, saveEntryRow.transform);
                    var controller = saveEntry.GetComponent<SaveEntryController>();

                    if (highlightMaterial != null)
                    {
                        controller.SetHighlightMaterial(highlightMaterial);
                    }

                    controller.SetHighlightAnimationSpeed(highlightAnimationSpeed);

                    saveEntryControllers.Add(controller);
                }
            }

            gameState.dialogueChanged.AddListener(OnDialogueChanged);
            I18n.LocaleChanged.AddListener(Refresh);

            if (GamePadNavigationManager.Instance != null)
            {
                GamePadNavigationManager.Instance.OnInputModeChanged += OnInputModeChanged;
            }
        }

        private GameObject FindSavePanelInChildren()
        {
            string[] possibleNames = { "SavePanel", "SaveView", "SaveLoadPanel", "ArchivePanel", "Background" };

            foreach (string name in possibleNames)
            {
                Transform panel = transform.Find(name);
                if (panel != null)
                {
                    return panel.gameObject;
                }
            }

            foreach (Transform child in transform)
            {
                if (child.name.Contains("Save") || child.name.Contains("save") ||
                    child.name.Contains("存档") || child.name.Contains("Archive"))
                {
                    return child.gameObject;
                }
            }

            return null;
        }

        protected override void Start()
        {
            base.Start();

            if (GamePadNavigationManager.Instance != null)
            {
                GamePadNavigationManager.Instance.RegisterPanel(this);
                currentInputMode = GamePadNavigationManager.Instance.GetCurrentInputMode();
                usingGamepad = (currentInputMode == InputMode.Gamepad);
            }

            previewEntry = myPanel.transform.Find("Background/Left/SaveEntry").GetComponent<SaveEntryController>();
            previewEntry.InitAsPreview(null, Hide);
            ShowPage();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            if (GamePadNavigationManager.Instance != null)
            {
                GamePadNavigationManager.Instance.UnregisterPanel(this);
                GamePadNavigationManager.Instance.OnInputModeChanged -= OnInputModeChanged;
            }

            backgroundButton.onClick.RemoveListener(Unselect);
            saveButton.onClick.RemoveListener(ShowSave);
            loadButton.onClick.RemoveListener(ShowLoad);
            leftButton.onClick.RemoveListener(PageLeft);
            rightButton.onClick.RemoveListener(PageRight);

            gameState.dialogueChanged.RemoveListener(OnDialogueChanged);
            I18n.LocaleChanged.RemoveListener(Refresh);
        }

        private void OnDialogueChanged(DialogueChangedData data)
        {
            currentDialogue = data.displayData;
        }

        private void OnInputModeChanged(InputMode newMode)
        {
            if (!enableInputModeDetection) return;

            currentInputMode = newMode;
            usingGamepad = (newMode == InputMode.Gamepad);

            if (IsActive)
            {
                UpdateHighlightForInputMode();
            }
        }

        private void UpdateHighlightForInputMode()
        {
            if (usingGamepad)
            {
                if (currentSelectedIndex == -1 && saveEntryControllers.Count > 0)
                {
                    currentSelectedIndex = 0;
                    UpdateAllHighlights();

                    var eventSystem = EventSystem.current;
                    if (eventSystem != null)
                    {
                        eventSystem.SetSelectedGameObject(saveEntryControllers[0].gameObject);
                    }
                }
            }
            else
            {
                ClearAllHighlights();
                currentSelectedIndex = -1;

                var eventSystem = EventSystem.current;
                if (eventSystem != null)
                {
                    eventSystem.SetSelectedGameObject(null);
                }
            }
        }

        #region INavigationPanel 实现
        public void OnPanelActivated()
        {
            if (enableDebugLogs)
            {
                Debug.Log($"[SaveViewController] 面板激活，输入模式: {currentInputMode}");
            }

            navigationCooldown = 0f;

            if (usingGamepad)
            {
                currentSelectedIndex = 0;
                UpdateAllHighlights();

                var eventSystem = EventSystem.current;
                if (eventSystem != null && saveEntryControllers.Count > 0 && saveEntryControllers[0] != null)
                {
                    eventSystem.SetSelectedGameObject(saveEntryControllers[0].gameObject);
                }
            }
            else
            {
                ClearAllHighlights();
                currentSelectedIndex = -1;

                var eventSystem = EventSystem.current;
                if (eventSystem != null)
                {
                    eventSystem.SetSelectedGameObject(null);
                }
            }
        }

        public void OnPanelDeactivated()
        {
            ClearAllHighlights();
        }

        public void HandleCustomNavigation()
        {
            if (!active || viewManager.currentView != CurrentViewType.UI || !usingGamepad)
                return;

            if (!IsFocusOnSaveView())
            {
                ForceFocusToSaveView();
                return;
            }

            if (navigationCooldown > 0)
            {
                navigationCooldown -= Time.unscaledDeltaTime;
                return;
            }

            HandleGamepadNavigation();
            HandleGamepadActions();
        }

        public bool ShouldIgnoreSubmit()
        {
            return false;
        }
        #endregion

        #region 焦点管理
        private bool IsFocusOnSaveView()
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null || eventSystem.currentSelectedGameObject == null)
                return false;

            GameObject selected = eventSystem.currentSelectedGameObject;

            foreach (var controller in saveEntryControllers)
            {
                if (controller != null && controller.gameObject == selected)
                    return true;
            }

            if (selected == saveButton?.gameObject || selected == loadButton?.gameObject ||
                selected == leftButton?.gameObject || selected == rightButton?.gameObject ||
                selected == backgroundButton?.gameObject)
                return true;

            return false;
        }

        private void ForceFocusToSaveView()
        {
            if (!usingGamepad) return;

            if (saveEntryControllers.Count > 0 && saveEntryControllers[currentSelectedIndex] != null)
            {
                var eventSystem = EventSystem.current;
                if (eventSystem != null)
                {
                    eventSystem.SetSelectedGameObject(saveEntryControllers[currentSelectedIndex].gameObject);
                    UpdateAllHighlights();
                }
            }
        }
        #endregion

        #region 高亮管理
        private void UpdateAllHighlights()
        {
            for (int i = 0; i < saveEntryControllers.Count; i++)
            {
                bool shouldHighlight = (i == currentSelectedIndex);
                saveEntryControllers[i].SetHighlight(shouldHighlight);
            }
        }

        private void ClearAllHighlights()
        {
            foreach (var controller in saveEntryControllers)
            {
                controller.SetHighlight(false);
                controller.SetMouseHover(false);
            }
        }

        private void SetMouseHoverHighlight(int index, bool hover)
        {
            if (index >= 0 && index < saveEntryControllers.Count)
            {
                if (highlightOnMouseHover)
                {
                    saveEntryControllers[index].SetMouseHover(hover);
                }

                if (hover && usingGamepad && currentSelectedIndex >= 0 && currentSelectedIndex < saveEntryControllers.Count)
                {
                    saveEntryControllers[currentSelectedIndex].SetHighlight(false);
                }
                else if (!hover && usingGamepad && currentSelectedIndex >= 0 && currentSelectedIndex < saveEntryControllers.Count)
                {
                    saveEntryControllers[currentSelectedIndex].SetHighlight(true);
                }
            }
        }
        #endregion

        #region 手柄控制
        private void HandleGamepadNavigation()
        {
            var gamepad = Gamepad.current;
            if (gamepad == null) return;

            Vector2 dpad = gamepad.dpad.ReadValue();
            bool navigationPerformed = false;

            if (Mathf.Abs(dpad.x) > 0.5f)
            {
                if (dpad.x > 0.5f)
                {
                    MoveSelectionRight();
                }
                else if (dpad.x < -0.5f)
                {
                    MoveSelectionLeft();
                }
                navigationPerformed = true;
            }

            if (Mathf.Abs(dpad.y) > 0.5f)
            {
                if (dpad.y > 0.5f)
                {
                    PageUp();
                }
                else if (dpad.y < -0.5f)
                {
                    PageDown();
                }
                navigationPerformed = true;
            }

            if (navigationPerformed)
            {
                navigationCooldown = navigationCooldownTime;
                viewManager.TryPlaySound(navigationSound);
            }
        }

        private void HandleGamepadActions()
        {
            var gamepad = Gamepad.current;
            if (gamepad == null) return;

            if (gamepad.buttonSouth.wasPressedThisFrame)
            {
                if (currentSelectedIndex >= 0)
                {
                    int saveID = GetSaveIDFromIndex(currentSelectedIndex);
                    OnThumbnailButtonClicked(saveID);
                }
            }

            if (gamepad.buttonEast.wasPressedThisFrame)
            {
                if (selectedSaveID != -1)
                {
                    Unselect();
                }
                else
                {
                    Hide();
                }
            }

            if (gamepad.buttonWest.wasPressedThisFrame && selectedSaveID != -1)
            {
                DeleteBookmarkWithAlert(selectedSaveID);
            }

            if (gamepad.buttonNorth.wasPressedThisFrame)
            {
                if (saveViewMode == SaveViewMode.Save)
                {
                    ShowLoad();
                }
                else
                {
                    ShowSave();
                }
            }
        }

        private void MoveSelectionRight()
        {
            if (currentSelectedIndex == -1)
            {
                currentSelectedIndex = 0;
            }
            else
            {
                int newIndex = currentSelectedIndex + 1;
                if (newIndex >= maxSaveEntry)
                {
                    newIndex = maxSaveEntry - 1;
                }
                currentSelectedIndex = newIndex;
            }

            UpdateAllHighlights();
        }

        private void MoveSelectionLeft()
        {
            if (currentSelectedIndex == -1)
            {
                currentSelectedIndex = 0;
            }
            else
            {
                int newIndex = currentSelectedIndex - 1;
                if (newIndex < 0)
                {
                    newIndex = 0;
                }
                currentSelectedIndex = newIndex;
            }

            UpdateAllHighlights();
        }

        private void PageUp()
        {
            if (saveViewBookmarkType == BookmarkType.AutoSave)
            {
                return;
            }
            else if (saveViewBookmarkType == BookmarkType.QuickSave)
            {
                saveViewBookmarkType = BookmarkType.AutoSave;
                page = 1;
            }
            else
            {
                if (page > 1)
                {
                    page--;
                }
                else
                {
                    if (!hostMode)
                    {
                        saveViewBookmarkType = BookmarkType.QuickSave;
                    }
                    else
                    {
                        saveViewBookmarkType = BookmarkType.AutoSave;
                    }
                    page = 1;
                }
            }

            // 修改：根据输入模式设置选中索引
            currentSelectedIndex = usingGamepad ? 0 : -1;
            ShowPage();

            if (usingGamepad)
            {
                ForceFocusAfterPageChange();
            }
            else
            {
                // 键鼠模式：清除事件系统选中
                var eventSystem = EventSystem.current;
                if (eventSystem != null)
                {
                    eventSystem.SetSelectedGameObject(null);
                }
            }
        }

        private void PageDown()
        {
            if (saveViewBookmarkType == BookmarkType.AutoSave)
            {
                if (!hostMode)
                {
                    saveViewBookmarkType = BookmarkType.QuickSave;
                }
                else
                {
                    saveViewBookmarkType = BookmarkType.NormalSave;
                }
                page = 1;
            }
            else if (saveViewBookmarkType == BookmarkType.QuickSave)
            {
                saveViewBookmarkType = BookmarkType.NormalSave;
                page = 1;
            }
            else
            {
                if (page < maxPage)
                {
                    page++;
                }
                else
                {
                    return;
                }
            }

            // 修改：根据输入模式设置选中索引
            currentSelectedIndex = usingGamepad ? 0 : -1;
            ShowPage();

            if (usingGamepad)
            {
                ForceFocusAfterPageChange();
            }
            else
            {
                // 键鼠模式：清除事件系统选中
                var eventSystem = EventSystem.current;
                if (eventSystem != null)
                {
                    eventSystem.SetSelectedGameObject(null);
                }
            }
        }

        private void ForceFocusAfterPageChange()
        {
            StartCoroutine(DelayedFocusAfterPageChange());
        }

        private System.Collections.IEnumerator DelayedFocusAfterPageChange()
        {
            yield return null;

            if (saveEntryControllers.Count > 0 && saveEntryControllers[currentSelectedIndex] != null)
            {
                var eventSystem = EventSystem.current;
                if (eventSystem != null)
                {
                    eventSystem.SetSelectedGameObject(saveEntryControllers[currentSelectedIndex].gameObject);
                    UpdateAllHighlights();
                }
            }
        }

        private int GetSaveIDFromIndex(int index)
        {
            return (page - 1) * maxSaveEntry + index + (int)saveViewBookmarkType;
        }
        #endregion

        #region Show and hide
        public override void Show(bool doTransition, Action onFinish)
        {
            if (active)
            {
                if (saveViewMode == SaveViewMode.Save && saveViewBookmarkType != BookmarkType.NormalSave)
                {
                    saveViewBookmarkType = BookmarkType.NormalSave;
                    page = 1;
                }
            }
            else
            {
                if (saveViewMode == SaveViewMode.Save)
                {
                    saveViewBookmarkType = BookmarkType.NormalSave;
                    int saveID = checkpointManager.QueryMinUnusedSaveID((int)BookmarkType.NormalSave, int.MaxValue);
                    page = SaveIDToPage(saveID);
                }
                else
                {
                    saveViewBookmarkType = BookmarkType.AutoSave;
                    page = 1;
                }
            }

            if (fromTitle)
            {
                saveButtonCanvasGroup.alpha = 0.0f;
                currentDialogue = null;
            }
            else
            {
                saveButtonCanvasGroup.alpha = 1.0f;
            }

            if (!active)
            {
                if (screenTexture != null)
                {
                    Destroy(screenTexture);
                }

                screenTexture = ScreenCapturer.GetBookmarkThumbnailTexture(blurMaterial);
                screenSprite = Utils.Texture2DToSprite(screenTexture);
            }

            selectedSaveID = -1;

            // 根据输入模式设置选中索引
            currentSelectedIndex = usingGamepad ? 0 : -1;

            ShowPage();

            if (usingGamepad)
            {
                ForceActivatePanel();
            }

            base.Show(doTransition, onFinish);
        }

        private void ForceActivatePanel()
        {
            if (GamePadNavigationManager.Instance != null)
            {
                GamePadNavigationManager.Instance.ForceSetNavigationMode(true);
                GamePadNavigationManager.Instance.UpdateActivePanel();
                StartCoroutine(DelayedFocusSetup());
            }
        }

        private System.Collections.IEnumerator DelayedFocusSetup()
        {
            yield return null;
            yield return null;

            if (usingGamepad && saveEntryControllers.Count > 0 && saveEntryControllers[0] != null)
            {
                var eventSystem = EventSystem.current;
                if (eventSystem != null)
                {
                    eventSystem.SetSelectedGameObject(saveEntryControllers[0].gameObject);
                }

                UpdateAllHighlights();
            }
        }

        public void ShowSaveWithCallback(Action onFinish)
        {
            if (active && fromTitle)
            {
                return;
            }

            saveViewMode = SaveViewMode.Save;
            fromTitle = false;
            this.Show(onFinish);
        }

        public void ShowSave()
        {
            ShowSaveWithCallback(null);
        }

        public void ShowLoadWithCallback(bool fromTitle, Action onFinish)
        {
            saveViewMode = SaveViewMode.Load;
            this.fromTitle = fromTitle;
            this.Show(onFinish);
        }

        public void ShowLoad()
        {
            ShowLoadWithCallback(false, null);
        }

        public void ShowLoadFromTitle()
        {
            ShowLoadWithCallback(true, null);
        }

        protected override void OnHideComplete()
        {
            if (active)
            {
                if (screenTexture != null)
                {
                    Destroy(screenTexture);
                    screenTexture = null;
                }

                if (screenSprite != null)
                {
                    Destroy(screenSprite);
                    screenSprite = null;
                }
            }

            currentSelectedIndex = -1;
            ClearAllHighlights();

            base.OnHideComplete();
        }
        #endregion

        #region Bookmark operations
        public void SaveBookmark(int saveID)
        {
            var bookmark = gameState.GetBookmark();
            bookmark.description = currentDialogue;
            bookmark.screenshot = screenSprite.texture;
            DeleteCachedThumbnailSprite(saveID);
            checkpointManager[saveID] = bookmark;
            bookmarkSaved.Invoke();

            ShowPage();
            ShowPreviewBookmark(saveID);
            viewManager.TryPlaySound(saveActionSound);
        }

        private void SaveBookmarkWithAlert(int saveID)
        {
            Alert.Show(
                null,
                I18n.GetLocalizedStrings("bookmark.overwrite.confirm", SaveIDToDisplayID(saveID)),
                () => SaveBookmark(saveID),
                null,
                "BookmarkOverwrite"
            );
        }

        public void LoadBookmark(int saveID)
        {
            var bookmark = checkpointManager[saveID];
            DeleteCachedThumbnailSprite(saveID);
            if (bookmark == null)
            {
                return;
            }

            gameState.LoadBookmark(bookmark);
            bookmarkLoaded.Invoke();

            var titleController = viewManager.GetController<TitleController>();
            if (titleController.active)
            {
                titleController.HideImmediate();
                viewManager.GetController<GameViewController>().ShowImmediate();
            }

            Hide();
            viewManager.TryPlaySound(loadActionSound);
            Alert.Show("bookmark.load.complete");
        }

        private void LoadBookmarkWithAlert(int saveID)
        {
            Alert.Show(
                null,
                I18n.GetLocalizedStrings("bookmark.load.confirm", SaveIDToDisplayID(saveID)),
                () => LoadBookmark(saveID),
                null,
                "BookmarkLoad"
            );
        }

        private void DeleteBookmark(int saveID)
        {
            DeleteCachedThumbnailSprite(saveID);
            checkpointManager.DeleteBookmark(saveID);

            ShowPage();
            selectedSaveID = -1;
            viewManager.TryPlaySound(deleteActionSound);
        }

        private void DeleteBookmarkWithAlert(int saveID)
        {
            Alert.Show(
                null,
                I18n.GetLocalizedStrings("bookmark.delete.confirm", SaveIDToDisplayID(saveID)),
                () => DeleteBookmark(saveID),
                null,
                "BookmarkDelete"
            );
        }

        private void AutoSaveBookmark(int beginSaveID, string tagText, Texture2D screenshot)
        {
            var bookmark = gameState.GetBookmark();
            bookmark.description = currentDialogue;
            bookmark.screenshot = screenshot;

            var saveID = checkpointManager.QueryMinUnusedSaveID(beginSaveID, beginSaveID + maxSaveEntry);
            if (saveID >= beginSaveID + maxSaveEntry)
            {
                saveID = checkpointManager.QuerySaveIDByTime(beginSaveID, beginSaveID + maxSaveEntry,
                    SaveIDQueryType.Earliest);
                DeleteCachedThumbnailSprite(saveID);
            }

            checkpointManager[saveID] = bookmark;
            bookmarkSaved.Invoke();
        }

        private void AutoSaveBookmark(int beginSaveID, string tagText)
        {
            var screenshot = ScreenCapturer.GetBookmarkThumbnailTexture(blurMaterial);
            AutoSaveBookmark(beginSaveID, tagText, screenshot);
            Destroy(screenshot);
        }

        public void AutoSaveBookmark(Texture2D screenshot)
        {
            AutoSaveBookmark((int)BookmarkType.AutoSave, I18n.__("bookmark.autosave.page"), screenshot);
        }

        public void AutoSaveBookmark()
        {
            AutoSaveBookmark((int)BookmarkType.AutoSave, I18n.__("bookmark.autosave.page"));
        }

        public void QuickSaveBookmark()
        {
            AutoSaveBookmark((int)BookmarkType.QuickSave, I18n.__("bookmark.quicksave.page"));
            viewManager.TryPlaySound(saveActionSound);
            Alert.Show("bookmark.quicksave.complete");
        }

        public void QuickSaveBookmarkWithAlert()
        {
            Alert.Show(null, "bookmark.quicksave.confirm", QuickSaveBookmark, null, "BookmarkQuickSave");
        }

        public void QuickLoadBookmark()
        {
            int saveID = checkpointManager.QuerySaveIDByTime((int)BookmarkType.QuickSave,
                (int)BookmarkType.NormalSave, SaveIDQueryType.Latest);
            var bookmark = checkpointManager[saveID];
            DeleteCachedThumbnailSprite(saveID);
            if (bookmark == null)
            {
                return;
            }

            gameState.LoadBookmark(bookmark);
            bookmarkLoaded.Invoke();

            viewManager.TryPlaySound(loadActionSound);
            Alert.Show("bookmark.load.complete");
        }

        public void QuickLoadBookmarkWithAlert()
        {
            if (checkpointManager.bookmarksMetadata.Values.Any(m =>
                    m.saveID >= (int)BookmarkType.QuickSave && m.saveID < (int)BookmarkType.QuickSave + maxSaveEntry))
            {
                Alert.Show(null, "bookmark.quickload.confirm", QuickLoadBookmark, null, "BookmarkQuickLoad");
            }
            else
            {
                Alert.Show(null, "bookmark.quickload.nosave");
            }
        }
        #endregion

        #region Preview
        private bool isMouse;

        private void OnThumbnailButtonClicked(int saveID)
        {
            if (isMouse)
            {
                if (saveViewMode == SaveViewMode.Save)
                {
                    if (checkpointManager.bookmarksMetadata.ContainsKey(saveID))
                    {
                        SaveBookmarkWithAlert(saveID);
                    }
                    else
                    {
                        SaveBookmark(saveID);
                    }
                }
                else
                {
                    if (checkpointManager.bookmarksMetadata.ContainsKey(saveID))
                    {
                        LoadBookmarkWithAlert(saveID);
                    }
                }
            }
            else
            {
                if (saveViewMode == SaveViewMode.Save)
                {
                    if (saveID == selectedSaveID)
                    {
                        SaveBookmarkWithAlert(saveID);
                    }
                    else
                    {
                        if (checkpointManager.bookmarksMetadata.ContainsKey(saveID))
                        {
                            selectedSaveID = saveID;
                        }
                        else
                        {
                            selectedSaveID = -1;
                            SaveBookmark(saveID);
                        }
                    }
                }
                else
                {
                    if (saveID == selectedSaveID)
                    {
                        LoadBookmarkWithAlert(saveID);
                    }
                    else
                    {
                        if (checkpointManager.bookmarksMetadata.ContainsKey(saveID))
                        {
                            selectedSaveID = saveID;
                        }
                        else
                        {
                            selectedSaveID = -1;
                        }
                    }
                }
            }
        }

        private void OnThumbnailButtonEnter(PointerEventData _eventData, int saveID)
        {
            if (viewManager.currentView != CurrentViewType.UI)
            {
                return;
            }

            var eventData = (ExtendedPointerEventData)_eventData;
            isMouse = !TouchFix.IsTouch(eventData);
            if (isMouse)
            {
                if (checkpointManager.bookmarksMetadata.ContainsKey(saveID))
                {
                    selectedSaveID = saveID;
                }

                int hoverIndex = GetIndexFromSaveID(saveID);
                SetMouseHoverHighlight(hoverIndex, true);
            }
        }

        private void OnThumbnailButtonExit(PointerEventData _eventData, int saveID)
        {
            if (viewManager.currentView != CurrentViewType.UI)
            {
                return;
            }

            var eventData = (ExtendedPointerEventData)_eventData;
            if (!TouchFix.IsTouch(eventData))
            {
                selectedSaveID = -1;

                int hoverIndex = GetIndexFromSaveID(saveID);
                SetMouseHoverHighlight(hoverIndex, false);
            }
        }

        private void Unselect()
        {
            selectedSaveID = -1;
        }

        private void ShowPreview(Sprite newThumbnailSprite, UnityAction onThumbnailButtonClicked, string newText)
        {
            previewEntry.InitAsPreview(newThumbnailSprite, onThumbnailButtonClicked);
            thumbnailTextProxy.text = newText;
        }

        private void ShowPreviewScreen()
        {
            ShowPreview(screenSprite, Hide, I18n.__(
                "bookmark.summary",
                fromTitle ? "" : DateTime.Now.ToString(DateTimeFormat),
                gameState.currentNode != null ? I18n.__(gameState.currentNode.displayNames) : "",
                currentDialogue != null ? currentDialogue.FormatNameDialogue() : ""
            ));
        }

        private void ShowPreviewBookmark(int saveID)
        {
            try
            {
                Bookmark bookmark = checkpointManager[saveID];
                var nodeName = checkpointManager.GetNodeRecord(bookmark.nodeOffset).name;
                var displayName = I18n.__(gameState.GetNode(nodeName, false).displayNames);
                ShowPreview(GetThumbnailSprite(saveID), Unselect, I18n.__(
                    "bookmark.summary",
                    checkpointManager.bookmarksMetadata[saveID].creationTime.ToString(DateTimeFormat),
                    displayName,
                    bookmark.description.FormatNameDialogue()
                ));
            }
            catch (Exception e)
            {
                Debug.LogWarning(e);
                ShowPreview(corruptedThumbnailSprite, null, I18n.__("bookmark.corrupted.title"));
            }
        }
        #endregion

        #region Page
        private void PageLeft()
        {
            if (page == 1)
            {
                if (saveViewMode == SaveViewMode.Load)
                {
                    if (hostMode)
                    {
                        if (saveViewBookmarkType == BookmarkType.NormalSave)
                        {
                            saveViewBookmarkType = BookmarkType.AutoSave;
                            page = 1;
                        }
                    }
                    else
                    {
                        if (saveViewBookmarkType == BookmarkType.QuickSave)
                        {
                            saveViewBookmarkType = BookmarkType.AutoSave;
                            page = 1;
                        }
                        else if (saveViewBookmarkType == BookmarkType.NormalSave)
                        {
                            saveViewBookmarkType = BookmarkType.QuickSave;
                            page = 1;
                        }
                    }
                }
            }
            else
            {
                --page;
            }

            // 修改：根据输入模式设置选中索引
            currentSelectedIndex = usingGamepad ? 0 : -1;
            ShowPage();

            if (usingGamepad)
            {
                ForceFocusAfterPageChange();
            }
            else
            {
                // 键鼠模式：清除事件系统选中
                var eventSystem = EventSystem.current;
                if (eventSystem != null)
                {
                    eventSystem.SetSelectedGameObject(null);
                }
            }
        }

        private void PageRight()
        {
            if (page == maxPage)
            {
                if (hostMode)
                {
                    if (saveViewBookmarkType == BookmarkType.AutoSave)
                    {
                        saveViewBookmarkType = BookmarkType.NormalSave;
                        page = 1;
                    }
                }
                else
                {
                    if (saveViewBookmarkType == BookmarkType.AutoSave)
                    {
                        saveViewBookmarkType = BookmarkType.QuickSave;
                        page = 1;
                    }
                    else if (saveViewBookmarkType == BookmarkType.QuickSave)
                    {
                        saveViewBookmarkType = BookmarkType.NormalSave;
                        page = 1;
                    }
                }
            }
            else
            {
                ++page;
            }

            // 修改：根据输入模式设置选中索引
            currentSelectedIndex = usingGamepad ? 0 : -1;
            ShowPage();

            if (usingGamepad)
            {
                ForceFocusAfterPageChange();
            }
            else
            {
                // 键鼠模式：清除事件系统选中
                var eventSystem = EventSystem.current;
                if (eventSystem != null)
                {
                    eventSystem.SetSelectedGameObject(null);
                }
            }
        }

        private void ShowPage()
        {
            saveButton.interactable = (saveViewMode != SaveViewMode.Save);
            loadButton.interactable = (saveViewMode != SaveViewMode.Load);
            saveText.color = (saveButton.interactable ? disabledTextColor : saveTextColor);
            loadText.color = (loadButton.interactable ? disabledTextColor : loadTextColor);

            if (saveViewBookmarkType == BookmarkType.NormalSave)
            {
                int maxSaveID = checkpointManager.QueryMaxSaveID((int)BookmarkType.NormalSave);
                if (checkpointManager.bookmarksMetadata.ContainsKey(maxSaveID))
                {
                    maxPage = SaveIDToPage(maxSaveID);
                    if (saveViewMode == SaveViewMode.Save)
                    {
                        ++maxPage;
                    }
                }
                else
                {
                    maxPage = 1;
                }
            }
            else
            {
                maxPage = 1;
            }

            if (maxPage < page)
            {
                page = maxPage;
            }

            if (saveViewBookmarkType == BookmarkType.AutoSave)
            {
                pageText.text = I18n.__("bookmark.autosave.page");
            }
            else if (saveViewBookmarkType == BookmarkType.QuickSave)
            {
                pageText.text = I18n.__("bookmark.quicksave.page");
            }
            else
            {
                pageText.text = $"{page} / {maxPage}";
            }

            bool canPageLeft = page > 1;
            bool canPageRight = page < maxPage;

            if (saveViewMode == SaveViewMode.Load)
            {
                if (hostMode)
                {
                    canPageLeft = canPageLeft || saveViewBookmarkType != BookmarkType.AutoSave;
                    canPageRight = canPageRight || saveViewBookmarkType != BookmarkType.NormalSave;
                }
                else
                {
                    canPageLeft = canPageLeft || saveViewBookmarkType != BookmarkType.AutoSave;
                    canPageRight = canPageRight || saveViewBookmarkType != BookmarkType.NormalSave;
                }
            }

            leftButton.interactable = canPageLeft;
            rightButton.interactable = canPageRight;
            leftButtonText.color = (leftButton.interactable ? defaultTextColor : disabledTextColor);
            rightButtonText.color = (rightButton.interactable ? defaultTextColor : disabledTextColor);

            int latestSaveID =
                checkpointManager.QuerySaveIDByTime((int)BookmarkType.NormalSave, int.MaxValue, SaveIDQueryType.Latest);

            for (int i = 0; i < maxSaveEntry; ++i)
            {
                int saveID = (page - 1) * maxSaveEntry + i + (int)saveViewBookmarkType;
                string newIDText = SaveIDToDisplayID(saveID).ToString();

                string newFooterText;
                Sprite newThumbnailSprite;
                UnityAction onDeleteButtonClicked;
                UnityAction onThumbnailButtonClicked;

                if (checkpointManager.bookmarksMetadata.ContainsKey(saveID))
                {
                    try
                    {
                        newFooterText = checkpointManager[saveID].creationTime.ToString(DateTimeFormat);
                        newThumbnailSprite = GetThumbnailSprite(saveID);
                        onDeleteButtonClicked = () => DeleteBookmarkWithAlert(saveID);
                        onThumbnailButtonClicked = () => OnThumbnailButtonClicked(saveID);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning(e);
                        newFooterText = I18n.__("bookmark.corrupted.title");
                        newThumbnailSprite = corruptedThumbnailSprite;
                        onDeleteButtonClicked = () => DeleteBookmarkWithAlert(saveID);
                        onThumbnailButtonClicked = null;
                    }
                }
                else
                {
                    newFooterText = "";
                    newThumbnailSprite = null;
                    onDeleteButtonClicked = null;
                    if (saveViewMode == SaveViewMode.Save)
                    {
                        onThumbnailButtonClicked = () => OnThumbnailButtonClicked(saveID);
                    }
                    else
                    {
                        onThumbnailButtonClicked = null;
                    }
                }

                UnityAction<PointerEventData> onThumbnailButtonEnter =
                    eventData => OnThumbnailButtonEnter(eventData, saveID);
                UnityAction<PointerEventData> onThumbnailButtonExit =
                    eventData => OnThumbnailButtonExit(eventData, saveID);

                var saveEntryController = saveEntryControllers[i];
                saveEntryController.mode = saveViewMode;
                saveEntryController.Init(newIDText, newFooterText, saveID == latestSaveID, newThumbnailSprite,
                    onDeleteButtonClicked, onThumbnailButtonClicked, onThumbnailButtonEnter, onThumbnailButtonExit);

                saveEntryController.SetHighlight(i == currentSelectedIndex);
            }

            previewEntry.mode = saveViewMode;

            UpdateAllHighlights();
        }

        private void Refresh()
        {
            if (previewEntry == null)
            {
                return;
            }

            ShowPage();
            selectedSaveID = selectedSaveID;
        }
        #endregion

        #region 辅助方法
        private Sprite GetThumbnailSprite(int saveID)
        {
            this.RuntimeAssert(checkpointManager.bookmarksMetadata.ContainsKey(saveID),
                "GetThumbnailSprite must use a saveID with existing bookmark.");
            if (!cachedThumbnailSprites.ContainsKey(saveID))
            {
                Bookmark bookmark = checkpointManager[saveID];
                cachedThumbnailSprites[saveID] = Utils.Texture2DToSprite(bookmark.screenshot);
            }

            return cachedThumbnailSprites[saveID];
        }

        private void DeleteCachedThumbnailSprite(int saveID)
        {
            if (cachedThumbnailSprites.ContainsKey(saveID))
            {
                Destroy(cachedThumbnailSprites[saveID]);
                cachedThumbnailSprites.Remove(saveID);
            }
        }

        private int SaveIDToPage(int saveID)
        {
            return (saveID - (int)BookmarkMetadata.SaveIDToBookmarkType(saveID) + maxSaveEntry) / maxSaveEntry;
        }

        private static int SaveIDToDisplayID(int saveID)
        {
            return saveID - (int)BookmarkMetadata.SaveIDToBookmarkType(saveID) + 1;
        }

        private SaveEntryController SaveIDToSaveEntryController(int saveID)
        {
            int i = (saveID - (int)BookmarkMetadata.SaveIDToBookmarkType(saveID)) % maxSaveEntry;
            if (i >= 0)
            {
                return saveEntryControllers[i];
            }
            else
            {
                return null;
            }
        }

        private int GetIndexFromSaveID(int saveID)
        {
            return (saveID - (int)BookmarkMetadata.SaveIDToBookmarkType(saveID)) % maxSaveEntry;
        }
        #endregion

        #region 公共方法
        public void SetHighlightMaterial(Material newHighlightMaterial)
        {
            highlightMaterial = newHighlightMaterial;

            foreach (var entryController in saveEntryControllers)
            {
                if (entryController != null)
                {
                    entryController.SetHighlightMaterial(newHighlightMaterial);
                }
            }
        }

        public void SetHighlightOnMouseHover(bool enabled)
        {
            highlightOnMouseHover = enabled;
        }

        public void SetHighlightAnimationSpeed(float speed)
        {
            highlightAnimationSpeed = speed;

            foreach (var entryController in saveEntryControllers)
            {
                if (entryController != null)
                {
                    entryController.SetHighlightAnimationSpeed(speed);
                }
            }
        }

        public void SetActualSavePanel(GameObject panel)
        {
            actualSavePanel = panel;
        }

        public InputMode GetCurrentInputMode()
        {
            return currentInputMode;
        }

        public bool IsUsingGamepad()
        {
            return usingGamepad;
        }
        #endregion

        #region 调试方法
        [ContextMenu("检查焦点状态")]
        public void CheckFocusStatus()
        {
            var eventSystem = EventSystem.current;
            if (eventSystem != null)
            {
                Debug.Log($"当前焦点对象: {eventSystem.currentSelectedGameObject?.name ?? "null"}");
                Debug.Log($"焦点在存档界面: {IsFocusOnSaveView()}");
                Debug.Log($"存档界面活跃: {active}");
                Debug.Log($"实际面板活跃: {actualSavePanel?.activeInHierarchy ?? false}");
                Debug.Log($"当前选中索引: {currentSelectedIndex}");
                Debug.Log($"实际面板对象: {actualSavePanel?.name ?? "null"}");
                Debug.Log($"当前输入模式: {currentInputMode}");
                Debug.Log($"使用手柄: {usingGamepad}");
            }
        }

        [ContextMenu("强制修复焦点")]
        public void ForceFixFocus()
        {
            ForceFocusToSaveView();
        }
        #endregion
    }
}
