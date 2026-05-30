using UnityEngine;

namespace TestBoids.Tuna
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed class TunaMotor : MonoBehaviour
    {
        private enum LocomotionState
        {
            Swimming,
            Airborne
        }

        [SerializeField] private TunaInputReader input;
        [SerializeField] private TunaCameraController cameraController;
        [SerializeField] private Transform cameraPivot;

        [Header("Water")]
        [Tooltip("World-space Y height of the water surface. Tuna becomes airborne above this height.")]
        [SerializeField] private float waterSurfaceHeight;
        [Tooltip("Distance below the water surface required before swimming control returns.")]
        [SerializeField, Min(0f)] private float fullySubmergedDepth = 0.75f;

        [Header("Airborne")]
        [Tooltip("TunaSway component to force into low-speed sway while the tuna is out of the water.")]
        [SerializeField] private TunaSway sway;
        [Tooltip("Degrees per second added around the tuna's local X axis while airborne.")]
        [SerializeField, Min(0f)] private float airbornePitchSpeed = 90f;
        [Tooltip("Maximum local X-axis pitch applied after leaving the water.")]
        [SerializeField, Min(0f)] private float maxAirbornePitchAngle = 35f;
        [Tooltip("Use 1 or -1 to choose which local X direction points the tuna nose-down.")]
        [SerializeField] private float airbornePitchDirection = 1f;

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
        private LocomotionState locomotionState = LocomotionState.Swimming;
        private float airbornePitchAmount;

        public Vector3 DesiredDirection => desiredDirection;
        public bool HasMoveInput => hasMoveInput;
        public bool IsAirborne => locomotionState == LocomotionState.Airborne;
        public float CurrentBankInput => bankAmount;
        public float CurrentTurnAmount { get; private set; }

        private void Reset()
        {
            input = GetComponent<TunaInputReader>();
            cameraController = GetComponentInChildren<TunaCameraController>();
            sway = GetComponentInChildren<TunaSway>();
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

            if (!sway)
            {
                sway = GetComponentInChildren<TunaSway>();
            }

            desiredDirection = transform.forward;

            if (configureRigidbodyOnAwake)
            {
                body.useGravity = false;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.maxAngularVelocity = maxAngularVelocity;
            }

            if (IsAboveWaterSurface())
            {
                EnterAirborne();
            }
            else
            {
                EnterSwimming();
            }
        }

        private void OnValidate()
        {
            fullySubmergedDepth = Mathf.Max(0f, fullySubmergedDepth);
            airbornePitchSpeed = Mathf.Max(0f, airbornePitchSpeed);
            maxAirbornePitchAngle = Mathf.Max(0f, maxAirbornePitchAngle);
        }

        private void FixedUpdate()
        {
            UpdateLocomotionState();
            if (locomotionState == LocomotionState.Airborne)
            {
                ClearControlState();
                UpdateAirborneMotion();
                UpdateTurnAmount();
                return;
            }

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

        private void UpdateLocomotionState()
        {
            if (locomotionState == LocomotionState.Swimming)
            {
                if (IsAboveWaterSurface())
                {
                    EnterAirborne();
                }

                return;
            }

            if (IsFullySubmerged())
            {
                EnterSwimming();
            }
        }

        private void EnterSwimming()
        {
            locomotionState = LocomotionState.Swimming;
            airbornePitchAmount = 0f;

            if (body)
            {
                body.useGravity = false;
            }

            SetSwayLowSpeedOverride(false);
        }

        private void EnterAirborne()
        {
            locomotionState = LocomotionState.Airborne;
            airbornePitchAmount = 0f;
            ClearControlState();

            if (body)
            {
                body.useGravity = true;
            }

            SetSwayLowSpeedOverride(true);
        }

        private void UpdateAirborneMotion()
        {
            if (!body || airbornePitchSpeed <= 0f || maxAirbornePitchAngle <= 0f)
            {
                return;
            }

            float remainingPitch = maxAirbornePitchAngle - airbornePitchAmount;
            if (remainingPitch <= 0f || Mathf.Approximately(airbornePitchDirection, 0f))
            {
                return;
            }

            float pitchDelta = Mathf.Min(airbornePitchSpeed * Time.fixedDeltaTime, remainingPitch);
            Quaternion pitchRotation = Quaternion.AngleAxis(
                pitchDelta * Mathf.Sign(airbornePitchDirection),
                Vector3.right);
            body.MoveRotation(body.rotation * pitchRotation);
            airbornePitchAmount += pitchDelta;
        }

        private void SetSwayLowSpeedOverride(bool forced)
        {
            if (sway)
            {
                sway.ForceLowSpeed = forced;
            }
        }

        private bool IsAboveWaterSurface()
        {
            return transform.position.y > waterSurfaceHeight;
        }

        private bool IsFullySubmerged()
        {
            return transform.position.y <= waterSurfaceHeight - fullySubmergedDepth;
        }

        private void ClearControlState()
        {
            hasMoveInput = false;
            hasThrustInput = false;
            turnInput = 0f;
            bankAmount = 0f;
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
