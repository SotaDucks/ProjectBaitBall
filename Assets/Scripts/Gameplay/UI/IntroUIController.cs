using UnityEngine;

namespace TestBoids.Gameplay.UI
{
    [DisallowMultipleComponent]
    public sealed class IntroUIController : MonoBehaviour
    {
        [SerializeField] private GameStateManager stateManager;
        [SerializeField] private IntroUIView introView;

        private bool subscribed;

        private void Awake()
        {
            ResolveReferences();
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

            ResolveReferences();
            if (!stateManager)
            {
                return;
            }

            stateManager.StateChanged += OnStateChanged;
            subscribed = true;
            ApplyState(stateManager.CurrentState);
        }

        private void ResolveReferences()
        {
            if (!stateManager)
            {
                stateManager = GameStateManager.Instance;
            }

            if (!stateManager)
            {
                stateManager = FindFirstObjectByType<GameStateManager>();
            }

            if (!introView)
            {
                introView = GetComponentInChildren<IntroUIView>(true);
            }
        }

        private void ApplyState(GameState state)
        {
            if (!introView)
            {
                return;
            }

            switch (state)
            {
                case GameState.Intro:
                    introView.ShowImmediate();
                    break;

                case GameState.PhaseBaitBallTransition:
                    introView.FadeOut();
                    break;

                default:
                    introView.HideImmediate();
                    break;
            }
        }
    }
}
