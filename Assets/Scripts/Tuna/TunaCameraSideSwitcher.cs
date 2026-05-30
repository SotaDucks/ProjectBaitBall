using Unity.Cinemachine;
using UnityEngine;

namespace TestBoids.Tuna
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CinemachineThirdPersonFollow))]
    public sealed class TunaCameraSideSwitcher : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private TunaInputReader input;
        [SerializeField] private CinemachineThirdPersonFollow thirdPersonFollow;

        [Header("Camera Side")]
        [Range(0f, 1f)] public float leftLookCameraSide = 0.2f;
        [Range(0f, 1f)] public float rightLookCameraSide = 0.8f;
        [Min(0f)] public float cameraSideChangeSpeed = 2f;

        [Header("Trigger")]
        [Min(0f)] public float lookThreshold = 0.1f;

        private float targetCameraSide;
        private bool hasTargetCameraSide;

        private void Reset()
        {
            ResolveReferences();
            targetCameraSide = thirdPersonFollow ? thirdPersonFollow.CameraSide : leftLookCameraSide;
            hasTargetCameraSide = true;
        }

        private void Awake()
        {
            ResolveReferences();
            InitializeTargetCameraSide();
        }

        private void OnEnable()
        {
            ResolveReferences();
            InitializeTargetCameraSide();
        }

        private void Update()
        {
            if (!thirdPersonFollow)
            {
                ResolveReferences();
                if (!thirdPersonFollow)
                {
                    return;
                }
            }

            InitializeTargetCameraSide();
            UpdateTargetFromLookInput();
            MoveCameraSideTowardTarget();
        }

        private void UpdateTargetFromLookInput()
        {
            float lookX = input ? input.Look.x : 0f;
            if (lookX > lookThreshold)
            {
                targetCameraSide = rightLookCameraSide;
            }
            else if (lookX < -lookThreshold)
            {
                targetCameraSide = leftLookCameraSide;
            }
        }

        private void MoveCameraSideTowardTarget()
        {
            if (cameraSideChangeSpeed <= 0f)
            {
                thirdPersonFollow.CameraSide = targetCameraSide;
                return;
            }

            thirdPersonFollow.CameraSide = Mathf.MoveTowards(
                thirdPersonFollow.CameraSide,
                targetCameraSide,
                cameraSideChangeSpeed * Time.deltaTime);
        }

        private void InitializeTargetCameraSide()
        {
            if (hasTargetCameraSide || !thirdPersonFollow)
            {
                return;
            }

            targetCameraSide = thirdPersonFollow.CameraSide;
            hasTargetCameraSide = true;
        }

        private void ResolveReferences()
        {
            if (!thirdPersonFollow)
            {
                thirdPersonFollow = GetComponent<CinemachineThirdPersonFollow>();
            }

            if (!input)
            {
                input = FindFirstObjectByType<TunaInputReader>();
            }
        }

        private void OnValidate()
        {
            leftLookCameraSide = Mathf.Clamp01(leftLookCameraSide);
            rightLookCameraSide = Mathf.Clamp01(rightLookCameraSide);
            cameraSideChangeSpeed = Mathf.Max(0f, cameraSideChangeSpeed);
            lookThreshold = Mathf.Max(0f, lookThreshold);
        }
    }
}
