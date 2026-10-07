using UnityEngine;

public partial class TerrainCarver
{
    private void CarvePad(PoiPlacement placement)
    {
        Vector2 forward = placement.Forward;
        Vector2 right = new(forward.y, -forward.x);
        Vector2 center = placement.Ground;

        float street = SampleNormalized(center.x + forward.x * placement.Footprint.y * 0.5f, center.y + forward.y * placement.Footprint.y * 0.5f);

        float halfWidth = placement.Footprint.x * 0.5f;
        float halfDepth = placement.Footprint.y * 0.5f;
        float margin = Mathf.Max(_config.PoiPadMargin, _cellSize);
        float skirt = _config.PoiPadSkirt;
        float reach = Mathf.Sqrt(halfWidth * halfWidth + halfDepth * halfDepth) + margin + skirt;

        float maxFill = _config.MaxPoiFill / _source.MaxHeight;
        float maxCut = _config.MaxPoiCut / _source.MaxHeight;

        int resolution = _source.Resolution;

        int minX = Mathf.Max(0, Mathf.FloorToInt((center.x - reach) / _cellSize));
        int maxX = Mathf.Min(resolution - 1, Mathf.CeilToInt((center.x + reach) / _cellSize));
        int minY = Mathf.Max(0, Mathf.FloorToInt((center.y - reach) / _cellSize));
        int maxY = Mathf.Min(resolution - 1, Mathf.CeilToInt((center.y + reach) / _cellSize));

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float deltaX = x * _cellSize - center.x;
                float deltaY = y * _cellSize - center.y;

                float alongRight = Mathf.Abs(deltaX * right.x + deltaY * right.y) - halfWidth;
                float alongForward = Mathf.Abs(deltaX * forward.x + deltaY * forward.y) - halfDepth;

                float outsideX = Mathf.Max(alongRight, 0f);
                float outsideY = Mathf.Max(alongForward, 0f);

                float distance = Mathf.Sqrt(outsideX * outsideX + outsideY * outsideY)
                    + Mathf.Min(Mathf.Max(alongRight, alongForward), 0f);

                if (distance > margin + skirt)
                    continue;

                int index = y * resolution + x;

                if (distance >= _padDistance[index])
                    continue;

                float ground = _source.Heights[index];

                _padDistance[index] = distance;
                _carveWeight[index] = distance <= margin ? 1f : Mathf.SmoothStep(0f, 1f, 1f - (distance - margin) / skirt);
                _carveTarget[index] = Mathf.Clamp(street, ground - maxCut, ground + maxFill);
            }
        }
    }
}
