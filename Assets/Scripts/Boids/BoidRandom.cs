using UnityEngine;

namespace TestBoids.Boids
{
    internal struct BoidRandom
    {
        private uint state;

        public BoidRandom(uint seed)
        {
            state = seed;
        }

        public float Next01()
        {
            uint t = state += 0x6D2B79F5u;
            t = (t ^ (t >> 15)) * (t | 1u);
            t ^= t + ((t ^ (t >> 7)) * (t | 61u));
            return ((t ^ (t >> 14)) & 0xFFFFFFFFu) / 4294967296f;
        }

        public Vector3 PointInAquarium(Vector3 halfSize, float scale)
        {
            return new Vector3(
                RandomRange(-halfSize.x * scale, halfSize.x * scale),
                RandomRange(-halfSize.y * scale, halfSize.y * scale),
                RandomRange(-halfSize.z * scale, halfSize.z * scale));
        }

        public Vector3 PointInSphere(float radius)
        {
            for (int i = 0; i < 24; i++)
            {
                Vector3 point = new(
                    RandomRange(-radius, radius),
                    RandomRange(-radius, radius),
                    RandomRange(-radius, radius));
                if (point.sqrMagnitude > 0.000001f && point.sqrMagnitude <= radius * radius)
                {
                    return point;
                }
            }

            return Vector3.forward;
        }

        private float RandomRange(float min, float max)
        {
            return Mathf.Lerp(min, max, Next01());
        }
    }
}
