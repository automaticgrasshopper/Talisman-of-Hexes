using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Nova
{
    public class ChapterSelectViewController : ViewControllerBase
    {
        [Header("Tab 列表（MenuChapter = Grid Layout Group）")]
        [SerializeField] private GameObject chapterSlotPrefab;
        [SerializeField] private Transform menuChapter;

        [Header("当前章节预览（背景海报 + 标题 + 简介）")]
        [Tooltip("背景海报 Image（点击页签时切换 sprite）")]
        [SerializeField] private Image backgroundPosterImage;
        [Tooltip("章节标题 TMP_Text（走 I18n.C，读 FlowChartChapter.chapterNameKey）")]
        [SerializeField] private TMP_Text titleText;
        [Tooltip("章节简介 TMP_Text（走 I18n.C，读 FlowChartChapter.introKey）")]
        [SerializeField] private TMP_Text introText;
        [Tooltip("切换章节时统一淡入淡出的 CanvasGroup（裹住背景/标题/简介），可空")]
        [SerializeField] private CanvasGroup contentGroup;

        [Header("操作按钮")]
        [Tooltip("返回主标题按钮（用户自拼，不再用旧的）")]
        [SerializeField] private Button returnButton;
        [Tooltip("进入当前章节按钮")]
        [SerializeField] private Button enterButton;

        [Header("切换动画 / 音效")]
        [Tooltip("切换章节淡入淡出总时长（秒）。0.1s = 半段 fade-out + 半段 fade-in")]
        [SerializeField] private float crossfadeDuration = 0.1f;
        [Tooltip("点击页签时播放的音效（走 UI 音量）")]
        [SerializeField] private AudioClip slotSelectSound;

        [Header("调试")]
        [SerializeField] private bool unlockAllNodes;
        [SerializeField] private bool unlockDebugNodes;

        [Header("FlowChart 数据源")]
        [SerializeField] private FlowChartDatabase flowChartDatabase;

        [Header("BGM（可空 = 不播）")]
        [SerializeField] private AudioController bgmController;
        [SerializeField] private string bgmName;
        [SerializeField] private float bgmVolume = 0.5f;
        [SerializeField] private float bgmFadeOutDuration = 1.0f;

        private NovaAnimation novaAnimation;
        private GameState gameState;
        private CheckpointManager checkpointManager;

        private IReadOnlyList<FlowChartChapter> chapters;
        private readonly HashSet<int> unlockedChapterIndices = new HashSet<int>();
        private readonly List<ChapterSlotView> slotViews = new List<ChapterSlotView>();
        private int currentChapterIndex = -1;
        private Coroutine crossfadeCo;
        private bool bgmNeedFadeOut;

        /// <summary>设为 true 可跳过单章自动开始（从流程图返回时用）</summary>
        [NonSerialized] public bool skipAutoStart;

        /// <summary>不为 null 时，返回按钮调用此回调而非普通 Hide</summary>
        [NonSerialized] public Action onReturn;

        /// <summary>不为 null 时，点击章节按钮调用此回调（传入章节索引）而非 GameStart</summary>
        [NonSerialized] public Action<int> onChapterClick;

        protected override void Awake()
        {
            base.Awake();

            var controller = Utils.FindNovaController();
            gameState = controller.GameState;
            checkpointManager = controller.CheckpointManager;
            novaAnimation = controller.UIAnimation;

            chapters = flowChartDatabase != null
                ? flowChartDatabase.chapters
                : new List<FlowChartChapter>();

            // 用于按 currentNode 反查 chapter；FlowChartController 也会建一次，重复建是幂等的
            if (flowChartDatabase != null) flowChartDatabase.BuildNodeMap();

            BuildSlots();

            if (returnButton != null) returnButton.onClick.AddListener(OnReturnClicked);
            if (enterButton != null) enterButton.onClick.AddListener(OnEnterClicked);

            I18n.LocaleChanged.AddListener(RefreshLocalizedTexts);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            I18n.LocaleChanged.RemoveListener(RefreshLocalizedTexts);
        }

        public override void Show(bool doTransition, Action onFinish)
        {
            UpdateNodes();

            // 默认 Title→开始 流程下，若存在 AutoSave 则直接续上
            if (!skipAutoStart && onReturn == null && onChapterClick == null
                && !inputManager.IsPressed(AbstractKey.EditorUnlock))
            {
                int autoSaveID = checkpointManager.QuerySaveIDByTime(
                    (int)BookmarkType.AutoSave,
                    (int)BookmarkType.QuickSave,
                    SaveIDQueryType.Latest);
                var autoBookmark = checkpointManager[autoSaveID];
                if (autoBookmark != null)
                {
                    onFinish?.Invoke();
                    viewManager.GetController<TitleController>().ScheduleBgmFadeOut();
                    viewManager.SwitchView<TitleController, GameViewController>(
                        () => gameState.LoadBookmark(autoBookmark));
                    return;
                }
            }

            if (unlockedChapterIndices.Count == 0)
            {
                Debug.LogWarning("Nova: No chapter is unlocked so the game cannot start.");
            }
            else if (!skipAutoStart && unlockedChapterIndices.Count == 1 && !inputManager.IsPressed(AbstractKey.EditorUnlock))
            {
                onFinish?.Invoke();
                var idx = unlockedChapterIndices.First();
                GameStart(chapters[idx].GetFirstNodeName());
                return;
            }

            skipAutoStart = false; // 使用一次后重置
            UpdateSlots();

            // 默认选中：优先选玩家进度所在章节；否则第一个已解锁；再否则第 0 个
            int defaultIndex = FindCurrentProgressChapterIndex();
            if (defaultIndex < 0)
            {
                defaultIndex = unlockedChapterIndices.Count > 0
                    ? unlockedChapterIndices.OrderBy(i => i).First()
                    : (chapters.Count > 0 ? 0 : -1);
            }
            // 强制刷新一次（即便 == currentChapterIndex 也要把选中态画出来）
            currentChapterIndex = -1;
            ApplyChapterContent(defaultIndex, instant: true);

            // 走和 TitleController 一样的模式：BGM 放到 base.Show 的 onFinish 里播，
            // 保证 transition 跑完、面板/子物体全部激活后再 Play
            base.Show(doTransition, () =>
            {
                if (bgmController != null && !string.IsNullOrEmpty(bgmName))
                {
                    bgmController.scriptVolume = bgmVolume;
                    bgmController.Play(bgmName);
                }
                onFinish?.Invoke();
            });
        }

        public override void Hide(bool doTransition, Action onFinish)
        {
            // 同 Title：只有显式 ScheduleBgmFadeOut() 才淡出（去 Game 时调）；
            // 回 Title 不淡出，让 Title 自己的 PlayBgm 接管
            if (bgmNeedFadeOut && bgmController != null && !string.IsNullOrEmpty(bgmName))
            {
                bgmNeedFadeOut = false;
                novaAnimation.Then(new VolumeAnimationProperty(bgmController, 0.0f), bgmFadeOutDuration)
                    .Then(new ActionAnimationProperty(bgmController.Stop));
            }
            base.Hide(doTransition, onFinish);
        }

        public void ScheduleBgmFadeOut()
        {
            bgmNeedFadeOut = true;
        }

        private void OnReturnClicked()
        {
            if (onReturn != null)
            {
                var cb = onReturn;
                onReturn = null;
                Hide(true, () => cb());
            }
            else
            {
                Hide();
            }
        }

        private void OnEnterClicked()
        {
            if (currentChapterIndex < 0 || currentChapterIndex >= chapters.Count) return;
            if (!unlockedChapterIndices.Contains(currentChapterIndex) && !unlockAllNodes) return;

            var chapter = chapters[currentChapterIndex];
            if (onChapterClick != null)
            {
                var cb = onChapterClick;
                var idx = currentChapterIndex;
                onChapterClick = null;
                Hide(true, () => cb(idx));
            }
            else
            {
                ScheduleBgmFadeOut();
                Hide(true, () => GameStart(chapter.GetFirstNodeName()));
            }
        }

        public void UpdateNodes()
        {
            unlockedChapterIndices.Clear();
            for (int i = 0; i < chapters.Count; i++)
            {
                var chapter = chapters[i];
                if (chapter == null) continue;
                var firstNode = chapter.GetFirstNodeName();
                if (firstNode == null) continue;

                bool isUnlocked = unlockAllNodes
                    || checkpointManager.IsReachedAnyHistory(firstNode, 0)
                    || gameState.GetStartNodeNames(StartNodeType.Unlocked).Contains(firstNode);

                if (isUnlocked)
                    unlockedChapterIndices.Add(i);
            }
        }

        public IEnumerable<string> GetUnlockedNodes()
        {
            return unlockedChapterIndices
                .Select(i => chapters[i].GetFirstNodeName())
                .Where(n => n != null);
        }

        public void GameStart(string nodeName)
        {
            if (string.IsNullOrEmpty(nodeName)) return;
            viewManager.GetController<TitleController>().ScheduleBgmFadeOut();
            viewManager.SwitchView<TitleController, GameViewController>(() => { gameState.GameStart(nodeName); });
        }

        public void UnlockNodes(bool normal, bool debug)
        {
            unlockAllNodes |= normal;
            unlockDebugNodes |= debug;
            UpdateNodes();
            UpdateSlots();
        }

        protected override void OnActivatedUpdate()
        {
            base.OnActivatedUpdate();

            if (inputManager.IsTriggered(AbstractKey.EditorUnlock))
            {
                UnlockNodes(true, true);
            }

            // 左右方向键切换章节（在已解锁的章节里循环）
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.leftArrowKey.wasPressedThisFrame) MoveSelection(-1);
                else if (kb.rightArrowKey.wasPressedThisFrame) MoveSelection(+1);
            }
        }

        private void MoveSelection(int dir)
        {
            if (chapters.Count == 0) return;
            var candidates = Enumerable.Range(0, chapters.Count)
                .Where(i => unlockAllNodes || unlockedChapterIndices.Contains(i))
                .ToList();
            if (candidates.Count == 0) return;

            int cur = candidates.IndexOf(currentChapterIndex);
            int next = cur < 0
                ? (dir > 0 ? candidates.First() : candidates.Last())
                : candidates[((cur + dir) % candidates.Count + candidates.Count) % candidates.Count];

            if (next == currentChapterIndex) return;
            if (slotSelectSound != null && viewManager != null) viewManager.TryPlaySound(slotSelectSound);
            ApplyChapterContent(next, instant: false);
        }

        private int FindCurrentProgressChapterIndex()
        {
            if (flowChartDatabase == null || gameState == null) return -1;
            var node = gameState.currentNode;
            if (node == null) return -1;
            if (!flowChartDatabase.TryGetByNodeName(node.name, out var chapter, out _)) return -1;
            for (int i = 0; i < chapters.Count; i++)
            {
                if (chapters[i] == chapter) return i;
            }
            return -1;
        }

        // ── 页签构建 / 刷新 ─────────────────────────────────────────────

        private void BuildSlots()
        {
            slotViews.Clear();
            if (chapterSlotPrefab == null || menuChapter == null) return;

            for (int i = 0; i < chapters.Count; i++)
            {
                int captured = i;
                var chapter = chapters[i];
                var go = Instantiate(chapterSlotPrefab, menuChapter);
                go.name = chapter != null ? $"ChapterSlot_{chapter.chapterNumber}" : $"ChapterSlot_{i}";
                var view = go.GetComponent<ChapterSlotView>();
                if (view == null) view = go.AddComponent<ChapterSlotView>();
                view.Init(() => OnSlotSelected(captured));
                slotViews.Add(view);
            }
        }

        private void UpdateSlots()
        {
            for (int i = 0; i < slotViews.Count && i < chapters.Count; i++)
            {
                var chapter = chapters[i];
                var view = slotViews[i];
                if (view == null) continue;

                if (chapter == null)
                {
                    view.gameObject.SetActive(false);
                    continue;
                }

                view.gameObject.SetActive(true);
                bool unlocked = unlockAllNodes || unlockedChapterIndices.Contains(i);
                view.SetData(chapter.chapterThumbnail, GetSubtitleKey(chapter), unlocked);
            }
        }

        private static string GetSubtitleKey(FlowChartChapter chapter)
        {
            int n = chapter.chapterNumber;
            if (n <= 0) return "title.chaptersubtitle.prologue";
            if (n >= 1 && n <= 6) return $"title.chaptersubtitle.chapter{n}";
            return null;
        }

        // ── 章节预览切换 ──────────────────────────────────────────────

        private void OnSlotSelected(int chapterIndex)
        {
            if (chapterIndex < 0 || chapterIndex >= chapters.Count) return;
            if (chapterIndex == currentChapterIndex) return;
            if (!unlockAllNodes && !unlockedChapterIndices.Contains(chapterIndex)) return;

            if (slotSelectSound != null && viewManager != null)
            {
                viewManager.TryPlaySound(slotSelectSound);
            }

            ApplyChapterContent(chapterIndex, instant: false);
        }

        private void ApplyChapterContent(int chapterIndex, bool instant)
        {
            currentChapterIndex = chapterIndex;

            if (instant || contentGroup == null || crossfadeDuration <= 0f)
            {
                WriteChapterContent(chapterIndex);
                if (contentGroup != null) contentGroup.alpha = 1f;
                return;
            }

            if (crossfadeCo != null) StopCoroutine(crossfadeCo);
            crossfadeCo = StartCoroutine(CrossfadeRoutine(chapterIndex));
        }

        private IEnumerator CrossfadeRoutine(int chapterIndex)
        {
            float half = crossfadeDuration * 0.5f;
            yield return FadeGroup(contentGroup.alpha, 0f, half);
            WriteChapterContent(chapterIndex);
            yield return FadeGroup(0f, 1f, half);
            crossfadeCo = null;
        }

        private IEnumerator FadeGroup(float from, float to, float duration)
        {
            if (contentGroup == null || duration <= 0f)
            {
                if (contentGroup != null) contentGroup.alpha = to;
                yield break;
            }
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                contentGroup.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / duration));
                yield return null;
            }
            contentGroup.alpha = to;
        }

        private void WriteChapterContent(int chapterIndex)
        {
            if (chapterIndex < 0 || chapterIndex >= chapters.Count) return;
            var chapter = chapters[chapterIndex];
            if (chapter == null) return;

            if (backgroundPosterImage != null)
            {
                backgroundPosterImage.sprite = chapter.backgroundPoster;
                backgroundPosterImage.enabled = chapter.backgroundPoster != null;
            }
            if (titleText != null)
            {
                titleText.text = string.IsNullOrEmpty(chapter.chapterNameKey)
                    ? "" : I18n.C(chapter.chapterNameKey);
            }
            if (introText != null)
            {
                introText.text = string.IsNullOrEmpty(chapter.introKey)
                    ? "" : I18n.C(chapter.introKey);
            }

            // 同步所有 slot 的选中态（被选中的换金色，其它换回普通）
            for (int i = 0; i < slotViews.Count; i++)
            {
                if (slotViews[i] != null) slotViews[i].SetSelected(i == chapterIndex);
            }
        }

        private void RefreshLocalizedTexts()
        {
            // Slot 副标题
            for (int i = 0; i < slotViews.Count && i < chapters.Count; i++)
            {
                var ch = chapters[i];
                if (ch == null || slotViews[i] == null) continue;
                slotViews[i].RefreshSubtitle(GetSubtitleKey(ch));
            }
            // 当前章节标题 / 简介
            if (currentChapterIndex >= 0 && currentChapterIndex < chapters.Count)
            {
                var ch = chapters[currentChapterIndex];
                if (ch != null)
                {
                    if (titleText != null)
                        titleText.text = string.IsNullOrEmpty(ch.chapterNameKey)
                            ? "" : I18n.C(ch.chapterNameKey);
                    if (introText != null)
                        introText.text = string.IsNullOrEmpty(ch.introKey)
                            ? "" : I18n.C(ch.introKey);
                }
            }
        }
    }
}
