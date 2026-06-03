using System.Collections;
using TestBoids.Tuna;
using Unity.Cinemachine;
using UnityEngine;

namespace TestBoids.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class TunaSchoolFocusSequenceController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GameplayEventBus eventBus;
        [SerializeField] private TunaFreezeController tunaFreezeController;
        [SerializeField] private CinemachineCamera thirdPersonAimCamera;
        [SerializeField] private CinemachineCamera tunaSchoolFocusCamera;
        [SerializeField] private AudioSource narrationSource;
        [SerializeField] private AudioClip narrationClip;
        [SerializeField] private bool autoResolveMissingReferences = true;

        [Header("Timing")]
        [SerializeField, Min(0f)] private float freezeDelayAfterFocusCamera;
        [SerializeField, Min(0f)] private float narrationDelay = 5f;
        [SerializeField] private bool useUnscaledTime;

        [Header("Priority")]
        [SerializeField] private int activePriority = 10;
        [SerializeField] private int inactivePriority;

        [Header("Behavior")]
        [SerializeField] private bool triggerOnce = true;
        [SerializeField] private bool stopNarrationOnDisable = true;

        private bool subscribed;
        private bool triggered;
        private bool running;
        private Coroutine sequenceRoutine;

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
            freezeDelayAfterFocusCamera = Mathf.Max(0f, freezeDelayAfterFocusCamera);
            narrationDelay = Mathf.Max(0f, narrationDelay);
        }

        private void OnDisable()
        {
            if (eventBus && subscribed)
            {
                eventBus.TunaSchoolFocusTriggered -= OnTunaSchoolFocusTriggered;
            }

            subscribed = false;
            StopRunningSequence();
        }

        private void OnTunaSchoolFocusTriggered(TunaSchoolFocusEvent focusEvent)
        {
            if (running || (triggerOnce && triggered))
            {
                return;
            }

            triggered = true;
            ResolveReferences();
            ResolveTunaFreezeController(focusEvent);
            sequenceRoutine = StartCoroutine(RunSequence(focusEvent));
        }

        private IEnumerator RunSequence(TunaSchoolFocusEvent focusEvent)
        {
            running = true;

            SetFocusCameraActive(true);
            yield return null;

            if (freezeDelayAfterFocusCamera > 0f)
            {
                yield return WaitForDuration(freezeDelayAfterFocusCamera);
            }

            FreezeTuna();
            PlayNarration();

            if (narrationDelay > 0f)
            {
                yield return WaitForDuration(narrationDelay);
            }

            UnfreezeTuna();
            SetFocusCameraActive(false);
            RaiseSardineSchoolGathered(focusEvent);

            running = false;
            sequenceRoutine = null;
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

            eventBus.TunaSchoolFocusTriggered += OnTunaSchoolFocusTriggered;
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

        }

        private void ResolveTunaFreezeController(TunaSchoolFocusEvent focusEvent)
        {
            if (!autoResolveMissingReferences || tunaFreezeController)
            {
                return;
            }

            if (focusEvent.Tuna)
            {
                tunaFreezeController = focusEvent.Tuna.GetComponent<TunaFreezeController>();
                if (!tunaFreezeController)
                {
                    tunaFreezeController = focusEvent.Tuna.GetComponentInChildren<TunaFreezeController>(true);
                }

                if (!tunaFreezeController)
                {
                    tunaFreezeController = focusEvent.Tuna.GetComponentInParent<TunaFreezeController>(true);
                }
            }

            if (!tunaFreezeController)
            {
                tunaFreezeController = FindFirstObjectByType<TunaFreezeController>(FindObjectsInactive.Include);
            }
        }

        private void FreezeTuna()
        {
            if (tunaFreezeController)
            {
                tunaFreezeController.Freeze();
            }
        }

        private void UnfreezeTuna()
        {
            if (tunaFreezeController)
            {
                tunaFreezeController.Unfreeze();
            }
        }

        private void PlayNarration()
        {
            if (!narrationSource)
            {
                return;
            }

            if (narrationClip)
            {
                narrationSource.PlayOneShot(narrationClip);
                return;
            }

            narrationSource.Play();
        }

        private void SetFocusCameraActive(bool active)
        {
            SetPriority(tunaSchoolFocusCamera, active ? activePriority : inactivePriority);
            SetPriority(thirdPersonAimCamera, active ? inactivePriority : activePriority);
        }

        private void RaiseSardineSchoolGathered(TunaSchoolFocusEvent focusEvent)
        {
            ResolveReferences();
            if (!eventBus)
            {
                return;
            }

            eventBus.RaiseSardineSchoolGathered(new SardineSchoolGatheredEvent(
                focusEvent.Tuna,
                focusEvent.FishSchool,
                focusEvent.Distance));
        }

        private IEnumerator WaitForDuration(float duration)
        {
            if (!useUnscaledTime)
            {
                yield return new WaitForSeconds(duration);
                yield break;
            }

            float endTime = Time.unscaledTime + duration;
            while (Time.unscaledTime < endTime)
            {
                yield return null;
            }
        }

        private void StopRunningSequence()
        {
            if (sequenceRoutine != null)
            {
                StopCoroutine(sequenceRoutine);
                sequenceRoutine = null;
            }

            if (!running)
            {
                return;
            }

            UnfreezeTuna();
            SetFocusCameraActive(false);

            if (stopNarrationOnDisable && narrationSource)
            {
                narrationSource.Stop();
            }

            running = false;
        }

        private static void SetPriority(CinemachineCamera camera, int priority)
        {
            if (camera)
            {
                camera.Priority = priority;
            }
        }
    }
}
