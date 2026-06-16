using UnityEngine;

namespace TestBoids.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class BoatOnHookRepositioner : MonoBehaviour
    {
        [SerializeField] private GameStateManager stateManager;
        [SerializeField, Min(0f)] private float distanceBehindLure = 50f;
        [SerializeField] private bool preserveBoatHeight = true;
        [SerializeField] private bool faceTunaAfterReposition = true;

        private bool subscribedToStateManager;
        private bool hasRepositionedForCurrentHook;

        private void Reset()
        {
            ResolveStateManager();
        }

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
            if (stateManager && stateManager.CurrentState == GameState.OnHook)
            {
                RepositionForCurrentHook();
            }
        }

        private void OnDisable()
        {
            if (stateManager && subscribedToStateManager)
            {
                stateManager.StateChanged -= OnStateChanged;
            }

            subscribedToStateManager = false;
        }

        private void OnValidate()
        {
            distanceBehindLure = Mathf.Max(0f, distanceBehindLure);
        }

        private void OnStateChanged(GameState previousState, GameState nextState)
        {
            if (nextState != GameState.OnHook)
            {
                hasRepositionedForCurrentHook = false;
                return;
            }

            RepositionForCurrentHook();
        }

        private void RepositionForCurrentHook()
        {
            if (hasRepositionedForCurrentHook)
            {
                return;
            }

            ResolveStateManager();
            if (!stateManager || !stateManager.HookedTuna || !stateManager.HookedLure)
            {
                return;
            }

            Transform tuna = stateManager.HookedTuna;
            Vector3 behindDirection = GetHorizontalDirection(-stateManager.HookedLureForward, -tuna.forward);
            Vector3 nextPosition = tuna.position + behindDirection * distanceBehindLure;
            if (preserveBoatHeight)
            {
                nextPosition.y = transform.position.y;
            }

            transform.position = nextPosition;

            if (faceTunaAfterReposition)
            {
                Vector3 lookDirection = GetHorizontalDirection(tuna.position - transform.position, transform.forward);
                transform.rotation = Quaternion.LookRotation(lookDirection, Vector3.up);
            }

            hasRepositionedForCurrentHook = true;
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
        }

        private void ResolveStateManager()
        {
            if (!stateManager)
            {
                stateManager = GameStateManager.Instance;
            }

            if (!stateManager)
            {
                stateManager = FindFirstObjectByType<GameStateManager>(FindObjectsInactive.Include);
            }
        }

        private static Vector3 GetHorizontalDirection(Vector3 direction, Vector3 fallback)
        {
            Vector3 horizontal = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (horizontal.sqrMagnitude <= 0.000001f)
            {
                horizontal = Vector3.ProjectOnPlane(fallback, Vector3.up);
            }

            return horizontal.sqrMagnitude > 0.000001f ? horizontal.normalized : Vector3.forward;
        }
    }
}
