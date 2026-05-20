using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Nova
{
    public class ChoicesController : MonoBehaviour
    {
        [SerializeField] private ChoiceButtonController choiceButtonPrefab;
        [SerializeField] private GameObject backPanel;
        [SerializeField] private string imageFolder;

        private GameState gameState;

        private Button[] buttons;
        public int activeChoiceCount { get; private set; }

        // 视频选项模式：把 Choice 按钮临时寄生到指定容器（VideoButton/ButtonCanvas）下，
        // 这样按钮和视频在同一个 Canvas 里渲染，绕开 sortingOrder 的坑。
        private Transform choiceContainerOverride;
        private Action onChoiceConsumed;
        // 限时选项用：branch{} 里某个 index 仅作为超时跳转目标，不生成按钮（玩家看不到这一项）。
        private int hiddenChoiceIndex = -1;

        private Transform ChoiceContainer =>
            choiceContainerOverride != null ? choiceContainerOverride : transform;

        public void SetChoiceContainerOverride(Transform container, Action onConsumed = null, int hiddenIndex = -1)
        {
            choiceContainerOverride = container;
            onChoiceConsumed = onConsumed;
            hiddenChoiceIndex = hiddenIndex;
        }

        public void ClearChoiceContainerOverride()
        {
            choiceContainerOverride = null;
            onChoiceConsumed = null;
            hiddenChoiceIndex = -1;
        }

        /// <summary>限时归零等情况下，跳过 Select 的 clamp，直接清理按钮 + 触发 consumed 回调 + 用给定 index
        /// 信号 branch fence（用来跳到一个没有可见按钮的隐藏 branch 分支）。</summary>
        public void CancelAndSignal(int index)
        {
            var consumed = onChoiceConsumed;
            RemoveAllChoices();
            consumed?.Invoke();
            gameState.SignalFence(index);
        }

        private bool _buttonsEnabled = true;

        public bool buttonsEnabled
        {
            get => _buttonsEnabled;
            set
            {
                if (buttons == null || value == _buttonsEnabled)
                {
                    return;
                }

                foreach (var button in buttons)
                {
                    button.enabled = value;
                }

                _buttonsEnabled = value;
            }
        }

        private void Awake()
        {
            RemoveAllChoices();

            gameState = Utils.FindNovaController().GameState;
            gameState.choiceOccurs.AddListener(OnChoiceOccurs);
            gameState.restoreStarts.AddListener(OnRestoreStarts);
        }

        private void OnDestroy()
        {
            gameState.choiceOccurs.RemoveListener(OnChoiceOccurs);
            gameState.restoreStarts.RemoveListener(OnRestoreStarts);
        }

        private void OnChoiceOccurs(ChoiceOccursData data)
        {
            RaiseChoices(data.choices);
        }

        public void RaiseChoices(IReadOnlyList<ChoiceOccursData.Choice> choices)
        {
            if (choices.Count == 0)
            {
                throw new ArgumentException("Nova: No active selection.");
            }

            var useOverride = choiceContainerOverride != null;
            var parent = ChoiceContainer;

            // 视频模式下不显示原本的黑色半透明 backPanel —— 视频本身就是背景。
            if (backPanel != null && !useOverride)
            {
                backPanel.SetActive(true);
            }

            for (var i = 0; i < choices.Count; i++)
            {
                // 隐藏的 timeout 分支不生成按钮，但保留它在 branch{} 的 index 位置以便 SignalFence 路由。
                if (i == hiddenChoiceIndex) continue;
                var choice = choices[i];
                var index = i;
                var button = Instantiate(choiceButtonPrefab, parent);
                // Prevent showing the button before init
                button.gameObject.SetActive(false);
                button.Init(choice.texts, choice.imageInfo, imageFolder, () => Select(index),
                    choice.interactable, choice.wasChosen);
                button.gameObject.SetActive(true);

                // 入场扫描特效（错峰 0.04s/按钮）
                var fx = button.GetComponent<ChoiceButtonFX>();
                if (fx != null) fx.PlayReveal(i * 0.04f);
            }

            buttons = parent.GetComponentsInChildren<Button>();
            activeChoiceCount = choices.Count;
        }

        public void Select(int index)
        {
            var consumed = onChoiceConsumed;
            RemoveAllChoices();
            // 通知视频模式可以拆掉 VideoButton 容器了
            consumed?.Invoke();
            gameState.SignalFence(index);
        }

        private void OnRestoreStarts(bool isInitial)
        {
            RemoveAllChoices();
        }

        private void RemoveAllChoices()
        {
            var useOverride = choiceContainerOverride != null;
            var parent = ChoiceContainer;
            foreach (Transform child in parent)
            {
                Destroy(child.gameObject);
            }

            activeChoiceCount = 0;

            if (backPanel != null && !useOverride)
            {
                backPanel.SetActive(false);
            }

            buttons = null;
        }
    }
}
