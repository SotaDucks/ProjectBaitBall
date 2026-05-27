using UnityEngine;

namespace TestBoids.Tuna
{
    public sealed class TunaCameraController : MonoBehaviour
    {
        [SerializeField] private TunaInputReader input;
        [SerializeField] private Transform cameraPivot;

        [Header("Look")]
        [SerializeField] private Vector2 sensitivity = new(0.12f, 0.12f);
        [SerializeField] private bool invertY;
        [SerializeField] private bool scaleLookByDeltaTime;
        [SerializeField] private float minimumPitch = -65f;
        [SerializeField] private float maximumPitch = 65f;

        private float yaw;
        private float pitch;

        public Quaternion LookRotation => Quaternion.Euler(pitch, yaw, 0f);
        public Vector3 Forward => LookRotation * Vector3.forward;
        public Vector3 Right => LookRotation * Vector3.right;

        private void Reset()
        {
            input = GetComponentInParent<TunaInputReader>();
            cameraPivot = transform;
        }

        private void Awake()
        {
            if (!input)
            {
                input = GetComponentInParent<TunaInputReader>();
            }

            if (!cameraPivot)
            {
                cameraPivot = transform;
            }

            Vector3 euler = cameraPivot.rotation.eulerAngles;
            yaw = NormalizeAngle(euler.y);
            pitch = Mathf.Clamp(NormalizeAngle(euler.x), minimumPitch, maximumPitch);
        }

        private void LateUpdate()
        {
            if (!cameraPivot)
            {
                return;
            }

            Vector2 look = input ? input.Look : Vector2.zero;
            float scale = scaleLookByDeltaTime ? Time.deltaTime : 1f;

            yaw += look.x * sensitivity.x * scale;
            pitch += (invertY ? look.y : -look.y) * sensitivity.y * scale;
            pitch = Mathf.Clamp(pitch, minimumPitch, maximumPitch);

            cameraPivot.rotation = LookRotation;
        }

        private static float NormalizeAngle(float angle)
        {
            angle %= 360f;
            if (angle > 180f)
            {
                angle -= 360f;
            }
            else if (angle < -180f)
            {
                angle += 360f;
            }

            return angle;
        }
    }
}
