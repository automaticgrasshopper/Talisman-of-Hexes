using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

namespace Nova
{
    public class NotificationNavigationPanel : BaseNavigationPanel
    {
        // 通知界面配置
        public override string PanelName => "Notification";
        public override int Priority => PanelPriority.NOTIFICATION;
        public override bool UseCustomNavigation => false;

        protected override void Start()
        {
            // 调用基类的Start方法，会自动收集Selectable
            base.Start();
        }

        /// <summary>
        /// 面板激活时的处理
        /// </summary>
        public override void OnPanelActivated()
        {
            base.OnPanelActivated();
        }

        /// <summary>
        /// 面板停用时的处理
        /// </summary>
        public override void OnPanelDeactivated()
        {
            base.OnPanelDeactivated();
        }

        /// <summary>
        /// 处理自定义导航（通知界面使用默认导航，所以这里为空）
        /// </summary>
        public override void HandleCustomNavigation()
        {
            // 通知界面使用默认导航逻辑，不需要自定义处理
        }

        /// <summary>
        /// 是否忽略确认键输入
        /// </summary>
        public override bool ShouldIgnoreSubmit()
        {
            // 通知界面不需要忽略确认键
            return false;
        }
    }
}
