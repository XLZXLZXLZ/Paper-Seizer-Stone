using UnityEngine;

namespace PaperSeizerStone.Presentation.Props
{
    public enum ShopTableActionKind { Burn, InspectDeck, Continue }

    // Attached to saved tabletop props; input is gated by the owning shop phase.
    public sealed class ShopTableAction : MonoBehaviour
    {
        public ShopTableActionKind Kind;
    }
}
