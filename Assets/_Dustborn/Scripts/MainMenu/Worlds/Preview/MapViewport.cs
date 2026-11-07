using UnityEngine;

public sealed class MapViewport
{
    public const float MIN_ZOOM = 1f;
    public const float MAX_ZOOM = 16f;

    private const float WHEEL_STEP = 1.2f;

    public float Zoom { get; private set; } = MIN_ZOOM;
    public Vector2 Center { get; private set; } = new(0.5f, 0.5f);

    public Rect UvRect
    {
        get
        {
            float size = 1f / Zoom;
            return new Rect(Center.x - size * 0.5f, Center.y - size * 0.5f, size, size);
        }
    }

    public void Wheel(float scroll, Vector2 pivot)
    {
        if (Mathf.Approximately(scroll, 0f))
            return;

        Vector2 anchor = UvAt(pivot);
        Zoom = Mathf.Clamp(Zoom * Mathf.Pow(WHEEL_STEP, Mathf.Sign(scroll)), MIN_ZOOM, MAX_ZOOM);
        Center = anchor - (pivot - new Vector2(0.5f, 0.5f)) / Zoom;
        Clamp();
    }

    public void Drag(Vector2 normalizedDelta)
    {
        Center -= normalizedDelta / Zoom;
        Clamp();
    }

    public void Reset()
    {
        Zoom = MIN_ZOOM;
        Center = new Vector2(0.5f, 0.5f);
    }

    public Vector2 UvAt(Vector2 normalized) => Center + (normalized - new Vector2(0.5f, 0.5f)) / Zoom;

    private void Clamp()
    {
        float half = 0.5f / Zoom;
        Center = new Vector2(Mathf.Clamp(Center.x, half, 1f - half), Mathf.Clamp(Center.y, half, 1f - half));
    }
}
