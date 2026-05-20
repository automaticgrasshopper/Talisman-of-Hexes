using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace Nova
{
    [ExportCustomType]
    [RequireComponent(typeof(VideoPlayer))]
    public class VideoController : MonoBehaviour, IRestorable
    {
        [SerializeField] private string luaName;
        [SerializeField] private string videoFolder; // unused at runtime; kept for Inspector reference
        [SerializeField] private GameObject skipButtonPrefab;
        [SerializeField] private GameObject subtitlePrefab;
        [SerializeField] private GameObject timeSliderPrefab;
        [SerializeField] private GameObject videoButtonPrefab;
        public float volume;

        // 默认覆盖 UI 之上；调 SetCanvasOrder(-1) 可以让视频垫到 UI 底下，
        // 这样在循环 / 限时视频里 branch 按钮和倒计时条会浮在视频上方。
        private const int DefaultCanvasSortingOrder = 1;

        public string currentVideoName { get; private set; }

        private GameState gameState;
        private VideoPlayer videoPlayer;
        private VideoClip loadedClip;

        // RenderTexture display
        private RenderTexture renderTexture;
        private RawImage rawImage;
        private AudioSource audioSource;
        private GameObject videoCanvas;
        private Canvas videoCanvasComp;
        private bool playRequested = false;
        private bool isVideoActive = false;
        private VideoSubtitleController subtitleController;

        // 选项倒计时器（限时选项视频模式用）
        private GameObject timerSliderInstance;
        private Coroutine timerCo;

        // 视频选项模式：把选项按钮寄到这个 prefab 实例下，让按钮和视频同处一个 Canvas。
        private GameObject videoButtonInstance;

        // 缓存 SkipButton：选项视频是玩家必经的判定环节，进 EnableVideoChoiceMode 时要隐藏。
        private GameObject skipButtonInstance;

        public double duration => videoPlayer.clip != null ? videoPlayer.clip.length : 0;
        public bool isPlaying => videoPlayer.isPlaying;

        /// <summary>视频是否正在播放期内（区别于 isPlaying：暂停时仍为 true）</summary>
        public bool IsVideoActive => isVideoActive;

        /// <summary>当前视频是否已完整看完（跨存档）。无视频时返回 false。</summary>
        public bool IsCurrentVideoWatched =>
            !string.IsNullOrEmpty(currentVideoName) && VideoWatchedTracker.IsWatched(currentVideoName);

        /// <summary>侧边栏 FF 按钮按住时调用：临时拉高视频速度</summary>
        public void SetPlaybackSpeed(float speed)
        {
            if (videoPlayer != null) videoPlayer.playbackSpeed = speed;
        }

        private void Awake()
        {
            gameState = Utils.FindNovaController().GameState;
            videoPlayer = GetComponent<VideoPlayer>();
            videoPlayer.errorReceived += OnError;
            videoPlayer.prepareCompleted += OnPrepareCompleted;
            videoPlayer.loopPointReached += OnVideoFinished;

            // Switch to RenderTexture mode
            videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            videoPlayer.aspectRatio = VideoAspectRatio.FitInside;
            videoPlayer.playOnAwake = false;
            videoPlayer.waitForFirstFrame = true;

            // Set up AudioSource for audio routing
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            videoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
            videoPlayer.SetTargetAudioSource(0, audioSource);

            // Create fullscreen Canvas + RawImage overlay
            CreateVideoDisplay();

            if (!string.IsNullOrEmpty(luaName))
            {
                LuaRuntime.Instance.BindObject(luaName, this);
                gameState.AddRestorable(this);
            }
        }

        private void CreateVideoDisplay()
        {
            videoCanvas = new GameObject("VideoCanvas");
            var canvas = videoCanvas.AddComponent<Canvas>();

            // 借用主 UI 的 render mode / camera / planeDistance，
            // 否则 Overlay 模式会永远盖在 Camera 模式的 ChoicePanel 上，sortingOrder 失效。
            var refCanvas = Utils.FindNovaController()?.GetComponentInChildren<Canvas>(true);
            if (refCanvas != null && refCanvas.renderMode == RenderMode.ScreenSpaceCamera)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = refCanvas.worldCamera;
                canvas.planeDistance = refCanvas.planeDistance;
                canvas.sortingLayerID = refCanvas.sortingLayerID;
            }
            else
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }
            canvas.sortingOrder = DefaultCanvasSortingOrder;
            videoCanvasComp = canvas;

            var scaler = videoCanvas.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            // GraphicRaycaster is required for any UI element in this canvas to receive pointer events
            videoCanvas.AddComponent<GraphicRaycaster>();

            var displayGO = new GameObject("VideoDisplay");
            displayGO.transform.SetParent(videoCanvas.transform, false);
            rawImage = displayGO.AddComponent<RawImage>();
            rawImage.raycastTarget = false; // display only, should not block clicks

            var rt = displayGO.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            if (skipButtonPrefab != null)
            {
                skipButtonInstance = Instantiate(skipButtonPrefab, videoCanvas.transform, false);
                var skipRT = skipButtonInstance.GetComponent<RectTransform>();
                skipRT.anchorMin = new Vector2(1, 1);
                skipRT.anchorMax = new Vector2(1, 1);
                skipRT.pivot = new Vector2(1, 1);
                skipRT.anchoredPosition = new Vector2(-20, -20);
                // onClick wired by VideoSkipButton component on the prefab
            }

            if (subtitlePrefab != null)
            {
                var subtitleGO = Instantiate(subtitlePrefab, videoCanvas.transform, false);
                var subtitleRT = subtitleGO.GetComponent<RectTransform>();
                subtitleRT.anchorMin = new Vector2(0, 0);
                subtitleRT.anchorMax = new Vector2(1, 0);
                subtitleRT.pivot = new Vector2(0.5f, 0);
                subtitleRT.anchoredPosition = new Vector2(0, 60);
                subtitleRT.sizeDelta = new Vector2(0, 100);
                subtitleController = subtitleGO.AddComponent<VideoSubtitleController>();
                subtitleController.Initialize(videoPlayer);
            }

            videoCanvas.SetActive(false);
        }

        private void OnVideoFinished(VideoPlayer vp)
        {
            if (!isVideoActive) return;
            // isLooping 时 loopPointReached 仍会 fire，但视频会自动回到 0 继续播。
            // 这种"循环点"不算播完，不要标记已看 / 不要灭画布 / 不要 SignalFence。
            if (videoPlayer.isLooping) return;
            // 自然播放完毕才记录已看（Skip / Pause / 中途离开不在此处）
            VideoWatchedTracker.MarkWatched(currentVideoName);
            isVideoActive = false;
            if (videoCanvas != null)
            {
                videoCanvas.SetActive(false);
            }
            gameState.SignalFence(true);
            FindObjectOfType<GameViewController>()?.ScheduleImmediateStep();
        }

        public void Skip()
        {
            if (!isVideoActive) return;
            isVideoActive = false;
            videoPlayer.Stop();
            if (videoCanvas != null)
            {
                videoCanvas.SetActive(false);
            }
            gameState.SignalFence(true);
            FindObjectOfType<GameViewController>()?.ScheduleImmediateStep();
        }

        private void OnDestroy()
        {
            videoPlayer.errorReceived -= OnError;
            videoPlayer.prepareCompleted -= OnPrepareCompleted;
            videoPlayer.loopPointReached -= OnVideoFinished;
            StopChoiceTimer();
            ReleaseLoadedClip();
            ReleaseRenderTexture();

            if (videoCanvas != null)
            {
                Destroy(videoCanvas);
            }

            if (!string.IsNullOrEmpty(luaName))
            {
                gameState.RemoveRestorable(this);
            }
        }

        private void OnError(VideoPlayer player, string message)
        {
            Debug.LogWarning($"Nova VideoController: {message}");
        }

        private void ReleaseLoadedClip()
        {
            if (loadedClip != null)
            {
                Resources.UnloadAsset(loadedClip);
                loadedClip = null;
            }
        }

        private void ReleaseRenderTexture()
        {
            if (renderTexture != null)
            {
                renderTexture.Release();
                Destroy(renderTexture);
                renderTexture = null;
            }
        }

        private void OnPrepareCompleted(VideoPlayer vp)
        {
            ReleaseRenderTexture();
            renderTexture = new RenderTexture((int)vp.clip.width, (int)vp.clip.height, 24);
            renderTexture.Create();
            vp.targetTexture = renderTexture;
            if (rawImage != null)
            {
                rawImage.texture = renderTexture;
            }

            if (playRequested)
            {
                playRequested = false;
                DoPlay();
            }
        }

        private void DoPlay()
        {
            isVideoActive = true;
            videoPlayer.playbackSpeed = 1f; // 每次播放都恢复 1× 速度
            videoPlayer.Play();
            if (audioSource != null)
            {
                audioSource.volume = volume;
            }
            if (videoCanvas != null)
            {
                videoCanvas.SetActive(true);
            }
        }

        #region Methods called by external scripts

        public void SetVideo(string videoName)
        {
            isVideoActive = false;
            videoPlayer.Stop();
            playRequested = false;
            if (videoName == currentVideoName)
            {
                return;
            }

            // 换片即清字幕：旧条目若不清，下一个没调 video_subtitle_apply 的视频会继续显示旧字幕。
            if (subtitleController != null) subtitleController.Clear();

            ReleaseLoadedClip();
            currentVideoName = videoName;
            StartCoroutine(LoadVideoAsync(videoName));
        }

        private IEnumerator LoadVideoAsync(string videoName)
        {
            var request = Resources.LoadAsync<VideoClip>(videoName);
            yield return request;

            if (request.asset != null)
            {
                if (currentVideoName == videoName)
                {
                    loadedClip = request.asset as VideoClip;
                    videoPlayer.clip = loadedClip;
                    videoPlayer.Prepare();
                }
            }
            else
            {
                Debug.LogError($"Nova: Failed to load video from Resources: {videoName}");
            }
        }

        public void ClearVideo()
        {
            isVideoActive = false;
            videoPlayer.Stop();
            if (string.IsNullOrEmpty(currentVideoName))
            {
                return;
            }

            videoPlayer.clip = null;
            currentVideoName = null;
            playRequested = false;
            if (videoCanvas != null)
            {
                videoCanvas.SetActive(false);
            }
            if (rawImage != null)
            {
                rawImage.texture = null;
            }
            ReleaseRenderTexture();
            ReleaseLoadedClip();
        }

        public void Play()
        {
            if (videoPlayer.isPrepared)
            {
                DoPlay();
            }
            else
            {
                // Clip still loading/preparing — play as soon as ready
                playRequested = true;
            }
        }

        public void Pause()
        {
            if (isVideoActive && videoPlayer.isPlaying)
                videoPlayer.Pause();
        }

        public void Resume()
        {
            if (isVideoActive && !videoPlayer.isPlaying)
                videoPlayer.Play();
        }

        public void AddSubtitle(double from, double to, string text)
        {
            if (subtitleController != null)
                subtitleController.AddEntry(from, to, text);
        }

        public void ClearSubtitles()
        {
            if (subtitleController != null)
                subtitleController.Clear();
        }

        /// <summary>开关视频循环（选项循环视频模式）。设为 true 后 loopPointReached 不会被当作"播完"。</summary>
        public void SetLoop(bool looping)
        {
            videoPlayer.isLooping = looping;
        }

        /// <summary>把视频画布排到指定 sortingOrder。-1 = 垫到 UI 底下，让 branch 按钮 / TimeSlider 浮上去。</summary>
        public void SetCanvasOrder(int order)
        {
            if (videoCanvasComp != null) videoCanvasComp.sortingOrder = order;
        }

        /// <summary>恢复默认 sortingOrder（视频覆盖在 UI 之上）。</summary>
        public void ResetCanvasOrder()
        {
            SetCanvasOrder(DefaultCanvasSortingOrder);
        }

        /// <summary>开始一个限时选项的倒计时。倒计时归零时会自动选中 timeoutIndex 对应的 branch（含按钮清理）。</summary>
        public void StartChoiceTimer(double seconds, int timeoutIndex)
        {
            StopChoiceTimer();
            if (timeSliderPrefab == null)
            {
                Debug.LogWarning("VideoController: timeSliderPrefab is not assigned; cannot show TimeSlider.");
                return;
            }
            if (videoCanvas == null) return;
            // 兜底：与 EnableVideoChoiceMode 同理，TimeSlider 父节点必须 active 才能渲染。
            if (!videoCanvas.activeSelf)
            {
                videoCanvas.SetActive(true);
            }
            timerSliderInstance = Instantiate(timeSliderPrefab, videoCanvas.transform, false);
            timerCo = StartCoroutine(CoChoiceTimer((float)seconds, timeoutIndex));
        }

        /// <summary>进入"视频中显示选项"模式：实例化 VideoButton 容器到 videoCanvas 下，
        /// 并把 ChoicesController 的按钮父节点重定向到容器内的 ButtonCanvas。
        /// 之后 branch{} 触发时，选项按钮就出现在视频上方而不是被视频盖住。</summary>
        public void EnableVideoChoiceMode(int hiddenIndex = -1)
        {
            if (videoButtonInstance != null) return;
            if (videoButtonPrefab == null)
            {
                Debug.LogWarning("VideoController: videoButtonPrefab is not assigned; cannot enter video choice mode.");
                return;
            }
            if (videoCanvas == null) return;

            // 视频可能还在异步 Prepare → DoPlay 未执行，videoCanvas 仍是 inactive。
            // 这里强制激活，否则寄生其下的 ChoiceButton 的 Awake 不会跑，
            // PlayReveal 拿不到 matInstance，shader 卡在 _RevealProgress=0 → 按钮不可见。
            if (!videoCanvas.activeSelf)
            {
                videoCanvas.SetActive(true);
            }

            videoButtonInstance = Instantiate(videoButtonPrefab, videoCanvas.transform, false);
            var buttonContainer = videoButtonInstance.transform.Find("ButtonCanvas");
            if (buttonContainer == null)
            {
                Debug.LogWarning("VideoController: VideoButton prefab missing 'ButtonCanvas' child.");
                return;
            }

            var choicesCtrl = FindObjectOfType<ChoicesController>(true);
            if (choicesCtrl != null)
            {
                choicesCtrl.SetChoiceContainerOverride(buttonContainer, OnVideoChoiceConsumed, hiddenIndex);
            }

            // 循环视频 / 限时视频都是玩家必经的判定环节，不允许跳过。
            if (skipButtonInstance != null)
            {
                skipButtonInstance.SetActive(false);
            }
        }

        /// <summary>退出视频选项模式，拆掉 VideoButton 容器并解除 ChoicesController 的重定向。
        /// 选项被点选后 ChoicesController 会通过回调自动调到这里；玩家中途切场景也可以手动调。</summary>
        public void DisableVideoChoiceMode()
        {
            var choicesCtrl = FindObjectOfType<ChoicesController>(true);
            if (choicesCtrl != null)
            {
                choicesCtrl.ClearChoiceContainerOverride();
            }
            if (videoButtonInstance != null)
            {
                Destroy(videoButtonInstance);
                videoButtonInstance = null;
            }
            // 选项模式结束后恢复跳过按钮，下一个普通视频又能跳了。
            if (skipButtonInstance != null)
            {
                skipButtonInstance.SetActive(true);
            }
        }

        private void OnVideoChoiceConsumed()
        {
            DisableVideoChoiceMode();
        }

        /// <summary>外部主动停止倒计时（清掉 slider 但不 SignalFence）。</summary>
        public void StopChoiceTimer()
        {
            if (timerCo != null)
            {
                StopCoroutine(timerCo);
                timerCo = null;
            }
            if (timerSliderInstance != null)
            {
                Destroy(timerSliderInstance);
                timerSliderInstance = null;
            }
        }

        private IEnumerator CoChoiceTimer(float duration, int timeoutIndex)
        {
            var slider = timerSliderInstance.GetComponentInChildren<Slider>(true);
            var label = timerSliderInstance.GetComponentInChildren<TMP_Text>(true);
            if (slider != null)
            {
                slider.minValue = 0f;
                slider.maxValue = 1f;
                slider.value = 1f;
            }

            // 玩家点击 branch 按钮 → ChoicesController.Select 会清空按钮 (activeChoiceCount = 0)。
            // 我们检测到后就静默退场，不再 SignalFence。
            // 注意：start_choice_timer 通常在 branch{} 渲染前就调用了，开局 activeChoiceCount = 0 是正常的。
            // 必须先看到 choices 出现过一次，才把"归零"当作玩家已选。
            var choicesCtrl = FindObjectOfType<ChoicesController>(true);
            float remaining = duration;
            bool sawChoices = false;
            while (remaining > 0f)
            {
                if (choicesCtrl != null)
                {
                    if (choicesCtrl.activeChoiceCount > 0)
                    {
                        sawChoices = true;
                    }
                    else if (sawChoices)
                    {
                        // 玩家已经选了，悄悄收掉
                        timerCo = null;
                        if (timerSliderInstance != null)
                        {
                            Destroy(timerSliderInstance);
                            timerSliderInstance = null;
                        }
                        yield break;
                    }
                }
                if (slider != null) slider.value = Mathf.Clamp01(remaining / duration);
                if (label != null) label.text = Mathf.CeilToInt(remaining).ToString();
                yield return null;
                remaining -= Time.unscaledDeltaTime;
            }

            if (slider != null) slider.value = 0f;
            if (label != null) label.text = "0";

            // 倒计时归零：销毁 slider，然后让 ChoicesController.Select 走完按钮清理 + SignalFence
            timerCo = null;
            if (timerSliderInstance != null)
            {
                Destroy(timerSliderInstance);
                timerSliderInstance = null;
            }
            if (choicesCtrl != null)
            {
                // 不走 Select() —— 那条路径会把 index clamp 到可见按钮数（隐藏的 timeout 分支会被 clamp 掉）。
                // CancelAndSignal 直接拿原始 index 去 SignalFence，让 branch 路由到隐藏的 timeout dest。
                choicesCtrl.CancelAndSignal(timeoutIndex);
            }
            else
            {
                // 兜底：没有 ChoicesController 时直接 SignalFence
                gameState.SignalFence(timeoutIndex);
            }
        }

        #endregion

        #region Restoration

        public string restorableName => luaName;

        [Serializable]
        private class VideoControllerRestoreData : IRestoreData
        {
            public readonly string currentVideoName;

            public VideoControllerRestoreData(VideoController parent)
            {
                currentVideoName = parent.currentVideoName;
            }
        }

        public IRestoreData GetRestoreData()
        {
            return new VideoControllerRestoreData(this);
        }

        public void Restore(IRestoreData restoreData)
        {
            var data = restoreData as VideoControllerRestoreData;
            if (!string.IsNullOrEmpty(data.currentVideoName))
            {
                SetVideo(data.currentVideoName);
            }
            else
            {
                ClearVideo();
            }
        }

        #endregion
    }
}
