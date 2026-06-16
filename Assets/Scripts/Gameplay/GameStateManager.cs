using System;
using TestBoids.Gameplay.Lure;
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
        public Transform HookedTuna { get; private set; }
        public Transform HookedLure { get; private set; }
        public AutomaticLureMotor HookedLureMotor { get; private set; }
        public Vector3 HookedLureForward { get; private set; }

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
            if (nextState != GameState.OnHook && nextState != GameState.TunaHanging)
            {
                ClearHookedLure();
            }

            CurrentState = nextState;
            StateChanged?.Invoke(previousState, nextState);

            if (nextState == GameState.OnHook)
            {
                Debug.Log("GameState entered OnHook.", this);
            }
            else if (nextState == GameState.TunaHanging)
            {
                Debug.Log("GameState entered TunaHanging.", this);
            }
        }

        private void OnLureBitten(LureBittenEvent bittenEvent)
        {
            if (CurrentState == GameState.OnHook || CurrentState == GameState.TunaHanging)
            {
                return;
            }

            HookedTuna = bittenEvent.Tuna;
            HookedLure = bittenEvent.Lure;
            HookedLureMotor = ResolveLureMotor(bittenEvent.Lure);
            HookedLureForward = ResolveLureForward(bittenEvent);

            StopAutomaticLureSpawners();
            BeginUnhookedLureSurfaceExit(HookedLureMotor);
            SetState(GameState.OnHook);
        }

        private static void BeginUnhookedLureSurfaceExit(AutomaticLureMotor hookedLure)
        {
            AutomaticLureMotor[] lures = FindObjectsByType<AutomaticLureMotor>(FindObjectsSortMode.None);
            for (int i = 0; i < lures.Length; i++)
            {
                AutomaticLureMotor lure = lures[i];
                if (!lure || lure == hookedLure)
                {
                    continue;
                }

                lure.BeginSurfaceExit();
            }
        }

        private static void StopAutomaticLureSpawners()
        {
            AutomaticLureSpawner[] spawners = FindObjectsByType<AutomaticLureSpawner>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int i = 0; i < spawners.Length; i++)
            {
                AutomaticLureSpawner spawner = spawners[i];
                if (spawner)
                {
                    spawner.DisableAutomaticSpawning();
                }
            }
        }

        private static AutomaticLureMotor ResolveLureMotor(Transform lureTransform)
        {
            if (!lureTransform)
            {
                return null;
            }

            AutomaticLureMotor lure = lureTransform.GetComponent<AutomaticLureMotor>();
            if (lure)
            {
                return lure;
            }

            lure = lureTransform.GetComponentInParent<AutomaticLureMotor>();
            if (lure)
            {
                return lure;
            }

            return lureTransform.GetComponentInChildren<AutomaticLureMotor>();
        }

        private static Vector3 ResolveLureForward(LureBittenEvent bittenEvent)
        {
            Vector3 lureForward = Vector3.ProjectOnPlane(bittenEvent.LureForward, Vector3.up);
            if (lureForward.sqrMagnitude > 0.000001f)
            {
                return lureForward.normalized;
            }

            if (bittenEvent.Lure)
            {
                lureForward = Vector3.ProjectOnPlane(bittenEvent.Lure.forward, Vector3.up);
                if (lureForward.sqrMagnitude > 0.000001f)
                {
                    return lureForward.normalized;
                }
            }

            return Vector3.forward;
        }

        private void ClearHookedLure()
        {
            HookedTuna = null;
            HookedLure = null;
            HookedLureMotor = null;
            HookedLureForward = Vector3.forward;
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
