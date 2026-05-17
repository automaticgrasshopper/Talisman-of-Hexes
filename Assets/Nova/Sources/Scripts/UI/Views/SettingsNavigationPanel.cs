using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;

namespace Nova
{
    public class SettingsNavigationPanel : BaseNavigationPanel
    {
        [Header("设置界面特殊设置")]
        [SerializeField] private bool rememberToggleState = true;
        [SerializeField] private bool autoSetupNavigation = true;

        // 直接使用导航管理器的保护机制
        private GameObject currentSelectedObject;
        private GameObject lastSelectedObject;

        // 存储所有可交互元素
        private List<Selectable> allSelectables = new List<Selectable>();

        // 设置界面配置
        public override string PanelName => "Settings";
        public override int Priority => PanelPriority.SETTINGS;
        public override bool UseCustomNavigation => false;

        protected override void Start()
        {
            base.Start();
            CollectAllSelectables();
            SetupExplicitNavigation();

            // 初始化选中对象
            lastSelectedObject = defaultSelection;
        }

        /// <summary>
        /// 收集所有可交互元素并排序
        /// </summary>
        private void CollectAllSelectables()
        {
            allSelectables.Clear();

            // 收集所有类型的可交互元素
            var buttons = GetComponentsInChildren<Button>(true).Where(b => IsSelectableValid(b));
            var toggles = GetComponentsInChildren<Toggle>(true).Where(t => IsSelectableValid(t));
            var sliders = GetComponentsInChildren<Slider>(true).Where(s => IsSelectableValid(s));

            // 合并并排序
            allSelectables.AddRange(buttons.Cast<Selectable>());
            allSelectables.AddRange(toggles.Cast<Selectable>());
            allSelectables.AddRange(sliders.Cast<Selectable>());

            // 按Hierarchy顺序排序
            allSelectables = allSelectables
                .OrderBy(s => GetDepth(s.transform))
                .ThenBy(s => s.transform.GetSiblingIndex())
                .ToList();

            if (GamePadNavigationManager.Instance != null && GamePadNavigationManager.Instance.enableDebugLogs)
            {
                Debug.Log($"[SettingsNavigationPanel] 收集到 {allSelectables.Count} 个可交互元素");
            }
        }

        /// <summary>
        /// 设置显式导航（与原代码一致）
        /// </summary>
        private void SetupExplicitNavigation()
        {
            if (!autoSetupNavigation) return;

            for (int i = 0; i < allSelectables.Count; i++)
            {
                var current = allSelectables[i];
                if (current == null) continue;

                Navigation navigation = current.navigation;
                navigation.mode = Navigation.Mode.Explicit;

                // 设置上下导航（循环导航）
                if (allSelectables.Count > 1)
                {
                    int upIndex = i > 0 ? i - 1 : allSelectables.Count - 1;
                    int downIndex = i < allSelectables.Count - 1 ? i + 1 : 0;

                    navigation.selectOnUp = allSelectables[upIndex];
                    navigation.selectOnDown = allSelectables[downIndex];
                }
                else
                {
                    navigation.selectOnUp = null;
                    navigation.selectOnDown = null;
                }

                // 左右导航保持为空，使用默认行为
                navigation.selectOnLeft = null;
                navigation.selectOnRight = null;

                current.navigation = navigation;

                // 确保Toggle和Slider可以交互
                EnsureSelectableInteractable(current);
            }

            if (GamePadNavigationManager.Instance != null && GamePadNavigationManager.Instance.enableDebugLogs)
            {
                Debug.Log($"[SettingsNavigationPanel] 显式导航设置完成");
            }
        }

        /// <summary>
        /// 确保Selectable可交互（与原代码逻辑一致）
        /// </summary>
        private void EnsureSelectableInteractable(Selectable selectable)
        {
            if (selectable == null) return;

            // 对于Toggle和Slider，确保它们可以交互
            if ((selectable is Toggle || selectable is Slider) && !selectable.interactable)
            {
                selectable.interactable = true;
            }

            // 确保有颜色过渡
            if (selectable.transition == Selectable.Transition.None)
            {
                selectable.transition = Selectable.Transition.ColorTint;
            }
        }

        /// <summary>
        /// 面板激活时的处理（复现原代码的激活逻辑）
        /// </summary>
        public override void OnPanelActivated()
        {
            base.OnPanelActivated();

            // 重新收集和设置导航
            CollectAllSelectables();
            SetupExplicitNavigation();

            // 延迟设置选中对象，确保UI布局完成
            if (GamePadNavigationManager.Instance != null && GamePadNavigationManager.Instance.IsUsingGamepad())
            {
                StartCoroutine(DelayedActivation());
            }
        }

        private System.Collections.IEnumerator DelayedActivation()
        {
            yield return null; // 等待一帧，确保UI布局完成

            GameObject targetObject = GetTargetSelection();

            if (targetObject != null)
            {
                // 使用导航管理器的SetSelectedObject，它会处理Toggle/Slider的特殊逻辑
                GamePadNavigationManager.Instance.SetSelectedObjectWithPanelNotify(targetObject);
                currentSelectedObject = targetObject;

                if (GamePadNavigationManager.Instance.enableDebugLogs)
                {
                    Debug.Log($"[SettingsNavigationPanel] 激活面板，选中: {targetObject.name}");
                }
            }
        }

