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

        public bool SyncNow()
        {
            ResolveFishSchool();
            if (!fishSchool)
            {
                return false;
            }

            int count = fishSchool.CurrentFishCount;
            if (count <= 0)
            {
                return false;
            }

            int resolvedIndex = clampIndexToAvailableFish
                ? Mathf.Clamp(fishIndex, 0, count - 1)
                : fishIndex;

            if (!fishSchool.TryGetFishPose(resolvedIndex, out Vector3 position, out Quaternion rotation, out _))
            {
                return false;
            }

            Vector3 targetPosition = position + rotation * localOffset;
            Quaternion targetRotation = matchRotation ? rotation : transform.rotation;
            transform.SetPositionAndRotation(targetPosition, targetRotation);
            return true;
        }

        private void Reset()
        {
            ResolveFishSchool();
        }

        private void OnValidate()
        {
            fishIndex = Mathf.Max(0, fishIndex);
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
    }
}
