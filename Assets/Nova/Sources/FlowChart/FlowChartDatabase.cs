using System.Collections.Generic;
using UnityEngine;

namespace Nova
{
    [CreateAssetMenu(fileName = "FlowChartDatabase", menuName = "Nova/FlowChart/Database")]
    public class FlowChartDatabase : ScriptableObject
    {
        [Tooltip("所有章节，顺序 = ChapterSelectView 显示顺序")]
        public List<FlowChartChapter> chapters = new List<FlowChartChapter>();

        [Tooltip("未访问分支的占位缩略图（SlotUnknown 状态）")]
        public Sprite slotUnknownThumbnail;

        [Tooltip("Slot hover 时 SlotInside / SlotChoicen 共用的材质")]
        public Material slotHoverMaterial;

        [Tooltip("已走过但不在当前金色路径上的连线材质（白色实线）")]
        public Material lineActiveMaterial;

        [Tooltip("当前分支高亮连线材质（金色流光）")]
        public Material lineCurrentBranchMaterial;

        [Tooltip("未走过的连线材质（灰色虚线）")]
        public Material lineLockedMaterial;

        [Header("流程图布局")]
        [Tooltip("列与列之间的水平间距（px）")]
        public float columnSpacing = 360f;

        [Tooltip("同列内 slot 之间的垂直间距（px）")]
        public float rowSpacing = 240f;

        [Header("连线样式")]
        [Tooltip("连线粗细（px）")]
        public float lineWidth = 4f;

        [Tooltip("转角圆弧半径，0 = 硬直角折角，>0 为圆角")]
        public float lineCornerRadius = 0f;

        [Header("Slot 交互音效（走 UI 音量）")]
        [Tooltip("鼠标悬停 slot 时播放（可空）")]
        public AudioClip slotHoverSound;

        [Tooltip("点击 slot 时播放（可空）")]
        public AudioClip slotClickSound;

        // ── 运行时反查表（nodeName → slot） ──────────────────────────────
        private Dictionary<string, (FlowChartChapter chapter, FlowChartSlot slot)> _nodeMap;

        public void BuildNodeMap()
        {
            _nodeMap = new Dictionary<string, (FlowChartChapter, FlowChartSlot)>();
            foreach (var chapter in chapters)
            {
                if (chapter == null) continue;
                foreach (var slot in chapter.slots)
                {
                    foreach (var nodeName in slot.nodeNames)
                    {
                        if (!string.IsNullOrEmpty(nodeName))
                            _nodeMap[nodeName] = (chapter, slot);
                    }
                }
            }
        }

        public bool TryGetByNodeName(string nodeName,
            out FlowChartChapter chapter, out FlowChartSlot slot)
        {
            if (_nodeMap != null && _nodeMap.TryGetValue(nodeName, out var result))
            {
                chapter = result.chapter;
                slot = result.slot;
                return true;
            }
            chapter = null;
            slot = null;
            return false;
        }

        public FlowChartSlot GetSlot(FlowChartChapter chapter, string slotId)
        {
            return chapter?.GetSlot(slotId);
        }
    }
}
