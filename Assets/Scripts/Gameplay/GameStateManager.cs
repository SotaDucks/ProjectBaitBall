using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace TestBoids.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class GameStateManager : MonoBehaviour
    {
        [SerializeField] private GameState initialState = GameState.Intro;
        [SerializeField] private bool transitionToPhaseBaitBallWithSpace = true;
        [SerializeField] private KeyCode phaseBaitBallTestKey = KeyCode.Space;
        [SerializeField] private GameplayEventBus eventBus;

        public static GameStateManager Instance { get; private set; }
        public GameState CurrentState { get; private set; }

        public event Action<GameState, GameState> StateChanged;

        private bool subscribedToEventBus;

        private void Awake()
        {
            if (Instance && Instance != this)
            {
                Debug.LogWarning($"{nameof(GameStateManager)} already exists. Replacing the static instance with {name}.", this);
            }

            Instance = this;
            CurrentState = initialState;
            ResolveEventBus();
        }

        private void OnEnable()
        {
            SubscribeToEventBus();
        }

        private void Start()
        {
            SubscribeToEventBus();
        }

        private void OnDisable()
        {
            if (eventBus && subscribedToEventBus)
            {
                eventBus.LureBitten -= OnLureBitten;
            }

            subscribedToEventBus = false;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            if (transitionToPhaseBaitBallWithSpace
                && CurrentState == GameState.Intro
                && WasPhaseBaitBallTestKeyPressed())
            {
                SetState(GameState.PhaseBaitBallTransition);
            }
        }

        public void SetState(GameState nextState)
        {
            if (CurrentState == nextState)
            {
                return;
            }

            GameState previousState = CurrentState;
            CurrentState = nextState;
            StateChanged?.Invoke(previousState, nextState);

            if (nextState == GameState.OnHook)
            {
                Debug.Log("GameState entered OnHook.", this);
            }
        }

        private void OnLureBitten(LureBittenEvent bittenEvent)
        {
            SetState(GameState.OnHook);
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

            eventBus.LureBitten += OnLureBitten;
            subscribedToEventBus = true;
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

        private bool WasPhaseBaitBallTestKeyPressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (phaseBaitBallTestKey == KeyCode.Space
                && Keyboard.current != null
                && Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                return true;
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(phaseBaitBallTestKey);
#else
            return false;
#endif
        }
    }
}
