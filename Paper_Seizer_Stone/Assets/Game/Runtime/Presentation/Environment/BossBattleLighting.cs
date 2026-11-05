using System.Collections.Generic;
using UnityEngine;

namespace PaperSeizerStone.Presentation
{
    // References point to the saved scene lights; boss battles only override their color.
    public sealed class BossBattleLighting : MonoBehaviour
    {
        [SerializeField] private Light[] lights;
        [SerializeField] private Color bossColor = new Color(1f, 0.04f, 0.025f, 1f);

        private struct LightState
        {
            public Color Color;
            public bool UseColorTemperature;
        }

        private readonly Dictionary<Light, LightState> originalStates = new Dictionary<Light, LightState>();

        public void EnterBossBattle()
        {
            if (!isActiveAndEnabled || lights == null) return;
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
