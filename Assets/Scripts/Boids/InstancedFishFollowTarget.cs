using UnityEngine;

namespace TestBoids.Boids
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-20)]
    public sealed class InstancedFishFollowTarget : MonoBehaviour
    {
        [SerializeField] private InstancedFishSchoolManager fishSchool;
        [SerializeField, Min(0)] private int fishIndex;
        [SerializeField] private bool clampIndexToAvailableFish = true;
        [SerializeField] private bool matchRotation = true;
        [SerializeField] private Vector3 localOffset;

        [Header("Smoothing")]
        [SerializeField] private bool smoothPosition = true;
        [SerializeField, Min(0f)] private float positionSmoothTime = 0.12f;
        [SerializeField, Min(0f)] private float maxPositionSpeed;
        [SerializeField, Min(0f)] private float snapDistance = 5f;
        [SerializeField] private bool smoothRotation = true;
        [SerializeField, Min(0f)] private float rotationSmoothTime = 0.12f;

        private Vector3 positionVelocity;
        private bool hasSyncedPose;

        public InstancedFishSchoolManager FishSchool
        {
            get => fishSchool;
            set => fishSchool = value;
        }

        public int FishIndex
        {
            get => fishIndex;
            set => fishIndex = Mathf.Max(0, value);
        }

        public void ResetSmoothing()
        {
            positionVelocity = Vector3.zero;
            hasSyncedPose = false;
        }

        public bool SyncNow()
        {
            ResolveFishSchool();
            if (!fishSchool)
            {
                ResetSmoothing();
                return false;
            }

            int count = fishSchool.CurrentFishCount;
            if (count <= 0)
            {
                ResetSmoothing();
                return false;
            }

            int resolvedIndex = clampIndexToAvailableFish
                ? Mathf.Clamp(fishIndex, 0, count - 1)
                : fishIndex;

            if (!fishSchool.TryGetFishPose(resolvedIndex, out Vector3 position, out Quaternion rotation, out _))
            {
                ResetSmoothing();
                return false;
            }

            Vector3 targetPosition = position + rotation * localOffset;
            Quaternion targetRotation = matchRotation
                ? SmoothRotation(rotation)
                : transform.rotation;

            targetPosition = SmoothPosition(targetPosition);
            transform.SetPositionAndRotation(targetPosition, targetRotation);
            hasSyncedPose = true;
            return true;
        }

        private void Reset()
        {
            ResolveFishSchool();
        }

        private void OnValidate()
        {
            fishIndex = Mathf.Max(0, fishIndex);
            positionSmoothTime = Mathf.Max(0f, positionSmoothTime);
            maxPositionSpeed = Mathf.Max(0f, maxPositionSpeed);
            snapDistance = Mathf.Max(0f, snapDistance);
            rotationSmoothTime = Mathf.Max(0f, rotationSmoothTime);
        }

        private void LateUpdate()
        {
            SyncNow();
        }

        private void ResolveFishSchool()
        {
            if (!fishSchool)
            {
                fishSchool = GetComponentInParent<InstancedFishSchoolManager>();
            }
        }

        private Vector3 SmoothPosition(Vector3 targetPosition)
        {
            if (!smoothPosition
                || positionSmoothTime <= 0f
                || !hasSyncedPose
                || ShouldSnapTo(targetPosition))
            {
                positionVelocity = Vector3.zero;
                return targetPosition;
            }

            float maxSpeed = maxPositionSpeed > 0f ? maxPositionSpeed : Mathf.Infinity;
            return Vector3.SmoothDamp(
                transform.position,
                targetPosition,
                ref positionVelocity,
                positionSmoothTime,
                maxSpeed,
                Time.deltaTime);
        }

        private Quaternion SmoothRotation(Quaternion targetRotation)
        {
            if (!smoothRotation || rotationSmoothTime <= 0f || !hasSyncedPose)
            {
                return targetRotation;
            }

            float t = 1f - Mathf.Exp(-Time.deltaTime / rotationSmoothTime);
            return Quaternion.Slerp(transform.rotation, targetRotation, t);
        }

        private bool ShouldSnapTo(Vector3 targetPosition)
        {
            return snapDistance > 0f
                && Vector3.Distance(transform.position, targetPosition) > snapDistance;
        }
    }
}
