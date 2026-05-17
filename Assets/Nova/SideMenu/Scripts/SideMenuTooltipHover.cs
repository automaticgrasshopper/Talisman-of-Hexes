using UnityEngine;
using UnityEngine.EventSystems;

namespace Nova
{
    /// <summary>
    /// Attach to a side-menu icon button. The referenced tooltip GameObject is hidden
    /// at startup and shown only while the pointer is over the button.
    /// </summary>
    public class SideMenuTooltipHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private GameObject tooltip;

        private void Awake()
        {
            if (tooltip != null) tooltip.SetActive(false);
        }

        private void OnDisable()
        {
            if (tooltip != null) tooltip.SetActive(false);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (tooltip != null) tooltip.SetActive(true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (tooltip != null) tooltip.SetActive(false);
        }
    }
}
