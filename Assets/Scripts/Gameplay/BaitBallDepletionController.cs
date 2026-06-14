using System;
using TestBoids.Boids;
using TestBoids.Tuna;
using UnityEngine;

namespace TestBoids.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class BaitBallDepletionController : MonoBehaviour
    {
        [Serializable]
        private struct DepletionSettings
        {
            [Range(0f, 1f)] public float HungerThreshold;
            [Min(0f)] public float ElapsedTimeThreshold;
            [Min(0)] public int TargetFishCount;
            [Min(0f)] public float DepletionDuration;

            public DepletionSettings(
                float hungerThreshold,
                float elapsedTimeThreshold,
                int targetFishCount,
                float depletionDuration)
            {
                HungerThreshold = hungerThreshold;
                ElapsedTimeThreshold = elapsedTimeThreshold;
                TargetFishCount = targetFishCount;
                DepletionDuration = depletionDuration;
            }
        }

        [Serializable]
        private struct LooseFormationSettings
        {
            [Min(0.001f)] public float Radius;
            [Min(0f)] public float CenteringWeight;
            [Min(0f)] public float ToroidalFlowWeight;
            [Min(0f)] public float SeparationRadius;
            [Min(0f)] public float AlignWeight;
            [Min(0f)] public float CohesionWeight;

            public LooseFormationSettings(
                float radius,
                float centeringWeight,
                float toroidalFlowWeight,
                float separationRadius,
                float alignWeight,
                float cohesionWeight)
            {
                Radius = radius;
                CenteringWeight = centeringWeight;
                ToroidalFlowWeight = toroidalFlowWeight;
                SeparationRadius = separationRadius;
                AlignWeight = alignWeight;
                CohesionWeight = cohesionWeight;
            }
        }

        [Header("References")]
        [SerializeField] private GameplayEventBus eventBus;
        [SerializeField] private TunaMotor tunaMotor;
        [SerializeField] private InstancedFishSchoolManager baitBallManager;
        [SerializeField] private BaitBallBehaviorModulator behaviorModulator;
        [SerializeField] private TunaSchoolFocusSequenceController focusSequenceController;
        [SerializeField] private bool autoResolveMissingReferences = true;

        [Header("Depletion")]
        [Tooltip("Depletion starts when either the hunger or cumulative time threshold is reached.")]
        [SerializeField] private DepletionSettings depletion = new(0.7f, 90f, 5000, 3f);

        [Header("Loose Formation")]
        [SerializeField] private bool transitionToLooseFormation = true;
        [SerializeField] private bool disableBehaviorModulatorOnDepletion = true;
        [SerializeField] private LooseFormationSettings looseFormation =
            new(10f, 0.15f, 0.1f, 3f, 0.15f, 0.1f);
        [SerializeField] private AnimationCurve formationTransitionCurve =
            AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Timing")]
        [Tooltip("When enabled, depletion continues even if gameplay time scale is paused.")]
        [SerializeField] private bool useUnscaledTime;

        private bool subscribed;
        private bool started;
        private bool monitoring;
        private bool depletionRunning;
        private int depletionTargetFishCount;
        private int plannedControllerRemovals;
        private int completedControllerRemovals;
        private float elapsedTime;
        private float depletionElapsed;
        private InstancedFishSchoolManager.FormationSettings startFormation;
        private InstancedFishSchoolManager.FormationSettings targetFormation;

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
            SubscribeToEventBus();
        }

        private void Start()
        {
            SubscribeToEventBus();
        }

        private void OnValidate()
        {
            depletion.HungerThreshold = Mathf.Clamp01(depletion.HungerThreshold);
            depletion.ElapsedTimeThreshold = Mathf.Max(0f, depletion.ElapsedTimeThreshold);
            depletion.TargetFishCount = Mathf.Max(0, depletion.TargetFishCount);
            depletion.DepletionDuration = Mathf.Max(0f, depletion.DepletionDuration);

            looseFormation.Radius = Mathf.Max(0.001f, looseFormation.Radius);
            looseFormation.CenteringWeight = Mathf.Max(0f, looseFormation.CenteringWeight);
            looseFormation.ToroidalFlowWeight = Mathf.Max(0f, looseFormation.ToroidalFlowWeight);
            looseFormation.SeparationRadius = Mathf.Max(0f, looseFormation.SeparationRadius);
            looseFormation.AlignWeight = Mathf.Max(0f, looseFormation.AlignWeight);
            looseFormation.CohesionWeight = Mathf.Max(0f, looseFormation.CohesionWeight);
        }

        private void OnDisable()
        {
            if (eventBus && subscribed)
            {
                eventBus.SardineSchoolGathered -= OnSardineSchoolGathered;
            }

            subscribed = false;
            monitoring = false;
            depletionRunning = false;
        }

        private void Update()
        {
            if (!monitoring)
            {
                return;
            }

            float deltaTime = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            elapsedTime += deltaTime;

            if (depletionRunning)
            {
                UpdateDepletion(deltaTime);
                return;
            }

            TryStartDepletion();
        }

        private void OnSardineSchoolGathered(SardineSchoolGatheredEvent gatheredEvent)
        {
            if (started)
            {
                return;
            }

            ResolveReferences(gatheredEvent);
            if (!tunaMotor || !baitBallManager)
            {
                return;
            }

            started = true;
            monitoring = true;
            elapsedTime = 0f;
            TryStartDepletion();
        }

        private void TryStartDepletion()
        {
            if (!monitoring || depletionRunning)
            {
                return;
            }

            bool hungerReached = tunaMotor && tunaMotor.HungerPercent >= depletion.HungerThreshold;
            bool timeReached = elapsedTime >= depletion.ElapsedTimeThreshold;
            if (!hungerReached && !timeReached)
            {
                return;
            }

            BeginDepletion();
        }

        private void BeginDepletion()
        {
            if (!baitBallManager)
            {
                monitoring = false;
                return;
            }

            depletionRunning = true;
            depletionElapsed = 0f;
            depletionTargetFishCount = Mathf.Max(0, depletion.TargetFishCount);
            plannedControllerRemovals = Mathf.Max(0, baitBallManager.CurrentFishCount - depletionTargetFishCount);
            completedControllerRemovals = 0;

            if (transitionToLooseFormation)
            {
                DisableBehaviorModulatorIfNeeded();
                startFormation = baitBallManager.GetFormationSettings();
                targetFormation = BuildLooseFormationTarget(startFormation);
            }

            if (depletion.DepletionDuration <= 0f)
            {
                ApplyDepletionProgress(1f);
                CompleteDepletion();
            }
        }

        private void UpdateDepletion(float deltaTime)
        {
            if (!baitBallManager)
            {
                monitoring = false;
                depletionRunning = false;
                return;
            }

            depletionElapsed += deltaTime;
            float progress = Mathf.Clamp01(depletionElapsed / depletion.DepletionDuration);
            ApplyDepletionProgress(progress);
            if (progress >= 1f)
            {
                CompleteDepletion();
            }
        }

        private void ApplyDepletionProgress(float progress)
        {
            int desiredControllerRemovals = progress >= 1f
                ? plannedControllerRemovals
                : Mathf.FloorToInt(plannedControllerRemovals * progress);

            int remainingControllerRemovals = desiredControllerRemovals - completedControllerRemovals;
            int removableFishCount = baitBallManager.CurrentFishCount - depletionTargetFishCount;
            if (remainingControllerRemovals > 0 && removableFishCount > 0)
            {
                int removeCount = Mathf.Min(remainingControllerRemovals, removableFishCount);
                completedControllerRemovals += baitBallManager.RemoveRandomFish(removeCount);
            }

            if (!transitionToLooseFormation)
            {
                return;
            }

            float shapedProgress = formationTransitionCurve != null
                ? Mathf.Clamp01(formationTransitionCurve.Evaluate(progress))
                : progress;
            baitBallManager.ApplyFormationSettings(
                InstancedFishSchoolManager.FormationSettings.Lerp(
                    startFormation,
                    targetFormation,
                    shapedProgress));
        }

        private void CompleteDepletion()
        {
            if (transitionToLooseFormation && baitBallManager)
            {
                baitBallManager.ApplyFormationSettings(targetFormation);
            }

            depletionRunning = false;
            monitoring = false;

            ResolveReferences();
            if (focusSequenceController)
            {
                focusSequenceController.RetireBarracudaSchool();
            }
        }

        private InstancedFishSchoolManager.FormationSettings BuildLooseFormationTarget(
            InstancedFishSchoolManager.FormationSettings source)
        {
            source.Radius = looseFormation.Radius;
            source.CenteringWeight = looseFormation.CenteringWeight;
            source.ToroidalFlowWeight = looseFormation.ToroidalFlowWeight;
            source.SeparationRadius = looseFormation.SeparationRadius;
            source.AlignWeight = looseFormation.AlignWeight;
            source.CohesionWeight = looseFormation.CohesionWeight;
            return source;
        }

        private void SubscribeToEventBus()
        {
            if (subscribed)
            {
                return;
            }

            ResolveReferences();
            if (!eventBus)
            {
                return;
            }

            eventBus.SardineSchoolGathered += OnSardineSchoolGathered;
            subscribed = true;
        }

        private void ResolveReferences()
        {
            if (!autoResolveMissingReferences)
            {
                return;
            }

            if (!eventBus)
            {
                eventBus = GameplayEventBus.Instance;
            }

            if (!eventBus)
            {
                eventBus = FindFirstObjectByType<GameplayEventBus>(FindObjectsInactive.Include);
            }

            if (!tunaMotor)
            {
                tunaMotor = FindFirstObjectByType<TunaMotor>(FindObjectsInactive.Include);
            }

            if (!baitBallManager)
            {
                baitBallManager = FindFirstObjectByType<InstancedFishSchoolManager>(FindObjectsInactive.Include);
            }

            if (!behaviorModulator && baitBallManager)
            {
                behaviorModulator = baitBallManager.GetComponent<BaitBallBehaviorModulator>();
            }

            if (!focusSequenceController)
            {
                focusSequenceController = FindFirstObjectByType<TunaSchoolFocusSequenceController>(
                    FindObjectsInactive.Include);
            }
        }

        private void ResolveReferences(SardineSchoolGatheredEvent gatheredEvent)
        {
            if (!autoResolveMissingReferences)
            {
                return;
            }

            if (gatheredEvent.Tuna)
            {
                TunaMotor eventTunaMotor = gatheredEvent.Tuna.GetComponent<TunaMotor>();
                if (!eventTunaMotor)
                {
                    eventTunaMotor = gatheredEvent.Tuna.GetComponentInChildren<TunaMotor>(true);
                }

                if (eventTunaMotor)
                {
                    tunaMotor = eventTunaMotor;
                }
            }

            if (gatheredEvent.FishSchool)
            {
                InstancedFishSchoolManager eventBaitBallManager =
                    gatheredEvent.FishSchool.GetComponent<InstancedFishSchoolManager>();
                if (!eventBaitBallManager)
                {
                    eventBaitBallManager =
                        gatheredEvent.FishSchool.GetComponentInChildren<InstancedFishSchoolManager>(true);
                }

                if (eventBaitBallManager)
                {
                    baitBallManager = eventBaitBallManager;
                    behaviorModulator = baitBallManager.GetComponent<BaitBallBehaviorModulator>();
                }
            }

            ResolveReferences();
        }

        private void DisableBehaviorModulatorIfNeeded()
        {
            if (!disableBehaviorModulatorOnDepletion)
            {
                return;
            }

            ResolveReferences();
            if (behaviorModulator)
            {
                behaviorModulator.enabled = false;
            }
        }
    }
}
