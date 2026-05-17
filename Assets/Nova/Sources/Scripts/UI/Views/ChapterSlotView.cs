using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Nova
{
    /// <summary>
    /// 章节选择界面上的单个页签 view（挂在 ChapterSlot 预制体根节点）。
    /// 视觉布局：
    ///   - rootImage     根背景
    ///   - blockImage    底部滑块底图
    ///   - innerPhoto    章节缩略图（159×212，竖图，从 FlowChartChapter.chapterThumbnail 读取）
    ///   - subtitleText  "序章 / 第一章 / ..." 副标题
    /// 状态：
    ///   - 选中态：用 rootSelectedSprite / blockSelectedSprite（金色底图）
    ///   - 普通态：用 rootNormalSprite / blockNormalSprite
    ///   - 锁定态：缩略图 + 文字按 locked 配色叠加，Button.interactable=false
    /// 选中由 controller 在点击或键盘切换时显式调 SetSelected(bool)，不走 Button 的 hover transition。
    /// </summary>
    public class ChapterSlotView : MonoBehaviour
    {
        [Header("Visual parts (拖 prefab 上对应节点)")]
        [SerializeField] private Image rootImage;
        [SerializeField] private Image blockImage;
        [SerializeField] private Image innerPhoto;
        [SerializeField] private TMP_Text subtitleText;

        [Header("Sprites — 普通/选中态切换")]
        [SerializeField] private Sprite rootNormalSprite;
        [SerializeField] private Sprite rootSelectedSprite;
        [SerializeField] private Sprite blockNormalSprite;
        [SerializeField] private Sprite blockSelectedSprite;

        [Header("Locked (未解锁) 状态着色")]
        [Tooltip("锁定时缩略图叠加色（深灰=置灰效果）")]
        [SerializeField] private Color lockedThumbnailTint = new Color(0.35f, 0.35f, 0.35f, 1f);
        [Tooltip("锁定时副标题文字颜色")]
        [SerializeField] private Color lockedSubtitleColor = new Color(0.6f, 0.6f, 0.6f, 1f);
        [SerializeField] private Color unlockedSubtitleColor = Color.white;

        private Action onClicked;
        private bool isLocked;
        private bool isSelected;

        /// <summary>
        /// 转发所有子 Button 的 onClick 到 selectedCallback。
        /// controller 在 Instantiate 之后立即调一次。
        /// </summary>
        public void Init(Action selectedCallback)
        {
            onClicked = selectedCallback;
            foreach (var btn in GetComponentsInChildren<Button>(true))
            {
                btn.onClick.RemoveListener(HandleClick);
                btn.onClick.AddListener(HandleClick);
                // 关掉 Button 自带的 SpriteSwap，统一由本脚本驱动
                btn.transition = Selectable.Transition.None;
            }
        }

        /// <summary>填入数据：缩略图、副标题 key、是否解锁。</summary>
        public void SetData(Sprite thumbnail, string subtitleKey, bool unlocked)
        {
            isLocked = !unlocked;

            if (innerPhoto != null)
            {
                innerPhoto.sprite = thumbnail;
                innerPhoto.color = unlocked ? Color.white : lockedThumbnailTint;
                innerPhoto.enabled = thumbnail != null;
            }

            if (subtitleText != null)
            {
                subtitleText.text = string.IsNullOrEmpty(subtitleKey) ? "" : I18n.__(subtitleKey);
                subtitleText.color = unlocked ? unlockedSubtitleColor : lockedSubtitleColor;
            }

            foreach (var btn in GetComponentsInChildren<Button>(true))
            {
                btn.interactable = unlocked;
            }

            // 锁定时强制非选中，避免视觉残留
            if (isLocked) SetSelected(false);
            else ApplySpritesForState();
        }

        /// <summary>由 controller 调用：把页签设为选中/未选中态（金色底图 vs 普通底图）。</summary>
        public void SetSelected(bool selected)
        {
            isSelected = selected && !isLocked;
            ApplySpritesForState();
        }

        public bool IsLocked => isLocked;

        /// <summary>locale 切换时只刷新文本（不重建 slot）。</summary>
        public void RefreshSubtitle(string subtitleKey)
        {
            if (subtitleText == null) return;
            subtitleText.text = string.IsNullOrEmpty(subtitleKey) ? "" : I18n.__(subtitleKey);
        }

        private void ApplySpritesForState()
        {
            if (rootImage != null)
            {
                var s = isSelected ? rootSelectedSprite : rootNormalSprite;
                if (s != null) rootImage.sprite = s;
            }
            if (blockImage != null)
            {
                var s = isSelected ? blockSelectedSprite : blockNormalSprite;
                if (s != null) blockImage.sprite = s;
            }
        }

        private void HandleClick()
        {
            if (isLocked) return;
            onClicked?.Invoke();
        }
    }
}
