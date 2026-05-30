using System.Collections;
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

        [Header("Scripted Exit")]
        [SerializeField, Min(0f)] private float scriptedExitDuration = 1.5f;
        [SerializeField, Min(0f)] private float scriptedExitLeftWeight = 0.75f;
        [SerializeField, Min(0f)] private float scriptedExitOutwardWeight = 0.55f;
        [SerializeField, Range(-1f, 1f)] private float scriptedExitTurnInput = -1f;
        [Tooltip("Multiplier for the scripted exit turn response. Lower than 1 turns slower; higher than 1 turns faster.")]
        [SerializeField, Min(0f)] private float scriptedExitTurnSpeedScale = 1f;

        private bool released;
        private bool subscribed;
        private Coroutine scriptedExitRoutine;
        private Vector3 scriptedExitDirection = Vector3.forward;

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
            StopScriptedExit();

            if (stateManager && subscribed)
            {
                stateManager.StateChanged -= OnStateChanged;
            }

            subscribed = false;
        }

        private void OnStateChanged(GameState previousState, GameState nextState)
        {
            if (nextState == GameState.PhaseBaitBallTransition)
            {
                BeginScriptedExit();
            }
            else if (nextState == GameState.PhaseBaitBall)
            {
                CompletePlayerControl();
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

            if (stateManager.CurrentState == GameState.PhaseBaitBallTransition)
            {
                BeginScriptedExit();
            }
            else if (stateManager.CurrentState == GameState.PhaseBaitBall)
            {
                CompletePlayerControl();
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

        private void BeginScriptedExit()
        {
            ReleasePlayerFish();
            EnableTunaMotor();

            scriptedExitDirection = BuildScriptedExitDirection();
            if (tunaMotor)
            {
                tunaMotor.BeginScriptedSwim(
                    scriptedExitDirection,
                    scriptedExitTurnInput,
                    scriptedExitTurnSpeedScale);
            }

            StopScriptedExit();
            if (scriptedExitDuration <= 0f)
            {
                AdvanceToPhaseBaitBall();
                return;
            }

            scriptedExitRoutine = StartCoroutine(RunScriptedExit());
        }

        private IEnumerator RunScriptedExit()
        {
            float remaining = scriptedExitDuration;
            while (remaining > 0f)
            {
                remaining -= Time.deltaTime;
                yield return null;
            }

            scriptedExitRoutine = null;
            AdvanceToPhaseBaitBall();
        }

        private void AdvanceToPhaseBaitBall()
        {
            if (stateManager && stateManager.CurrentState == GameState.PhaseBaitBallTransition)
            {
                stateManager.SetState(GameState.PhaseBaitBall);
            }
            else
            {
                CompletePlayerControl();
            }
        }

        private void CompletePlayerControl()
        {
            StopScriptedExit();
            ReleasePlayerFish();
            EnableTunaMotor();

            if (tunaMotor)
            {
                tunaMotor.EndScriptedSwim();
            }
        }

        private void StopScriptedExit()
        {
            if (scriptedExitRoutine == null)
            {
                return;
            }

            StopCoroutine(scriptedExitRoutine);
            scriptedExitRoutine = null;
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

            EnableTunaMotor();
        }

        private void EnableTunaMotor()
        {
            if (activateMotorOnPhaseBaitBall && tunaMotor)
            {
                tunaMotor.enabled = true;
            }
        }

        private Vector3 BuildScriptedExitDirection()
        {
            Vector3 left = transform.right.sqrMagnitude > 0.000001f
                ? -transform.right.normalized
                : Vector3.left;
            Vector3 direction = left * scriptedExitLeftWeight;

            if (schoolManager)
            {
                Vector3 outward = transform.position - schoolManager.transform.position;
                if (outward.sqrMagnitude > 0.000001f)
                {
                    direction += outward.normalized * scriptedExitOutwardWeight;
                }
            }

            return direction.sqrMagnitude > 0.000001f ? direction.normalized : left;
        }
    }
}
