using System.Collections.Generic;
using UnityEngine;
using PaperSeizerStone.Audio;

namespace PaperSeizerStone.Presentation
{
    // References point to the saved scene lights; boss battles only override their color.
    [DisallowMultipleComponent]
    public sealed class BossBattleLighting : MonoBehaviour
    {
        [SerializeField] private Light[] lights;
        [SerializeField] private Color bossColor = new Color32(255, 123, 109, 255);

        private struct LightState
        {
            public Color Color;
            public bool UseColorTemperature;
        }

        private readonly Dictionary<Light, LightState> originalStates = new Dictionary<Light, LightState>();

        public void EnterBossBattle()
        {
            if (!isActiveAndEnabled)
            {
                Debug.LogError("头目战灯光组件未启用，无法切换红灯。", this);
                return;
            }
            if (lights == null || lights.Length == 0)
            {
                Debug.LogError("头目战灯光没有配置目标灯。", this);
                return;
            }
            bool entering = originalStates.Count == 0;
            foreach (Light target in lights)
            {
                if (target == null || originalStates.ContainsKey(target)) continue;
                originalStates.Add(target, new LightState
                {
                    Color = target.color,
                    UseColorTemperature = target.useColorTemperature
                });
                target.useColorTemperature = false;
                target.color = bossColor;
            }
            if (originalStates.Count == 0)
                Debug.LogError("头目战灯光没有有效目标，请绑定牌桌的 TableLight 和 Light。", this);
            else if (entering)
                AudioManager.Play(AudioCue.BossLightSwitch);
        }

        public void Restore()
        {
            foreach (KeyValuePair<Light, LightState> entry in originalStates)
            {
                if (entry.Key == null) continue;
                entry.Key.color = entry.Value.Color;
                entry.Key.useColorTemperature = entry.Value.UseColorTemperature;
            }
            originalStates.Clear();
        }

        private void OnDisable() { Restore(); }
        private void OnDestroy() { Restore(); }
    }
}
