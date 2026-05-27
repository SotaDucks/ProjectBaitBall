using UnityEngine;

namespace TestBoids.Tuna
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed class TunaMotor : MonoBehaviour
    {
        [SerializeField] private TunaInputReader input;
        [SerializeField] private TunaCameraController cameraController;
        [SerializeField] private Transform cameraPivot;

        [Header("Movement")]
        [SerializeField, Min(0f)] private float acceleration = 16f;
        [SerializeField, Min(0f)] private float reverseAccelerationScale = 0.45f;
        [SerializeField, Min(0f)] private float maxSpeed = 9f;
        [SerializeField, Min(0f)] private float waterDrag = 0.75f;
        [SerializeField, Min(0f)] private float idleDragMultiplier = 1.8f;
        [SerializeField, Min(0f)] private float speedLimitDamping = 6f;
        [SerializeField, Range(0f, 1f)] private float directionalThrustBlend = 0.45f;
        [SerializeField, Range(0f, 1f)] private float inputDeadZone = 0.08f;

        [Header("Turning")]
        [SerializeField, Min(0f)] private float turnSpring = 42f;
        [SerializeField, Min(0f)] private float turnDamping = 9f;
        [SerializeField, Min(0f)] private float maxAngularVelocity = 8f;
        [SerializeField, Min(0f)] private float coastFacingSpeed = 0.6f;
        [SerializeField] private bool configureRigidbodyOnAwake = true;

        private Rigidbody body;
        private Vector3 desiredDirection = Vector3.forward;
        private bool hasMoveInput;

        public Vector3 DesiredDirection => desiredDirection;
        public bool HasMoveInput => hasMoveInput;
        public float CurrentTurnAmount { get; private set; }

        private void Reset()
        {
            input = GetComponent<TunaInputReader>();
            cameraController = GetComponentInChildren<TunaCameraController>();
            body = GetComponent<Rigidbody>();
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            if (!input)
            {
                input = GetComponent<TunaInputReader>();
            }

            if (!cameraController)
            {
                cameraController = GetComponentInChildren<TunaCameraController>();
            }

            if (!cameraPivot && cameraController)
            {
                cameraPivot = cameraController.transform;
            }

            desiredDirection = transform.forward;

            if (configureRigidbodyOnAwake)
            {
                body.useGravity = false;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.maxAngularVelocity = maxAngularVelocity;
            }
        }

        private void FixedUpdate()
        {
            Vector2 move = input ? input.Move : Vector2.zero;
            hasMoveInput = move.sqrMagnitude > inputDeadZone * inputDeadZone;

            if (hasMoveInput)
            {
                desiredDirection = BuildDesiredDirection(move);
                ApplyThrust(move);
            }

            ApplyWaterDrag();
            ApplySpeedLimit();
            ApplyTurn();
            UpdateTurnAmount();
        }

        private Vector3 BuildDesiredDirection(Vector2 move)
        {
            Vector3 forward;
            Vector3 right;

            if (cameraController)
            {
                forward = cameraController.Forward;
                right = cameraController.Right;
            }
            else if (cameraPivot)
            {
                forward = cameraPivot.forward;
                right = cameraPivot.right;
            }
            else
            {
                forward = transform.forward;
                right = transform.right;
            }

            Vector3 direction = forward * move.y + right * move.x;
            if (direction.sqrMagnitude <= 0.000001f)
            {
                return transform.forward;
            }

            return direction.normalized;
        }

        private void ApplyThrust(Vector2 move)
        {
            float inputStrength = Mathf.Clamp01(move.magnitude);
            float reverseScale = move.y < -Mathf.Abs(move.x) ? reverseAccelerationScale : 1f;
            Vector3 thrustDirection = Vector3.Slerp(transform.forward, desiredDirection, directionalThrustBlend).normalized;
            body.AddForce(thrustDirection * (acceleration * inputStrength * reverseScale), ForceMode.Acceleration);
        }

        private void ApplyWaterDrag()
        {
            Vector3 velocity = body.linearVelocity;
            if (velocity.sqrMagnitude <= 0.000001f)
            {
                return;
            }

            float drag = hasMoveInput ? waterDrag : waterDrag * idleDragMultiplier;
            body.AddForce(-velocity * drag, ForceMode.Acceleration);
        }

        private void ApplySpeedLimit()
        {
            Vector3 velocity = body.linearVelocity;
            float speed = velocity.magnitude;
            if (speed <= maxSpeed || speed <= 0.0001f)
            {
                return;
            }

            float excessSpeed = speed - maxSpeed;
            body.AddForce(-velocity.normalized * (excessSpeed * speedLimitDamping), ForceMode.Acceleration);
        }

        private void ApplyTurn()
        {
            Vector3 targetDirection = desiredDirection;
            Vector3 velocity = body.linearVelocity;
            if (!hasMoveInput && velocity.sqrMagnitude > coastFacingSpeed * coastFacingSpeed)
            {
                targetDirection = velocity.normalized;
            }

            if (targetDirection.sqrMagnitude <= 0.000001f)
            {
                return;
            }

            Quaternion targetRotation = Quaternion.LookRotation(targetDirection.normalized, BuildDorsal(targetDirection));
            Quaternion rotationError = targetRotation * Quaternion.Inverse(body.rotation);
            rotationError.ToAngleAxis(out float angleDegrees, out Vector3 axis);

            if (angleDegrees > 180f)
            {
                angleDegrees -= 360f;
            }

            if (axis.sqrMagnitude <= 0.000001f || float.IsNaN(axis.x))
            {
                return;
            }

            Vector3 springTorque = axis.normalized * (angleDegrees * Mathf.Deg2Rad * turnSpring);
            Vector3 dampingTorque = -body.angularVelocity * turnDamping;
            body.AddTorque(springTorque + dampingTorque, ForceMode.Acceleration);
        }

        private Vector3 BuildDorsal(Vector3 forward)
        {
            Vector3 dorsal = Vector3.up - forward * Vector3.Dot(Vector3.up, forward);
            if (dorsal.sqrMagnitude > 0.000001f)
            {
                return dorsal.normalized;
            }

            Vector3 fallback = Mathf.Abs(Vector3.Dot(forward.normalized, Vector3.forward)) < 0.92f
                ? Vector3.forward
                : Vector3.right;
            dorsal = fallback - forward * Vector3.Dot(fallback, forward);
            return dorsal.sqrMagnitude > 0.000001f ? dorsal.normalized : Vector3.up;
        }

        private void UpdateTurnAmount()
        {
            Vector3 localAngularVelocity = transform.InverseTransformDirection(body.angularVelocity);
            CurrentTurnAmount = maxAngularVelocity > 0.0001f
                ? Mathf.Clamp(localAngularVelocity.y / maxAngularVelocity, -1f, 1f)
                : 0f;
        }
    }
}
