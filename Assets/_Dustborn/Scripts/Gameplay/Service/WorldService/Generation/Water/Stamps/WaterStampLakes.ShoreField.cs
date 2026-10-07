using System;
using System.Threading.Tasks;
using UnityEngine;

public sealed partial class WaterStampLakes
{
    private sealed class ShoreField
    {
        private float[] _signed;
        private int _width;
        private int _height;
        private Vector2 _origin;
        private float _step;

        public static ShoreField Build(Func<Vector2, float> sample, Vector2 min, Vector2 max, float step)
        {
            var field = new ShoreField
            {
                _origin = min,
                _step = step,
                _width = Mathf.Max(2, Mathf.CeilToInt((max.x - min.x) / step) + 1),
                _height = Mathf.Max(2, Mathf.CeilToInt((max.y - min.y) / step) + 1)
            };

            int count = field._width * field._height;
            var mask = new float[count];
            var distance = new float[count];

            Parallel.For(0, field._height, j =>
            {
                for (int i = 0; i < field._width; i++)
                    mask[j * field._width + i] = sample(new Vector2(min.x + i * step, min.y + j * step));
            });

            float level = WaterStampTracer.LAKE_LEVEL;

            for (int j = 0; j < field._height; j++)
            {
                for (int i = 0; i < field._width; i++)
                {
                    int index = j * field._width + i;
                    distance[index] = float.MaxValue;

                    float dx = mask[j * field._width + Mathf.Min(i + 1, field._width - 1)] - mask[j * field._width + Mathf.Max(i - 1, 0)];
                    float dz = mask[Mathf.Min(j + 1, field._height - 1) * field._width + i] - mask[Mathf.Max(j - 1, 0) * field._width + i];
                    float gradient = 0.5f * Mathf.Sqrt(dx * dx + dz * dz) / step;

                    if (gradient < MIN_GRADIENT)
                        continue;

                    float estimate = Mathf.Abs(mask[index] - level) / gradient;

                    if (estimate <= SEED_CELLS * step)
                        distance[index] = estimate;
                }
            }

            Chamfer(distance, field._width, field._height, step);

            field._signed = new float[count];

            for (int index = 0; index < count; index++)
            {
                float value = distance[index] == float.MaxValue ? 1e6f : distance[index];
                field._signed[index] = mask[index] >= level ? value : -value;
            }

            return field;
        }

        public float Sample(Vector2 world)
        {
            float u = Mathf.Clamp((world.x - _origin.x) / _step, 0f, _width - 1.001f);
            float v = Mathf.Clamp((world.y - _origin.y) / _step, 0f, _height - 1.001f);
            int i = (int)u, j = (int)v;
            float tx = u - i, tz = v - j;
            int origin = j * _width + i;

            float top = Mathf.Lerp(_signed[origin], _signed[origin + 1], tx);
            float bottom = Mathf.Lerp(_signed[origin + _width], _signed[origin + _width + 1], tx);

            return Mathf.Lerp(top, bottom, tz);
        }

        private static void Chamfer(float[] distance, int width, int height, float step)
        {
            float diagonal = step * 1.41421356f;

            for (int j = 0; j < height; j++)
            {
                for (int i = 0; i < width; i++)
                {
                    Relax(distance, width, height, i, j, i - 1, j, step);
                    Relax(distance, width, height, i, j, i - 1, j - 1, diagonal);
                    Relax(distance, width, height, i, j, i, j - 1, step);
                    Relax(distance, width, height, i, j, i + 1, j - 1, diagonal);
                }
            }

            for (int j = height - 1; j >= 0; j--)
            {
                for (int i = width - 1; i >= 0; i--)
                {
                    Relax(distance, width, height, i, j, i + 1, j, step);
                    Relax(distance, width, height, i, j, i + 1, j + 1, diagonal);
                    Relax(distance, width, height, i, j, i, j + 1, step);
                    Relax(distance, width, height, i, j, i - 1, j + 1, diagonal);
                }
            }
        }

        private static void Relax(float[] distance, int width, int height, int i, int j, int ni, int nj, float step)
        {
            if (ni < 0 || nj < 0 || ni >= width || nj >= height)
                return;

            float other = distance[nj * width + ni];

            if (other == float.MaxValue)
                return;

            if (other + step < distance[j * width + i])
                distance[j * width + i] = other + step;
        }
    }

    private struct Fit
    {
        public WaterStampPlacement Placement;
        public float Score;
    }
}
