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
        [SerializeField, Min(0f)] private float minimumSwimSpeed = 1.5f;
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

        [Header("Banking")]
        [SerializeField, Min(0f)] private float maxBankAngle = 35f;
        [SerializeField, Range(0f, 1f)] private float manualBankBlend = 0.35f;
        [SerializeField] private float bankDirection = -1f;

        private Rigidbody body;
        private Vector3 desiredDirection = Vector3.forward;
        private bool hasMoveInput;
        private bool hasThrustInput;
        private float turnInput;
        private float bankAmount;

        public Vector3 DesiredDirection => desiredDirection;
        public bool HasMoveInput => hasMoveInput;
        public float CurrentBankInput => bankAmount;
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
            hasThrustInput = Mathf.Abs(move.y) > inputDeadZone;
            turnInput = Mathf.Abs(move.x) > inputDeadZone ? Mathf.Clamp(move.x, -1f, 1f) : 0f;
            desiredDirection = BuildDesiredDirection(hasThrustInput ? move.y : 1f);

            if (hasThrustInput)
            {
                ApplyThrust(move);
            }

            ApplyWaterDrag();
            ApplySpeedLimit();
            ApplyMinimumSwimSpeed();
            ApplyTurn();
            UpdateTurnAmount();
        }

        private Vector3 BuildDesiredDirection(float thrustInput)
        {
            if (cameraController)
            {
                return cameraController.Forward * Mathf.Sign(thrustInput);
            }

            if (cameraPivot)
            {
                return cameraPivot.forward * Mathf.Sign(thrustInput);
            }

            return transform.forward * Mathf.Sign(thrustInput);
        }

        private void ApplyThrust(Vector2 move)
        {
            float inputStrength = Mathf.Clamp01(Mathf.Abs(move.y));
            float reverseScale = move.y < 0f ? reverseAccelerationScale : 1f;
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

        private void ApplyMinimumSwimSpeed()
        {
            if (minimumSwimSpeed <= 0f)
            {
                return;
            }

            float effectiveMinimumSpeed = maxSpeed > 0f ? Mathf.Min(minimumSwimSpeed, maxSpeed) : minimumSwimSpeed;
            Vector3 velocity = body.linearVelocity;
            float speed = velocity.magnitude;
            if (speed >= effectiveMinimumSpeed)
            {
                return;
            }

            body.linearVelocity = desiredDirection.normalized * effectiveMinimumSpeed;
        }

        private void ApplyTurn()
        {
            Vector3 targetDirection = desiredDirection;
            Vector3 velocity = body.linearVelocity;
            if (!hasThrustInput)
            {
                if (Mathf.Abs(turnInput) > 0f || velocity.sqrMagnitude <= coastFacingSpeed * coastFacingSpeed || minimumSwimSpeed > 0f)
                {
                    targetDirection = desiredDirection;
                }
                else
                {
                    targetDirection = velocity.normalized;
                }
            }

            if (targetDirection.sqrMagnitude <= 0.000001f)
            {
                return;
            }

            float targetBankAmount = BuildBankAmount();
            Quaternion targetRotation = Quaternion.LookRotation(targetDirection.normalized, BuildDorsal(targetDirection, targetBankAmount));
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

        private Vector3 BuildDorsal(Vector3 forward, float bankInput)
        {
            Vector3 dorsal = Vector3.up - forward * Vector3.Dot(Vector3.up, forward);
            if (dorsal.sqrMagnitude > 0.000001f)
            {
                dorsal = dorsal.normalized;
                return Quaternion.AngleAxis(bankInput * maxBankAngle * bankDirection, forward.normalized) * dorsal;
            }

            Vector3 fallback = Mathf.Abs(Vector3.Dot(forward.normalized, Vector3.forward)) < 0.92f
                ? Vector3.forward
                : Vector3.right;
            dorsal = fallback - forward * Vector3.Dot(fallback, forward);
            dorsal = dorsal.sqrMagnitude > 0.000001f ? dorsal.normalized : Vector3.up;
            return Quaternion.AngleAxis(bankInput * maxBankAngle * bankDirection, forward.normalized) * dorsal;
        }

        private float BuildBankAmount()
        {
            Vector3 localAngularVelocity = transform.InverseTransformDirection(body.angularVelocity);
            float turnBank = maxAngularVelocity > 0.0001f
                ? Mathf.Clamp(localAngularVelocity.y / maxAngularVelocity, -1f, 1f)
                : 0f;
            bankAmount = Mathf.Clamp(turnBank + turnInput * manualBankBlend, -1f, 1f);
            return bankAmount;
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
