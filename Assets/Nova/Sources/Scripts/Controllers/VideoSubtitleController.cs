using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Video;

namespace Nova
{
    public class VideoSubtitleController : MonoBehaviour
    {
        private struct Entry
        {
            public double from;
            public double to;
            public string text;
        }

        private TextMeshProUGUI tmp;
        private VideoPlayer videoPlayer;
        private readonly List<Entry> entries = new List<Entry>();
        private string lastText;

        // Lazy lookup: LogController panel is inactive before first show, and might not exist
        // during early scene boot. Cache once found to avoid per-frame FindObjectOfType cost.
        private LogController logController;
        private bool logSearched;

        private LogController GetLogController()
        {
            if (!logSearched)
            {
                logSearched = true;
                logController = FindObjectOfType<LogController>(true);
            }
            return logController;
        }

        public void Initialize(VideoPlayer vp)
        {
            videoPlayer = vp;
            tmp = GetComponent<TextMeshProUGUI>();
            tmp.raycastTarget = false;
            tmp.text = "";
            tmp.enabled = false;
        }

        public void AddEntry(double from, double to, string text)
        {
            entries.Add(new Entry { from = from, to = to, text = text });
        }

        public void Clear()
        {
            entries.Clear();
            lastText = null;
            if (tmp != null) { tmp.text = ""; tmp.enabled = false; }
        }

        private void Update()
        {
            if (videoPlayer == null || tmp == null) return;

            if (!videoPlayer.isPlaying)
            {
                if (tmp.enabled) tmp.enabled = false;
                return;
            }

            double t = videoPlayer.time;
            string current = null;
            foreach (var e in entries)
            {
                if (t >= e.from && t <= e.to)
                {
                    current = e.text;
                    break;
                }
            }

            if (current != null)
            {
                if (!tmp.enabled) tmp.enabled = true;
                if (current != lastText)
                {
                    tmp.text = current;
                    lastText = current;
                    // Push to log when subtitle becomes visible. Interleaves naturally
                    // with dialogue entries in chronological order.
                    GetLogController()?.AddSubtitleEntry(current);
                }
            }
            else
            {
                if (tmp.enabled) tmp.enabled = false;
                lastText = null;
            }
        }
    }
}
