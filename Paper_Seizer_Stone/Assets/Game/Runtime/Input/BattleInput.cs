using UnityEngine;
using DG.Tweening;
using PaperSeizerStone.Flow;
using PaperSeizerStone.Presentation;

namespace PaperSeizerStone.InputControl
{
    public sealed class BattleInput : MonoBehaviour
    {
        private BattleController controller;
        private CardView dragging;
        private Vector3 grabOffset;
        private CardView highlightedCard;
        private TableTarget highlightedTarget;
        private Plane dragPlane;

        public void Initialize(BattleController battle)
        {
            controller = battle;
            Transform anchor = controller.View.DragAnchor;
            dragPlane = new Plane(anchor.up, anchor.position);
        }

        private bool Hit(out RaycastHit hit)
        {
            int mask = Physics.DefaultRaycastLayers & ~(1 << PaperSeizerStone.Presentation.Props.PlayerVisibilityLayer.Layer);
            return Physics.Raycast(controller.Camera.ScreenPointToRay(Input.mousePosition), out hit, 100, mask);
        }

        private Vector3 DragPosition()
        {
            Ray ray = controller.Camera.ScreenPointToRay(Input.mousePosition);
            return dragPlane.Raycast(ray, out float distance) ? ray.GetPoint(distance) : Vector3.zero;
        }

        private void Update()
        {
            if (controller == null || controller.Camera == null || controller.View == null) return;
            controller.View.SetConfirmInteraction(controller.CanInteract && dragging == null);
            controller.CameraRig.SetPrecisionMode(dragging != null);
            if (dragging != null)
            {
                dragging.transform.position = Vector3.Lerp(dragging.transform.position, DragPosition() + grabOffset,
                    1 - Mathf.Exp(-25 * Time.deltaTime));
                UpdateHighlight();
                if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1)) { CancelDrag(); return; }
                if (Input.GetMouseButtonUp(0))
                {
                    CardView source = dragging;
                    CardView targetCard = null;
                    TableTarget target = null;
                    if (Hit(out RaycastHit drop))
                    {
                        targetCard = drop.collider.GetComponentInParent<CardView>();
                        target = drop.collider.GetComponentInParent<TableTarget>();
                    }
                    dragging = null;
                    controller.View.SetDraggingCard(null);
                    source.SetHitbox(true);
                    source.SetHighlight(false);
                    ClearHighlight();
                    controller.Drop(source.CardId, targetCard, target);
                }
                return;
            }
            bool cursorInside = Application.isFocused && Input.mousePosition.y >= 0 && Input.mousePosition.y <= Screen.height &&
                Input.mousePosition.x >= 0 && Input.mousePosition.x <= Screen.width;
            controller.View.SetHandExpanded(controller.CanDrag && cursorInside &&
                controller.View.WantsHandExpanded(Input.mousePosition.y / Mathf.Max(1, Screen.height)));
            if (!controller.CanInteract) return;
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space))
            {
                controller.ConfirmOrPass();
                return;
            }
            if (!Input.GetMouseButtonDown(0) || !Hit(out RaycastHit hit)) return;
            CardView card = hit.collider.GetComponentInParent<CardView>();
            if (controller.CanDrag && card != null && card.CanDrag)
            {
                dragging = card;
                dragging.transform.DOKill();
                controller.View.SetDraggingCard(dragging);
                controller.View.SetHandExpanded(true);
                Vector3 lifted = dragPlane.ClosestPointOnPlane(dragging.transform.position);
                grabOffset = dragging.IsOnBoard ? lifted - DragPosition() : Vector3.zero;
                dragging.transform.DORotateQuaternion(controller.View.DragAnchor.rotation, 0.16f);
                dragging.transform.DOScale(Vector3.one, 0.16f);
                dragging.SetHitbox(false);
                dragging.SetHighlight(true);
                return;
            }
            TableTarget tableTarget = hit.collider.GetComponentInParent<TableTarget>();
            if (tableTarget == null) return;
            if (tableTarget.Kind == TableTargetKind.Confirm) controller.ConfirmOrPass();
            else if (tableTarget.Kind == TableTargetKind.PlayerDeck) controller.DrawExtra();
        }

        private void CancelDrag()
        {
            if (dragging == null) return;
            dragging.SetHitbox(true);
            dragging.SetHighlight(false);
            dragging = null;
            controller.View.SetDraggingCard(null);
            controller.CameraRig.SetPrecisionMode(false);
            ClearHighlight();
            controller.View.RestoreLayout();
        }

        private void UpdateHighlight()
        {
            CardView card = null;
            TableTarget target = null;
            if (Hit(out RaycastHit hit))
            {
                card = hit.collider.GetComponentInParent<CardView>();
                target = hit.collider.GetComponentInParent<TableTarget>();
            }
            if (!controller.IsLegalDrop(dragging.CardId, card, target)) { card = null; target = null; }
            if (card == highlightedCard && target == highlightedTarget) return;
            ClearHighlight();
            highlightedCard = card;
            highlightedTarget = target;
            highlightedCard?.SetHighlight(true);
            highlightedTarget?.SetHighlight(true);
        }

        private void ClearHighlight()
        {
            if (highlightedCard != null) highlightedCard.SetHighlight(false);
            if (highlightedTarget != null) highlightedTarget.SetHighlight(false);
            highlightedCard = null;
            highlightedTarget = null;
        }

        private void OnApplicationFocus(bool hasFocus) { if (!hasFocus) CancelDrag(); }
        private void OnDisable() { if (controller != null && controller.View != null) CancelDrag(); }
    }
}
