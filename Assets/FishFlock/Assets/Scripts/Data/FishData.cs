using UnityEngine;

namespace FishFlock
{
    public enum FishMovementAxis
    {
        XYZ,
        XY,
        XZ,
    };

    public struct FishData
    {
        public Vector3 position;
        public Vector3 velocity;
        public float speed;
        public float rot_speed;
        public float speed_offset;
        public float scale;
    }
}