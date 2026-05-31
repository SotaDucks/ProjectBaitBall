using UnityEngine;

namespace TestBoids.Gameplay.UI
{
    [DisallowMultipleComponent]
    public sealed class WorldSpaceUIManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GameStateManager stateManager;
        [SerializeField] private Transform playerFishTarget;
        [SerializeField] private FishStaminaRingView staminaRingPrefab;

        [Header("Stamina Ring")]
        [SerializeField] private Vector3 staminaRingOffset = new(0f, 0.8f, 0f);
        [SerializeField] private bool billboardToCamera = true;

        private FishStaminaRingView staminaRingInstance;
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
            HideStaminaRing();
        }

        private void LateUpdate()
        {
            if (!staminaRingInstance || !staminaRingInstance.gameObject.activeSelf)
            {
                return;
            }

            if (!playerFishTarget)
            {
                ResolvePlayerFishTarget();
                if (!playerFishTarget)
                {
                    HideStaminaRing();
                    return;
                }
            }

            if (!mainCamera)
            {
                mainCamera = Camera.main;
            }

            Transform ringTransform = staminaRingInstance.transform;
            ringTransform.position = playerFishTarget.position + playerFishTarget.TransformDirection(staminaRingOffset);

            if (billboardToCamera && mainCamera)
            {
                ringTransform.rotation = mainCamera.transform.rotation;
            }
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
                ShowStaminaRing();
                return;
            }

            HideStaminaRing();
        }

        private void ShowStaminaRing()
        {
            ResolvePlayerFishTarget();
            if (!playerFishTarget || !staminaRingPrefab)
            {
                return;
            }

            if (!staminaRingInstance)
            {
                staminaRingInstance = Instantiate(staminaRingPrefab, transform);
            }

            staminaRingInstance.transform.SetParent(transform, false);
            staminaRingInstance.gameObject.SetActive(true);
            staminaRingInstance.SetProgress(1f);
        }

        private void HideStaminaRing()
        {
            if (staminaRingInstance)
            {
                staminaRingInstance.gameObject.SetActive(false);
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
    }
}
