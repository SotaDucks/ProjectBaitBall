using UnityEngine;

namespace TestBoids.Gameplay.Lure
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class AutomaticLureMotor : MonoBehaviour
    {
        private enum TravelStage
        {
            ApproachingTuna,
            Departing
        }

        private enum MotionPhase
        {
            Retrieve,
            Jerk
        }

        [Header("References")]
        [Tooltip("Optional model child used for visual sway. Leave empty to disable sway.")]
        [SerializeField] private Transform visualRoot;

        [Header("Steady Retrieve")]
        [SerializeField, Min(0.1f)] private float retrieveSpeed = 5f;
        [SerializeField, Min(0.1f)] private float acceleration = 8f;
        [SerializeField, Min(0.1f)] private float rotationSpeed = 8f;
        [SerializeField, Min(0f)] private float retrieveSwayAmplitude = 18f;
        [SerializeField, Min(0.1f)] private float retrieveSwayFrequency = 5f;

        [Header("Jerk")]
        [SerializeField] private Vector2 jerkIntervalRange = new(0.8f, 1.8f);
        [SerializeField, Min(0.05f)] private float jerkDuration = 0.25f;
        [SerializeField, Min(0.1f)] private float jerkSpeed = 10f;
        [SerializeField, Range(0f, 90f)] private float jerkAngleRange = 28f;
        [SerializeField, Min(0f)] private float jerkSwayAmplitude = 55f;
        [SerializeField, Min(0.1f)] private float jerkSwayFrequency = 14f;

        [Header("Pass And Departure")]
        [SerializeField, Min(0.1f)] private float passArrivalDistance = 1.2f;
        [SerializeField, Min(0.1f)] private float departureDistance = 18f;
        [SerializeField, Min(0.1f)] private float maximumLifetime = 20f;

        private Rigidbody body;
        private Transform tuna;
        private Vector3 horizontalPassOffset;
        private float passWorldY;
        private Vector3 departureDirection;
        private Vector3 departureStartPosition;
        private Quaternion visualBaseLocalRotation;
        private TravelStage travelStage;
        private MotionPhase motionPhase;
        private Vector3 jerkDirection;
        private float phaseEndsAt;
        private float nextJerkAt;
        private float spawnedAt;
        private bool configured;

        public bool HasPassedTuna => configured && travelStage == TravelStage.Departing;

        private void Reset()
        {
            body = GetComponent<Rigidbody>();
            visualRoot = transform.childCount > 0 ? transform.GetChild(0) : null;
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            if (visualRoot)
            {
                visualBaseLocalRotation = visualRoot.localRotation;
            }

            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
        }

        private void OnEnable()
        {
            spawnedAt = Time.time;
            motionPhase = MotionPhase.Retrieve;
            ScheduleNextJerk();
        }

        private void OnDisable()
        {
            if (visualRoot)
            {
                visualRoot.localRotation = visualBaseLocalRotation;
            }
        }

        private void OnValidate()
        {
            retrieveSpeed = Mathf.Max(0.1f, retrieveSpeed);
            acceleration = Mathf.Max(0.1f, acceleration);
            rotationSpeed = Mathf.Max(0.1f, rotationSpeed);
            retrieveSwayAmplitude = Mathf.Max(0f, retrieveSwayAmplitude);
            retrieveSwayFrequency = Mathf.Max(0.1f, retrieveSwayFrequency);
            jerkIntervalRange.x = Mathf.Max(0.05f, jerkIntervalRange.x);
            jerkIntervalRange.y = Mathf.Max(jerkIntervalRange.x, jerkIntervalRange.y);
            jerkDuration = Mathf.Max(0.05f, jerkDuration);
            jerkSpeed = Mathf.Max(retrieveSpeed, jerkSpeed);
            jerkSwayAmplitude = Mathf.Max(0f, jerkSwayAmplitude);
            jerkSwayFrequency = Mathf.Max(0.1f, jerkSwayFrequency);
            passArrivalDistance = Mathf.Max(0.1f, passArrivalDistance);
            departureDistance = Mathf.Max(0.1f, departureDistance);
            maximumLifetime = Mathf.Max(0.1f, maximumLifetime);
        }

        public void ConfigurePass(Transform tunaTarget, Vector3 worldPassPoint)
        {
            tuna = tunaTarget;
            if (tuna)
            {
                horizontalPassOffset = worldPassPoint - tuna.position;
                horizontalPassOffset.y = 0f;
                passWorldY = worldPassPoint.y;
            }

            travelStage = TravelStage.ApproachingTuna;
            configured = tuna;
            spawnedAt = Time.time;
            motionPhase = MotionPhase.Retrieve;
            ScheduleNextJerk();

            if (!configured)
            {
                return;
            }

            Vector3 direction = GetApproachDirection();
            if (direction.sqrMagnitude > 0.000001f)
            {
                body.rotation = Quaternion.LookRotation(direction);
                body.linearVelocity = direction * retrieveSpeed;
            }
        }

        private void FixedUpdate()
        {
            if (!configured || !tuna)
            {
                return;
            }

            if (Time.time - spawnedAt >= maximumLifetime)
            {
                Destroy(gameObject);
                return;
            }

            UpdateTravelStage();
            UpdateMotionPhase();
            ApplyMovement();
        }

        private void LateUpdate()
        {
            if (!visualRoot)
            {
                return;
            }

            float amplitude = motionPhase == MotionPhase.Jerk ? jerkSwayAmplitude : retrieveSwayAmplitude;
            float frequency = motionPhase == MotionPhase.Jerk ? jerkSwayFrequency : retrieveSwayFrequency;
            float swayAngle = Mathf.Sin(Time.time * frequency * Mathf.PI * 2f) * amplitude;
            visualRoot.localRotation = visualBaseLocalRotation * Quaternion.AngleAxis(swayAngle, Vector3.up);
        }

        private void UpdateTravelStage()
        {
            if (travelStage == TravelStage.Departing)
            {
                if (Vector3.Distance(transform.position, departureStartPosition) >= departureDistance)
                {
                    Destroy(gameObject);
                }

                return;
            }

            Vector3 passPoint = GetPassPoint();
            if (Vector3.Distance(transform.position, passPoint) > passArrivalDistance)
            {
                return;
            }

            travelStage = TravelStage.Departing;
            departureStartPosition = transform.position;
            departureDirection = body.linearVelocity.sqrMagnitude > 0.000001f
                ? body.linearVelocity.normalized
                : transform.forward;
        }

        private void UpdateMotionPhase()
        {
            if (motionPhase == MotionPhase.Jerk)
            {
                if (Time.time >= phaseEndsAt)
                {
                    motionPhase = MotionPhase.Retrieve;
                    ScheduleNextJerk();
                }

                return;
            }

            if (Time.time < nextJerkAt)
            {
                return;
            }

            motionPhase = MotionPhase.Jerk;
            phaseEndsAt = Time.time + jerkDuration;
            Vector3 baseDirection = GetTravelDirection();
            jerkDirection = Quaternion.AngleAxis(
                Random.Range(-jerkAngleRange, jerkAngleRange),
                Random.onUnitSphere) * baseDirection;
            jerkDirection.Normalize();
        }

        private void ApplyMovement()
        {
            Vector3 direction = motionPhase == MotionPhase.Jerk ? jerkDirection : GetTravelDirection();
            if (direction.sqrMagnitude <= 0.000001f)
            {
                return;
            }

            direction.Normalize();
            float targetSpeed = motionPhase == MotionPhase.Jerk ? jerkSpeed : retrieveSpeed;
            Vector3 targetVelocity = direction * targetSpeed;
            float velocityBlend = 1f - Mathf.Exp(-acceleration * Time.fixedDeltaTime);
            body.linearVelocity = Vector3.Lerp(body.linearVelocity, targetVelocity, velocityBlend);

            if (body.linearVelocity.sqrMagnitude <= 0.000001f)
            {
                return;
            }

            Quaternion targetRotation = Quaternion.LookRotation(body.linearVelocity.normalized);
            float rotationBlend = 1f - Mathf.Exp(-rotationSpeed * Time.fixedDeltaTime);
            body.MoveRotation(Quaternion.Slerp(body.rotation, targetRotation, rotationBlend));
        }

        private Vector3 GetTravelDirection()
        {
            return travelStage == TravelStage.Departing
                ? departureDirection
                : GetApproachDirection();
        }

        private Vector3 GetApproachDirection()
        {
            Vector3 passPoint = GetPassPoint();
            return (passPoint - transform.position).normalized;
        }

        private Vector3 GetPassPoint()
        {
            Vector3 passPoint = tuna.position + horizontalPassOffset;
            passPoint.y = passWorldY;
            return passPoint;
        }

        private void ScheduleNextJerk()
        {
            nextJerkAt = Time.time + Random.Range(jerkIntervalRange.x, jerkIntervalRange.y);
        }
    }
}
