using UnityEngine;
using TestBoids.Tuna;

namespace TestBoids.Gameplay.UI
{
    [DisallowMultipleComponent]
    public sealed class WorldSpaceUIManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GameStateManager stateManager;
        [SerializeField] private Transform playerFishTarget;
        [SerializeField] private FishStaminaRingView staminaRingPrefab;
        [SerializeField] private FishStaminaRingView hungerRingPrefab;
        [SerializeField] private TunaCameraSideSwitcher cameraSideSwitcher;
        [SerializeField] private TunaMotor tunaMotor;

        [Header("Stamina Ring")]
        [SerializeField] private Vector3 staminaRingOffset = new(0f, 0.8f, 0f);

        [Header("Hunger Ring")]
        [SerializeField] private Vector3 hungerRingOffset = new(0f, 0.6f, 0f);

        [Header("Display")]
        [SerializeField] private bool billboardToCamera = true;

        private FishStaminaRingView staminaRingInstance;
        private FishStaminaRingView hungerRingInstance;
        private Camera mainCamera;
        private bool subscribed;

        private void Awake()
        {
            ResolveReferences();
            mainCamera = Camera.main;
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
            HideProgressRings();
        }

        private void LateUpdate()
        {
            if (!HasActiveRing())
            {
                return;
            }

            if (!playerFishTarget)
            {
                ResolvePlayerFishTarget();
                if (!playerFishTarget)
                {
                    HideProgressRings();
                    return;
                }
            }

            if (!mainCamera)
            {
                mainCamera = Camera.main;
            }

            ResolveTunaMotor();
            UpdateRing(staminaRingInstance, tunaMotor ? tunaMotor.StaminaPercent : 1f, staminaRingOffset);
            UpdateRing(hungerRingInstance, tunaMotor ? tunaMotor.HungerPercent : 0f, hungerRingOffset);
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

        private void ApplyState(GameState state)
        {
            if (state == GameState.PhaseBaitBall)
            {
                ShowProgressRings();
                return;
            }

            HideProgressRings();
        }

        private void ShowProgressRings()
        {
            ResolvePlayerFishTarget();
            if (!playerFishTarget)
            {
                return;
            }

            staminaRingInstance = ShowRing(staminaRingInstance, staminaRingPrefab);
            hungerRingInstance = ShowRing(hungerRingInstance, hungerRingPrefab);

            ResolveTunaMotor();
            UpdateRing(staminaRingInstance, tunaMotor ? tunaMotor.StaminaPercent : 1f, staminaRingOffset);
            UpdateRing(hungerRingInstance, tunaMotor ? tunaMotor.HungerPercent : 0f, hungerRingOffset);
        }

        private void HideProgressRings()
        {
            HideRing(staminaRingInstance);
            HideRing(hungerRingInstance);
        }

        private FishStaminaRingView ShowRing(
            FishStaminaRingView instance,
            FishStaminaRingView prefab)
        {
            if (!prefab)
            {
                return instance;
            }

            if (!instance)
            {
                instance = Instantiate(prefab, transform);
            }

            instance.transform.SetParent(transform, false);
            instance.gameObject.SetActive(true);
            return instance;
        }

        private void HideRing(FishStaminaRingView ring)
        {
            if (ring)
            {
                ring.gameObject.SetActive(false);
            }
        }

        private bool HasActiveRing()
        {
            return IsRingActive(staminaRingInstance) || IsRingActive(hungerRingInstance);
        }

        private bool IsRingActive(FishStaminaRingView ring)
        {
            return ring && ring.gameObject.activeSelf;
        }

        private void UpdateRing(
            FishStaminaRingView ring,
            float percent,
            Vector3 offset)
        {
            if (!IsRingActive(ring) || !playerFishTarget)
            {
                return;
            }

            ring.SetProgress(percent);

            Transform ringTransform = ring.transform;
            ringTransform.position = playerFishTarget.position
                + playerFishTarget.TransformDirection(GetCameraSideAdjustedOffset(offset));

            if (billboardToCamera && mainCamera)
            {
                ringTransform.rotation = mainCamera.transform.rotation;
            }
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

            ResolvePlayerFishTarget();
            ResolveCameraSideSwitcher();
            ResolveTunaMotor();
        }

        private void ResolvePlayerFishTarget()
        {
            if (playerFishTarget)
            {
                return;
            }

            PlayerFishSchoolBridge bridge = FindFirstObjectByType<PlayerFishSchoolBridge>(
                FindObjectsInactive.Include);
            if (bridge)
            {
                playerFishTarget = bridge.transform;
            }
        }

        private void ResolveTunaMotor()
        {
            if (tunaMotor)
            {
                return;
            }

            if (playerFishTarget)
            {
                tunaMotor = playerFishTarget.GetComponent<TunaMotor>();
                if (tunaMotor)
                {
                    return;
                }
            }

            tunaMotor = FindFirstObjectByType<TunaMotor>(FindObjectsInactive.Include);
        }

        private void ResolveCameraSideSwitcher()
        {
            if (cameraSideSwitcher)
            {
                return;
            }

            cameraSideSwitcher = FindFirstObjectByType<TunaCameraSideSwitcher>(
                FindObjectsInactive.Include);
        }

        private Vector3 GetCameraSideAdjustedOffset(Vector3 offset)
        {
            if (!cameraSideSwitcher)
            {
                ResolveCameraSideSwitcher();
                if (!cameraSideSwitcher)
                {
                    return offset;
                }
            }

            float side = cameraSideSwitcher.NormalizedCameraSide;
            float mirroredX = -offset.x;
            return new Vector3(
                Mathf.Lerp(offset.x, mirroredX, side),
                offset.y,
                offset.z);
        }
    }
}
