using UnityEngine;
using DG.Tweening;
using PaperSeizerStone.Flow;
using PaperSeizerStone.Presentation;

namespace PaperSeizerStone.InputControl
{
    public sealed class BattleInput : MonoBehaviour
    {
        [SerializeField, Min(0), Tooltip("槽位外额外接纳松手的距离，以1080p屏幕像素为参考。")]
        private float dropPadding = 42;
        [SerializeField] private CardHoverPreview previewPrefab;
        private CardHoverPreview preview;
        private BattleController controller;
        private CardView dragging;
        private Vector3 grabOffset;
        private CardView highlightedCard;
        private TableTarget highlightedTarget;
        private Plane dragPlane;

        public void Initialize(BattleController battle)
        {
            controller = battle;
            if (preview == null && previewPrefab != null)
            {
                preview = Instantiate(previewPrefab);
                preview.Hide();
            }
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
                preview?.Hide();
                dragging.transform.position = Vector3.Lerp(dragging.transform.position, DragPosition() + grabOffset,
                    1 - Mathf.Exp(-25 * Time.deltaTime));
                UpdateHighlight();
                if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1)) { CancelDrag(); return; }
                if (Input.GetMouseButtonUp(0))
                {
                    CardView source = dragging;
                    ResolveDrop(out CardView targetCard, out TableTarget target);
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
                controller.View.WantsHandExpanded(Input.mousePosition));
            if (preview != null)
            {
                CardView hovered = cursorInside && controller.CanInteract && Hit(out RaycastHit hoverHit)
                    ? hoverHit.collider.GetComponentInParent<CardView>() : null;
                preview.Show(hovered, Input.mousePosition);
            }
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
                preview?.Hide();
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
            ResolveDrop(out CardView card, out TableTarget target);
            if (card == highlightedCard && target == highlightedTarget) return;
            ClearHighlight();
            highlightedCard = card;
            highlightedTarget = target;
            highlightedCard?.SetHighlight(true);
            highlightedTarget?.SetHighlight(true);
        }

        private void ResolveDrop(out CardView card, out TableTarget target)
        {
            card = null; target = null;
            if (Hit(out RaycastHit hit))
            {
                card = hit.collider.GetComponentInParent<CardView>();
                target = hit.collider.GetComponentInParent<TableTarget>();
                if (controller.IsLegalDrop(dragging.CardId, card, target)) return;
            }
            card = null; target = null;
            if (!controller.Camera.pixelRect.Contains((Vector2)Input.mousePosition)) return;
            float best = Mathf.Max(0, dropPadding) * controller.Camera.pixelHeight / 1080f;
            Vector2 cursor = Input.mousePosition;
            foreach (TableTarget slot in controller.View.PlayerDropSlots())
            {
                if (!controller.IsLegalDrop(dragging.CardId, null, slot)) continue;
                var box = slot.GetComponent<Collider>();
                if (box == null || !box.enabled) continue;
                Bounds bounds = box.bounds;
                Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
                Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
                bool behind = false;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 point = controller.Camera.WorldToScreenPoint(corner);
                    if (point.z <= 0) { behind = true; break; }
                    min = Vector2.Min(min, point); max = Vector2.Max(max, point);
                }
                if (behind) continue;
                Vector2 nearest = new Vector2(Mathf.Clamp(cursor.x, min.x, max.x), Mathf.Clamp(cursor.y, min.y, max.y));
                float distance = Vector2.Distance(cursor, nearest);
                if (distance > best) continue;
                best = distance; target = slot;
            }
        }

        private void ClearHighlight()
        {
            if (highlightedCard != null) highlightedCard.SetHighlight(false);
            if (highlightedTarget != null) highlightedTarget.SetHighlight(false);
            highlightedCard = null;
            highlightedTarget = null;
        }

        private void OnApplicationFocus(bool hasFocus) { if (!hasFocus) { preview?.Hide(); CancelDrag(); } }
        private void OnDisable()
        {
            preview?.Hide();
            if (controller != null && controller.View != null) CancelDrag();
        }
        private void OnDestroy() { if (preview != null) Destroy(preview.gameObject); }
    }
}
