using System.Collections.Generic;
using UnityEngine;

namespace Nova
{
    [CreateAssetMenu(fileName = "Chapter", menuName = "Nova/FlowChart/Chapter")]
    public class FlowChartChapter : ScriptableObject
    {
        [Tooltip("章节编号，如 1、2、3")]
        public int chapterNumber;

        [Tooltip("i18n key，如 flowchart.chapter.1")]
        public string chapterNameKey;

        [Tooltip("章节起点 slot 的 slotId（ChapterSelect 点击时跳这个 slot 的 nodeNames[0]）")]
        public string firstSlotId;

        [Header("ChapterSelect 显示")]
        [Tooltip("章节选择界面：页签缩略图（ChapterSlot 的 Innerphoto，159×212 竖图）")]
        public Sprite chapterThumbnail;

        [Tooltip("章节选择界面：选中此章节时的背景海报")]
        public Sprite backgroundPoster;

        [Tooltip("章节选择界面：章节简介文本的 i18n key（走 I18n.C / LocalizedContent），可空")]
        public string introKey;

        public List<FlowChartSlot> slots = new List<FlowChartSlot>();

        public FlowChartSlot GetSlot(string slotId)
        {
            return slots.Find(s => s.slotId == slotId);
        }

        /// <summary>返回章节起点的第一个 scenario 节点名（用于 GameStart / 解锁判断）</summary>
        public string GetFirstNodeName()
        {
            var first = GetSlot(firstSlotId);
            if (first == null || first.nodeNames == null || first.nodeNames.Count == 0)
                return null;
            return first.nodeNames[0];
        }
    }
}
