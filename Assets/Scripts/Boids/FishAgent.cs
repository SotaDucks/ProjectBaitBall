using UnityEngine;

namespace TestBoids.Boids
{
    public sealed class FishAgent : MonoBehaviour
    {
        [SerializeField] private Vector3 localForwardAxis = Vector3.forward;
        [SerializeField] private Vector3 localDorsalAxis = Vector3.up;
        [SerializeField] private bool applyBank = true;

        private Quaternion localAxisCorrection = Quaternion.identity;

        public Vector3 Position { get; private set; }
        public Vector3 Velocity { get; private set; }
        public float Bank { get; private set; }

        private void Awake()
        {
            RefreshAxisCorrection();
        }

        private void OnValidate()
        {
            RefreshAxisCorrection();
        }

        public void Initialize(Vector3 position, Vector3 velocity, float bank)
        {
            Position = position;
            Velocity = velocity;
            Bank = bank;
            ApplyPose(position, velocity, bank);
        }

        public void ApplyPose(Vector3 position, Vector3 velocity, float bank)
        {
            Position = position;
            Velocity = velocity;
            Bank = bank;

            transform.position = position;

            if (velocity.sqrMagnitude <= 0.000001f)
            {
                return;
            }

            Vector3 forward = velocity.normalized;
            Vector3 dorsal = Vector3.up - forward * Vector3.Dot(Vector3.up, forward);
            if (dorsal.sqrMagnitude < 0.000001f)
            {
                Vector3 fallback = Mathf.Abs(Vector3.Dot(forward, Vector3.forward)) < 0.92f
                    ? Vector3.forward
                    : Vector3.right;
                dorsal = fallback - forward * Vector3.Dot(fallback, forward);
            }

            dorsal.Normalize();
            if (applyBank && Mathf.Abs(bank) > 0.000001f)
            {
                dorsal = Quaternion.AngleAxis(bank * Mathf.Rad2Deg, forward) * dorsal;
            }

            transform.rotation = Quaternion.LookRotation(forward, dorsal) * localAxisCorrection;
        }

        private void RefreshAxisCorrection()
        {
            Vector3 forward = localForwardAxis.sqrMagnitude > 0.000001f
                ? localForwardAxis.normalized
                : Vector3.forward;
            Vector3 dorsal = localDorsalAxis.sqrMagnitude > 0.000001f
                ? localDorsalAxis.normalized
                : Vector3.up;

            dorsal -= forward * Vector3.Dot(dorsal, forward);
            if (dorsal.sqrMagnitude < 0.000001f)
            {
                dorsal = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) < 0.92f
                    ? Vector3.up
                    : Vector3.forward;
                dorsal -= forward * Vector3.Dot(dorsal, forward);
            }

            localAxisCorrection = Quaternion.Inverse(Quaternion.LookRotation(forward, dorsal.normalized));
        }
    }
}
