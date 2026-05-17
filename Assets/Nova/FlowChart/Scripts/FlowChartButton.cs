using UnityEngine;
using UnityEngine.UI;

namespace Nova
{
    /// <summary>
    /// 游戏内左上角入口按钮。
    /// 挂在 GameUIPanel 下的按钮 GameObject 上，
    /// Button.onClick 直接调用 OnClick() 即可。
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class FlowChartButton : MonoBehaviour
    {
        private FlowChartController flowChartController;
        private VideoController videoController;

        private void Awake()
        {
            flowChartController = FindObjectOfType<FlowChartController>(true);
            videoController = FindObjectOfType<VideoController>(true);
            GetComponent<Button>().onClick.AddListener(OnClick);
        }

        private void OnDestroy()
        {
            GetComponent<Button>().onClick.RemoveListener(OnClick);
        }

        public void OnClick()
        {
            if (flowChartController == null) return;

            // 视频正在播放时先暂停
            videoController?.Pause();

            flowChartController.Show(true, null);
        }
    }
}
