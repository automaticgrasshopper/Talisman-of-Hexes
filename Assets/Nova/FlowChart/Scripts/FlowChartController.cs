using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Nova
{
    /// <summary>
    /// 流程图主控制器。挂在 FlowChartView 根节点上。
    /// 通过 ViewManager 注册，调用 Show() / Hide() 打开关闭。
    /// </summary>
    public class FlowChartController : ViewControllerBase, IDeathRouteHandler
    {
        [Header("数据")]
        [SerializeField] private FlowChartDatabase database;

        [Header("UI 引用")]
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private RectTransform content;
        [SerializeField] private Button backButton;
        [SerializeField] private Button locateButton;
        [SerializeField] private TMPro.TMP_Text chapterNameText; // 顶部章节名（可选）

        [Header("Prefab")]
        [SerializeField] private GameObject slotPrefab;

        [Header("缩放（由 FlowChartZoomController 填入）")]
        public FlowChartZoomController zoomController;

        [Header("BGM（可空 = 不播）")]
        [SerializeField] private AudioController bgmController;
        [SerializeField] private string bgmName;
        [SerializeField] private float bgmVolume = 0.5f;
        [SerializeField] private float bgmFadeOutDuration = 1.0f;

        // ── 运行时状态 ──────────────────────────────────────────────────────
        private GameState gameState;
        private CheckpointManager checkpointManager;
        private SaveViewController saveViewController;
        private VideoController videoController;
        private NovaAnimation novaAnimation;

        private readonly List<FlowChartSlotView> spawnedSlots = new List<FlowChartSlotView>();
        private readonly List<GameObject> spawnedLines = new List<GameObject>();
        private FlowChartSlotView currentSlotView;

        // ShowForChapter 时临时覆盖章节，null = 使用 gameState.currentNode 判断
        private FlowChartChapter _overrideChapter;

        // 死亡结局回流程图：override 当前节点名（让 currentSlot 锁定到死亡节点）
        private string _overrideCurrentNodeName;

        // 当前章节的金色路径 slot 集合 + slot→next 边集合（仅在 BuildLines 内使用）
        private readonly HashSet<FlowChartSlot> _goldSlots = new HashSet<FlowChartSlot>();
        private readonly HashSet<(string from, string to)> _goldEdges = new HashSet<(string, string)>();

        // 跟踪上一帧的 slot（用于检测 slot 切换并追加到当前会话）
        private string _lastTrackedNodeName;
        private FlowChartSlot _lastTrackedSlot;
        private FlowChartChapter _lastTrackedChapter;

        // Content 在 zoom=100% 时的 sizeDelta，布局完成后记录
        private Vector2 nominalContentSize;

        private Coroutine bgmFadeCo;

        protected override void Awake()
        {
            base.Awake();

            var ctrl = Utils.FindNovaController();
            gameState = ctrl.GameState;
            checkpointManager = ctrl.CheckpointManager;
            saveViewController = viewManager.GetController<SaveViewController>();
            novaAnimation = ctrl.UIAnimation;

            database.BuildNodeMap();
            videoController = FindObjectOfType<VideoController>();

            backButton.onClick.AddListener(OnBackClicked);
            locateButton.onClick.AddListener(OnLocateClicked);

            // 注册为死亡路由处理者（FlowChartView 不在 ViewManager 子层级下，
            // 所以 GameViewController 走 viewManager.GetController 找不到我们，必须走静态注册表）
            DeathRouteRegistry.Current = this;

            // 监听节点变化，追加到当前金色路径会话
            gameState.nodeChanged.AddListener(OnNodeChangedForGold);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            backButton.onClick.RemoveListener(OnBackClicked);
            locateButton.onClick.RemoveListener(OnLocateClicked);

            if (gameState != null)
                gameState.nodeChanged.RemoveListener(OnNodeChangedForGold);

            if (DeathRouteRegistry.Current == (IDeathRouteHandler)this)
                DeathRouteRegistry.Current = null;
        }

        // ── Show / Hide ────────────────────────────────────────────────────
        public override void Show(bool doTransition, Action onFinish)
        {
            BuildFlowChart();
            zoomController?.ResetToDefault();
            CenterOnCurrentSlot(keepZoom: false);
            PlayBgm();
            base.Show(doTransition, onFinish);
        }

        public override void Hide(bool doTransition, Action onFinish)
        {
            FadeOutBgm();
            base.Hide(doTransition, onFinish);
        }

        protected override void OnHideFinish()
        {
            base.OnHideFinish();
            ClearSlots();
            // 关闭流程图时恢复视频（仅当 Back/Locate 关闭，跳节点时不走这里）
            videoController?.Resume();
        }

        private void PlayBgm()
        {
            // [DISABLED 2026-05-18] 暂时取消流程图音乐，保留代码方便后续恢复
            // if (bgmController == null || string.IsNullOrEmpty(bgmName)) return;
            // // 取消上一轮 FadeOut，避免它继续把音量拉到 0 然后 Stop
            // if (bgmFadeCo != null) { StopCoroutine(bgmFadeCo); bgmFadeCo = null; }
            // bgmController.scriptVolume = bgmVolume;
            // bgmController.Play(bgmName);
        }

        private void FadeOutBgm()
        {
            if (bgmController == null || string.IsNullOrEmpty(bgmName)) return;
            if (bgmFadeCo != null) StopCoroutine(bgmFadeCo);
            bgmFadeCo = StartCoroutine(FadeOutBgmRoutine());
        }

        private IEnumerator FadeOutBgmRoutine()
        {
            float startVol = bgmController.scriptVolume;
            float t = 0f;
            while (t < bgmFadeOutDuration)
            {
                t += Time.unscaledDeltaTime;
                bgmController.scriptVolume = Mathf.Lerp(startVol, 0f, Mathf.Clamp01(t / bgmFadeOutDuration));
                yield return null;
            }
            bgmController.scriptVolume = 0f;
            bgmController.Stop();
            bgmFadeCo = null;
        }

        // ── 构建流程图 ────────────────────────────────────────────────────
        private void BuildFlowChart()
        {
            ClearSlots();

            FlowChartChapter currentChapter;
            FlowChartSlot currentSlot;

            if (_overrideChapter != null)
            {
                // 从章节选择点入 / 死亡结局回流程图：以 _overrideChapter 为章节
                currentChapter = _overrideChapter;
                currentSlot = null;
                // _overrideCurrentNodeName 优先（死亡结局），否则用 gameState.currentNode（章节选择路径）
                var lookupName = _overrideCurrentNodeName
                                 ?? gameState.currentNode?.name;
                if (!string.IsNullOrEmpty(lookupName))
                    database.TryGetByNodeName(lookupName, out _, out currentSlot);
            }
            else
            {
                if (gameState.currentNode == null) return;
                if (!database.TryGetByNodeName(gameState.currentNode.name,
                        out currentChapter, out currentSlot))
                {
                    Debug.LogWarning($"[FlowChart] 当前节点 {gameState.currentNode.name} 不在 Database 里");
                    return;
                }
            }

            if (chapterNameText != null)
                chapterNameText.text = I18n.C(currentChapter.chapterNameKey);

            // 按 column 自动算出每个 slot 的 anchoredPosition
            var positions = ComputeSlotPositions(currentChapter);

            // ── 暂时注销：按到达列过滤（粗粒度，按 column 整列裁剪） ──────
            // 现规则按"每个 slot 自身的访问状态"裁剪（更精细），列过滤已不再需要。
            // 想恢复"按 column 渐进解锁"再放回来。
            // int maxReachedColumn = ComputeMaxReachedColumn(currentChapter, currentSlot);
            int maxReachedColumn = int.MaxValue;

            // 计算当前章节的金色路径（slot 集合 + 边集合）
            ComputeGoldenPath(currentChapter, currentSlot);

            foreach (var slot in currentChapter.slots)
            {
                // if (slot.column > maxReachedColumn) continue; // 暂时注销：按列粗过滤

                var state = GetSlotState(slot, currentSlot);
                // 未走过 → 不渲染（"选了 1_2_A 就只展示 1_2_A，不展示 1_2_B / 1_2_C"）
                if (state == SlotState.Unknown) continue;

                var go = Instantiate(slotPrefab, content);
                var rt = go.GetComponent<RectTransform>();
                rt.anchoredPosition = positions[slot];

                var view = go.GetComponent<FlowChartSlotView>();
                view.Init(slot, state.Value, database, this);

                spawnedSlots.Add(view);
                if (state == SlotState.Current) currentSlotView = view;
            }

            // 连线（slot 实例化完成后再画，确保位置已定）
            BuildLines(currentChapter, positions, maxReachedColumn);

            // 记录内容区原始大小，供缩放使用
            Canvas.ForceUpdateCanvases();
            nominalContentSize = content.sizeDelta;
            if (zoomController != null) zoomController.NominalContentSize = nominalContentSize;
        }

        private void ClearSlots()
        {
            foreach (var sv in spawnedSlots)
                if (sv != null) Destroy(sv.gameObject);
            spawnedSlots.Clear();

            foreach (var go in spawnedLines)
                if (go != null) Destroy(go);
            spawnedLines.Clear();

            currentSlotView = null;
            _overrideChapter = null;
            _overrideCurrentNodeName = null;
        }

        // ── 连线生成 ──────────────────────────────────────────────────────
        // 两遍构建保证渲染层级正确：
        //   sibling 0 = 最底层，最后 sibling = 最顶层；slot 已先于 BuildLines 创建。
        //   每次 SetAsFirstSibling 把新线压到底，先建的线会被后建的挤到更高 sibling。
        //   ⇒ 先建金线（被白线挤下去）、再建白线（沉到最底）
        //     最终层级（自下而上）：白线 → 金线 → slot，金线永远盖住白线、不会遮 slot。
        private void BuildLines(FlowChartChapter chapter, Dictionary<FlowChartSlot, Vector2> positions, int maxReachedColumn)
        {
            // 锚点：以 slot 中心为基准，对同源/同目标多条边做小步长偏移分散视觉。
            // 出边偏移按目标 Y 排序、入边偏移按源 Y 排序 —— 避免线交叉。
            // 由于布局已让 source.Y 落在 target.Y 的中位数上，"指向中位数目标"那条线
            // 偏移恰好为 0，与目标中心连成纯水平直线。
            const float anchorStep = 10f;

            var edgeOutY = new Dictionary<(string, string), float>();
            var edgeInY = new Dictionary<(string, string), float>();

            // 出口偏移：每个源把出边按目标 Y 降序排，给等距偏移
            foreach (var s in chapter.slots)
            {
                if (s == null || s.nextSlotIds == null) continue;
                var nexts = new List<string>();
                foreach (var nid in s.nextSlotIds)
                    if (chapter.GetSlot(nid) != null) nexts.Add(nid);
                if (nexts.Count == 0) continue;

                nexts.Sort((a, b) => positions[chapter.GetSlot(b)].y.CompareTo(positions[chapter.GetSlot(a)].y));

                int n = nexts.Count;
                for (int i = 0; i < n; i++)
                {
                    float offset = ((n - 1) * 0.5f - i) * anchorStep;
                    edgeOutY[(s.slotId, nexts[i])] = positions[s].y + offset;
                }
            }

            // 入口偏移：每个目标把入边按源 Y 降序排，给等距偏移
            var sourcesByTarget = new Dictionary<FlowChartSlot, List<FlowChartSlot>>();
            foreach (var s in chapter.slots)
            {
                if (s == null || s.nextSlotIds == null) continue;
                foreach (var nid in s.nextSlotIds)
                {
                    var t = chapter.GetSlot(nid);
                    if (t == null) continue;
                    if (!sourcesByTarget.TryGetValue(t, out var list))
                    {
                        list = new List<FlowChartSlot>();
                        sourcesByTarget[t] = list;
                    }
                    list.Add(s);
                }
            }
            foreach (var kv in sourcesByTarget)
            {
                var t = kv.Key;
                var srcs = kv.Value;
                if (srcs.Count == 0) continue;

                srcs.Sort((a, b) => positions[b].y.CompareTo(positions[a].y));

                int n = srcs.Count;
                for (int i = 0; i < n; i++)
                {
                    float offset = ((n - 1) * 0.5f - i) * anchorStep;
                    edgeInY[(srcs[i].slotId, t.slotId)] = positions[t].y + offset;
                }
            }

            // 同 Y 边强制走中心：source.y == target.y 的边两端都用中心 Y，保证完美水平。
            // 其他多边的偏移不变，仍然分散。
            foreach (var s in chapter.slots)
            {
                if (s == null || s.nextSlotIds == null) continue;
                foreach (var nid in s.nextSlotIds)
                {
                    var t = chapter.GetSlot(nid);
                    if (t == null) continue;
                    if (Mathf.Abs(positions[s].y - positions[t].y) < 0.5f)
                    {
                        edgeOutY[(s.slotId, nid)] = positions[s].y;
                        edgeInY[(s.slotId, nid)] = positions[t].y;
                    }
                }
            }

            // Pass 1: 仅金线（CurrentBranch）
            EmitLines(chapter, positions, maxReachedColumn, edgeOutY, edgeInY, goldOnly: true);
            // Pass 2: 仅白线（Visited）。后建被压到 sibling 0，反而把金线挤到上层。
            EmitLines(chapter, positions, maxReachedColumn, edgeOutY, edgeInY, goldOnly: false);
        }

        private void EmitLines(FlowChartChapter chapter, Dictionary<FlowChartSlot, Vector2> positions,
            int maxReachedColumn,
            Dictionary<(string, string), float> edgeOutY,
            Dictionary<(string, string), float> edgeInY,
            bool goldOnly)
        {
            foreach (var slot in chapter.slots)
            {
                if (slot.column > maxReachedColumn) continue; // 起点在未到达列
                foreach (var nextId in slot.nextSlotIds)
                {
                    var nextSlot = chapter.GetSlot(nextId);
                    if (nextSlot == null) continue;
                    if (nextSlot.column > maxReachedColumn) continue; // 终点在未到达列

                    // 起止状态：任一为 Unknown → 整条线不渲染（"只展示走过的节点和线"）
                    var fromState = GetSlotStateRaw(slot);
                    var toState   = GetSlotStateRaw(nextSlot);
                    if (fromState == SlotState.Unknown || toState == SlotState.Unknown) continue;

                    bool isGold = _goldEdges.Contains((slot.slotId, nextId));
                    if (isGold != goldOnly) continue;

                    var lineState = isGold
                        ? FlowChartConnectionLine.LineState.CurrentBranch
                        : FlowChartConnectionLine.LineState.Visited;

                    var key = (slot.slotId, nextId);
                    float outY = edgeOutY.TryGetValue(key, out var oy) ? oy : positions[slot].y;
                    float inY  = edgeInY.TryGetValue(key, out var iy)  ? iy : positions[nextSlot].y;

                    Vector2 startPos = new Vector2(positions[slot].x, outY);
                    Vector2 endPos   = new Vector2(positions[nextSlot].x, inY);

                    var lineGo = new GameObject($"Line_{slot.slotId}->{nextId}");
                    lineGo.transform.SetParent(content, false);
                    lineGo.transform.SetAsFirstSibling();

                    var line = lineGo.AddComponent<FlowChartConnectionLine>();
                    line.lineWidth = database.lineWidth;
                    line.cornerRadius = database.lineCornerRadius;
                    line.SetLine(
                        startPos,
                        endPos,
                        lineState,
                        database.lineCurrentBranchMaterial,
                        database.lineActiveMaterial,
                        database.lineLockedMaterial);

                    spawnedLines.Add(lineGo);
                }
            }
        }

        // ── 计算玩家在当前章节到达的最大列号 ──────────────────────────────
        // 当前 slot 或任一 history 命中 slot 的 nodeName 视为「已到达」
        private int ComputeMaxReachedColumn(FlowChartChapter chapter, FlowChartSlot currentSlot)
        {
            int maxCol = -1;
            foreach (var slot in chapter.slots)
            {
                bool reached = (currentSlot != null && slot == currentSlot);
                if (!reached)
                {
                    foreach (var nodeName in slot.nodeNames)
                    {
                        if (checkpointManager.IsReachedAnyHistory(nodeName, 0))
                        {
                            reached = true;
                            break;
                        }
                    }
                }
                if (reached && slot.column > maxCol) maxCol = slot.column;
            }
            return maxCol;
        }

        // ── 自动布局：按 column 计算每个 slot 的 anchoredPosition ─────────
        // 规则：
        //   x = (column - maxColumn/2) * columnSpacing      → 整体水平居中于 (0,0)
        //   y = ((countInColumn-1)/2 - indexInColumn) * rowSpacing → 各列 y=0 中点对齐
        // 同列内的上下顺序 = slot 在 chapter.slots 列表中的相对顺序（先出现的在上）
        private Dictionary<FlowChartSlot, Vector2> ComputeSlotPositions(FlowChartChapter chapter)
        {
            var result = new Dictionary<FlowChartSlot, Vector2>();
            if (chapter == null || chapter.slots == null || chapter.slots.Count == 0)
                return result;

            // 按 column 分组，保持 chapter.slots 中的相对顺序
            var groups = new Dictionary<int, List<FlowChartSlot>>();
            int maxColumn = 0;
            foreach (var s in chapter.slots)
            {
                if (!groups.TryGetValue(s.column, out var list))
                {
                    list = new List<FlowChartSlot>();
                    groups[s.column] = list;
                }
                list.Add(s);
                if (s.column > maxColumn) maxColumn = s.column;
            }

            float colSpacing = database.columnSpacing;
            float rowSpacing = database.rowSpacing;
            float halfMaxColumn = maxColumn * 0.5f;

            // 从右向左反向布局：
            //   最右列 = 均匀分布（基准）
            //   其余列 = 每个 slot 落到 "出边目标 Y 的中位数"
            //           若整列的理想顺序与 chapter.slots 顺序冲突，整列退化为均匀分布
            for (int col = maxColumn; col >= 0; col--)
            {
                if (!groups.TryGetValue(col, out var list)) continue;
                int count = list.Count;
                float x = (col - halfMaxColumn) * colSpacing;
                float halfCount = (count - 1) * 0.5f;

                if (col == maxColumn)
                {
                    // 最右列：均匀分布
                    for (int i = 0; i < count; i++)
                        result[list[i]] = new Vector2(x, (halfCount - i) * rowSpacing);
                    continue;
                }

                // 算每个 slot 的"理想 Y"
                var idealY = new float[count];
                for (int i = 0; i < count; i++)
                {
                    var s = list[i];
                    var ys = new List<float>();
                    if (s.nextSlotIds != null)
                    {
                        foreach (var nid in s.nextSlotIds)
                        {
                            var t = chapter.GetSlot(nid);
                            if (t != null && result.TryGetValue(t, out var pos)) ys.Add(pos.y);
                        }
                    }
                    if (ys.Count > 0)
                    {
                        ys.Sort();
                        int mid = ys.Count / 2;
                        idealY[i] = (ys.Count % 2 == 1) ? ys[mid] : (ys[mid - 1] + ys[mid]) * 0.5f;
                    }
                    else
                    {
                        // 无出边（死亡节点）→ 用均匀分布的 Y 作 fallback
                        idealY[i] = (halfCount - i) * rowSpacing;
                    }
                }

                // 一致性检查：chapter.slots 顺序里靠前的 slot 理想 Y 应不小于靠后的
                bool consistent = true;
                float lastY = float.PositiveInfinity;
                for (int i = 0; i < count; i++)
                {
                    if (idealY[i] > lastY) { consistent = false; break; }
                    lastY = idealY[i];
                }

                if (consistent)
                {
                    // 用理想 Y，但相邻最小间距 = rowSpacing（避免重叠）
                    // 不做整列居中——必须保持与右侧 target Y 对齐，否则横线对不上。
                    float prevY = float.PositiveInfinity;
                    for (int i = 0; i < count; i++)
                    {
                        float y = (i == 0) ? idealY[i] : Mathf.Min(idealY[i], prevY - rowSpacing);
                        result[list[i]] = new Vector2(x, y);
                        prevY = y;
                    }
                }
                else
                {
                    // 冲突 → 均匀分布
                    for (int i = 0; i < count; i++)
                        result[list[i]] = new Vector2(x, (halfCount - i) * rowSpacing);
                }
            }

            return result;
        }

        /// <summary>不依赖 currentSlot 引用的状态判定（给连线用）</summary>
        private SlotState GetSlotStateRaw(FlowChartSlot slot)
        {
            if (currentSlotView != null && currentSlotView.SlotData == slot)
                return SlotState.Current;
            foreach (var nodeName in slot.nodeNames)
                if (checkpointManager.IsReachedAnyHistory(nodeName, 0))
                    return SlotState.Visited;
            return SlotState.Unknown;
        }

        // ── Slot 状态判定 ────────────────────────────────────────────────
        private SlotState? GetSlotState(FlowChartSlot slot, FlowChartSlot currentSlot)
        {
            if (currentSlot != null && slot == currentSlot) return SlotState.Current;

            // 任意一个 nodeName 被 history 记录过 → Visited
            foreach (var nodeName in slot.nodeNames)
            {
                if (checkpointManager.IsReachedAnyHistory(nodeName, 0))
                    return SlotState.Visited;
            }

            // 检查是否可达：只要是 currentChapter 的 slots 里就渲染为 Unknown
            // （不可达 slot 需要你自己不放进 chapter.slots，暂不做图遍历）
            return SlotState.Unknown;
        }

        // ── 金色当前分支：每 slot 时间戳 + 前进式路径推导 ─────────────────
        // 规则：每个 slot 记一个"最近访问时间"，自然游玩进入 slot 或在流程图
        // 点击 Visited slot 跳转都会刷新该 slot 的时间戳。
        // 画金色路径：自章节起点前进，在每个分支挑该 slot.nextSlotIds 中
        // 已访问的、时间戳最新的下游。死亡 slot（小结局）与正常 leaf
        // （如 1_5_A/1_5_B）一样都是合法终点，无特殊处理 —— 走到没有
        // 已访问的下游时自然停止。
        //
        // slotKey 格式: "ChapterAssetName:slotId"，跨章节不冲突。
        private static string SlotKey(FlowChartChapter chapter, FlowChartSlot slot) =>
            chapter != null && slot != null ? chapter.name + ":" + slot.slotId : null;

        private FlowChartGoldenData LoadGoldenData()
        {
            var data = checkpointManager.Get<FlowChartGoldenData>(FlowChartGoldenData.SaveKey, null);
            if (data == null)
            {
                data = new FlowChartGoldenData();
                checkpointManager.Set(FlowChartGoldenData.SaveKey, data);
            }
            return data;
        }

        // 由 GameState.nodeChanged 调用：检测 slot 变化，刷新该 slot 的时间戳
        private void OnNodeChangedForGold(NodeChangedData ev)
        {
            if (database == null || ev == null || string.IsNullOrEmpty(ev.newNode)) return;
            if (!database.TryGetByNodeName(ev.newNode, out var chapter, out var slot)) return;
            if (slot == null) return;

            // 同 slot 内多个 node 切换：不刷新时间戳（避免单 slot 内来回 node 拉时间）
            if (slot == _lastTrackedSlot && chapter == _lastTrackedChapter)
            {
                _lastTrackedNodeName = ev.newNode;
                return;
            }

            RefreshSlotVisitTime(chapter, slot);

            _lastTrackedNodeName = ev.newNode;
            _lastTrackedSlot = slot;
            _lastTrackedChapter = chapter;
        }

        // 把 slot 的最近访问时间刷成"现在"
        private void RefreshSlotVisitTime(FlowChartChapter chapter, FlowChartSlot slot)
        {
            if (chapter == null || slot == null) return;
            var data = LoadGoldenData();
            data.visitTimes[SlotKey(chapter, slot)] = System.DateTime.UtcNow.Ticks;
            checkpointManager.Set(FlowChartGoldenData.SaveKey, data);
        }

        // 计算当前章节的金色路径 slot/edge 集合
        // 反向回溯：从 currentSlot 沿父节点向起点走，每步选时间戳最大的已访问父节点。
        // 这样金线必然经过当前位置，不会被死亡支线的较新时间戳吸走。
        private void ComputeGoldenPath(FlowChartChapter chapter, FlowChartSlot currentSlot)
        {
            _goldSlots.Clear();
            _goldEdges.Clear();
            if (chapter == null || string.IsNullOrEmpty(chapter.firstSlotId)) return;
            if (currentSlot == null) return;

            var data = checkpointManager.Get<FlowChartGoldenData>(FlowChartGoldenData.SaveKey, null);
            if (data == null || data.visitTimes.Count == 0) return;

            // currentSlot 必须已经走过才高亮（避免从一个未访问 slot 起步）
            if (GetSlotStateRaw(currentSlot) == SlotState.Unknown) return;

            // 构建反向邻接表：child -> [parents...]
            var parents = new Dictionary<FlowChartSlot, List<FlowChartSlot>>();
            foreach (var s in chapter.slots)
            {
                if (s == null || s.nextSlotIds == null) continue;
                foreach (var nextId in s.nextSlotIds)
                {
                    var nextSlot = chapter.GetSlot(nextId);
                    if (nextSlot == null) continue;
                    if (!parents.TryGetValue(nextSlot, out var list))
                    {
                        list = new List<FlowChartSlot>();
                        parents[nextSlot] = list;
                    }
                    list.Add(s);
                }
            }

            var visited = new HashSet<FlowChartSlot>();
            var current = currentSlot;
            _goldSlots.Add(current);

            while (current != null && !visited.Contains(current))
            {
                visited.Add(current);

                if (!parents.TryGetValue(current, out var parentList) || parentList.Count == 0)
                    break;

                FlowChartSlot pickedParent = null;
                long bestTs = long.MinValue;
                foreach (var p in parentList)
                {
                    if (!data.visitTimes.TryGetValue(SlotKey(chapter, p), out var ts)) continue;
                    if (ts > bestTs)
                    {
                        bestTs = ts;
                        pickedParent = p;
                    }
                }

                if (pickedParent == null) break;
                _goldEdges.Add((pickedParent.slotId, current.slotId));
                _goldSlots.Add(pickedParent);
                current = pickedParent;
            }

            // 正向延伸：从 currentSlot 顺着已访问的子节点继续走到最远端，
            // 每步在已访问且非死亡的子节点里挑时间戳最大的，直到没得走。
            var forwardVisited = new HashSet<FlowChartSlot>(visited);
            var forward = currentSlot;
            while (forward != null && forward.nextSlotIds != null)
            {
                FlowChartSlot pickedNext = null;
                long bestTs = long.MinValue;
                foreach (var nextId in forward.nextSlotIds)
                {
                    var nextSlot = chapter.GetSlot(nextId);
                    if (nextSlot == null) continue;
                    if (nextSlot.isDeath) continue;
                    if (forwardVisited.Contains(nextSlot)) continue;
                    if (!data.visitTimes.TryGetValue(SlotKey(chapter, nextSlot), out var ts)) continue;
                    if (ts > bestTs)
                    {
                        bestTs = ts;
                        pickedNext = nextSlot;
                    }
                }

                if (pickedNext == null) break;
                _goldEdges.Add((forward.slotId, pickedNext.slotId));
                _goldSlots.Add(pickedNext);
                forwardVisited.Add(pickedNext);
                forward = pickedNext;
            }
        }

        // ── Slot 音效（由 SlotView 调用） ─────────────────────────────────
        public void PlaySlotHoverSound()
        {
            if (database != null && database.slotHoverSound != null && viewManager != null)
                viewManager.TryPlaySound(database.slotHoverSound);
        }

        public void PlaySlotClickSound()
        {
            if (database != null && database.slotClickSound != null && viewManager != null)
                viewManager.TryPlaySound(database.slotClickSound);
        }

        // ── 点击 Slot ─────────────────────────────────────────────────────
        public void OnSlotClicked(FlowChartSlotView view)
        {
            if (view.State == SlotState.Current)
            {
                // 关闭流程图即可，OnHideFinish 会 Resume 视频
                Hide();
            }
            else if (view.State == SlotState.Visited)
            {
                Alert.Show(
                    null,
                    "flowchart.moveback.confirm",
                    () =>
                    {
                        var firstNode = view.SlotData.nodeNames[0];
                        // 在跳转前刷新该 slot 的访问时间戳：让金色路径立即在该 slot 所在列切到这个分支
                        if (database != null && database.TryGetByNodeName(firstNode, out var jumpChapter, out var jumpSlot))
                            RefreshSlotVisitTime(jumpChapter, jumpSlot);

                        Hide(true, () =>
                        {
                            viewManager.GetController<TitleController>()?.ScheduleBgmFadeOut();
                            viewManager.SwitchView<FlowChartController, GameViewController>(
                                () => gameState.GameStart(firstNode));
                        });
                    },
                    null);
            }
        }

        private void LoadLatestAutoSave()
        {
            var saveID = checkpointManager.QuerySaveIDByTime(
                (int)BookmarkType.AutoSave,
                (int)BookmarkType.AutoSave + 100,
                SaveIDQueryType.Latest);

            var bookmark = checkpointManager[saveID];
            if (bookmark == null) return;

            gameState.LoadBookmark(bookmark);
            saveViewController?.bookmarkLoaded?.Invoke();
        }

        // ── 按钮回调 ──────────────────────────────────────────────────────
        private void OnBackClicked()
        {
            var chapterSelect = viewManager.GetController<ChapterSelectViewController>();
            chapterSelect.skipAutoStart = true;

            // 从章节选择返回时：直接回到标题界面（不再返回流程图）
            chapterSelect.onReturn = () =>
            {
                var gameView = viewManager.GetController<GameViewController>();
                if (gameView != null) gameView.HideImmediate();
                var title = viewManager.GetController<TitleController>();
                if (title != null) title.Show();
            };

            // 点击章节时：打开对应章节的流程图
            chapterSelect.onChapterClick = (index) =>
            {
                if (index < 0 || index >= database.chapters.Count) return;
                viewManager.GetController<GameViewController>().ShowImmediate();
                ShowForChapter(database.chapters[index]);
            };

            Hide(true, () =>
            {
                viewManager.SwitchView<GameViewController, ChapterSelectViewController>();
            });
        }

        /// <summary>强制以指定章节重建流程图并显示（不依赖 gameState.currentNode）</summary>
        public void ShowForChapter(FlowChartChapter chapter)
        {
            _overrideChapter = chapter;
            Show(true, null);
        }

        // ── 死亡结局接入 ──────────────────────────────────────────────────
        /// <summary>该节点对应的 slot 是否标记为死亡结局</summary>
        public bool IsDeathNode(string nodeName)
        {
            if (database == null || string.IsNullOrEmpty(nodeName)) return false;
            if (!database.TryGetByNodeName(nodeName, out _, out var slot)) return false;
            return slot != null && slot.isDeath;
        }

        /// <summary>
        /// 玩家在死亡结局节点 routeEnded 时调用：把流程图打开到该章节，currentSlot 锁定到死亡节点。
        /// 由 GameViewController 通过 SwitchView 切过来前先调一次设置 override。
        /// </summary>
        public void PrepareDeathEnding(string deathNodeName)
        {
            if (database == null) return;
            if (!database.TryGetByNodeName(deathNodeName, out var chapter, out _)) return;
            _overrideChapter = chapter;
            _overrideCurrentNodeName = deathNodeName;
        }

        private void OnLocateClicked()
        {
            StartCoroutine(SmoothCenterOnCurrentSlot());
        }

        // ── 居中当前 Slot ─────────────────────────────────────────────────
        private void CenterOnCurrentSlot(bool keepZoom)
        {
            if (currentSlotView == null) return;
            var target = CalcNormalizedPosition(keepZoom);
            scrollRect.normalizedPosition = target;
        }

        private IEnumerator SmoothCenterOnCurrentSlot()
        {
            if (currentSlotView == null) yield break;
            var target = CalcNormalizedPosition(keepZoom: true);
            var duration = 0.35f;
            var elapsed = 0f;
            var start = scrollRect.normalizedPosition;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                scrollRect.normalizedPosition = Vector2.Lerp(start, target, Mathf.SmoothStep(0f, 1f, elapsed / duration));
                yield return null;
            }
            scrollRect.normalizedPosition = target;
        }

        private Vector2 CalcNormalizedPosition(bool keepZoom)
        {
            var viewportRect = scrollRect.viewport.rect;
            var slotLocalPos = (Vector2)content.InverseTransformPoint(
                currentSlotView.GetComponent<RectTransform>().position);

            var zoom = keepZoom ? (zoomController != null ? zoomController.CurrentZoom : 1f) : 1f;
            var contentSize = nominalContentSize * zoom;
            var halfViewport = viewportRect.size * 0.5f;

            var normalizedX = Mathf.Clamp01(
                (slotLocalPos.x * zoom + contentSize.x * 0.5f - halfViewport.x)
                / Mathf.Max(contentSize.x - viewportRect.width, 1f));
            var normalizedY = Mathf.Clamp01(
                (slotLocalPos.y * zoom + contentSize.y * 0.5f - halfViewport.y)
                / Mathf.Max(contentSize.y - viewportRect.height, 1f));

            return new Vector2(normalizedX, normalizedY);
        }
    }
}
