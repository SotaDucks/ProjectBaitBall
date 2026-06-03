using TestBoids.Gameplay;
using UnityEngine;

namespace TestBoids.Boids
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public sealed class BaitBallFormationController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private InstancedFishSchoolManager target;
        [SerializeField] private BaitBallBehaviorModulator behaviorModulator;
        [SerializeField] private GameplayEventBus eventBus;
        [SerializeField] private bool autoResolveMissingReferences = true;

        [Header("Formation")]
        [SerializeField] private InstancedFishSchoolManager.FormationSettings dispersedSettings =
            InstancedFishSchoolManager.FormationSettings.CreateDispersedDefault();
        [SerializeField, Min(0f)] private float focusTransitionDuration = 3f;
        [SerializeField] private AnimationCurve focusTransitionCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Modulator")]
        [SerializeField] private bool disableModulatorUntilFocused = true;
        [SerializeField] private bool enableModulatorAfterFocused = true;

        private InstancedFishSchoolManager.FormationSettings focusedSettings;
        private InstancedFishSchoolManager.FormationSettings transitionStartSettings;
        private bool capturedFocusedSettings;
        private bool focused;
        private bool transitioning;
        private bool subscribed;
        private float transitionElapsed;

        public bool IsFocused => focused;
        public bool IsTransitioning => transitioning;

        private void Reset()
        {
            ResolveReferences();
            dispersedSettings = InstancedFishSchoolManager.FormationSettings.CreateDispersedDefault();
        }

        private void Awake()
        {
            PrepareDispersedFormation();
        }

        private void OnEnable()
        {
            PrepareDispersedFormation();
            SubscribeToEventBus();
        }

        private void Start()
        {
            PrepareDispersedFormation();
            SubscribeToEventBus();
        }

        private void OnDisable()
        {
            if (eventBus && subscribed)
            {
                eventBus.TunaSchoolFocusTriggered -= OnTunaSchoolFocusTriggered;
            }

            subscribed = false;
            transitioning = false;
        }

        private void Update()
        {
            if (!transitioning || !target)
            {
                return;
            }

            if (focusTransitionDuration <= 0f)
            {
                CompleteFocusTransition();
                return;
            }

            transitionElapsed += Time.deltaTime;
            float linearT = Mathf.Clamp01(transitionElapsed / focusTransitionDuration);
            float shapedT = focusTransitionCurve != null
                ? Mathf.Clamp01(focusTransitionCurve.Evaluate(linearT))
                : linearT;

            target.ApplyFormationSettings(
                InstancedFishSchoolManager.FormationSettings.Lerp(
                    transitionStartSettings,
                    focusedSettings,
                    shapedT));

            if (linearT >= 1f)
            {
                CompleteFocusTransition();
            }
        }

        private void OnTunaSchoolFocusTriggered(TunaSchoolFocusEvent focusEvent)
        {
            if (focused || transitioning)
            {
                return;
            }

            ResolveReferences();
            CaptureFocusedSettingsIfNeeded();
            if (!target || !capturedFocusedSettings)
            {
                return;
            }

            transitionStartSettings = target.GetFormationSettings();
            transitionElapsed = 0f;
            transitioning = true;

            if (focusTransitionDuration <= 0f)
            {
                CompleteFocusTransition();
            }
        }

        private void PrepareDispersedFormation()
        {
            ResolveReferences();
            CaptureFocusedSettingsIfNeeded();
            DisableModulatorIfNeeded();

            if (!target || focused || transitioning)
            {
                return;
            }

            target.ApplyFormationSettings(dispersedSettings, true);
        }

        private void CompleteFocusTransition()
        {
            if (!target)
            {
                transitioning = false;
                return;
            }

            target.ApplyFormationSettings(focusedSettings, true);
            transitioning = false;
            focused = true;
            EnableModulatorIfNeeded();
        }

        private void CaptureFocusedSettingsIfNeeded()
        {
            if (capturedFocusedSettings || !target)
            {
                return;
            }

            focusedSettings = target.GetFormationSettings();
            capturedFocusedSettings = true;
        }

        private void DisableModulatorIfNeeded()
        {
            if (disableModulatorUntilFocused && behaviorModulator && behaviorModulator.enabled)
            {
                behaviorModulator.enabled = false;
            }
        }

        private void EnableModulatorIfNeeded()
        {
            if (enableModulatorAfterFocused && behaviorModulator && !behaviorModulator.enabled)
            {
                behaviorModulator.enabled = true;
            }
        }

        private void SubscribeToEventBus()
        {
            if (subscribed)
            {
                return;
            }

            ResolveEventBus();
            if (!eventBus)
            {
                return;
            }

            eventBus.TunaSchoolFocusTriggered += OnTunaSchoolFocusTriggered;
            subscribed = true;
        }

        private void ResolveReferences()
        {
            if (!autoResolveMissingReferences)
            {
                return;
            }

            if (!target)
            {
                target = GetComponent<InstancedFishSchoolManager>();
            }

            if (!behaviorModulator)
            {
                behaviorModulator = GetComponent<BaitBallBehaviorModulator>();
            }

            ResolveEventBus();
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
    }
}
