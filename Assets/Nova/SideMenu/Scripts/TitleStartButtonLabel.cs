using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Nova
{
    /// <summary>
    /// 挂在标题界面"开始游戏"按钮下的 Text/TMP_Text 节点上。
    /// 启用时根据是否存在 AutoSave 切换文案：
    ///   有 AutoSave → I18n.__("ui.title.start.continue")  "继续游戏"
    ///   无 AutoSave → I18n.__("ui.title.start.new")       "开始游戏"
    /// 切换语言时自动刷新。
    /// </summary>
    public class TitleStartButtonLabel : MonoBehaviour
    {
        [SerializeField] private string newGameKey = "ui.title.start.new";
        [SerializeField] private string continueKey = "ui.title.start.continue";

        private Text legacyText;
        private TMP_Text tmpText;
        private CheckpointManager checkpointManager;

        private void Awake()
        {
            legacyText = GetComponent<Text>();
            tmpText = GetComponent<TMP_Text>();
            var controller = Utils.FindNovaController();
            if (controller != null) checkpointManager = controller.CheckpointManager;
        }

        private void OnEnable()
        {
            Refresh();
            I18n.LocaleChanged.AddListener(Refresh);
        }

        private void OnDisable()
        {
            I18n.LocaleChanged.RemoveListener(Refresh);
        }

        private void Refresh()
        {
            string key = HasAutoSave() ? continueKey : newGameKey;
            string s = I18n.__(key);
            if (tmpText != null) tmpText.text = s;
            else if (legacyText != null) legacyText.text = s;
        }

        private bool HasAutoSave()
        {
            if (checkpointManager == null) return false;
            int saveID = checkpointManager.QuerySaveIDByTime(
                (int)BookmarkType.AutoSave,
                (int)BookmarkType.QuickSave,
                SaveIDQueryType.Latest);
            return checkpointManager[saveID] != null;
        }
    }
}
