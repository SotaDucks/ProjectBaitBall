using Unity.Cinemachine;
using UnityEngine;

namespace TestBoids.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class GameStateCameraPrioritySwitcher : MonoBehaviour
    {
        [SerializeField] private GameStateManager stateManager;
        [SerializeField] private CinemachineCamera introCamera;
        [SerializeField] private CinemachineCamera thirdPersonAimCamera;

        [Header("Priority")]
        [SerializeField] private int activePriority = 10;
        [SerializeField] private int inactivePriority;

        private bool subscribed;

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
            if (stateManager && subscribed)
            {
                stateManager.StateChanged -= OnStateChanged;
            }

            subscribed = false;
        }

        private void OnStateChanged(GameState previousState, GameState nextState)
        {
            ApplyState(nextState);
        }

        private void SubscribeToStateManager()
        {
            if (subscribed)
            {
                return;
            }

            ResolveStateManager();
            if (!stateManager)
            {
                return;
            }

            stateManager.StateChanged += OnStateChanged;
            subscribed = true;
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
            bool isIntro = state == GameState.Intro;
            SetPriority(introCamera, isIntro ? activePriority : inactivePriority);
            SetPriority(thirdPersonAimCamera, isIntro ? inactivePriority : activePriority);
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
