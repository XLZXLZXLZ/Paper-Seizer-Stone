using System;
using UnityEngine;

namespace PaperSeizerStone.Configuration
{
    public enum TutorialNode
    {
        Greeting, DrawFour, ExtraCard, PlaceCards, BellPrompt,
        FirstBell, FirstHurt, EnemyDeath, Shop, Engraving, Basics
    }

    public enum ManualPage { Confrontation, Cheating, Life, Shop, Engraving }

    [Serializable]
    public sealed class TutorialLine
    {
        [TextArea(2, 4)] public string text;
        [Tooltip("留空时使用机械说话短音。支持 Text Animator 的 <shake> 等标签。")]
        public AudioClip voice;
        [Min(0.01f)] public float characterSeconds = 0.055f;
        [Min(0), Tooltip("字幕全文显示后自动停留的时间；有整句语音时也会等语音结束。")]
        public float holdSeconds = 1;
    }

    [Serializable]
    public sealed class TutorialSequence
    {
        public TutorialNode node;
        public TutorialLine[] lines = Array.Empty<TutorialLine>();
        public bool openManual;
        public ManualPage page;
    }

    [Serializable]
    public sealed class ManualEntry
    {
        public ManualPage page;
        public string title;
        [TextArea(8, 16)] public string body;
    }

    // Shared authoring data only. Completion belongs to the current adventure's director.
    public sealed class TutorialDefinition : ScriptableObject
    {
        public TutorialSequence[] sequences = Array.Empty<TutorialSequence>();
        public ManualEntry[] pages = Array.Empty<ManualEntry>();
        public AudioClip[] speechBlips = Array.Empty<AudioClip>();
        [Range(0, 1)] public float speechVolume = 0.3f;
        public Vector2 blipPitch = new Vector2(0.88f, 1.12f);
        [Min(0.03f)] public float blipInterval = 0.075f;

        public TutorialSequence Find(TutorialNode node) => Array.Find(sequences, entry => entry != null && entry.node == node);
        public ManualEntry Find(ManualPage page) => Array.Find(pages, entry => entry != null && entry.page == page);
    }
}
