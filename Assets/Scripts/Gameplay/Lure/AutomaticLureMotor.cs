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
            Departing,
            Surfacing
        }

        [Header("References")]
        [Tooltip("Optional model child used for visual sway. Leave empty to disable sway.")]
        [SerializeField] private Transform visualRoot;

        [Header("Steady Retrieve")]
        [SerializeField, Min(0.1f)] private float retrieveSpeed = 5f;
        [SerializeField, Min(0.1f)] private float acceleration = 8f;
        [SerializeField, Min(0.1f)] private float rotationSpeed = 8f;
        [SerializeField, Range(0f, 15f)] private float retrieveUpwardAngle = 1.5f;
        [SerializeField, Min(0f)] private float retrieveSwayAmplitude = 18f;
        [SerializeField, Min(0.1f)] private float retrieveSwayFrequency = 5f;

        [Header("Pass And Departure")]
        [SerializeField, Min(0.1f)] private float passArrivalDistance = 1.2f;
        [SerializeField, Min(0.1f)] private float departureDistance = 18f;
        [SerializeField, Min(0.1f)] private float maximumLifetime = 20f;

        [Header("Surface Exit")]
        [SerializeField, Range(30f, 89f)] private float surfaceExitAngle = 65f;
        [SerializeField, Min(0.1f)] private float surfaceExitSpeed = 12f;
        [SerializeField, Min(0f)] private float surfaceExitHeightOffset = 0.1f;

        private Rigidbody body;
        private Transform tuna;
        private Vector3 horizontalPassOffset;
        private Vector3 departureDirection;
        private Vector3 departureStartPosition;
        private Vector3 surfaceExitDirection;
        private Quaternion visualBaseLocalRotation;
        private TravelStage travelStage;
        private float waterSurfaceHeight;
        private float spawnedAt;
        private bool configured;

        public bool HasPassedTuna => configured && travelStage != TravelStage.ApproachingTuna;

        private void Reset()
        {
            body = GetComponent<Rigidbody>();
            visualRoot = transform.childCount > 0 ? transform.GetChild(0) : null;
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            if (HasIndependentVisualRoot())
            {
                visualBaseLocalRotation = visualRoot.localRotation;
            }

            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
        }

        private void OnEnable()
        {
            spawnedAt = Time.time;
        }

        private void OnDisable()
        {
            if (HasIndependentVisualRoot())
            {
                visualRoot.localRotation = visualBaseLocalRotation;
            }
        }

        private void OnValidate()
        {
            retrieveSpeed = Mathf.Max(0.1f, retrieveSpeed);
            acceleration = Mathf.Max(0.1f, acceleration);
            rotationSpeed = Mathf.Max(0.1f, rotationSpeed);
            retrieveUpwardAngle = Mathf.Clamp(retrieveUpwardAngle, 0f, 15f);
            retrieveSwayAmplitude = Mathf.Max(0f, retrieveSwayAmplitude);
            retrieveSwayFrequency = Mathf.Max(0.1f, retrieveSwayFrequency);
            passArrivalDistance = Mathf.Max(0.1f, passArrivalDistance);
            departureDistance = Mathf.Max(0.1f, departureDistance);
            maximumLifetime = Mathf.Max(0.1f, maximumLifetime);
            surfaceExitAngle = Mathf.Clamp(surfaceExitAngle, 30f, 89f);
            surfaceExitSpeed = Mathf.Max(0.1f, surfaceExitSpeed);
            surfaceExitHeightOffset = Mathf.Max(0f, surfaceExitHeightOffset);
        }

        public void ConfigurePass(Transform tunaTarget, Vector3 worldPassPoint, float worldWaterSurfaceHeight)
        {
            tuna = tunaTarget;
            if (tuna)
            {
                horizontalPassOffset = worldPassPoint - tuna.position;
                horizontalPassOffset.y = 0f;
            }

            waterSurfaceHeight = worldWaterSurfaceHeight;
            travelStage = TravelStage.ApproachingTuna;
            configured = tuna;
            spawnedAt = Time.time;

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

            if (travelStage != TravelStage.Surfacing
                && Time.time - spawnedAt >= maximumLifetime)
            {
                BeginSurfaceExit();
            }

            UpdateTravelStage();
            ApplyMovement();
        }

        private void LateUpdate()
        {
            if (!HasIndependentVisualRoot())
            {
                return;
            }

            if (travelStage == TravelStage.Surfacing)
            {
                visualRoot.localRotation = visualBaseLocalRotation;
                return;
            }

            visualRoot.localRotation = visualBaseLocalRotation
                * Quaternion.AngleAxis(GetRetrieveSwayAngle(), Vector3.up);
        }

        private bool HasIndependentVisualRoot()
        {
            return visualRoot && visualRoot != transform;
        }

        private void UpdateTravelStage()
        {
            if (travelStage == TravelStage.Surfacing)
            {
                if (transform.position.y >= waterSurfaceHeight + surfaceExitHeightOffset)
                {
                    Destroy(gameObject);
                }

                return;
            }

            if (travelStage == TravelStage.Departing)
            {
                Vector3 departureTravel = Vector3.ProjectOnPlane(
                    transform.position - departureStartPosition,
                    Vector3.up);
                if (departureTravel.magnitude >= departureDistance)
                {
                    BeginSurfaceExit();
                }

                return;
            }

            Vector3 horizontalToPassPoint = Vector3.ProjectOnPlane(
                GetPassPoint() - transform.position,
                Vector3.up);
            if (horizontalToPassPoint.magnitude > passArrivalDistance)
            {
                return;
            }

            travelStage = TravelStage.Departing;
            departureStartPosition = transform.position;
            departureDirection = GetHorizontalDirection(body.linearVelocity, transform.forward);
        }

        private void ApplyMovement()
        {
            Vector3 direction = GetTravelDirection();
            if (direction.sqrMagnitude <= 0.000001f)
            {
                return;
            }

            direction.Normalize();
            float targetSpeed = travelStage == TravelStage.Surfacing ? surfaceExitSpeed : retrieveSpeed;
            Vector3 targetVelocity = direction * targetSpeed;
            float velocityBlend = 1f - Mathf.Exp(-acceleration * Time.fixedDeltaTime);
            body.linearVelocity = Vector3.Lerp(body.linearVelocity, targetVelocity, velocityBlend);

            if (body.linearVelocity.sqrMagnitude <= 0.000001f)
            {
                return;
            }

            Quaternion targetRotation = Quaternion.LookRotation(body.linearVelocity.normalized);
            if (travelStage != TravelStage.Surfacing && !HasIndependentVisualRoot())
            {
                targetRotation *= Quaternion.AngleAxis(GetRetrieveSwayAngle(), Vector3.up);
            }

            float rotationBlend = 1f - Mathf.Exp(-rotationSpeed * Time.fixedDeltaTime);
            body.MoveRotation(Quaternion.Slerp(body.rotation, targetRotation, rotationBlend));
        }

        private Vector3 GetTravelDirection()
        {
            if (travelStage == TravelStage.Surfacing)
            {
                return surfaceExitDirection;
            }

            Vector3 horizontalDirection = travelStage == TravelStage.Departing
                ? departureDirection
                : GetApproachHorizontalDirection();
            return ApplyUpwardAngle(horizontalDirection, retrieveUpwardAngle);
        }

        private Vector3 GetApproachDirection()
        {
            return ApplyUpwardAngle(GetApproachHorizontalDirection(), retrieveUpwardAngle);
        }

        private Vector3 GetApproachHorizontalDirection()
        {
            return GetHorizontalDirection(GetPassPoint() - transform.position, transform.forward);
        }

        private Vector3 GetPassPoint()
        {
            Vector3 passPoint = tuna.position + horizontalPassOffset;
            return passPoint;
        }

        private void BeginSurfaceExit()
        {
            if (travelStage == TravelStage.Surfacing)
            {
                return;
            }

            Vector3 horizontalDirection = GetHorizontalDirection(body.linearVelocity, transform.forward);
            surfaceExitDirection = ApplyUpwardAngle(horizontalDirection, surfaceExitAngle);
            travelStage = TravelStage.Surfacing;

            body.linearVelocity = surfaceExitDirection * surfaceExitSpeed;
            body.rotation = Quaternion.LookRotation(surfaceExitDirection);
        }

        private float GetRetrieveSwayAngle()
        {
            return Mathf.Sin(Time.time * retrieveSwayFrequency * Mathf.PI * 2f)
                * retrieveSwayAmplitude;
        }

        private static Vector3 ApplyUpwardAngle(Vector3 horizontalDirection, float angleDegrees)
        {
            float angleRadians = angleDegrees * Mathf.Deg2Rad;
            return horizontalDirection.normalized * Mathf.Cos(angleRadians)
                + Vector3.up * Mathf.Sin(angleRadians);
        }

        private static Vector3 GetHorizontalDirection(Vector3 direction, Vector3 fallback)
        {
            Vector3 horizontal = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (horizontal.sqrMagnitude <= 0.000001f)
            {
                horizontal = Vector3.ProjectOnPlane(fallback, Vector3.up);
            }

            return horizontal.sqrMagnitude > 0.000001f ? horizontal.normalized : Vector3.forward;
        }
    }
}
