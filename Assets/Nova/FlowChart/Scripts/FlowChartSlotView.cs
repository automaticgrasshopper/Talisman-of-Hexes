using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Nova
{
    public enum SlotState
    {
        Current,    // 玩家当前所在节点：Inside + Choicen 高亮
        Visited,    // 已访问但非当前：Inside + Outside
        Unknown,    // 可达但未访问：SlotUnknown 缩略图 + Outside，不可交互
    }

    /// <summary>
    /// 挂在 SlotPrefab 根节点上。负责状态显示、hover 效果、点击响应。
    /// </summary>
    public class FlowChartSlotView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        [Header("子对象引用")]
        [SerializeField] private Image slotInside;       // 缩略图层（应用 Shader）
        [SerializeField] private Image slotChoicen;      // 当前状态高亮框（不应用任何 Shader）
        [SerializeField] private Image slotOutside;      // 已访问外框
        [SerializeField] private GameObject slotDeath;        // 死亡结局装饰图（isDeath=true 且非 Current 时显示）
        [SerializeField] private GameObject slotDeathChoicen; // 死亡结局且 Current 时显示（玩家死亡回流程图时停在此节点）
        [SerializeField] private GameObject nodeText;     // hover 时显示的节点名，默认隐藏
        private TMP_Text nodeTextLabel;

        // 运行时填入
        public FlowChartSlot SlotData { get; private set; }
        public SlotState State { get; private set; }

        private FlowChartDatabase database;
        private FlowChartController controller;

        // 仅记录 slotInside 的原始材质
        private Material insideOriginalMat;

        public void Init(FlowChartSlot slotData, SlotState state,
            FlowChartDatabase db, FlowChartController ctrl)
        {
            SlotData = slotData;
            State = state;
            database = db;
            controller = ctrl;

            // 缩略图
            var thumb = (state == SlotState.Unknown)
                ? db.slotUnknownThumbnail
                : slotData.thumbnail;
            if (slotInside != null) slotInside.sprite = thumb;

            // 仅记录 slotInside 的原始材质
            if (slotInside != null) insideOriginalMat = slotInside.material;

            // NodeText 文字（i18n）
            if (nodeText != null)
            {
                nodeTextLabel = nodeText.GetComponentInChildren<TMP_Text>();
                if (nodeTextLabel != null)
                    nodeTextLabel.text = I18n.C(slotData.slotNameKey);
                nodeText.SetActive(false);
            }

            // 把所有子 Button 的点击都转发到 OnSlotClicked（解决子 Button 拦截事件问题）
            foreach (var btn in GetComponentsInChildren<Button>(true))
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() =>
                {
                    if (State == SlotState.Unknown) return;
                    controller.PlaySlotClickSound();
                    controller.OnSlotClicked(this);
                });
            }

            // 子对象激活状态
            if (slotInside != null) slotInside.gameObject.SetActive(true);

            // 死亡结局：SlotDeath / SlotDeathChoicen 接管装饰层，Choicen / Outside 都不显示
            // Unknown 状态仍按未访问处理（玩家还没到过这里，不知道是死路）
            bool isDeathDisplay = slotData.isDeath && state != SlotState.Unknown;
            bool isDeathCurrent = isDeathDisplay && state == SlotState.Current;
            // Current → 用 slotDeathChoicen；Visited → 用 slotDeath
            if (slotDeath != null) slotDeath.SetActive(isDeathDisplay && !isDeathCurrent);
            if (slotDeathChoicen != null) slotDeathChoicen.SetActive(isDeathCurrent);

            if (isDeathDisplay)
            {
                if (slotChoicen != null) slotChoicen.gameObject.SetActive(false);
                if (slotOutside != null) slotOutside.gameObject.SetActive(false);
            }
            else
            {
                switch (state)
                {
                    case SlotState.Current:
                        if (slotChoicen != null) slotChoicen.gameObject.SetActive(true);
                        if (slotOutside != null) slotOutside.gameObject.SetActive(false);
                        break;
                    case SlotState.Visited:
                        if (slotChoicen != null) slotChoicen.gameObject.SetActive(false);
                        if (slotOutside != null) slotOutside.gameObject.SetActive(true);
                        break;
                    case SlotState.Unknown:
                        if (slotChoicen != null) slotChoicen.gameObject.SetActive(false);
                        if (slotOutside != null) slotOutside.gameObject.SetActive(true);
                        break;
                }
            }
        }

        // ── Hover ──────────────────────────────────────────────────────────
        public void OnPointerEnter(PointerEventData _)
        {
            if (State == SlotState.Unknown) return;

            // 仅对 slotInside 应用 hover 材质，slotChoicen 不做任何材质替换
            if (database.slotHoverMaterial != null && slotInside != null)
                slotInside.material = database.slotHoverMaterial;

            if (nodeText != null) nodeText.SetActive(true);

            controller?.PlaySlotHoverSound();
        }

        public void OnPointerExit(PointerEventData _)
        {
            if (State == SlotState.Unknown) return;

            // 恢复 slotInside 的原始材质
            if (slotInside != null) slotInside.material = insideOriginalMat;

            if (nodeText != null) nodeText.SetActive(false);
        }

        // ── Click ──────────────────────────────────────────────────────────
        public void OnPointerClick(PointerEventData eventData)
        {
            if (State == SlotState.Unknown) return;
            controller?.PlaySlotClickSound();
            controller.OnSlotClicked(this);
        }
    }
}
