using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;

namespace Nova
{
    public class BaseNavigationPanel : MonoBehaviour, INavigationPanel
    {
        [Header("面板基础设置")]
        [SerializeField] protected string panelName = "Unnamed Panel";
        [SerializeField] protected int priority = 10;
        [SerializeField] protected bool useCustomNavigation = false;
        [SerializeField] protected GameObject defaultSelection;

        [Header("自动收集设置")]
        [SerializeField] public bool autoCollectSelectables = true;
        [SerializeField] public bool includeInactiveInCollection = false;

        // 自动收集的Selectable列表
        protected List<Selectable> collectedSelectables = new List<Selectable>();

        // 改为 virtual 属性，允许子类重写
        public virtual string PanelName => panelName;
        public virtual int Priority => priority;
        public virtual bool IsActive => gameObject.activeInHierarchy;
        public virtual bool UseCustomNavigation => useCustomNavigation;
        public virtual GameObject DefaultSelection => defaultSelection;

        protected virtual void Start()
        {
            // 自动收集可交互元素
            if (autoCollectSelectables)
            {
                CollectSelectables();
            }

            // 设置默认选中对象
            if (defaultSelection == null && collectedSelectables.Count > 0)
            {
                defaultSelection = collectedSelectables[0].gameObject;
            }

            // 注册到导航管理器
            if (GamePadNavigationManager.Instance != null)
            {
                GamePadNavigationManager.Instance.RegisterPanel(this);
            }
        }

        /// <summary>
        /// 自动收集所有可交互的UI元素
        /// </summary>
        protected virtual void CollectSelectables()
        {
            collectedSelectables.Clear();

            // 获取所有子对象中的Selectable组件
            Selectable[] allSelectables = GetComponentsInChildren<Selectable>(includeInactiveInCollection);

            // 按照在Hierarchy中的顺序排序
            collectedSelectables = allSelectables
                .Where(selectable => selectable != null && IsSelectableValid(selectable))
                .OrderBy(selectable => GetDepth(selectable.transform))
                .ThenBy(selectable => selectable.transform.GetSiblingIndex())
                .ToList();

            if (GamePadNavigationManager.Instance != null && GamePadNavigationManager.Instance.enableDebugLogs)
            {
                Debug.Log($"[{PanelName}] 自动收集到 {collectedSelectables.Count} 个可交互元素");
                foreach (var selectable in collectedSelectables)
                {
                    Debug.Log($"  - {selectable.gameObject.name} ({selectable.GetType().Name}) - Interactable: {IsSelectableInteractable(selectable)}");
                }
            }
        }

        /// <summary>
        /// 检查Selectable是否有效（包括检查Toggle和Slider的特殊情况）
        /// </summary>
        protected virtual bool IsSelectableValid(Selectable selectable)
        {
            if (selectable == null) return false;

            // 对于Toggle，即使interactable为false，只要游戏对象是激活的，我们也应该考虑
            if (selectable is Toggle toggle)
            {
                return toggle.gameObject.activeInHierarchy;
            }

            // 对于Slider，同样处理
            if (selectable is Slider slider)
            {
                return slider.gameObject.activeInHierarchy;
            }

            // 对于其他Selectable，检查interactable和active状态
            return selectable.interactable && selectable.gameObject.activeInHierarchy;
        }

        /// <summary>
        /// 检查Selectable是否可交互
        /// </summary>
        protected virtual bool IsSelectableInteractable(Selectable selectable)
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
        public int GetDepth(Transform transform)
        {
            int depth = 0;
            while (transform.parent != null && transform.parent != this.transform)
            {
                depth++;
                transform = transform.parent;
            }
            return depth;
        }

        protected virtual void OnDestroy()
        {
            // 从导航管理器注销
            if (GamePadNavigationManager.Instance != null)
            {
                GamePadNavigationManager.Instance.UnregisterPanel(this);
            }
        }

        public virtual void OnPanelActivated()
        {
            // 面板被激活时的逻辑
        }

        public virtual void OnPanelDeactivated()
        {
            // 面板被停用时的逻辑
        }

        public virtual void HandleCustomNavigation()
        {
            // 自定义导航逻辑
        }

        public virtual bool ShouldIgnoreSubmit()
        {
            // 返回是否应该忽略确认键输入
            return false;
        }

        // 设置默认选中对象
        public void SetDefaultSelection(GameObject selection)
        {
            defaultSelection = selection;
        }

        // 设置是否使用自定义导航
        public void SetUseCustomNavigation(bool useCustom)
        {
            useCustomNavigation = useCustom;
        }

        /// <summary>
        /// 获取自动收集的Selectable列表（只读）
        /// </summary>
        public IReadOnlyList<Selectable> GetCollectedSelectables()
        {
            return collectedSelectables.AsReadOnly();
        }

        /// <summary>
        /// 手动添加Selectable到列表
        /// </summary>
        public void AddSelectable(Selectable selectable)
        {
            if (selectable != null && !collectedSelectables.Contains(selectable))
            {
                collectedSelectables.Add(selectable);
            }
        }

        /// <summary>
        /// 手动移除Selectable
        /// </summary>
        public void RemoveSelectable(Selectable selectable)
        {
            if (selectable != null)
            {
                collectedSelectables.Remove(selectable);
            }
        }

        /// <summary>
        /// 重新收集Selectable（用于动态添加UI元素的情况）
        /// </summary>
        /// <summary>
        /// 重新收集Selectable（用于动态添加UI元素的情况）
        /// </summary>
        public void RefreshSelectables()
        {
            if (autoCollectSelectables)
            {
                CollectSelectables();

                // 如果是SettingsNavigationPanel，调用特殊刷新
                if (this is SettingsNavigationPanel settingsPanel)
                {
                    // 通过反射调用SettingsNavigationPanel的特殊方法
                    var method = typeof(SettingsNavigationPanel).GetMethod("ManualRefreshNavigation",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    method?.Invoke(settingsPanel, null);
                }
            }
        }


    }
}
