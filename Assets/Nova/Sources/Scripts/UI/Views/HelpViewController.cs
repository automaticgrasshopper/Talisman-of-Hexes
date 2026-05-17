using System;
using UnityEngine;
using UnityEngine.UI;

namespace Nova
{
    public class HelpViewController : ViewControllerBase
    {
        [SerializeField] private Button returnButton;
        [SerializeField] private Button returnButton2;

        protected override void Awake()
        {
            base.Awake();

            returnButton.onClick.AddListener(Hide);
            returnButton2.onClick.AddListener(Hide);
        }

        public override void Hide(bool doTransition, Action onFinish)
        {
            base.Hide(doTransition, () =>
            {
                viewManager.GetController<TitleController>().ShowHints();
                onFinish?.Invoke();
            });
        }

        public override void Show(bool doTransition, Action onFinish)
        {
            base.Show(doTransition, () =>
            {
                Debug.Log("帮助界面显示完成");
            });
        }
    }
}
