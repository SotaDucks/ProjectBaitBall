using System.Collections.Generic;
using TestBoids.Gameplay;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace TestBoids.Tuna
{
    [DefaultExecutionOrder(-50)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class TunaOnHookController : MonoBehaviour
    {
        private enum HookControlState
        {
            Dragged,
            Turnaround,
            Escaping
        }

        [Header("References")]
        public Transform pullTarget;
        [SerializeField] private GameStateManager stateManager;
        [SerializeField] private TunaMotor tunaMotor;
        [SerializeField] private TunaInputReader input;

        [Header("Dragged")]
        [SerializeField, Min(0f)] private float pullAcceleration = 8f;
        [SerializeField, Min(0f)] private float draggedMaxSpeed = 7f;
        [SerializeField, Min(0f)] private float draggedSpeedLimitDamping = 4f;
        [SerializeField, Min(0f)] private float draggedVelocityDamping = 0.35f;
        [SerializeField, Min(0f)] private float draggedRotationSpeed = 540f;
        public Vector2 draggedRollAngleRange = new Vector2(-45f, 45f);
        [Min(0f)] public float draggedRollCycleSpeed = 8f;
        [SerializeField, Min(0f)] private float angularVelocityDamping = 8f;
        [SerializeField, Range(0f, 45f)] private float draggedSteerLimitAngle = 10f;
        [SerializeField, Min(0f)] private float draggedMouseDegreesPerUnit = 0.04f;
        [SerializeField, Min(0f)] private float draggedSteerReturnSpeed = 30f;

        [Header("Player Steering")]
        [Min(0f)] public float playerSteeringSpeedMultiplier = 0.3f;

        [Header("Escape Trigger")]
        [SerializeField, Range(0f, 1f)] private float escapeMoveThreshold = 0.25f;
        [SerializeField, Min(2)] private int escapeRequiredClicks = 3;
        [SerializeField, Min(0.01f)] private float escapeClickWindow = 0.75f;
        [SerializeField, Min(0.01f)] private float escapeMaxAverageClickInterval = 0.25f;
        [SerializeField, Min(0f)] private float clickEscapeDuration = 0.6f;

        [Header("Turnaround")]
        [SerializeField, Min(0f)] private float turnaroundRotationSpeed = 720f;
        [SerializeField, Min(0f)] private float turnaroundMinimumDuration = 0.18f;
        [SerializeField, Range(0f, 90f)] private float turnaroundCompleteAngle = 8f;
        [SerializeField, Range(0f, 1f)] private float turnaroundPullScale = 0.35f;
        [SerializeField, Min(0f)] private float turnaroundVelocityDamping = 1.4f;

        [Header("Escape Steering")]
        [SerializeField, Range(0f, 90f)] private float escapeSteerLimitAngle = 28f;
        [SerializeField, Min(0f)] private float escapeMouseDegreesPerUnit = 0.12f;
        [SerializeField, Min(0f)] private float escapeSteerReturnSpeed = 45f;
        [SerializeField, Min(0f)] private float escapeMinimumDuration = 0.3f;

        private readonly Queue<float> recentMouseClicks = new Queue<float>();
        private Rigidbody body;
        private HookControlState controlState = HookControlState.Dragged;
        private bool isActive;
        private bool subscribedToStateManager;
        private bool warnedMissingPullTarget;
        private bool pendingClickSprint;
        private float clickEscapeEndsAt;
        private float turnaroundCanCompleteAt;
        private float escapeCanEndAt;
        private float draggedYawOffset;
        private float draggedPitchOffset;
        private float escapeYawOffset;
        private float escapePitchOffset;

        private void Reset()
        {
            ResolveReferences();
        }

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            SubscribeToStateManager();
        }

        private void Start()
        {
            SubscribeToStateManager();
            ApplyState(stateManager ? stateManager.CurrentState : GameState.Intro);
        }

        private void OnDisable()
        {
            if (stateManager && subscribedToStateManager)
            {
                stateManager.StateChanged -= OnStateChanged;
            }

            subscribedToStateManager = false;
            DeactivateControl();
        }

        private void OnValidate()
        {
            pullAcceleration = Mathf.Max(0f, pullAcceleration);
            draggedMaxSpeed = Mathf.Max(0f, draggedMaxSpeed);
            draggedSpeedLimitDamping = Mathf.Max(0f, draggedSpeedLimitDamping);
            draggedVelocityDamping = Mathf.Max(0f, draggedVelocityDamping);
            draggedRotationSpeed = Mathf.Max(0f, draggedRotationSpeed);
            draggedRollCycleSpeed = Mathf.Max(0f, draggedRollCycleSpeed);
            angularVelocityDamping = Mathf.Max(0f, angularVelocityDamping);
            draggedMouseDegreesPerUnit = Mathf.Max(0f, draggedMouseDegreesPerUnit);
            draggedSteerReturnSpeed = Mathf.Max(0f, draggedSteerReturnSpeed);
            playerSteeringSpeedMultiplier = Mathf.Max(0f, playerSteeringSpeedMultiplier);
            escapeRequiredClicks = Mathf.Max(2, escapeRequiredClicks);
            escapeClickWindow = Mathf.Max(0.01f, escapeClickWindow);
            escapeMaxAverageClickInterval = Mathf.Max(0.01f, escapeMaxAverageClickInterval);
            clickEscapeDuration = Mathf.Max(0f, clickEscapeDuration);
            turnaroundRotationSpeed = Mathf.Max(0f, turnaroundRotationSpeed);
            turnaroundMinimumDuration = Mathf.Max(0f, turnaroundMinimumDuration);
            turnaroundVelocityDamping = Mathf.Max(0f, turnaroundVelocityDamping);
            escapeMouseDegreesPerUnit = Mathf.Max(0f, escapeMouseDegreesPerUnit);
            escapeSteerReturnSpeed = Mathf.Max(0f, escapeSteerReturnSpeed);
            escapeMinimumDuration = Mathf.Max(0f, escapeMinimumDuration);
        }

        private void Update()
        {
            if (!isActive)
            {
                return;
            }

            RecordMouseClickInput();

            if (!pullTarget)
            {
                WarnMissingPullTarget();
                return;
            }

            bool escapeInputActive = IsEscapeInputActive();
            switch (controlState)
            {
                case HookControlState.Dragged:
                    UpdateDraggedSteering();
                    if (escapeInputActive)
                    {
                        BeginTurnaround();
                    }

                    break;

                case HookControlState.Escaping:
                    UpdateEscapeSteering();
                    if (!escapeInputActive
                        && Time.time >= escapeCanEndAt
                        && (!tunaMotor || !tunaMotor.IsSprinting))
                    {
                        BeginDragged();
                    }

                    break;
            }
        }

        private void FixedUpdate()
        {
            if (!isActive || !body)
            {
                return;
            }

            if (!pullTarget)
            {
                if (tunaMotor)
                {
                    tunaMotor.SetExternalControl(transform.forward, 0f, 0f, false, true, true);
                }

                return;
            }

            Vector3 pullDirection = GetPullDirection();
            switch (controlState)
            {
                case HookControlState.Dragged:
                    UpdateDragged(pullDirection);
                    break;

                case HookControlState.Turnaround:
                    UpdateTurnaround(pullDirection);
                    break;

                case HookControlState.Escaping:
                    UpdateEscaping();
                    break;
            }
        }

        private void OnStateChanged(GameState previousState, GameState nextState)
        {
            ApplyState(nextState);
        }

        private void ApplyState(GameState state)
        {
            if (state == GameState.OnHook && IsThisHookedTuna())
            {
                ActivateControl();
                return;
            }

            DeactivateControl();
        }

        private void ActivateControl()
        {
            if (isActive)
            {
                return;
            }

            isActive = true;
            warnedMissingPullTarget = false;
            recentMouseClicks.Clear();
            clickEscapeEndsAt = 0f;
            pendingClickSprint = false;
            ResolveReferences();

            if (tunaMotor)
            {
                tunaMotor.BeginExternalControl(GetInitialControlDirection());
            }

            BeginDragged();
        }

        private void DeactivateControl()
        {
            if (!isActive)
            {
                return;
            }

            isActive = false;
            recentMouseClicks.Clear();
            clickEscapeEndsAt = 0f;
            pendingClickSprint = false;
            draggedYawOffset = 0f;
            draggedPitchOffset = 0f;
            escapeYawOffset = 0f;
            escapePitchOffset = 0f;

            if (tunaMotor && tunaMotor.IsExternallyControlled)
            {
                tunaMotor.EndExternalControl();
            }
        }

        private void BeginDragged()
        {
            controlState = HookControlState.Dragged;
            draggedYawOffset = 0f;
            draggedPitchOffset = 0f;
            escapeYawOffset = 0f;
            escapePitchOffset = 0f;
            pendingClickSprint = false;

            if (tunaMotor)
            {
                tunaMotor.SetExternalControl(GetInitialControlDirection(), 0f, 0f, false, true, true);
            }
        }

        private void BeginTurnaround()
        {
            controlState = HookControlState.Turnaround;
            escapeYawOffset = 0f;
            escapePitchOffset = 0f;
            turnaroundCanCompleteAt = Time.time + turnaroundMinimumDuration;

            if (tunaMotor)
            {
                tunaMotor.SetExternalControl(GetEscapeDirection(), 0f, 0f, false, true, true);
            }
        }

        private void BeginEscaping()
        {
            controlState = HookControlState.Escaping;
            escapeCanEndAt = Time.time + escapeMinimumDuration;
            UpdateEscapeSteering();
            UpdateEscaping();

            if ((pendingClickSprint || IsForwardInputHeld()) && tunaMotor)
            {
                tunaMotor.TriggerExternalSprint(1);
            }

            pendingClickSprint = false;
        }

        private void UpdateDragged(Vector3 pullDirection)
        {
            Vector3 draggedDirection = BuildSteeredDraggedDirection(pullDirection);
            float turnInput = draggedSteerLimitAngle > 0f
                ? Mathf.Clamp(draggedYawOffset / draggedSteerLimitAngle, -1f, 1f)
                : 0f;

            if (tunaMotor)
            {
                tunaMotor.SetExternalControl(draggedDirection, 0f, turnInput, false, true, true);
            }

            body.AddForce(pullDirection * pullAcceleration, ForceMode.Acceleration);
            body.AddForce(-body.linearVelocity * draggedVelocityDamping, ForceMode.Acceleration);
            ApplySpeedLimit(draggedMaxSpeed);
            RotateToward(draggedDirection, draggedRotationSpeed, GetDraggedRollAngle());
            DampAngularVelocity();
        }

        private void UpdateTurnaround(Vector3 pullDirection)
        {
            Vector3 escapeDirection = GetEscapeDirection();
            if (tunaMotor)
            {
                tunaMotor.SetExternalControl(escapeDirection, 0f, 0f, false, true, true);
            }

            if (turnaroundPullScale > 0f)
            {
                body.AddForce(pullDirection * (pullAcceleration * turnaroundPullScale), ForceMode.Acceleration);
            }

            body.AddForce(-body.linearVelocity * turnaroundVelocityDamping, ForceMode.Acceleration);
            ApplySpeedLimit(draggedMaxSpeed);
            RotateToward(escapeDirection, turnaroundRotationSpeed, 0f);
            DampAngularVelocity();

            float angleToEscape = Vector3.Angle(transform.forward, escapeDirection);
            if (Time.time >= turnaroundCanCompleteAt && angleToEscape <= turnaroundCompleteAngle)
            {
                BeginEscaping();
            }
        }

        private void UpdateEscaping()
        {
            Vector3 escapeDirection = BuildSteeredEscapeDirection();
            float turnInput = escapeSteerLimitAngle > 0f
                ? Mathf.Clamp(escapeYawOffset / escapeSteerLimitAngle, -1f, 1f)
                : 0f;

            if (tunaMotor)
            {
                tunaMotor.SetExternalControl(escapeDirection, 1f, turnInput, true, false);
            }
        }

        private void UpdateEscapeSteering()
        {
            UpdateSteeringOffsets(
                ref escapeYawOffset,
                ref escapePitchOffset,
                escapeMouseDegreesPerUnit,
                escapeSteerReturnSpeed,
                escapeSteerLimitAngle);
        }

        private void UpdateDraggedSteering()
        {
            UpdateSteeringOffsets(
                ref draggedYawOffset,
                ref draggedPitchOffset,
                draggedMouseDegreesPerUnit,
                draggedSteerReturnSpeed,
                draggedSteerLimitAngle);
        }

        private Vector3 BuildSteeredEscapeDirection()
        {
            Vector3 escapeDirection = GetEscapeDirection();
            return BuildSteeredDirection(escapeDirection, escapeYawOffset, escapePitchOffset);
        }

        private Vector3 BuildSteeredDraggedDirection(Vector3 pullDirection)
        {
            return BuildSteeredDirection(pullDirection, draggedYawOffset, draggedPitchOffset);
        }

        private void UpdateSteeringOffsets(
            ref float yawOffset,
            ref float pitchOffset,
            float mouseDegreesPerUnit,
            float returnSpeed,
            float limitAngle)
        {
            Vector2 look = input ? input.Look : Vector2.zero;
            if (look.sqrMagnitude > 0.000001f)
            {
                float scaledMouseDegreesPerUnit = mouseDegreesPerUnit * playerSteeringSpeedMultiplier;
                yawOffset += look.x * scaledMouseDegreesPerUnit;
                pitchOffset -= look.y * scaledMouseDegreesPerUnit;
            }
            else if (returnSpeed > 0f)
            {
                float returnDelta = returnSpeed * Time.deltaTime;
                yawOffset = Mathf.MoveTowards(yawOffset, 0f, returnDelta);
                pitchOffset = Mathf.MoveTowards(pitchOffset, 0f, returnDelta);
            }

            Vector2 clampedOffsets = Vector2.ClampMagnitude(
                new Vector2(yawOffset, pitchOffset),
                limitAngle);
            yawOffset = clampedOffsets.x;
            pitchOffset = clampedOffsets.y;
        }

        private static Vector3 BuildSteeredDirection(Vector3 baseDirection, float yawOffset, float pitchOffset)
        {
            if (baseDirection.sqrMagnitude <= 0.000001f)
            {
                return Vector3.forward;
            }

            Vector3 direction = baseDirection.normalized;
            direction = Quaternion.AngleAxis(yawOffset, Vector3.up) * direction;

            Vector3 pitchAxis = Vector3.Cross(Vector3.up, direction);
            if (pitchAxis.sqrMagnitude <= 0.000001f)
            {
                pitchAxis = Vector3.right;
            }

            direction = Quaternion.AngleAxis(pitchOffset, pitchAxis.normalized) * direction;
            return direction.sqrMagnitude > 0.000001f ? direction.normalized : baseDirection.normalized;
        }

        private float GetDraggedRollAngle()
        {
            float minAngle = Mathf.Min(draggedRollAngleRange.x, draggedRollAngleRange.y);
            float maxAngle = Mathf.Max(draggedRollAngleRange.x, draggedRollAngleRange.y);
            float span = maxAngle - minAngle;
            if (span <= 0.0001f || draggedRollCycleSpeed <= 0f)
            {
                return minAngle;
            }

            return minAngle + Mathf.PingPong(Time.time * draggedRollCycleSpeed, span);
        }

        private Vector3 GetInitialControlDirection()
        {
            return pullTarget ? GetPullDirection() : transform.forward;
        }

        private Vector3 GetPullDirection()
        {
            Vector3 direction = pullTarget.position - transform.position;
            if (direction.sqrMagnitude > 0.000001f)
            {
                return direction.normalized;
            }

            return transform.forward.sqrMagnitude > 0.000001f ? transform.forward.normalized : Vector3.forward;
        }

        private Vector3 GetEscapeDirection()
        {
            Vector3 horizontalPullDirection = Vector3.ProjectOnPlane(GetPullDirection(), Vector3.up);
            if (horizontalPullDirection.sqrMagnitude > 0.000001f)
            {
                return -horizontalPullDirection.normalized;
            }

            Vector3 horizontalForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            return horizontalForward.sqrMagnitude > 0.000001f
                ? -horizontalForward.normalized
                : -Vector3.forward;
        }

        private void RotateToward(Vector3 forward, float rotationSpeed, float rollAngle)
        {
            if (forward.sqrMagnitude <= 0.000001f || rotationSpeed <= 0f)
            {
                return;
            }

            Quaternion targetRotation = Quaternion.LookRotation(forward.normalized, Vector3.up)
                * Quaternion.Euler(0f, 0f, rollAngle);
            Quaternion nextRotation = Quaternion.RotateTowards(
                body.rotation,
                targetRotation,
                rotationSpeed * Time.fixedDeltaTime);
            body.MoveRotation(nextRotation);
        }

        private void DampAngularVelocity()
        {
            if (angularVelocityDamping <= 0f)
            {
                return;
            }

            body.angularVelocity = Vector3.Lerp(
                body.angularVelocity,
                Vector3.zero,
                angularVelocityDamping * Time.fixedDeltaTime);
        }

        private void ApplySpeedLimit(float maxSpeed)
        {
            if (maxSpeed <= 0f || draggedSpeedLimitDamping <= 0f)
            {
                return;
            }

            Vector3 velocity = body.linearVelocity;
            float speed = velocity.magnitude;
            if (speed <= maxSpeed || speed <= 0.0001f)
            {
                return;
            }

            body.AddForce(
                -velocity.normalized * ((speed - maxSpeed) * draggedSpeedLimitDamping),
                ForceMode.Acceleration);
        }

        private bool IsEscapeInputActive()
        {
            return IsForwardInputHeld() || Time.time < clickEscapeEndsAt;
        }

        private bool IsForwardInputHeld()
        {
            if (input && input.Move.y > escapeMoveThreshold)
            {
                return true;
            }

#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.wKey.isPressed)
            {
                return true;
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKey(KeyCode.W);
#else
            return false;
#endif
        }

        private void RecordMouseClickInput()
        {
            if (!WasMousePressedThisFrame())
            {
                TrimMouseClicks(Time.time);
                return;
            }

            float now = Time.time;
            recentMouseClicks.Enqueue(now);
            TrimMouseClicks(now);

            if (IsRapidClickCadence(now))
            {
                clickEscapeEndsAt = now + clickEscapeDuration;
                pendingClickSprint = true;
            }
        }

        private void TrimMouseClicks(float now)
        {
            while (recentMouseClicks.Count > 0 && now - recentMouseClicks.Peek() > escapeClickWindow)
            {
                recentMouseClicks.Dequeue();
            }
        }

        private bool IsRapidClickCadence(float latestClickTime)
        {
            if (recentMouseClicks.Count < escapeRequiredClicks)
            {
                return false;
            }

            int skipCount = recentMouseClicks.Count - escapeRequiredClicks;
            int index = 0;
            float firstRelevantClickTime = latestClickTime;
            foreach (float clickTime in recentMouseClicks)
            {
                if (index == skipCount)
                {
                    firstRelevantClickTime = clickTime;
                    break;
                }

                index++;
            }

            float averageInterval = (latestClickTime - firstRelevantClickTime) / (escapeRequiredClicks - 1);
            return averageInterval <= escapeMaxAverageClickInterval;
        }

        private static bool WasMousePressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                return true;
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButtonDown(0);
#else
            return false;
#endif
        }

        private bool IsThisHookedTuna()
        {
            if (!stateManager || !stateManager.HookedTuna)
            {
                return true;
            }

            Transform hookedTuna = stateManager.HookedTuna;
            return hookedTuna == transform
                || hookedTuna.IsChildOf(transform)
                || transform.IsChildOf(hookedTuna);
        }

        private void WarnMissingPullTarget()
        {
            if (warnedMissingPullTarget)
            {
                return;
            }

            warnedMissingPullTarget = true;
            Debug.LogWarning(
                $"{nameof(TunaOnHookController)} on {name} needs a Pull Target before OnHook control can pull the tuna.",
                this);
        }

        private void SubscribeToStateManager()
        {
            if (subscribedToStateManager)
            {
                return;
            }

            ResolveStateManager();
            if (!stateManager)
            {
                return;
            }

            stateManager.StateChanged += OnStateChanged;
            subscribedToStateManager = true;
        }

        private void ResolveReferences()
        {
            if (!body)
            {
                body = GetComponent<Rigidbody>();
            }

            if (!tunaMotor)
            {
                tunaMotor = GetComponent<TunaMotor>();
            }

            if (!input)
            {
                input = GetComponent<TunaInputReader>();
            }

            ResolveStateManager();
        }

        private void ResolveStateManager()
        {
            if (!stateManager)
            {
                stateManager = GameStateManager.Instance;
            }

            if (!stateManager)
            {
                stateManager = FindFirstObjectByType<GameStateManager>(FindObjectsInactive.Include);
            }
        }
    }
}
