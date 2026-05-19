using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Nova
{
    /// <summary>
    /// 见闻主面板。挂在 StoryGalleryView 根节点上（ViewControllerBase）。
    /// 4 个 Tab + 左侧条目滑动列表 + 右侧详情面板（标题/图片/描述）。
    /// 打开行为：选中"最近解锁但未读"所在的 tab；若有多个，按 Hero→News→Message→Item 从左到右挑第一个；都无未读时选中第一个 tab。
    /// </summary>
    public class StoryGalleryViewController : ViewControllerBase
    {
        [Header("基础")]
        [SerializeField] private Button closeButton;

        [Header("Tabs（按 Hero/News/Message/Item 顺序拖入 4 个 GalleryTabView）")]
        [SerializeField] private GalleryTabView[] tabs;

        [Header("条目列表")]
        [SerializeField] private RectTransform entryContent;     // ScrollView > Viewport > Content
        [SerializeField] private GalleryEntryView entryPrefab;   // 即配置好脚本的 StoryItem.prefab

        [Header("右侧详情面板")]
        [SerializeField] private Image detailImage;
        [SerializeField] private TMP_Text detailDescriptionText;
        [SerializeField] private GameObject detailPanelRoot;       // 整个右侧详情容器（无选中时隐藏）

        [Header("Badge prefab（NewShowTitle.prefab）")]
        [SerializeField] private NewBadge newBadgePrefab;

        private GalleryCategory currentCategory;
        private readonly List<GalleryEntryView> spawnedEntries = new List<GalleryEntryView>();
        private GalleryEntry currentDetailEntry;
        private string currentSelectedId;

        protected override void Awake()
        {
            base.Awake();
            if (closeButton != null)
            {
                closeButton.onClick.RemoveAllListeners();
                closeButton.onClick.AddListener(() => Hide());
            }
        }

        public override void Show(bool doTransition, System.Action onFinish)
        {
            base.Show(doTransition, onFinish);
            EnsureTabsInit();
            currentCategory = PickDefaultCategory();
            currentSelectedId = null;

            var svc = GalleryService.Instance;
            if (svc != null) svc.onDataChanged += OnGalleryChanged;

            RefreshTabs();
            RefreshEntryList();
            ClearDetail();

            I18n.LocaleChanged.AddListener(OnLocaleChanged);
        }

        public override void Hide(bool doTransition, System.Action onFinish)
        {
            var svc = GalleryService.Instance;
            if (svc != null) svc.onDataChanged -= OnGalleryChanged;

            I18n.LocaleChanged.RemoveListener(OnLocaleChanged);
            base.Hide(doTransition, onFinish);
        }

        private void OnGalleryChanged()
        {
            RefreshTabs();
            RefreshEntryList();
            // 若当前详情条目已被 revoke，清空详情
            if (currentDetailEntry != null && GalleryService.Instance != null
                && !GalleryService.Instance.IsUnlocked(currentDetailEntry.id))
            {
                ClearDetail();
            }
        }

        private void OnLocaleChanged()
        {
            // tab 标题靠 I18nText 组件自刷新；这里只刷条目和详情
            RefreshEntryList();
            if (currentDetailEntry != null) ShowDetail(currentDetailEntry);
        }

        // ──────────────────────────── Tab 逻辑 ────────────────────────────

        private void EnsureTabsInit()
        {
            if (tabs == null) return;
            var svc = GalleryService.Instance;
            var db = svc != null ? svc.Database : null;
            for (int i = 0; i < tabs.Length; ++i)
            {
                var t = tabs[i];
                if (t == null) continue;
                var cat = (GalleryCategory)i;
                if (db != null)
                {
                    t.Init(this, cat, db.tabNormalSprite, db.tabSelectedSprite, db.tabHoverMaterial, newBadgePrefab);
                }
            }
        }

        private GalleryCategory PickDefaultCategory()
        {
            var svc = GalleryService.Instance;
            if (svc == null) return GalleryCategory.Hero;
            for (int i = 0; i < 4; ++i)
            {
                var cat = (GalleryCategory)i;
                if (svc.HasUnreadInCategory(cat)) return cat;
            }
            return GalleryCategory.Hero;
        }

        private void RefreshTabs()
        {
            if (tabs == null) return;
            var svc = GalleryService.Instance;
            for (int i = 0; i < tabs.Length; ++i)
            {
                var t = tabs[i];
                if (t == null) continue;
                var cat = (GalleryCategory)i;
                t.SetSelected(cat == currentCategory);
                t.SetHasUnread(svc != null && svc.HasUnreadInCategory(cat));
            }
        }

        public void OnTabClicked(GalleryTabView tab)
        {
            if (tab == null) return;
            var svc = GalleryService.Instance;
            var db = svc != null ? svc.Database : null;
            if (db != null && db.tabClickSound != null && viewManager != null)
            {
                viewManager.TryPlaySound(db.tabClickSound);
            }
            if (tab.Category == currentCategory) return;
            currentCategory = tab.Category;
            currentSelectedId = null;
            RefreshTabs();
            RefreshEntryList();
            ClearDetail();
        }

        public void PlayTabHoverSound()
        {
            var svc = GalleryService.Instance;
            var db = svc != null ? svc.Database : null;
            if (db != null && db.tabHoverSound != null && viewManager != null)
            {
                viewManager.TryPlaySound(db.tabHoverSound);
            }
        }

        // ──────────────────────────── 条目列表 ────────────────────────────

        private void RefreshEntryList()
        {
            ClearEntryList();
            if (entryContent == null || entryPrefab == null) return;
            var svc = GalleryService.Instance;
            if (svc == null) return;
            var db = svc.Database;
            var selectedSprite = db != null ? db.entrySelectedSprite : null;
            var hoverMat = db != null ? db.tabHoverMaterial : null;

            var list = svc.GetVisibleEntries(currentCategory);
            foreach (var e in list)
            {
                var view = Instantiate(entryPrefab, entryContent, false);
                bool unread = !svc.IsRead(e.id);
                view.Init(this, e, unread, selectedSprite, hoverMat);
                view.SetSelected(e.id == currentSelectedId);
                spawnedEntries.Add(view);
            }
        }

        private void RefreshSelectionVisual()
        {
            foreach (var v in spawnedEntries)
            {
                if (v == null || v.Entry == null) continue;
                v.SetSelected(v.Entry.id == currentSelectedId);
            }
        }

        private void ClearEntryList()
        {
            foreach (var v in spawnedEntries)
            {
                if (v != null) Destroy(v.gameObject);
            }
            spawnedEntries.Clear();
        }

        public void OnEntryClicked(GalleryEntryView view)
        {
            if (view == null || view.Entry == null) return;
            var svc = GalleryService.Instance;
            var db = svc != null ? svc.Database : null;
            if (db != null && db.entryClickSound != null && viewManager != null)
            {
                viewManager.TryPlaySound(db.entryClickSound);
            }
            currentSelectedId = view.Entry.id;
            ShowDetail(view.Entry);
            RefreshSelectionVisual();
            // 只标记被点击的那一条已读，其它"new"条目保持未读：
            // 点一个消除一个，不点就不消除，退出再进来也保留。
            // MarkRead 会触发 onDataChanged → RefreshEntryList 重建 view 实例，
            // 所以这里 view 在调用后失效，需要的数据 (view.Entry.id) 已先存进 currentSelectedId。
            if (svc != null) svc.MarkRead(view.Entry.id);
        }

        // ──────────────────────────── 详情面板 ────────────────────────────

        private void ShowDetail(GalleryEntry entry)
        {
            currentDetailEntry = entry;
            if (detailPanelRoot != null) detailPanelRoot.SetActive(true);
            if (detailDescriptionText != null) detailDescriptionText.text = I18n.C(entry.descKey);
            if (detailImage != null)
            {
                var svc = GalleryService.Instance;
                var db = svc != null ? svc.Database : null;
                var sp = entry.image != null
                    ? entry.image
                    : (db != null ? db.defaultDetailImage : null);
                detailImage.sprite = sp;
                detailImage.enabled = sp != null;
            }
        }

        private void ClearDetail()
        {
            currentDetailEntry = null;
            currentSelectedId = null;
            if (detailPanelRoot != null) detailPanelRoot.SetActive(false);
            RefreshSelectionVisual();
        }

    }
}
