using System.Collections;
using UnityEngine;

namespace PaperSeizerStone.Presentation.MainMenu
{
    public enum MenuAction { StartGame, Settings, Quit }

    public sealed class MenuSector : MonoBehaviour
    {
        [SerializeField] private MenuAction action;
        [SerializeField, Tooltip("只移动外观，根节点上的点击碰撞体保持原位。")] private Transform visual;
        [SerializeField, Min(0)] private float hoverHeight = 0.14f;
        [SerializeField, Min(0.01f)] private float smoothing = 14;
        [SerializeField, Range(0.8f, 1)] private float pressedScale = 0.94f;
        [SerializeField, Min(0.05f)] private float clickDuration = 0.18f;
        private Vector3 restPosition;
        private Vector3 restScale;
        private bool hovered;
        private bool clicking;

        public MenuAction Action => action;

        private void Awake()
        {
            restPosition = visual.localPosition;
            restScale = visual.localScale;
        }

        public void SetHovered(bool value) => hovered = value;

        private void Update()
        {
            float blend = 1 - Mathf.Exp(-smoothing * Time.unscaledDeltaTime);
            visual.localPosition = Vector3.Lerp(visual.localPosition,
                restPosition + (hovered ? Vector3.up * hoverHeight : Vector3.zero), blend);
            if (!clicking) visual.localScale = Vector3.Lerp(visual.localScale, restScale, blend);
        }

        public IEnumerator ClickFeedback()
        {
            clicking = true;
            float elapsed = 0;
            while (elapsed < clickDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float pulse = Mathf.Sin(Mathf.Clamp01(elapsed / clickDuration) * Mathf.PI);
                visual.localScale = restScale * Mathf.Lerp(1, pressedScale, pulse);
                yield return null;
            }
            visual.localScale = restScale;
            clicking = false;
        }

        private void OnDisable()
        {
            hovered = clicking = false;
            if (visual != null && restScale != Vector3.zero)
            {
                visual.localPosition = restPosition;
                visual.localScale = restScale;
            }
        }
    }
}
