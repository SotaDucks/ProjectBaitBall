using TestBoids.Boids;
using TestBoids.Gameplay.UI;
using TestBoids.Tuna;
using UnityEngine;

namespace TestBoids.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class TunaSchoolFocusDetector : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GameplayEventBus eventBus;
        [SerializeField] private Transform tuna;
        [SerializeField] private Transform fishSchool;
        [SerializeField] private ScreenEdgeGuideIndicator screenGuide;

        [Header("Trigger")]
        [SerializeField, Min(0f)] private float focusDistance = 60f;
        [SerializeField] private bool triggerOnce = true;
        [SerializeField] private bool autoResolveMissingReferences = true;

        private bool triggered;
        private bool warnedMissingEventBus;

        public bool HasTriggered => triggered;
        public float CurrentDistance { get; private set; }

        private void Reset()
        {
            ResolveReferences();
        }

        private void Awake()
        {
            ResolveReferences();
        }

        private void Update()
        {
            if (triggerOnce && triggered)
            {
                return;
            }

            ResolveReferences();

            if (!tuna || !fishSchool)
            {
                return;
            }

            Vector3 offset = fishSchool.position - tuna.position;
            float sqrDistance = offset.sqrMagnitude;
            CurrentDistance = Mathf.Sqrt(sqrDistance);

            float sqrFocusDistance = focusDistance * focusDistance;
            if (sqrDistance > sqrFocusDistance)
            {
                return;
            }

            if (!IsFishSchoolInScreenFocus())
            {
                return;
            }

            RaiseFocusEvent();
        }

        public void ResetTrigger()
        {
            triggered = false;
        }

        private bool IsFishSchoolInScreenFocus()
        {
            return screenGuide && screenGuide.IsTargetInsideScreenBounds;
        }

        private void RaiseFocusEvent()
        {
            ResolveEventBus();

            if (!eventBus)
            {
                WarnMissingEventBusOnce();
                return;
            }

            triggered = true;
            eventBus.RaiseTunaSchoolFocusTriggered(new TunaSchoolFocusEvent(tuna, fishSchool, CurrentDistance));
        }

        private void ResolveReferences()
        {
            if (!autoResolveMissingReferences)
            {
                return;
            }

            ResolveEventBus();
            ResolveScreenGuide();
            ResolveTargets();
        }

        private void ResolveEventBus()
        {
            if (!eventBus)
            {
                eventBus = GetComponent<GameplayEventBus>();
            }

            if (!eventBus)
            {
                eventBus = GameplayEventBus.Instance;
            }

            if (!eventBus)
            {
                eventBus = FindFirstObjectByType<GameplayEventBus>(FindObjectsInactive.Include);
            }
        }

        private void ResolveScreenGuide()
        {
            if (!screenGuide)
            {
                screenGuide = FindFirstObjectByType<ScreenEdgeGuideIndicator>(FindObjectsInactive.Include);
            }
        }

        private void ResolveTargets()
        {
            if (screenGuide)
            {
                if (!tuna)
                {
                    tuna = screenGuide.Source;
                }

                if (!fishSchool)
                {
                    fishSchool = screenGuide.Target;
                }
            }

            if (!tuna)
            {
                TunaMotor tunaMotor = FindFirstObjectByType<TunaMotor>(FindObjectsInactive.Include);
                if (tunaMotor)
                {
                    tuna = tunaMotor.transform;
                }
            }

            if (!fishSchool)
            {
                FishSchoolManager fishSchoolManager = FindFirstObjectByType<FishSchoolManager>(FindObjectsInactive.Include);
                if (fishSchoolManager)
                {
                    fishSchool = fishSchoolManager.transform;
                }
            }

            if (!fishSchool)
            {
                InstancedFishSchoolManager instancedFishSchool = FindFirstObjectByType<InstancedFishSchoolManager>(
                    FindObjectsInactive.Include);
                if (instancedFishSchool)
                {
                    fishSchool = instancedFishSchool.transform;
                }
            }
        }

        private void WarnMissingEventBusOnce()
        {
            if (warnedMissingEventBus)
            {
                return;
            }

            warnedMissingEventBus = true;
            Debug.LogWarning($"{nameof(TunaSchoolFocusDetector)} cannot raise focus event because no {nameof(GameplayEventBus)} was found.", this);
        }
    }
}
