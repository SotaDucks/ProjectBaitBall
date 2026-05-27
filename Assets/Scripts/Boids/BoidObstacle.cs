using UnityEngine;

namespace TestBoids.Boids
{
    public enum BoidObstacleShape
    {
        Sphere,
        Box,
        Plate
    }

    public sealed class BoidObstacle : MonoBehaviour
    {
        [SerializeField] private BoidObstacleShape shape = BoidObstacleShape.Sphere;
        [SerializeField] private float radius = 1.557692f;
        [SerializeField] private Vector3 size = Vector3.one;

        public BoidObstacleShape Shape => shape;
        public Vector3 Position => transform.position;
        public Quaternion Rotation => transform.rotation;
        public float Radius => Mathf.Max(0f, radius);
        public Vector3 Size => new(Mathf.Max(0f, size.x), Mathf.Max(0f, size.y), Mathf.Max(0f, size.z));

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.35f, 0.25f, 0.35f);
            if (shape == BoidObstacleShape.Sphere)
            {
                Gizmos.DrawWireSphere(Position, Radius);
                return;
            }

            Matrix4x4 previous = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(Position, Rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, Size);
            Gizmos.matrix = previous;
        }
    }
}
