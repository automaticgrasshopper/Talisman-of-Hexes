using System;
using System.Collections.Generic;
using UnityEngine;

namespace Nova
{
    /// <summary>
    /// 见闻系统主服务。挂在场景里一个常驻 GameObject 上（建议 NovaController 旁边）。
    /// - 持有 GalleryDatabase 引用
    /// - 与 globalSave 之间读写 GalleryUnlockData
    /// - 暴露给 lua 的入口：Unlock(id) / Revoke(id) / Replace(oldId, newId)
    /// - 触发右上角通知（走 Alert.Show → NotificationController）
    /// - 派发事件：onDataChanged（解锁/撤销/已读时） 给 SideMenu 红点 + 见闻面板用
    /// </summary>
    [ExportCustomType]
    public class GalleryService : MonoBehaviour
    {
        [SerializeField] private GalleryDatabase database;

        private CheckpointManager checkpointManager;

        /// <summary>解锁 / 撤销 / 已读状态变更时触发。无参，订阅方自己拉数据。</summary>
        public event Action onDataChanged;

        public GalleryDatabase Database => database;

        public static GalleryService Instance { get; private set; }

        private void Awake()
        {
            Instance = this;
            var controller = Utils.FindNovaController();
            checkpointManager = controller.CheckpointManager;
            LuaRuntime.Instance.BindObject("gallery", this);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ──────────────────────────── globalSave ────────────────────────────

        private GalleryUnlockData LoadData()
        {
            var data = checkpointManager.Get<GalleryUnlockData>(GalleryUnlockData.SaveKey, null);
            if (data == null)
            {
                data = new GalleryUnlockData();
                checkpointManager.Set(GalleryUnlockData.SaveKey, data);
            }
            return data;
        }

        private void SaveData(GalleryUnlockData data)
        {
            checkpointManager.Set(GalleryUnlockData.SaveKey, data);
        }

        // ──────────────────────────── 公共查询 ────────────────────────────

        public bool IsUnlocked(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            return LoadData().unlockTimes.ContainsKey(id);
        }

        public bool IsRead(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            var data = LoadData();
            return data.readIds.TryGetValue(id, out var v) && v;
        }

        /// <summary>该分类是否有"已解锁但未读"的条目（决定 tab 是否显示红点）。</summary>
        public bool HasUnreadInCategory(GalleryCategory category)
        {
            if (database == null) return false;
            var list = database.GetEntriesOf(category);
            if (list == null) return false;
            var data = LoadData();
            foreach (var e in list)
            {
                if (e == null || string.IsNullOrEmpty(e.id)) continue;
                if (!data.unlockTimes.ContainsKey(e.id)) continue;
                if (!(data.readIds.TryGetValue(e.id, out var r) && r)) return true;
            }
            return false;
        }

        /// <summary>整体是否有任意未读（决定 SideMenu 见闻按钮红点）。</summary>
        public bool HasAnyUnread()
        {
            for (int c = 0; c < 4; ++c)
                if (HasUnreadInCategory((GalleryCategory)c)) return true;
            return false;
        }

        /// <summary>取该 id 的解锁时间戳，未解锁返回 0。用于"最新解锁顶置"排序。</summary>
        public long GetUnlockTime(string id)
        {
            var data = LoadData();
            return data.unlockTimes.TryGetValue(id, out var t) ? t : 0L;
        }

        /// <summary>
        /// 返回该分类下"可见的、按显示顺序排好"的条目列表：
        ///   1) 未读 & 已解锁的，按解锁时间倒序（最新顶置）
        ///   2) 已读 & 已解锁的，按 Database 列表里的原顺序
        ///   3) 未解锁的：不出现
        /// </summary>
        public List<GalleryEntry> GetVisibleEntries(GalleryCategory category)
        {
            var result = new List<GalleryEntry>();
            if (database == null) return result;
            var list = database.GetEntriesOf(category);
            if (list == null) return result;
            var data = LoadData();

            var unread = new List<GalleryEntry>();
            var read = new List<GalleryEntry>();
            foreach (var e in list)
            {
                if (e == null || string.IsNullOrEmpty(e.id)) continue;
                if (!data.unlockTimes.ContainsKey(e.id)) continue;
                bool isRead = data.readIds.TryGetValue(e.id, out var r) && r;
                if (isRead) read.Add(e);
                else        unread.Add(e);
            }

            // 未读：按 unlockTime 降序（最新在前）
            unread.Sort((a, b) =>
                data.unlockTimes[b.id].CompareTo(data.unlockTimes[a.id]));

            result.AddRange(unread);
            result.AddRange(read);
            return result;
        }

        // ──────────────────────────── 解锁 / 撤销 / 已读 ────────────────────────────

        /// <summary>
        /// 解锁一条见闻。lua: gallery_unlock('id').
        /// 已解锁的 id 重复调用安静返回（不刷新时间戳，避免乱顶置）。
        /// </summary>
        public void Unlock(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (database == null) { Debug.LogWarning("GalleryService: database 未配置"); return; }
            if (!database.TryGetById(id, out var entry, out _))
            {
                Debug.LogWarning($"GalleryService: 未在 Database 中找到见闻 id='{id}'");
                return;
            }

            var data = LoadData();
            if (data.unlockTimes.ContainsKey(id)) return;  // 已解锁 → 不重复
            data.unlockTimes[id] = DateTime.UtcNow.Ticks;
            // 解锁默认未读
            if (data.readIds.ContainsKey(id)) data.readIds.Remove(id);
            SaveData(data);

            // 触发右上角通知：构造 LocalizedStrings，走 Alert.Show 即可
            var title = I18n.C(entry.titleKey);
            try
            {
                var dict = I18n.GetLocalizedStrings("gallery.notification.added", title);
                Alert.Show(dict);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"GalleryService: 通知失败 - {e.Message}");
            }

            onDataChanged?.Invoke();
        }

