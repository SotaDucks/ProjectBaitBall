using TestBoids.Boids;
using UnityEngine;

namespace TestBoids.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class FishFlockSwitcherToGameState : MonoBehaviour
    {
        [SerializeField] private GameStateManager stateManager;

        [Header("Scene Objects")]
        [SerializeField] private GameObject baitBallManager;
        [SerializeField] private GameObject tunaSchoolManager;

        [Header("Auto Resolve")]
        [SerializeField] private bool autoResolveMissingReferences = true;
        [SerializeField] private string baitBallManagerObjectName = "BaitBallManager";
        [SerializeField] private string tunaSchoolManagerObjectName = "TunaSchoolManager";

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

            if (!autoResolveMissingReferences)
            {
                return;
            }

            if (!baitBallManager)
            {
                baitBallManager = ResolveSceneObject<InstancedFishSchoolManager>(baitBallManagerObjectName);
            }

            if (!tunaSchoolManager)
            {
                tunaSchoolManager = ResolveSceneObject<FishSchoolManager>(tunaSchoolManagerObjectName);
            }
        }

        private void ApplyState(GameState state)
        {
            switch (state)
            {
                case GameState.PhaseBaitBallTransition:
                    SetActiveIfNeeded(baitBallManager, true);
                    break;

                case GameState.PhaseBaitBall:
                    SetActiveIfNeeded(baitBallManager, true);
                    SetActiveIfNeeded(tunaSchoolManager, false);
                    break;
            }
        }

        private static GameObject ResolveSceneObject<T>(string objectName) where T : Component
        {
            T[] components = FindObjectsByType<T>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            T fallback = null;
            int validCount = 0;

            foreach (T component in components)
            {
                if (!component)
                {
                    continue;
                }

                validCount++;
                if (component.gameObject.name == objectName)
                {
                    return component.gameObject;
                }

                fallback = component;
            }

            return validCount == 1 && fallback ? fallback.gameObject : null;
        }

        private static void SetActiveIfNeeded(GameObject target, bool active)
        {
            if (target && target.activeSelf != active)
            {
                target.SetActive(active);
            }
        }
    }
}
