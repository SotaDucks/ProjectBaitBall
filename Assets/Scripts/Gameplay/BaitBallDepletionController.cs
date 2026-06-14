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
        private struct DepletionStage
        {
            [Range(0f, 1f)] public float HungerThreshold;
            [Min(0f)] public float ElapsedTimeThreshold;
            [Min(0)] public int TargetFishCount;
            [Min(0f)] public float DepletionDuration;

            public DepletionStage(
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
        [SerializeField] private bool autoResolveMissingReferences = true;

        [Header("Depletion Stages")]
        [Tooltip("Stages run in array order. A stage starts when either its hunger or cumulative time threshold is reached.")]
        [SerializeField] private DepletionStage[] stages =
        {
            new(0.25f, 30f, 50, 1.5f),
            new(0.5f, 60f, 30, 2f),
            new(0.75f, 90f, 12, 3f)
        };

        [Header("Final Loose Formation")]
        [SerializeField] private bool transitionFinalStageToLooseFormation = true;
        [SerializeField] private bool disableBehaviorModulatorOnFinalStage = true;
        [SerializeField] private LooseFormationSettings finalLooseFormation =
            new(10f, 0.15f, 0.1f, 3f, 0.15f, 0.1f);
        [SerializeField] private AnimationCurve finalFormationTransitionCurve =
            AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Timing")]
        [Tooltip("When enabled, depletion continues even if gameplay time scale is paused.")]
        [SerializeField] private bool useUnscaledTime;

        private bool subscribed;
        private bool started;
        private bool monitoring;
        private bool stageRunning;
        private int activeStageIndex;
        private int stageStartFishCount;
        private int stageTargetFishCount;
        private int plannedControllerRemovals;
        private int completedControllerRemovals;
        private float elapsedTime;
        private float stageElapsed;
        private InstancedFishSchoolManager.FormationSettings stageStartFormation;
        private InstancedFishSchoolManager.FormationSettings stageTargetFormation;

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
            if (stages != null)
            {
                for (int i = 0; i < stages.Length; i++)
                {
                    DepletionStage stage = stages[i];
                    stage.HungerThreshold = Mathf.Clamp01(stage.HungerThreshold);
                    stage.ElapsedTimeThreshold = Mathf.Max(0f, stage.ElapsedTimeThreshold);
                    stage.TargetFishCount = Mathf.Max(0, stage.TargetFishCount);
                    stage.DepletionDuration = Mathf.Max(0f, stage.DepletionDuration);
                    stages[i] = stage;
                }
            }

            finalLooseFormation.Radius = Mathf.Max(0.001f, finalLooseFormation.Radius);
            finalLooseFormation.CenteringWeight = Mathf.Max(0f, finalLooseFormation.CenteringWeight);
            finalLooseFormation.ToroidalFlowWeight = Mathf.Max(0f, finalLooseFormation.ToroidalFlowWeight);
            finalLooseFormation.SeparationRadius = Mathf.Max(0f, finalLooseFormation.SeparationRadius);
            finalLooseFormation.AlignWeight = Mathf.Max(0f, finalLooseFormation.AlignWeight);
            finalLooseFormation.CohesionWeight = Mathf.Max(0f, finalLooseFormation.CohesionWeight);
        }

        private void OnDisable()
        {
            if (eventBus && subscribed)
            {
                eventBus.SardineSchoolGathered -= OnSardineSchoolGathered;
            }

            subscribed = false;
            monitoring = false;
            stageRunning = false;
        }

        private void Update()
        {
            if (!monitoring)
            {
                return;
            }

            float deltaTime = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            elapsedTime += deltaTime;

            if (stageRunning)
            {
                UpdateActiveStage(deltaTime);
                return;
            }

            TryStartNextStage();
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
            activeStageIndex = 0;
            elapsedTime = 0f;
            TryStartNextStage();
        }

        private void TryStartNextStage()
        {
            if (!monitoring || stageRunning)
            {
                return;
            }

            if (stages == null || activeStageIndex >= stages.Length)
            {
                monitoring = false;
                return;
            }

            DepletionStage stage = stages[activeStageIndex];
            bool hungerReached = tunaMotor && tunaMotor.HungerPercent >= stage.HungerThreshold;
            bool timeReached = elapsedTime >= stage.ElapsedTimeThreshold;
            if (!hungerReached && !timeReached)
            {
                return;
            }

            BeginStage(stage);
        }

        private void BeginStage(DepletionStage stage)
        {
            if (!baitBallManager)
            {
                monitoring = false;
                return;
            }

            stageRunning = true;
            stageElapsed = 0f;
            stageStartFishCount = baitBallManager.CurrentFishCount;
            stageTargetFishCount = Mathf.Max(0, stage.TargetFishCount);
            plannedControllerRemovals = Mathf.Max(0, stageStartFishCount - stageTargetFishCount);
            completedControllerRemovals = 0;

            if (ShouldTransitionFormation())
            {
                DisableBehaviorModulatorIfNeeded();
                stageStartFormation = baitBallManager.GetFormationSettings();
                stageTargetFormation = BuildLooseFormationTarget(stageStartFormation);
            }

            if (stage.DepletionDuration <= 0f)
            {
                ApplyStageProgress(1f);
                CompleteActiveStage();
                return;
            }

            if (baitBallManager.CurrentFishCount <= stageTargetFishCount && !ShouldTransitionFormation())
            {
                CompleteActiveStage();
            }
        }

        private void UpdateActiveStage(float deltaTime)
        {
            if (!baitBallManager || stages == null || activeStageIndex >= stages.Length)
            {
                monitoring = false;
                stageRunning = false;
                return;
            }

            DepletionStage stage = stages[activeStageIndex];
            stageElapsed += deltaTime;
            float progress = Mathf.Clamp01(stageElapsed / stage.DepletionDuration);
            ApplyStageProgress(progress);

            bool reachedTargetEarly = baitBallManager.CurrentFishCount <= stageTargetFishCount;
            if (progress >= 1f || (reachedTargetEarly && !ShouldTransitionFormation()))
            {
                CompleteActiveStage();
            }
        }

        private void ApplyStageProgress(float progress)
        {
            int desiredControllerRemovals = progress >= 1f
                ? plannedControllerRemovals
                : Mathf.FloorToInt(plannedControllerRemovals * progress);

            int remainingControllerRemovals = desiredControllerRemovals - completedControllerRemovals;
            int removableFishCount = baitBallManager.CurrentFishCount - stageTargetFishCount;
            if (remainingControllerRemovals > 0 && removableFishCount > 0)
            {
                int removeCount = Mathf.Min(remainingControllerRemovals, removableFishCount);
                completedControllerRemovals += baitBallManager.RemoveRandomFish(removeCount);
            }

            if (!ShouldTransitionFormation())
            {
                return;
            }

            float shapedProgress = finalFormationTransitionCurve != null
                ? Mathf.Clamp01(finalFormationTransitionCurve.Evaluate(progress))
                : progress;
            baitBallManager.ApplyFormationSettings(
                InstancedFishSchoolManager.FormationSettings.Lerp(
                    stageStartFormation,
                    stageTargetFormation,
                    shapedProgress));
        }

        private void CompleteActiveStage()
        {
            if (ShouldTransitionFormation() && baitBallManager)
            {
                baitBallManager.ApplyFormationSettings(stageTargetFormation);
            }

            stageRunning = false;
            activeStageIndex++;
            TryStartNextStage();
        }

        private bool ShouldTransitionFormation()
        {
            return transitionFinalStageToLooseFormation
                && stages != null
                && stages.Length > 0
                && activeStageIndex == stages.Length - 1;
        }

        private InstancedFishSchoolManager.FormationSettings BuildLooseFormationTarget(
            InstancedFishSchoolManager.FormationSettings source)
        {
            source.Radius = finalLooseFormation.Radius;
            source.CenteringWeight = finalLooseFormation.CenteringWeight;
            source.ToroidalFlowWeight = finalLooseFormation.ToroidalFlowWeight;
            source.SeparationRadius = finalLooseFormation.SeparationRadius;
            source.AlignWeight = finalLooseFormation.AlignWeight;
            source.CohesionWeight = finalLooseFormation.CohesionWeight;
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
            if (!disableBehaviorModulatorOnFinalStage)
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
