using System;
using System.Collections;
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
        public float volume;

        public string currentVideoName { get; private set; }

        private GameState gameState;
        private VideoPlayer videoPlayer;
        private VideoClip loadedClip;

        // RenderTexture display
        private RenderTexture renderTexture;
        private RawImage rawImage;
        private AudioSource audioSource;
        private GameObject videoCanvas;
        private bool playRequested = false;
        private bool isVideoActive = false;
        private VideoSubtitleController subtitleController;

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
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1;

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
                var skipGO = Instantiate(skipButtonPrefab, videoCanvas.transform, false);
                var skipRT = skipGO.GetComponent<RectTransform>();
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
