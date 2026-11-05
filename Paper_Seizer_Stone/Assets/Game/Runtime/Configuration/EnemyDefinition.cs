using UnityEngine;

namespace PaperSeizerStone.Configuration
{
    public sealed class EnemyDefinition : ScriptableObject
    {
        [SerializeField] private string enemyId;
        [SerializeField] private string displayName;
        [SerializeField, Min(1)] private int life = 1;
        [SerializeField] private bool isBoss;
        [SerializeField] private DeckDefinition deck;
        [SerializeField] private GameObject appearance;
        public string Id => enemyId;
        public string DisplayName => displayName;
        public int Life => life;
        public bool IsBoss => isBoss;
        public DeckDefinition Deck => deck;
        public GameObject Appearance => appearance;
    }
}
