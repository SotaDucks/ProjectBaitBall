using Unity.Cinemachine;
using UnityEngine;

namespace TestBoids.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class CameraPrioritySwitcher : MonoBehaviour
    {
        [SerializeField] private GameStateManager stateManager;
        [SerializeField] private CinemachineCamera introCamera;
        [SerializeField] private CinemachineCamera thirdPersonAimCamera;

        [Header("Priority")]
        [SerializeField] private int activePriority = 10;
        [SerializeField] private int inactivePriority;

        private bool subscribedToStateManager;

        private void Awake()
        {
            ResolveStateManager();
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
            CinemachineCamera activeCamera = GetActiveCamera(state);

            SetPriority(introCamera, introCamera == activeCamera ? activePriority : inactivePriority);
            SetPriority(thirdPersonAimCamera, thirdPersonAimCamera == activeCamera ? activePriority : inactivePriority);
        }

        private CinemachineCamera GetActiveCamera(GameState state)
        {
            switch (state)
            {
                case GameState.Intro:
                    return introCamera;

                case GameState.PhaseBaitBallTransition:
                case GameState.PhaseBaitBall:
                default:
                    return thirdPersonAimCamera;
            }
        }

        private static void SetPriority(CinemachineCamera camera, int priority)
        {
            if (camera)
            {
                camera.Priority = priority;
            }
        }
    }
}
