using UnityEngine;

namespace Nova
{
    /// <summary>
    /// 全局（跨存档）已看视频记录。基于 PlayerPrefs，按视频名（Resources 路径）独立打勾。
    /// 只在视频自然播放完毕时调用 <see cref="MarkWatched"/>；Skip / Pause / 中途离开都不记入。
    /// </summary>
    public static class VideoWatchedTracker
    {
        private const string KeyPrefix = "Nova.VideoWatched.";

        public static bool IsWatched(string videoName)
        {
            if (string.IsNullOrEmpty(videoName)) return false;
            return PlayerPrefs.GetInt(KeyPrefix + videoName, 0) != 0;
        }

        public static void MarkWatched(string videoName)
        {
            if (string.IsNullOrEmpty(videoName)) return;
            PlayerPrefs.SetInt(KeyPrefix + videoName, 1);
            PlayerPrefs.Save();
        }
    }
}
