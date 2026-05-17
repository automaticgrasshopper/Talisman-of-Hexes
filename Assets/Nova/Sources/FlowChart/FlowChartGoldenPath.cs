using System.Collections.Generic;

namespace Nova
{
    /// <summary>
    /// 存于 globalSave.data["flowchart.goldenpath"]，记录每个 slot 的最近访问时间戳。
    /// 用于在 ChapterSelect 流程图上画"当前分支"金色线：
    ///   走金线时自起点前进，在每个分支挑该列已访问的最新非死亡 slot。
    /// 玩家自然游玩进入 slot 或在流程图点击 Visited slot 跳转，都会刷新对应 slot 的时间戳。
    /// </summary>
    public class FlowChartGoldenData : ISerializedData
    {
        // slotKey 格式 "ChapterAssetName:slotId" → 最近访问 ticks（UTC）
        public Dictionary<string, long> visitTimes = new Dictionary<string, long>();

        public const string SaveKey = "flowchart.goldenpath";
    }
}