        /// <summary>
        /// 获取目标选中对象（复现原代码的选择逻辑）
        /// </summary>
        private GameObject GetTargetSelection()
        {
            // 1. 首先尝试最后选中的对象
            if (rememberToggleState && lastSelectedObject != null && lastSelectedObject.activeInHierarchy)
            {
                var selectable = lastSelectedObject.GetComponent<Selectable>();
                if (selectable != null && IsSelectableInteractable(selectable))
                {
                    return lastSelectedObject;
                }
            }

            // 2. 尝试默认选中对象
            if (defaultSelection != null && defaultSelection.activeInHierarchy)
            {
                var selectable = defaultSelection.GetComponent<Selectable>();
                if (selectable != null && IsSelectableInteractable(selectable))
                {
                    return defaultSelection;
                }
            }

            // 3. 使用第一个可交互元素
            foreach (var selectable in allSelectables)
            {
                if (selectable != null && IsSelectableInteractable(selectable) && selectable.gameObject.activeInHierarchy)
                {
                    return selectable.gameObject;
                }
            }

            return null;
        }

        /// <summary>
        /// 面板停用时的处理
        /// </summary>
        public override void OnPanelDeactivated()
        {
            base.OnPanelDeactivated();

            // 保存最后选中的对象
            if (currentSelectedObject != null)
            {
                lastSelectedObject = currentSelectedObject;
            }

            currentSelectedObject = null;
        }

        /// <summary>
        /// 处理自定义导航
        /// </summary>
        public override void HandleCustomNavigation()
        {
            // 设置界面使用默认导航
            if (GamePadNavigationManager.Instance != null && GamePadNavigationManager.Instance.IsUsingGamepad())
            {
                // 确保当前选中对象保持高亮
                var currentSelected = GamePadNavigationManager.Instance.GetCurrentSelectedGameObject();
                if (currentSelected != currentSelectedObject && currentSelected != null)
                {
                    UpdateCurrentSelection(currentSelected);
                }
            }
        }

        /// <summary>
        /// 是否忽略确认键输入 - 现在完全由GamePadNavigationManager处理
        /// </summary>
        public override bool ShouldIgnoreSubmit()
        {
            return false;
        }

        /// <summary>
        /// 更新当前选中对象（由导航管理器调用）
        /// </summary>
        public void UpdateCurrentSelection(GameObject selectedObject)
        {
            currentSelectedObject = selectedObject;
            if (selectedObject != null)
            {
                lastSelectedObject = selectedObject;
            }
        }

        /// <summary>
        /// 检查对象是否可选中（与原代码一致）
        /// </summary>
        private bool IsSelectableValid(Selectable selectable)
        {
            if (selectable == null) return false;

            // 对于Toggle和Slider，只要游戏对象激活就认为有效
            if (selectable is Toggle || selectable is Slider)
            {
                return selectable.gameObject.activeInHierarchy;
            }

            // 对于其他Selectable，需要可交互且激活
            return selectable.interactable && selectable.gameObject.activeInHierarchy;
        }

        /// <summary>
        /// 检查Selectable是否可交互
        /// </summary>
        private bool IsSelectableInteractable(Selectable selectable)
        {
            if (selectable == null) return false;

            // 对于Toggle和Slider，我们更宽松一些
            if (selectable is Toggle || selectable is Slider)
            {
                return selectable.gameObject.activeInHierarchy;
            }

            return selectable.interactable && selectable.gameObject.activeInHierarchy;
        }

        /// <summary>
        /// 获取Transform在层级中的深度
        /// </summary>
        private int GetDepth(Transform transform)
        {
            int depth = 0;
            while (transform.parent != null && transform.parent != this.transform)
            {
                depth++;
                transform = transform.parent;
            }
            return depth;
        }

        /// <summary>
        /// 手动刷新导航（用于动态UI更新）
        /// </summary>
        public void ManualRefreshNavigation()
        {
            CollectAllSelectables();
            SetupExplicitNavigation();

            if (GamePadNavigationManager.Instance != null && GamePadNavigationManager.Instance.enableDebugLogs)
            {
                Debug.Log($"[SettingsNavigationPanel] 手动刷新导航完成");
            }
        }

        // 调试方法
        [ContextMenu("调试打印导航信息")]
        public void DebugPrintNavigationInfo()
        {
            Debug.Log($"[SettingsNavigationPanel] 导航信息:");
            Debug.Log($"- 当前选中: {currentSelectedObject?.name ?? "无"}");
            Debug.Log($"- 最后选中: {lastSelectedObject?.name ?? "无"}");
            Debug.Log($"- 默认选中: {defaultSelection?.name ?? "无"}");
            Debug.Log($"- 可交互元素数量: {allSelectables.Count}");
        }

        [ContextMenu("手动触发导航设置")]
        public void ManualTriggerNavigationSetup()
        {
            CollectAllSelectables();
            SetupExplicitNavigation();
            Debug.Log($"[SettingsNavigationPanel] 手动触发导航设置完成");
        }
    }
}