        /// <summary>
        /// 撤销一条见闻。lua: gallery_revoke('id').
        /// 从已解锁/已读中移除。用于"一划命符 → 两划命符"这种条目替换。
        /// </summary>
        public void Revoke(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            var data = LoadData();
            bool changed = false;
            if (data.unlockTimes.Remove(id)) changed = true;
            if (data.readIds.Remove(id))     changed = true;
            if (changed)
            {
                SaveData(data);
                onDataChanged?.Invoke();
            }
        }

        /// <summary>
        /// 替换 = revoke(oldId) + unlock(newId)。lua: gallery_replace('old', 'new').
        /// </summary>
        public void Replace(string oldId, string newId)
        {
            Revoke(oldId);
            Unlock(newId);
        }

        /// <summary>玩家点开条目详情时调用，写入"已读"。已是已读则空操作。</summary>
        public void MarkRead(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            var data = LoadData();
            if (!data.unlockTimes.ContainsKey(id)) return;
            if (data.readIds.TryGetValue(id, out var r) && r) return;
            data.readIds[id] = true;
            SaveData(data);
            onDataChanged?.Invoke();
        }

        /// <summary>
        /// 将一个分类下所有"已解锁但未读"的条目一次性置为已读。
        /// 用于：玩家点击该分类下任意条目时，把同分类其它"新"条目也消掉，
        /// 让原本顶置的未读项落回它在 Database 中的自然顺序。仅触发一次 onDataChanged。
        /// </summary>
        public void MarkAllReadInCategory(GalleryCategory category)
        {
            if (database == null) return;
            var list = database.GetEntriesOf(category);
            if (list == null) return;
            var data = LoadData();
            bool changed = false;
            foreach (var e in list)
            {
                if (e == null || string.IsNullOrEmpty(e.id)) continue;
                if (!data.unlockTimes.ContainsKey(e.id)) continue;
                if (data.readIds.TryGetValue(e.id, out var r) && r) continue;
                data.readIds[e.id] = true;
                changed = true;
            }
            if (changed)
            {
                SaveData(data);
                onDataChanged?.Invoke();
            }
        }
    }
}
