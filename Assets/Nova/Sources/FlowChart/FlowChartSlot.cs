using System;
using System.Collections.Generic;
using UnityEngine;

namespace Nova
{
    [Serializable]
    public class FlowChartSlot
    {
        [Tooltip("唯一 ID，如 ch0_1")]
        public string slotId;

        [Tooltip("i18n key，如 flowchart.slot.ch0_1")]
        public string slotNameKey;

        [Tooltip("SlotInside 缩略图")]
        public Sprite thumbnail;

        [Tooltip("该 slot 对应的 scenario 节点名（可多个）")]
        public List<string> nodeNames = new List<string>();

        [Tooltip("所在列（0=最左列）。同列内的上下顺序 = 在 chapter.slots 列表中的相对顺序，先出现的在上")]
        public int column;

        [Tooltip("下游 slot 的 slotId 列表（用于画连线）")]
        public List<string> nextSlotIds = new List<string>();

        [Tooltip("是否为死亡结局 slot。\n- 视觉：显示 prefab 上的 SlotDeath 特殊图，SlotChoicen / SlotOutside 都不显示\n- 行为：节点 is_end / is_dead 触发后，玩家被送回流程图（停在此 slot）而非主界面\n- 脚本端约定：死亡结局节点写 is_dead() 替代 is_end()（功能完全等同 is_end，只是语义化标注）")]
        public bool isDeath;
    }
}
