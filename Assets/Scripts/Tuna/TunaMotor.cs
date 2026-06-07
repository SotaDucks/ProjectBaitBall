using System.Collections.Generic;
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

        private enum ControlMode
        {
            Manual,
            Scripted
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
        [Tooltip("Time to ignore look input after the tuna fully re-enters the water.")]
        [SerializeField, Min(0f)] private float airborneLookResumeDelay = 0.15f;
        [Tooltip("Horizontal speed retained once when the tuna breaks through the water surface.")]
        [SerializeField, Range(0f, 1f)] private float airborneEntryHorizontalSpeedRetention = 0.8f;
        [Tooltip("Upward speed retained once when the tuna breaks through the water surface.")]
        [SerializeField, Range(0f, 1f)] private float airborneEntryUpwardSpeedRetention = 0.45f;
        [Tooltip("Maximum speed allowed immediately after leaving the water. Use 0 to disable this cap.")]
        [SerializeField, Min(0f)] private float airborneEntryMaxSpeed = 10f;

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

        [Header("Sprint")]
        [SerializeField] private bool enableClickSprint = true;
        [Tooltip("Recent clicks required before a sprint can trigger.")]
        [SerializeField, Min(2)] private int sprintRequiredClicks = 3;
        [Tooltip("Only clicks inside this rolling time window count toward sprint.")]
        [SerializeField, Min(0.01f)] private float sprintClickWindow = 0.75f;
        [Tooltip("Seconds sprint remains active after the latest valid click cadence.")]
        [SerializeField, Min(0f)] private float sprintDuration = 0.45f;
        [Tooltip("Seconds after sprint fully drops before another sprint can start.")]
        [SerializeField, Min(0f)] private float sprintCooldown = 0.15f;
        [Tooltip("Slowest average click interval that still triggers tier 1 sprint.")]
        [SerializeField, Min(0f)] private float sprintTier1MaxAverageClickInterval = 0.3f;
        [Tooltip("Average click interval required for tier 2 sprint.")]
        [SerializeField, Min(0f)] private float sprintTier2MaxAverageClickInterval = 0.22f;
        [Tooltip("Average click interval required for tier 3 sprint.")]
        [SerializeField, Min(0f)] private float sprintTier3MaxAverageClickInterval = 0.15f;
        [SerializeField, Min(0f)] private float sprintTier1MaxSpeed = 12f;
        [SerializeField, Min(0f)] private float sprintTier2MaxSpeed = 15f;
        [SerializeField, Min(0f)] private float sprintTier3MaxSpeed = 18f;
        [SerializeField, Min(0f)] private float sprintTier1Acceleration = 28f;
        [SerializeField, Min(0f)] private float sprintTier2Acceleration = 36f;
        [SerializeField, Min(0f)] private float sprintTier3Acceleration = 46f;

        [Header("Stamina")]
        [SerializeField, Min(0.01f)] private float maxStamina = 100f;
        [SerializeField, Min(0f)] private float sprintStaminaDrainPerSecond = 35f;
        [SerializeField, Min(0f)] private float staminaRecoveryPerSecond = 20f;

        [Header("Hunger")]
        [SerializeField, Min(0.01f)] private float maxHunger = 100f;
        [SerializeField, Min(0f)] private float initialHunger = 0f;

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
        private ControlMode controlMode = ControlMode.Manual;
        private float airbornePitchAmount;
        private Vector3 scriptedDirection = Vector3.forward;
        private float scriptedTurnInput;
        private float scriptedTurnSpeedScale = 1f;
        private readonly Queue<float> sprintClickTimes = new Queue<float>();
        private int currentSprintTier;
        private float currentSprintMaxSpeed;
        private float currentSprintAcceleration;
        private float sprintEndsAt;
        private float sprintCooldownEndsAt;
        private float currentStamina;
        private float currentHunger;
        private float staminaRecoveryStartsAt;
        private const float StaminaRecoveryDelay = 1f;

        public Vector3 DesiredDirection => desiredDirection;
        public bool HasMoveInput => hasMoveInput;
        public bool IsAirborne => locomotionState == LocomotionState.Airborne;
        public bool IsScripted => controlMode == ControlMode.Scripted;
        public bool IsSprinting => currentSprintTier > 0 && Time.time < sprintEndsAt;
        public int CurrentSprintTier => IsSprinting ? currentSprintTier : 0;
        public float MaxHunger => maxHunger;
        public float CurrentHunger => currentHunger;
        public float HungerPercent => maxHunger > 0f ? Mathf.Clamp01(currentHunger / maxHunger) : 0f;
        public float StaminaPercent => maxStamina > 0f ? Mathf.Clamp01(currentStamina / maxStamina) : 0f;
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
            currentStamina = maxStamina;
            currentHunger = Mathf.Clamp(initialHunger, 0f, maxHunger);

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
            airborneLookResumeDelay = Mathf.Max(0f, airborneLookResumeDelay);
            airborneEntryHorizontalSpeedRetention = Mathf.Clamp01(airborneEntryHorizontalSpeedRetention);
            airborneEntryUpwardSpeedRetention = Mathf.Clamp01(airborneEntryUpwardSpeedRetention);
            airborneEntryMaxSpeed = Mathf.Max(0f, airborneEntryMaxSpeed);
            sprintRequiredClicks = Mathf.Max(2, sprintRequiredClicks);
            sprintClickWindow = Mathf.Max(0.01f, sprintClickWindow);
            sprintDuration = Mathf.Max(0f, sprintDuration);
            sprintCooldown = Mathf.Max(0f, sprintCooldown);
            sprintTier1MaxAverageClickInterval = Mathf.Max(0f, sprintTier1MaxAverageClickInterval);
            sprintTier2MaxAverageClickInterval = Mathf.Max(0f, sprintTier2MaxAverageClickInterval);
            sprintTier3MaxAverageClickInterval = Mathf.Max(0f, sprintTier3MaxAverageClickInterval);
            maxStamina = Mathf.Max(0.01f, maxStamina);
            sprintStaminaDrainPerSecond = Mathf.Max(0f, sprintStaminaDrainPerSecond);
            staminaRecoveryPerSecond = Mathf.Max(0f, staminaRecoveryPerSecond);
            maxHunger = Mathf.Max(0.01f, maxHunger);
            initialHunger = Mathf.Clamp(initialHunger, 0f, maxHunger);
        }

        private void FixedUpdate()
        {
            UpdateLocomotionState();

            if (locomotionState == LocomotionState.Airborne)
            {
                UpdateSprintState();
                UpdateStamina();
                DiscardSprintClickInput();
                ClearControlState();
                UpdateAirborneMotion();
                UpdateTurnAmount();
                return;
            }

            Vector2 move = ReadControlMove();
            hasMoveInput = move.sqrMagnitude > inputDeadZone * inputDeadZone;
            hasThrustInput = Mathf.Abs(move.y) > inputDeadZone;
            turnInput = Mathf.Abs(move.x) > inputDeadZone ? Mathf.Clamp(move.x, -1f, 1f) : 0f;
            desiredDirection = BuildDesiredDirection(hasThrustInput ? move.y : 1f);

            UpdateSprintClickInput();
            UpdateSprintState();
            UpdateStamina();

            if (hasThrustInput)
            {
                ApplyThrust(move);
            }

            ApplySprint();
            ApplyWaterDrag();
            ApplySpeedLimit();
            ApplyMinimumSwimSpeed();
            ApplyTurn();
            UpdateTurnAmount();
        }

        public void BeginScriptedSwim(Vector3 worldDirection, float turnInput, float turnSpeedScale = 1f)
        {
            controlMode = ControlMode.Scripted;
            scriptedDirection = ResolveScriptedDirection(worldDirection);
            scriptedTurnInput = Mathf.Clamp(turnInput, -1f, 1f);
            scriptedTurnSpeedScale = Mathf.Max(0f, turnSpeedScale);
            ClearSprintState();
        }

        public void SetScriptedSwimDirection(Vector3 worldDirection)
        {
            scriptedDirection = ResolveScriptedDirection(worldDirection);
        }

        public void EndScriptedSwim()
        {
            if (controlMode != ControlMode.Scripted)
            {
                return;
            }

            controlMode = ControlMode.Manual;
            scriptedTurnSpeedScale = 1f;
            ClearControlState();
        }

        private Vector2 ReadControlMove()
        {
            if (controlMode == ControlMode.Scripted)
            {
                return new Vector2(scriptedTurnInput, 0f);
            }

            return input ? input.Move : Vector2.zero;
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
            bool wasAirborne = locomotionState == LocomotionState.Airborne;
            locomotionState = LocomotionState.Swimming;
            airbornePitchAmount = 0f;

            if (body)
            {
                body.useGravity = false;
            }

            if (wasAirborne && cameraController)
            {
                cameraController.ResetLookToForward(transform.forward, airborneLookResumeDelay);
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
                ApplyAirborneEntrySpeedLoss();
            }

            ClearSprintState();
            if (input)
            {
                input.ClearSprintClicks();
            }

            SetSwayLowSpeedOverride(true);
        }

        private void ApplyAirborneEntrySpeedLoss()
        {
            Vector3 velocity = body.linearVelocity;
            if (velocity.sqrMagnitude <= 0.000001f)
            {
                return;
            }

            Vector3 horizontalVelocity = Vector3.ProjectOnPlane(velocity, Vector3.up) * airborneEntryHorizontalSpeedRetention;
            float verticalSpeed = velocity.y > 0f
                ? velocity.y * airborneEntryUpwardSpeedRetention
                : velocity.y;
            Vector3 reducedVelocity = horizontalVelocity + Vector3.up * verticalSpeed;

            if (airborneEntryMaxSpeed > 0f && reducedVelocity.sqrMagnitude > airborneEntryMaxSpeed * airborneEntryMaxSpeed)
            {
                reducedVelocity = reducedVelocity.normalized * airborneEntryMaxSpeed;
            }

            body.linearVelocity = reducedVelocity;
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

        private void ClearSprintState()
        {
            bool hadSprintState = currentSprintTier > 0;
            sprintClickTimes.Clear();
            currentSprintTier = 0;
            currentSprintMaxSpeed = 0f;
            currentSprintAcceleration = 0f;
            sprintEndsAt = 0f;
            sprintCooldownEndsAt = 0f;

            if (hadSprintState)
            {
                staminaRecoveryStartsAt = Time.time + StaminaRecoveryDelay;
            }
        }

        private Vector3 BuildDesiredDirection(float thrustInput)
        {
            if (controlMode == ControlMode.Scripted)
            {
                return scriptedDirection * Mathf.Sign(thrustInput);
            }

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

        private Vector3 ResolveScriptedDirection(Vector3 worldDirection)
        {
            if (worldDirection.sqrMagnitude > 0.000001f)
            {
                return worldDirection.normalized;
            }

            if (desiredDirection.sqrMagnitude > 0.000001f)
            {
                return desiredDirection.normalized;
            }

            return transform.forward.sqrMagnitude > 0.000001f ? transform.forward.normalized : Vector3.forward;
        }

        private void ApplyThrust(Vector2 move)
        {
            float inputStrength = Mathf.Clamp01(Mathf.Abs(move.y));
            float reverseScale = move.y < 0f ? reverseAccelerationScale : 1f;
            Vector3 thrustDirection = Vector3.Slerp(transform.forward, desiredDirection, directionalThrustBlend).normalized;
            body.AddForce(thrustDirection * (acceleration * inputStrength * reverseScale), ForceMode.Acceleration);
        }

        private void UpdateSprintState()
        {
            if (currentSprintTier > 0 && Time.time >= sprintEndsAt)
            {
                EndSprintWithCooldown();
            }
        }

        private void UpdateStamina()
        {
            if (IsSprinting)
            {
                if (sprintStaminaDrainPerSecond > 0f)
                {
                    currentStamina = Mathf.Max(
                        0f,
                        currentStamina - sprintStaminaDrainPerSecond * Time.fixedDeltaTime);
                }

                staminaRecoveryStartsAt = Time.time + StaminaRecoveryDelay;

                if (currentStamina <= 0f)
                {
                    EndSprintWithCooldown();
                }

                return;
            }

            if (currentStamina >= maxStamina || Time.time < staminaRecoveryStartsAt)
            {
                return;
            }

            currentStamina = Mathf.Min(
                maxStamina,
                currentStamina + staminaRecoveryPerSecond * Time.fixedDeltaTime);
        }

        private void UpdateSprintClickInput()
        {
            if (!input)
            {
                return;
            }

            if (!enableClickSprint || controlMode != ControlMode.Manual)
            {
                DiscardSprintClickInput();
                return;
            }

            while (input.TryConsumeSprintClick(out float clickTime))
            {
                RecordSprintClick(clickTime);
            }
        }

        private void DiscardSprintClickInput()
        {
            if (!input)
            {
                return;
            }

            input.ClearSprintClicks();
        }

        private void RecordSprintClick(float clickTime)
        {
            sprintClickTimes.Enqueue(clickTime);
            TrimSprintClicks(clickTime);

            int tier = ResolveSprintTier(clickTime);
            if (tier <= 0)
            {
                return;
            }

            if (!IsSprinting && Time.time < sprintCooldownEndsAt)
            {
                return;
            }

            if (currentStamina <= 0f)
            {
                return;
            }

            RefreshSprint(tier);
        }

        private void TrimSprintClicks(float now)
        {
            while (sprintClickTimes.Count > 0 && now - sprintClickTimes.Peek() > sprintClickWindow)
            {
                sprintClickTimes.Dequeue();
            }
        }

        private int ResolveSprintTier(float latestClickTime)
        {
            if (sprintClickTimes.Count < sprintRequiredClicks)
            {
                return 0;
            }

            float averageInterval = CalculateRecentAverageClickInterval(latestClickTime);
            if (sprintTier3MaxAverageClickInterval > 0f && averageInterval <= sprintTier3MaxAverageClickInterval)
            {
                return 3;
            }

            if (sprintTier2MaxAverageClickInterval > 0f && averageInterval <= sprintTier2MaxAverageClickInterval)
            {
                return 2;
            }

            if (sprintTier1MaxAverageClickInterval > 0f && averageInterval <= sprintTier1MaxAverageClickInterval)
            {
                return 1;
            }

            return 0;
        }

        private float CalculateRecentAverageClickInterval(float latestClickTime)
        {
            int skipCount = sprintClickTimes.Count - sprintRequiredClicks;
            int index = 0;
            float firstRelevantClickTime = latestClickTime;

            foreach (float clickTime in sprintClickTimes)
            {
                if (index == skipCount)
                {
                    firstRelevantClickTime = clickTime;
                    break;
                }

                index++;
            }

            return (latestClickTime - firstRelevantClickTime) / (sprintRequiredClicks - 1);
        }

        private void RefreshSprint(int tier)
        {
            currentSprintTier = tier;
            currentSprintMaxSpeed = GetSprintMaxSpeed(tier);
            currentSprintAcceleration = GetSprintAcceleration(tier);
            sprintEndsAt = Time.time + sprintDuration;
        }

        private void EndSprintWithCooldown()
        {
            currentSprintTier = 0;
            currentSprintMaxSpeed = 0f;
            currentSprintAcceleration = 0f;
            sprintEndsAt = 0f;
            sprintCooldownEndsAt = Time.time + sprintCooldown;
            staminaRecoveryStartsAt = Time.time + StaminaRecoveryDelay;
        }

        private float GetSprintMaxSpeed(int tier)
        {
            switch (tier)
            {
                case 3:
                    return sprintTier3MaxSpeed;
                case 2:
                    return sprintTier2MaxSpeed;
                default:
                    return sprintTier1MaxSpeed;
            }
        }

        private float GetSprintAcceleration(int tier)
        {
            switch (tier)
            {
                case 3:
                    return sprintTier3Acceleration;
                case 2:
                    return sprintTier2Acceleration;
                default:
                    return sprintTier1Acceleration;
            }
        }

        private void ApplySprint()
        {
            if (!IsSprinting || currentSprintAcceleration <= 0f)
            {
                return;
            }

            Vector3 sprintDirection = Vector3.Slerp(transform.forward, desiredDirection, directionalThrustBlend);
            if (sprintDirection.sqrMagnitude <= 0.000001f)
            {
                return;
            }

            body.AddForce(sprintDirection.normalized * currentSprintAcceleration, ForceMode.Acceleration);
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
            float effectiveMaxSpeed = GetEffectiveMaxSpeed();
            if (speed <= effectiveMaxSpeed || speed <= 0.0001f)
            {
                return;
            }

            float excessSpeed = speed - effectiveMaxSpeed;
            body.AddForce(-velocity.normalized * (excessSpeed * speedLimitDamping), ForceMode.Acceleration);
        }

        private void ApplyMinimumSwimSpeed()
        {
            if (minimumSwimSpeed <= 0f)
            {
                return;
            }

            float effectiveMaxSpeed = GetEffectiveMaxSpeed();
            float effectiveMinimumSpeed = effectiveMaxSpeed > 0f ? Mathf.Min(minimumSwimSpeed, effectiveMaxSpeed) : minimumSwimSpeed;
            Vector3 velocity = body.linearVelocity;
            if (controlMode == ControlMode.Scripted)
            {
                body.linearVelocity = desiredDirection.normalized * effectiveMinimumSpeed;
                return;
            }

            float speed = velocity.magnitude;
            if (speed >= effectiveMinimumSpeed)
            {
                return;
            }

            body.linearVelocity = desiredDirection.normalized * effectiveMinimumSpeed;
        }

        private float GetEffectiveMaxSpeed()
        {
            return IsSprinting ? Mathf.Max(maxSpeed, currentSprintMaxSpeed) : maxSpeed;
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

            float effectiveTurnSpring = controlMode == ControlMode.Scripted
                ? turnSpring * scriptedTurnSpeedScale
                : turnSpring;
            Vector3 springTorque = axis.normalized * (angleDegrees * Mathf.Deg2Rad * effectiveTurnSpring);
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
