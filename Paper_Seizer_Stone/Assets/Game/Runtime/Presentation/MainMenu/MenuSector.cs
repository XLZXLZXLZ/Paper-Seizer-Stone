using System.Collections;
using TMPro;
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
        [Header("悬停粒子文字")]
        [SerializeField] private bool showHoverLabel = true;
        [SerializeField, Tooltip("可指定现有文字；留空时先找 Action 子节点，再加载独立文字预制体。")]
        private TMP_Text hoverLabel;
        [SerializeField, Tooltip("相对现有文字位置，或自动生成文字的盘面中心位置的偏移。")]
        private Vector3 hoverLabelOffset;
        [SerializeField, Tooltip("在文字原本朝向上追加的局部旋转。")]
        private Vector3 hoverLabelEulerOffset;
        [SerializeField, Min(0.1f), Tooltip("文字整体大小倍率；1保留现有大小或自动适配盘面。")]
        private float hoverLabelScale = 1f;
        [SerializeField] private MenuHoverLabelSettings hoverLabelEffect = new MenuHoverLabelSettings();
        private Vector3 restPosition;
        private Vector3 restScale;
        private bool hovered;
        private bool clicking;
        private MenuHoverLabelView labelView;

        public MenuAction Action => action;

        private void Awake()
        {
            if (visual == null) return;
            restPosition = visual.localPosition;
            restScale = visual.localScale;
            if (showHoverLabel) PrepareHoverLabel();
        }

        private void PrepareHoverLabel()
        {
            if (hoverLabel == null)
                foreach (TMP_Text candidate in visual.GetComponentsInChildren<TMP_Text>(true))
                    if (candidate.name == "Action") { hoverLabel = candidate; break; }

            bool generated = hoverLabel == null;
            if (generated)
            {
                GameObject template = Resources.Load<GameObject>("UI/MenuHoverLabel");
                if (template == null) return;
                GameObject labelObject = Instantiate(template, visual, false);
                labelObject.name = "Action (Hover)";
                labelObject.layer = visual.gameObject.layer;
                hoverLabel = labelObject.GetComponent<TMP_Text>();
                if (hoverLabel == null) { Destroy(labelObject); return; }
            }

            hoverLabel.gameObject.SetActive(true);
            hoverLabel.raycastTarget = false;
            string caption = action == MenuAction.StartGame ? "开始游戏"
                : action == MenuAction.Settings ? "设置" : "退出游戏";
            hoverLabel.text = caption;
            GlobalFontService.Apply(hoverLabel);

            if (generated)
            {
                MeshFilter filter = visual.GetComponent<MeshFilter>();
                Bounds bounds = filter != null && filter.sharedMesh != null
                    ? filter.sharedMesh.bounds : new Bounds(Vector3.zero, new Vector3(1, 0.1f, 1));
                hoverLabel.transform.localPosition = new Vector3(bounds.center.x, bounds.max.y + 0.025f, bounds.center.z);
                hoverLabel.transform.localRotation = Quaternion.Euler(90, 0, 0);
                hoverLabel.ForceMeshUpdate();
                // Fit only the generated label. Existing, manually placed labels keep their layout.
                float span = Mathf.Max(0.1f, Mathf.Min(bounds.size.x, bounds.size.z));
                float scale = Mathf.Min(span * 0.62f / Mathf.Max(0.01f, hoverLabel.textBounds.size.x),
                    span * 0.16f / Mathf.Max(0.01f, hoverLabel.textBounds.size.y));
                hoverLabel.transform.localScale = Vector3.one * scale;
            }

            hoverLabel.transform.localPosition += hoverLabelOffset;
            hoverLabel.transform.localRotation *= Quaternion.Euler(hoverLabelEulerOffset);
            hoverLabel.transform.localScale *= hoverLabelScale;
            labelView = hoverLabel.GetComponent<MenuHoverLabelView>();
            if (labelView == null) labelView = hoverLabel.gameObject.AddComponent<MenuHoverLabelView>();
            labelView.Initialize(hoverLabel, hoverLabelEffect ?? new MenuHoverLabelSettings());
        }

        public void SetHovered(bool value)
        {
            hovered = value;
            if (labelView != null) labelView.SetHovered(value);
        }

        private void Update()
        {
            if (visual == null) return;
            float blend = 1 - Mathf.Exp(-smoothing * Time.unscaledDeltaTime);
            visual.localPosition = Vector3.Lerp(visual.localPosition,
                restPosition + (hovered ? Vector3.up * hoverHeight : Vector3.zero), blend);
            if (!clicking) visual.localScale = Vector3.Lerp(visual.localScale, restScale, blend);
        }

        public IEnumerator ClickFeedback()
        {
            if (visual == null) yield break;
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
            if (labelView != null) labelView.HideImmediately();
            if (visual != null && restScale != Vector3.zero)
            {
                visual.localPosition = restPosition;
                visual.localScale = restScale;
            }
        }
    }
}
