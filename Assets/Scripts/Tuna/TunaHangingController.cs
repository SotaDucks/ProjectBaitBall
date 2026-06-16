using System.Collections;
using TestBoids.Gameplay;
using UnityEngine;

namespace TestBoids.Tuna
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class TunaHangingController : MonoBehaviour
    {
        private const string DefaultHangingPositionObjectName = "TunaHangingPosition";

        [Header("References")]
        [SerializeField] private GameStateManager stateManager;
        [SerializeField] private TunaOnHookController onHookController;
        [SerializeField] private TunaMotor tunaMotor;
        [SerializeField] private Rigidbody body;
        [SerializeField] private Transform hangingPosition;
        [SerializeField] private Transform hangingPositionSearchRoot;
        [SerializeField] private string hangingPositionObjectName = DefaultHangingPositionObjectName;

        [Header("Motion")]
        [SerializeField, Min(0f)] private float moveDuration = 2f;
        [SerializeField] private bool followHangingPositionAfterArrival = true;

        private bool subscribedToStateManager;
        private bool isHanging;
        private bool warnedMissingHangingPosition;
        private bool originalBodyIsKinematic;
        private bool originalBodyUseGravity;
        private bool originalTunaMotorEnabled;
        private bool originalOnHookControllerEnabled;
        private Coroutine hangingRoutine;

        private void Reset()
        {
            ResolveReferences();
        }

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
            ApplyState(stateManager ? stateManager.CurrentState : GameState.Intro);
        }

        private void OnDisable()
        {
            if (stateManager && subscribedToStateManager)
            {
                stateManager.StateChanged -= OnStateChanged;
            }

            subscribedToStateManager = false;
            StopHanging(true);
        }

        private void OnValidate()
        {
            moveDuration = Mathf.Max(0f, moveDuration);
            if (string.IsNullOrWhiteSpace(hangingPositionObjectName))
            {
                hangingPositionObjectName = DefaultHangingPositionObjectName;
            }
        }

        private void OnStateChanged(GameState previousState, GameState nextState)
        {
            ApplyState(nextState);
        }

        private void ApplyState(GameState state)
        {
            if (state == GameState.TunaHanging && IsThisHookedTuna())
            {
                BeginHanging();
                return;
            }

            StopHanging(true);
        }

        private void BeginHanging()
        {
            if (isHanging)
            {
                return;
            }

            ResolveReferences();
            isHanging = true;
            warnedMissingHangingPosition = false;

            if (body)
            {
                originalBodyIsKinematic = body.isKinematic;
                originalBodyUseGravity = body.useGravity;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.useGravity = false;
                body.isKinematic = true;
            }

            if (tunaMotor)
            {
                originalTunaMotorEnabled = tunaMotor.enabled;
                if (tunaMotor.IsExternallyControlled)
                {
                    tunaMotor.EndExternalControl();
                }

                tunaMotor.enabled = false;
            }

            if (onHookController)
            {
                originalOnHookControllerEnabled = onHookController.enabled;
                onHookController.enabled = false;
            }

            if (hangingRoutine != null)
            {
                StopCoroutine(hangingRoutine);
            }

            hangingRoutine = StartCoroutine(RunHangingMotion());
        }

        private IEnumerator RunHangingMotion()
        {
            while (isHanging && !TryResolveHangingPosition())
            {
                WarnMissingHangingPositionOnce();
                yield return null;
            }

            if (!isHanging || !hangingPosition)
            {
                hangingRoutine = null;
                yield break;
            }

            Vector3 startPosition = transform.position;
            Quaternion startRotation = transform.rotation;

            if (moveDuration <= 0f)
            {
                transform.SetPositionAndRotation(hangingPosition.position, hangingPosition.rotation);
            }
            else
            {
                float elapsed = 0f;
                while (isHanging && hangingPosition && elapsed < moveDuration)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / moveDuration);
                    t = t * t * (3f - 2f * t);

                    transform.SetPositionAndRotation(
                        Vector3.Lerp(startPosition, hangingPosition.position, t),
                        Quaternion.Slerp(startRotation, hangingPosition.rotation, t));

                    yield return null;
                }
            }

            while (isHanging && followHangingPositionAfterArrival)
            {
                if (TryResolveHangingPosition())
                {
                    transform.SetPositionAndRotation(hangingPosition.position, hangingPosition.rotation);
                }

                yield return null;
            }

            hangingRoutine = null;
        }

        private void StopHanging(bool restoreControl)
        {
            if (hangingRoutine != null)
            {
                StopCoroutine(hangingRoutine);
                hangingRoutine = null;
            }

            if (!isHanging)
            {
                return;
            }

            isHanging = false;

            if (body)
            {
                body.isKinematic = originalBodyIsKinematic;
                body.useGravity = originalBodyUseGravity;
            }

            if (!restoreControl)
            {
                return;
            }

            if (tunaMotor)
            {
                tunaMotor.enabled = originalTunaMotorEnabled;
            }

            if (onHookController)
            {
                onHookController.enabled = originalOnHookControllerEnabled;
            }
        }

        private bool TryResolveHangingPosition()
        {
            if (hangingPosition)
            {
                return true;
            }

            ResolveReferences();
            if (hangingPosition)
            {
                return true;
            }

            if (hangingPositionSearchRoot)
            {
                hangingPosition = FindChildByName(hangingPositionSearchRoot, hangingPositionObjectName);
                if (hangingPosition)
                {
                    return true;
                }
            }

            if (onHookController && onHookController.pullTarget)
            {
                Transform root = onHookController.pullTarget.root;
                hangingPosition = FindChildByName(root, hangingPositionObjectName);
                if (hangingPosition)
                {
                    return true;
                }
            }

            hangingPosition = FindTransformByName(hangingPositionObjectName);
            return hangingPosition;
        }

        private bool IsThisHookedTuna()
        {
            if (!stateManager || !stateManager.HookedTuna)
            {
                return true;
            }

            Transform hookedTuna = stateManager.HookedTuna;
            return hookedTuna == transform
                || hookedTuna.IsChildOf(transform)
                || transform.IsChildOf(hookedTuna);
        }

        private void WarnMissingHangingPositionOnce()
        {
            if (warnedMissingHangingPosition)
            {
                return;
            }

            warnedMissingHangingPosition = true;
            Debug.LogWarning(
                $"{nameof(TunaHangingController)} on {name} needs a {hangingPositionObjectName} Transform to hang the tuna.",
                this);
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

        private void ResolveReferences()
        {
            if (!body)
            {
                body = GetComponent<Rigidbody>();
            }

            if (!onHookController)
            {
                onHookController = GetComponent<TunaOnHookController>();
            }

            if (!tunaMotor)
            {
                tunaMotor = GetComponent<TunaMotor>();
            }

            ResolveStateManager();
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

        private static Transform FindChildByName(Transform root, string childName)
        {
            if (!root || string.IsNullOrWhiteSpace(childName))
            {
                return null;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.name == childName)
                {
                    return child;
                }

                Transform nested = FindChildByName(child, childName);
                if (nested)
                {
                    return nested;
                }
            }

            return null;
        }

        private static Transform FindTransformByName(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
            {
                return null;
            }

            Transform[] transforms = FindObjectsByType<Transform>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform candidate = transforms[i];
                if (candidate && candidate.name == objectName)
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}
