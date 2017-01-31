using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GlmSharp;

namespace ProceduralWorld
{
    public static class RandomExtensions
    {
        public static float NextNormalFloatClamped(this Random r, float mean, float deviation, float min, float max)
        {
            return Math.Min(Math.Max(min, r.NextNormalFloat(mean, deviation)), max);
        }

        public static float NextNormalFloat(this Random r, float mean = 0.0f, float deviation = 1.0f)
        {
            double q = 0;
            float u1 = 0;
            float u2 = 0;
            while (q == 0 || q >= 1)
            {
                u1 = r.NextFloat(-1, 1);
                u2 = r.NextFloat(-1, 1);
                q = u1 * u1 + u2 * u2;
            }
            double p = Math.Sqrt(-2 * Math.Log(q) / q);
            return mean + deviation * (u1 * (float)p);
        }

        public static float NextFloat(this Random r, float min = 0f, float max = 1f)
        {
            return (float)(r.NextDouble() * (max - min) + min);
        }

        public static vec3 NextDir3(this Random r)
        {
            float x, y, z;
            do
            {
                x = r.NextFloat(-1f, 1f);
                y = r.NextFloat(-1f, 1f);
                z = r.NextFloat(-1f, 1f);
            } while (x * x + y * y + z * z > 1);
            return new vec3(x, y, z).Normalized;
        }

        public static vec2 NextDir2(this Random r)
        {
            float x, y;
            do
            {
                x = r.NextFloat(-1f, 1f);
                y = r.NextFloat(-1f, 1f);
            } while (x * x + y * y > 1);
            return new vec2(x, y).Normalized;
        }
    }
}
