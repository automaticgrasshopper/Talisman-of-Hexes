using UnityEngine;
using UnityEngine.UI;

namespace Nova
{
    [RequireComponent(typeof(Button))]
    [RequireComponent(typeof(CanvasGroup))]
    public class VideoSkipButton : MonoBehaviour
    {
        private Button button;
        private CanvasGroup canvasGroup;
        private VideoController videoController;

        private void Awake()
        {
            // CanvasGroup 控制可见性 + 交互，而不是 SetActive。
            // 用 SetActive(false) 会让 GameObject 失活后 Update 永久停摆 ——
            // 同一次启动内第二次播视频时，watched 状态再也不会被刷新到按钮。
            canvasGroup = GetComponent<CanvasGroup>();
        }

        private void Start()
        {
            button = GetComponent<Button>();
            button.onClick.AddListener(OnClick);
            videoController = FindObjectOfType<VideoController>();
        }

        private void Update()
        {
            // 仅在当前视频已完整看过时才允许 Skip：首次观看时整体隐藏按钮（连图标都不可见）
            if (button == null || canvasGroup == null) return;
            if (videoController == null)
            {
                videoController = FindObjectOfType<VideoController>();
            }
            bool watched = videoController != null && videoController.IsCurrentVideoWatched;
            canvasGroup.alpha = watched ? 1f : 0f;
            canvasGroup.interactable = watched;
            canvasGroup.blocksRaycasts = watched;
        }

        private void OnClick()
        {
            if (videoController == null) return;
            if (!videoController.IsCurrentVideoWatched) return;
            videoController.Skip();
        }
    }
}
