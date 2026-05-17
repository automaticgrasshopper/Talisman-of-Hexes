using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Nova
{
    /// <summary>
    /// ClickTheButton 示例小游戏。
    ///
    /// 玩法：开局 Right Button 从屏幕左边滑到右边，限时 <see cref="duration"/> 秒走完。
    /// - 期间点 Right Button       → 成功
    /// - 期间点 Wrong Area Button  → 失败
    /// - 时间走完都没点            → 超时
    ///
    /// 结果通过 Nova 的 variables 写出 v_minigame_result（'success' / 'wrong' / 'timeout'），
    /// 再 SignalFence 解除 lua 端 wait_fence。lua 里随后用 branch 跳节点即可。
    ///
    /// 音频：SoundController / 主 BGM AudioController（luaGlobalName=bgm）的引用
    /// 通过 Inspector 拖入。运行时 Awake 会自动兜底 FindObjectOfType，万一忘了拖也能跑。
    /// 所有播放走 Nova 自己的通道，玩家在设置里调的音量自动生效。
    /// </summary>
    public class ClickTheButtonMinigame : MonoBehaviour
    {
        // ───────── Inspector 绑定 ─────────

        [Header("UI 引用")]
        public RectTransform rightButtonRect;     // 滑动的按钮（拖 RightButton 的 RectTransform）
        public Button rightButton;                 // RightButton 的 Button 组件
        public Button wrongButton;                 // WrongAreaButton 的 Button 组件
        public TMP_Text tikNumText;                // 倒计时显示（TikNum）

        [Header("玩法参数")]
        [Tooltip("从左到右滑完一共多少秒")] public float duration = 5f;
        [Tooltip("起点 anchoredPosition")] public Vector2 startAnchoredPos = new Vector2(-763, 82);
        [Tooltip("终点 anchoredPosition")] public Vector2 endAnchoredPos = new Vector2(763, 82);

        [Header("音频通道（不拖会自动 Find）")]
        public SoundController soundController;   // 播 SFX
        public AudioController bgmController;     // 播 BGM（拖场景里 luaGlobalName=bgm 的那个）

        [Header("音效（资源名相对 SoundController.audioFolder，留空则不播）")]
        public string sfxOnStart;
        public string sfxOnSuccess;
        public string sfxOnWrong;
        public string sfxOnTimeout;
        [Range(0f, 1f)] public float sfxVolume = 1f;

        [Header("BGM（资源名相对 bgmController.audioFolder，留空则不播）")]
        public string bgmName;
        [Range(0f, 1f)] public float bgmVolume = 0.5f;

        // ───────── 运行时 ─────────

        private GameState gameState;
        private Variables variables;
        private float elapsed;
        private bool finished;

        // ───────── 生命周期 ─────────

        private void Awake()
        {
            var controller = Utils.FindNovaController();
            gameState = controller.GameState;
            variables = gameState.variables;

            if (soundController == null) soundController = FindObjectOfType<SoundController>();
            if (bgmController == null)
            {
                foreach (var ac in FindObjectsOfType<AudioController>())
                {
                    if (ac.luaGlobalName == "bgm")
                    {
                        bgmController = ac;
                        break;
                    }
                }
            }

            if (rightButton != null) rightButton.onClick.AddListener(OnRightClicked);
            if (wrongButton != null) wrongButton.onClick.AddListener(OnWrongClicked);
        }

        private void Start()
        {
            // 摆到起点 + 刷新计时显示
            if (rightButtonRect != null)
            {
                rightButtonRect.anchoredPosition = startAnchoredPos;
            }
            UpdateTimerLabel(duration);

            // 起 BGM（会替换 scenario 之前播的，scenario 后续节点重新 play 即可恢复）
            if (!string.IsNullOrEmpty(bgmName) && bgmController != null)
            {
                bgmController.scriptVolume = bgmVolume;
                bgmController.Play(bgmName);
            }

            PlaySfx(sfxOnStart);
        }

        private void Update()
        {
            if (finished) return;

            elapsed += Time.unscaledDeltaTime;
            float t = duration > 0f ? Mathf.Clamp01(elapsed / duration) : 1f;

            if (rightButtonRect != null)
            {
                rightButtonRect.anchoredPosition = Vector2.Lerp(startAnchoredPos, endAnchoredPos, t);
            }
            UpdateTimerLabel(Mathf.Max(0f, duration - elapsed));

            if (elapsed >= duration)
            {
                Finish("timeout", sfxOnTimeout);
            }
        }

        private void OnDestroy()
        {
            if (rightButton != null) rightButton.onClick.RemoveListener(OnRightClicked);
            if (wrongButton != null) wrongButton.onClick.RemoveListener(OnWrongClicked);
        }

        // ───────── 输入回调 ─────────

        private void OnRightClicked()
        {
            if (finished) return;
            Finish("success", sfxOnSuccess);
        }

        private void OnWrongClicked()
        {
            if (finished) return;
            Finish("wrong", sfxOnWrong);
        }

        // ───────── 完结 ─────────

        private void Finish(string result, string sfx)
        {
            finished = true;
            PlaySfx(sfx);
            // 把结果写入 variables，供 lua 端 branch 用
            variables.Set("v_minigame_result", result);
            // 解除 lua wait_fence
            gameState.SignalFence(true);
        }

        // ───────── 工具 ─────────

        private void PlaySfx(string audioName)
        {
            if (string.IsNullOrEmpty(audioName) || soundController == null) return;
            soundController.PlayClip(audioName, sfxVolume, Vector3.zero, false);
        }

        private void UpdateTimerLabel(float secondsLeft)
        {
            if (tikNumText == null) return;
            // 显示成整数秒；不想取整可换成 secondsLeft.ToString("0.0")
            tikNumText.text = Mathf.CeilToInt(secondsLeft).ToString();
        }
    }
}
