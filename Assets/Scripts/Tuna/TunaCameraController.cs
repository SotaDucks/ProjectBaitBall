using UnityEngine;

namespace TestBoids.Tuna
{
    public sealed class TunaCameraController : MonoBehaviour
    {
        [SerializeField] private TunaInputReader input;
        [SerializeField] private Transform cameraPivot;

        [Header("Follow")]
        public Transform followTarget;
        public Vector3 followOffset;
        public Vector3 positionDamping = Vector3.zero;

        [Header("Look")]
        [SerializeField] private Vector2 sensitivity = new(0.12f, 0.12f);
        [SerializeField] private bool invertY;
        [SerializeField] private bool scaleLookByDeltaTime;
        [SerializeField] private float minimumPitch = -65f;
        [SerializeField] private float maximumPitch = 65f;

        [Header("Angular Damping")]
        [Min(0f)] public float pitchDamping;
        [Min(0f)] public float yawDamping;
        [Min(0f)] public float rollDamping;

        private float yaw;
        private float pitch;
        private float roll;
        private float dampedYaw;
        private float dampedPitch;
        private float dampedRoll;
        private float yawVelocity;
        private float pitchVelocity;
        private float rollVelocity;
        private Vector3 positionVelocity;

        public Quaternion LookRotation => Quaternion.Euler(dampedPitch, dampedYaw, dampedRoll);
        public Vector3 Forward => LookRotation * Vector3.forward;
        public Vector3 Right => LookRotation * Vector3.right;

        private void Reset()
        {
            input = GetComponentInParent<TunaInputReader>();
            cameraPivot = transform;
            followTarget = cameraPivot.parent;
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

            if (!followTarget && cameraPivot.parent)
            {
                followTarget = cameraPivot.parent;
            }

            if (followTarget && followOffset == Vector3.zero)
            {
                followOffset = followTarget.InverseTransformPoint(cameraPivot.position);
            }

            Vector3 euler = cameraPivot.rotation.eulerAngles;
            yaw = NormalizeAngle(euler.y);
            pitch = Mathf.Clamp(NormalizeAngle(euler.x), minimumPitch, maximumPitch);
            roll = 0f;
            dampedYaw = yaw;
            dampedPitch = pitch;
            dampedRoll = NormalizeAngle(euler.z);
        }

        private void LateUpdate()
        {
            if (!cameraPivot)
            {
                return;
            }

            ApplyPositionDamping();

            Vector2 look = input ? input.Look : Vector2.zero;
            float scale = scaleLookByDeltaTime ? Time.deltaTime : 1f;

            yaw += look.x * sensitivity.x * scale;
            pitch += (invertY ? look.y : -look.y) * sensitivity.y * scale;
            pitch = Mathf.Clamp(pitch, minimumPitch, maximumPitch);

            dampedYaw = DampAngle(dampedYaw, yaw, ref yawVelocity, yawDamping);
            dampedPitch = DampAngle(dampedPitch, pitch, ref pitchVelocity, pitchDamping);
            dampedRoll = DampAngle(dampedRoll, roll, ref rollVelocity, rollDamping);

            cameraPivot.rotation = LookRotation;
        }

        private void ApplyPositionDamping()
        {
            if (!followTarget)
            {
                return;
            }

            Vector3 targetPosition = followTarget.TransformPoint(followOffset);
            cameraPivot.position = new Vector3(
                DampPositionAxis(cameraPivot.position.x, targetPosition.x, ref positionVelocity.x, positionDamping.x),
                DampPositionAxis(cameraPivot.position.y, targetPosition.y, ref positionVelocity.y, positionDamping.y),
                DampPositionAxis(cameraPivot.position.z, targetPosition.z, ref positionVelocity.z, positionDamping.z));
        }

        private static float DampAngle(float current, float target, ref float velocity, float damping)
        {
            if (damping <= 0f)
            {
                velocity = 0f;
                return target;
            }

            return Mathf.SmoothDampAngle(current, target, ref velocity, damping, Mathf.Infinity, Time.deltaTime);
        }

        private static float DampPositionAxis(float current, float target, ref float velocity, float damping)
        {
            if (damping <= 0f)
            {
                velocity = 0f;
                return target;
            }

            return Mathf.SmoothDamp(current, target, ref velocity, damping, Mathf.Infinity, Time.deltaTime);
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
