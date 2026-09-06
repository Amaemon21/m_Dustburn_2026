using System;

namespace Unity.Mathematics
{
    // Порт Ashima/Gustavson snoise — тот же алгоритм, что в Unity.Mathematics.noise.snoise,
    // поэтому харнесс генерирует близкий к юнити рельеф, а не заглушку.
    public static class Simplex
    {
        private const float F2 = 0.366025403784439f;
        private const float G2 = 0.211324865405187f;
        private const float H2 = -0.577350269189626f;
        private const float K2 = 0.024390243902439f;

        public static float Snoise(float vx, float vy)
        {
            float s = (vx + vy) * F2;
            float ix = MathF.Floor(vx + s);
            float iy = MathF.Floor(vy + s);

            float t = (ix + iy) * G2;
            float x0 = vx - ix + t;
            float y0 = vy - iy + t;

            float i1x = x0 > y0 ? 1f : 0f;
            float i1y = x0 > y0 ? 0f : 1f;

            float x1 = x0 + G2 - i1x;
            float y1 = y0 + G2 - i1y;
            float x2 = x0 + H2;
            float y2 = y0 + H2;

            ix = Mod289(ix);
            iy = Mod289(iy);

            float p0 = Permute(Permute(iy) + ix);
            float p1 = Permute(Permute(iy + i1y) + ix + i1x);
            float p2 = Permute(Permute(iy + 1f) + ix + 1f);

            float m0 = MathF.Max(0.5f - (x0 * x0 + y0 * y0), 0f);
            float m1 = MathF.Max(0.5f - (x1 * x1 + y1 * y1), 0f);
            float m2 = MathF.Max(0.5f - (x2 * x2 + y2 * y2), 0f);

            m0 *= m0; m0 *= m0;
            m1 *= m1; m1 *= m1;
            m2 *= m2; m2 *= m2;

            float g0 = Gradient(p0, x0, y0, ref m0);
            float g1 = Gradient(p1, x1, y1, ref m1);
            float g2 = Gradient(p2, x2, y2, ref m2);

            return 130f * (m0 * g0 + m1 * g1 + m2 * g2);
        }

        private static float Gradient(float p, float x, float y, ref float m)
        {
            float a = 2f * Fract(p * K2) - 1f;
            float h = MathF.Abs(a) - 0.5f;
            float ox = MathF.Floor(a + 0.5f);
            float a0 = a - ox;

            m *= 1.79284291400159f - 0.85373472095314f * (a0 * a0 + h * h);

            return a0 * x + h * y;
        }

        private static float Mod289(float x) => x - MathF.Floor(x * (1f / 289f)) * 289f;

        private static float Permute(float x) => Mod289((x * 34f + 1f) * x);

        private static float Fract(float x) => x - MathF.Floor(x);
    }
}
