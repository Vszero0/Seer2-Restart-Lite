using System;
using UnityEngine;
using UnityEngine.EventSystems;

public sealed class StoryPropDragHandler : MonoBehaviour, IPointerClickHandler, IDragHandler
{
    private RectTransform stage;
    private string propId;
    private Action<string> onSelected;
    private Action<Vector2> onPositionChanged;

    public void Configure(RectTransform stage, string propId, Action<string> onSelected,
        Action<Vector2> onPositionChanged)
    {
        this.stage = stage;
        this.propId = propId;
        this.onSelected = onSelected;
        this.onPositionChanged = onPositionChanged;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        onSelected?.Invoke(propId);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (stage == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                stage, eventData.position, eventData.pressEventCamera, out Vector2 local))
        {
            return;
        }

        Rect bounds = stage.rect;
        Vector2 normalized = new Vector2(
            Mathf.InverseLerp(bounds.xMin, bounds.xMax, local.x),
            Mathf.InverseLerp(bounds.yMin, bounds.yMax, local.y));
        onSelected?.Invoke(propId);
        onPositionChanged?.Invoke(normalized);
    }
}
