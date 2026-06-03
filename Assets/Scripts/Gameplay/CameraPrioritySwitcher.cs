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
        [SerializeField] private CinemachineCamera tunaSchoolFocusCamera;

        [Header("Events")]
        [SerializeField] private GameplayEventBus eventBus;

        [Header("Priority")]
        [SerializeField] private int activePriority = 10;
        [SerializeField] private int inactivePriority;

        private bool subscribedToStateManager;
        private bool subscribedToEventBus;
        private bool tunaSchoolFocusCameraActive;

        private void Awake()
        {
            ResolveStateManager();
            ResolveEventBus();
        }

        private void OnEnable()
        {
            SubscribeToStateManager();
            SubscribeToEventBus();
        }

        private void Start()
        {
            SubscribeToStateManager();
            SubscribeToEventBus();
        }

        private void OnDisable()
        {
            if (stateManager && subscribedToStateManager)
            {
                stateManager.StateChanged -= OnStateChanged;
            }

            if (eventBus && subscribedToEventBus)
            {
                eventBus.TunaSchoolFocusTriggered -= OnTunaSchoolFocusTriggered;
            }

            subscribedToStateManager = false;
            subscribedToEventBus = false;
        }

        private void OnStateChanged(GameState previousState, GameState nextState)
        {
            ApplyState(nextState);
        }

        private void OnTunaSchoolFocusTriggered(TunaSchoolFocusEvent focusEvent)
        {
            tunaSchoolFocusCameraActive = true;
            ApplyCurrentState();
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

        private void SubscribeToEventBus()
        {
            if (subscribedToEventBus)
            {
                return;
            }

            ResolveEventBus();
            if (!eventBus)
            {
                return;
            }

            eventBus.TunaSchoolFocusTriggered += OnTunaSchoolFocusTriggered;
            subscribedToEventBus = true;
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

        private void ResolveEventBus()
        {
            if (!eventBus)
            {
                eventBus = GameplayEventBus.Instance;
            }

            if (!eventBus)
            {
                eventBus = FindFirstObjectByType<GameplayEventBus>(FindObjectsInactive.Include);
            }
        }

        private void ApplyCurrentState()
        {
            ApplyState(stateManager ? stateManager.CurrentState : GameState.PhaseBaitBall);
        }

        private void ApplyState(GameState state)
        {
            CinemachineCamera activeCamera = GetActiveCamera(state);

            SetPriority(introCamera, introCamera == activeCamera ? activePriority : inactivePriority);
            SetPriority(thirdPersonAimCamera, thirdPersonAimCamera == activeCamera ? activePriority : inactivePriority);
            SetPriority(tunaSchoolFocusCamera, tunaSchoolFocusCamera == activeCamera ? activePriority : inactivePriority);
        }

        private CinemachineCamera GetActiveCamera(GameState state)
        {
            if (tunaSchoolFocusCameraActive)
            {
                return tunaSchoolFocusCamera ? tunaSchoolFocusCamera : thirdPersonAimCamera;
            }

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
