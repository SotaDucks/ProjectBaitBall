using Unity.Cinemachine;
using UnityEngine;

namespace TestBoids.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class CameraPrioritySwitcher : MonoBehaviour
    {
        private const string DefaultTunaOnHookCameraObjectName = "TunaOnHookCamera";
        private const string DefaultTunaHangingCameraObjectName = "TunaHangingCamera";

        [SerializeField] private GameStateManager stateManager;
        [SerializeField] private CinemachineCamera introCamera;
        [SerializeField] private CinemachineCamera thirdPersonAimCamera;
        [SerializeField] private CinemachineCamera tunaOnHookCamera;
        [SerializeField] private CinemachineCamera tunaHangingCamera;
        [SerializeField] private string tunaOnHookCameraObjectName = DefaultTunaOnHookCameraObjectName;
        [SerializeField] private string tunaHangingCameraObjectName = DefaultTunaHangingCameraObjectName;

        [Header("Priority")]
        [SerializeField] private int activePriority = 10;
        [SerializeField] private int inactivePriority;

        private bool subscribedToStateManager;

        private void Awake()
        {
            ResolveStateManager();
            ResolveNamedCameras();
        }

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(tunaOnHookCameraObjectName))
            {
                tunaOnHookCameraObjectName = DefaultTunaOnHookCameraObjectName;
            }

            if (string.IsNullOrWhiteSpace(tunaHangingCameraObjectName))
            {
                tunaHangingCameraObjectName = DefaultTunaHangingCameraObjectName;
            }
        }

        private void OnEnable()
        {
            SubscribeToStateManager();
        }

        private void Start()
        {
            SubscribeToStateManager();
        }

        private void OnDisable()
        {
            if (stateManager && subscribedToStateManager)
            {
                stateManager.StateChanged -= OnStateChanged;
            }

            subscribedToStateManager = false;
        }

        private void OnStateChanged(GameState previousState, GameState nextState)
        {
            ApplyState(nextState);
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
            ApplyState(stateManager.CurrentState);
        }

        private void ResolveStateManager()
        {
            if (!stateManager)
            {
                stateManager = GameStateManager.Instance;
            }

            if (!stateManager)
            {
                stateManager = FindFirstObjectByType<GameStateManager>();
            }
        }

        private void ApplyState(GameState state)
        {
            ResolveNamedCameras();
            CinemachineCamera activeCamera = GetActiveCamera(state);

            SetPriority(introCamera, introCamera == activeCamera ? activePriority : inactivePriority);
            SetPriority(thirdPersonAimCamera, thirdPersonAimCamera == activeCamera ? activePriority : inactivePriority);
            SetPriority(tunaOnHookCamera, tunaOnHookCamera == activeCamera ? activePriority : inactivePriority);
            SetPriority(tunaHangingCamera, tunaHangingCamera == activeCamera ? activePriority : inactivePriority);
        }

        private CinemachineCamera GetActiveCamera(GameState state)
        {
            switch (state)
            {
                case GameState.Intro:
                    return introCamera;

                case GameState.OnHook:
                    return tunaOnHookCamera ? tunaOnHookCamera : thirdPersonAimCamera;

                case GameState.TunaHanging:
                    if (tunaHangingCamera)
                    {
                        return tunaHangingCamera;
                    }

                    return tunaOnHookCamera ? tunaOnHookCamera : thirdPersonAimCamera;

                case GameState.PhaseBaitBallTransition:
                case GameState.PhaseBaitBall:
                default:
                    return thirdPersonAimCamera;
            }
        }

        private void ResolveNamedCameras()
        {
            if (!tunaOnHookCamera)
            {
                tunaOnHookCamera = FindCinemachineCameraByName(tunaOnHookCameraObjectName);
            }

            if (!tunaHangingCamera)
            {
                tunaHangingCamera = FindCinemachineCameraByName(tunaHangingCameraObjectName);
            }
        }

        private static void SetPriority(CinemachineCamera camera, int priority)
        {
            if (camera)
            {
                camera.Priority = priority;
            }
        }

        private static CinemachineCamera FindCinemachineCameraByName(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
            {
                return null;
            }

            CinemachineCamera[] cameras = FindObjectsByType<CinemachineCamera>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            foreach (CinemachineCamera camera in cameras)
            {
                if (camera && camera.name == objectName)
                {
                    return camera;
                }
            }

            return null;
        }
    }
}
