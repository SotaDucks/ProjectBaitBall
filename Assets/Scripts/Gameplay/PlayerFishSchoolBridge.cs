using TestBoids.Boids;
using TestBoids.Tuna;
using UnityEngine;

namespace TestBoids.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class PlayerFishSchoolBridge : MonoBehaviour
    {
        [SerializeField] private GameStateManager stateManager;
        [SerializeField] private FishSchoolManager schoolManager;
        [SerializeField] private FishAgent playerAgent;
        [SerializeField] private TunaMotor tunaMotor;
        [SerializeField] private Rigidbody playerRigidbody;
        [SerializeField] private bool inheritSchoolVelocity = true;
        [SerializeField] private bool activateMotorOnPhaseBaitBall = true;
        [SerializeField] private bool destroyFishAgentOnRelease = true;

        private bool released;
        private bool subscribed;

        private void Reset()
        {
            playerAgent = GetComponent<FishAgent>();
            tunaMotor = GetComponent<TunaMotor>();
            playerRigidbody = GetComponent<Rigidbody>();
        }

        private void Awake()
        {
            if (!stateManager)
            {
                stateManager = GameStateManager.Instance;
            }

            if (!schoolManager)
            {
                schoolManager = FindFirstObjectByType<FishSchoolManager>();
            }

            if (!playerAgent)
            {
                playerAgent = GetComponent<FishAgent>();
            }

            if (!tunaMotor)
            {
                tunaMotor = GetComponent<TunaMotor>();
            }

            if (!playerRigidbody)
            {
                playerRigidbody = GetComponent<Rigidbody>();
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
            if (stateManager && subscribed)
            {
                stateManager.StateChanged -= OnStateChanged;
            }

            subscribed = false;
        }

        private void OnStateChanged(GameState previousState, GameState nextState)
        {
            if (nextState == GameState.PhaseBaitBall)
            {
                ReleasePlayerFish();
            }
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

            if (stateManager.CurrentState == GameState.PhaseBaitBall)
            {
                ReleasePlayerFish();
            }
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

        private void ReleasePlayerFish()
        {
            if (released)
            {
                return;
            }

            released = true;
            Vector3 inheritedVelocity = playerAgent && playerAgent.Velocity.sqrMagnitude > 0.000001f
                ? playerAgent.Velocity
                : transform.forward;

            if (schoolManager && playerAgent && schoolManager.TryReleaseAgent(playerAgent, out Vector3 schoolVelocity))
            {
                if (schoolVelocity.sqrMagnitude > 0.000001f)
                {
                    inheritedVelocity = schoolVelocity;
                }
            }

            if (inheritSchoolVelocity && playerRigidbody)
            {
                playerRigidbody.linearVelocity = inheritedVelocity;
            }

            if (destroyFishAgentOnRelease && playerAgent)
            {
                Destroy(playerAgent);
                playerAgent = null;
            }

            if (activateMotorOnPhaseBaitBall && tunaMotor)
            {
                tunaMotor.enabled = true;
            }
        }
    }
}
