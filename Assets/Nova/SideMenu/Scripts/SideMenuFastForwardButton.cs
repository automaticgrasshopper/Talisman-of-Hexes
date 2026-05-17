using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Nova
{
    /// <summary>
    /// Hold-to-fast-forward button used in the side menu.
    /// PointerDown  → set DialogueState.state = FastForward
    ///              → play 2-second hold sound
    ///              → tint targetImage to heldColor over pressDuration (default 2s)
    /// PointerUp    → restore state to Normal (only if still FastForward)
    ///              → stop the hold sound
    ///              → tint back to original color over releaseDuration (default 0.2s)
    ///
    /// Intentionally does NOT touch DialogueState.fastForwardShortcutHolding, so the
    /// existing "no fast-forward on unread content" rule (stopFastForward) stays in force.
    /// </summary>
    public class SideMenuFastForwardButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        [SerializeField] private Image targetImage;
        [SerializeField] private float pressDuration = 2f;
        [SerializeField] private float releaseDuration = 0.2f;
        [SerializeField] private Color heldColor = Color.black;
        [SerializeField] private AudioClip holdSound;
        [SerializeField] private float videoFastForwardSpeed = 4f;

        private DialogueState dialogueState;
        private ViewManager viewManager;
        private Color originalColor;
        private Tween colorTween;
        private bool isHeld;
        private bool videoSpeedBoosted;

        private void Awake()
        {
            dialogueState = Utils.FindNovaController().DialogueState;
            viewManager = Utils.FindViewManager();
            if (targetImage == null) targetImage = GetComponent<Image>();
            if (targetImage != null) originalColor = targetImage.color;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (isHeld) return;
            isHeld = true;

            if (dialogueState != null)
            {
                dialogueState.state = DialogueState.State.FastForward;
            }

            // 视频中且该视频已看完 → 临时拉高播放速度
            var vc = FindObjectOfType<VideoController>();
            if (vc != null && vc.IsVideoActive && vc.IsCurrentVideoWatched)
            {
                vc.SetPlaybackSpeed(videoFastForwardSpeed);
                videoSpeedBoosted = true;
            }

            if (viewManager != null && holdSound != null)
            {
                viewManager.TryPlaySound(holdSound);
            }

            if (targetImage != null)
            {
                colorTween?.Kill();
                colorTween = targetImage
                    .DOColor(heldColor, pressDuration)
                    .SetEase(Ease.Linear)
                    .SetUpdate(true);
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!isHeld) return;
            ReleaseHold();
        }

        private void OnDisable()
        {
            if (isHeld) ReleaseHold();
        }

        private void ReleaseHold()
        {
            isHeld = false;

            if (dialogueState != null && dialogueState.state == DialogueState.State.FastForward)
            {
                dialogueState.state = DialogueState.State.Normal;
            }

            if (videoSpeedBoosted)
            {
                videoSpeedBoosted = false;
                var vc = FindObjectOfType<VideoController>();
                if (vc != null) vc.SetPlaybackSpeed(1f);
            }

            if (viewManager != null)
            {
                viewManager.TryStopSound();
            }

            if (targetImage != null)
            {
                colorTween?.Kill();
                colorTween = targetImage
                    .DOColor(originalColor, releaseDuration)
                    .SetEase(Ease.Linear)
                    .SetUpdate(true);
            }
        }
    }
}
