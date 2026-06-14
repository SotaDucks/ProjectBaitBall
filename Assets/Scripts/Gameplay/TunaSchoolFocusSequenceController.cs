using System.Collections;
using TestBoids.Boids;
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
        [SerializeField] private TunaMotor tunaMotor;
        [SerializeField] private CinemachineCamera thirdPersonAimCamera;
        [SerializeField] private CinemachineCamera tunaSchoolFocusCamera;
        [SerializeField] private CinemachineCamera barracudaCamera;
        [SerializeField] private CinemachineCamera gtCamera;
        [SerializeField] private Transform barracudaSchool;
        [SerializeField] private PredatorStrikeController barracudaPredatorStrikeController;
        [SerializeField] private AudioSource narrationSource;
        [SerializeField] private AudioClip narrationClip;
        [SerializeField] private bool autoResolveMissingReferences = true;

        [Header("Timing")]
        [SerializeField, Min(0f)] private float freezeDelayAfterFocusCamera;
        [SerializeField] private bool useUnscaledTime;

        [Header("Priority")]
        [SerializeField] private int activePriority = 10;
        [SerializeField] private int inactivePriority;

        [Header("Behavior")]
        [SerializeField] private bool triggerOnce = true;
        [SerializeField] private bool stopNarrationOnDisable = true;

        [Header("Barracuda Retirement")]
        [SerializeField] private float barracudaRetirementZ = 250f;
        [SerializeField, Min(0f)] private float barracudaDeactivationDelay = 5f;

        [Header("Hunger Trigger")]
        [Tooltip("Tuna hunger percentage required to start the Barracuda camera sequence.")]
        [SerializeField, Range(0f, 1f)] private float barracudaTriggerHungerPercent = 0.8f;

        [Header("Auto Resolve Names")]
        [SerializeField] private string barracudaSchoolObjectName = "BarracudaSchoolManager";
        [SerializeField] private string barracudaCameraObjectName = "BarracudaCamera";
        [SerializeField] private string gtCameraObjectName = "GTCamera";

        private bool subscribed;
        private bool triggered;
        private bool running;
        private bool monitoringHunger;
        private bool barracudaSequenceTriggered;
        private Coroutine sequenceRoutine;
        private Coroutine barracudaRetirementRoutine;
        private TunaSchoolFocusEvent pendingFocusEvent;

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
            barracudaDeactivationDelay = Mathf.Max(0f, barracudaDeactivationDelay);
            barracudaTriggerHungerPercent = Mathf.Clamp01(barracudaTriggerHungerPercent);
        }

        private void OnDisable()
        {
            if (eventBus && subscribed)
            {
                eventBus.TunaSchoolFocusTriggered -= OnTunaSchoolFocusTriggered;
            }

            subscribed = false;
            StopMonitoringHunger();
            StopRunningSequence();
            StopBarracudaRetirement();
        }

        public void RetireBarracudaSchool()
        {
            ResolveReferences();
            if (!barracudaSchool)
            {
                return;
            }

            StopBarracudaRetirement();

            Vector3 position = barracudaSchool.position;
            position.z = barracudaRetirementZ;
            barracudaSchool.position = position;

            if (barracudaDeactivationDelay <= 0f)
            {
                barracudaSchool.gameObject.SetActive(false);
                return;
            }

            barracudaRetirementRoutine = StartCoroutine(DeactivateBarracudaSchoolAfterDelay());
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
            ResolveTunaMotor(focusEvent);
            sequenceRoutine = StartCoroutine(RunSardineSequence(focusEvent));
        }

        private IEnumerator RunSardineSequence(TunaSchoolFocusEvent focusEvent)
        {
            running = true;

            SetActiveCamera(tunaSchoolFocusCamera);
            yield return null;

            if (freezeDelayAfterFocusCamera > 0f)
            {
                yield return WaitForDuration(freezeDelayAfterFocusCamera);
            }

            FreezeTuna();
            PlayNarration();

            float sardineCameraDuration = GetFocusTransitionDuration();
            if (sardineCameraDuration > 0f)
            {
                yield return WaitForDuration(sardineCameraDuration);
            }

            SetActiveCamera(thirdPersonAimCamera);
            UnfreezeTuna();
            RaiseSardineSchoolGathered(focusEvent);

            running = false;
            sequenceRoutine = null;
            BeginMonitoringHunger(focusEvent);
        }

        private IEnumerator RunBarracudaSequence(TunaSchoolFocusEvent focusEvent)
        {
            running = true;

            FreezeTuna();
            PrepareBarracudaSchool(focusEvent.FishSchool);

            if (barracudaCamera)
            {
                SetActiveCamera(barracudaCamera);
            }

            float barracudaCameraDuration = GetBarracudaCameraDuration();
            if (barracudaCameraDuration > 0f)
            {
                yield return WaitForDuration(barracudaCameraDuration);
            }

            if (gtCamera)
            {
                SetActiveCamera(gtCamera);
            }

            float gtCameraDuration = GetGTCameraDuration();
            if (gtCameraDuration > 0f)
            {
                yield return WaitForDuration(gtCameraDuration);
            }

            SetActiveCamera(thirdPersonAimCamera);
            UnfreezeTuna();
            EnableBarracudaPredatorStrike();

            running = false;
            sequenceRoutine = null;
        }

        private IEnumerator DeactivateBarracudaSchoolAfterDelay()
        {
            yield return WaitForDuration(barracudaDeactivationDelay);

            if (barracudaSchool)
            {
                barracudaSchool.gameObject.SetActive(false);
            }

            barracudaRetirementRoutine = null;
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

            ResolveNamedCameras();
            ResolveBarracudaSchool();
            ResolveBarracudaPredatorStrikeController();
        }

        private void ResolveTunaMotor(TunaSchoolFocusEvent focusEvent)
        {
            if (!autoResolveMissingReferences || tunaMotor)
            {
                return;
            }

            if (focusEvent.Tuna)
            {
                tunaMotor = focusEvent.Tuna.GetComponent<TunaMotor>();
                if (!tunaMotor)
                {
                    tunaMotor = focusEvent.Tuna.GetComponentInChildren<TunaMotor>(true);
                }

                if (!tunaMotor)
                {
                    tunaMotor = focusEvent.Tuna.GetComponentInParent<TunaMotor>(true);
                }
            }

            if (!tunaMotor)
            {
                tunaMotor = FindFirstObjectByType<TunaMotor>(FindObjectsInactive.Include);
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

        private void SetActiveCamera(CinemachineCamera activeCamera)
        {
            SetPriority(thirdPersonAimCamera, thirdPersonAimCamera == activeCamera ? activePriority : inactivePriority);
            SetPriority(tunaSchoolFocusCamera, tunaSchoolFocusCamera == activeCamera ? activePriority : inactivePriority);
            SetPriority(barracudaCamera, barracudaCamera == activeCamera ? activePriority : inactivePriority);
            SetPriority(gtCamera, gtCamera == activeCamera ? activePriority : inactivePriority);
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
            SetActiveCamera(thirdPersonAimCamera);

            if (stopNarrationOnDisable && narrationSource)
            {
                narrationSource.Stop();
            }

            running = false;
        }

        private void StopBarracudaRetirement()
        {
            if (barracudaRetirementRoutine == null)
            {
                return;
            }

            StopCoroutine(barracudaRetirementRoutine);
            barracudaRetirementRoutine = null;
        }

        private void BeginMonitoringHunger(TunaSchoolFocusEvent focusEvent)
        {
            pendingFocusEvent = focusEvent;
            ResolveTunaMotor(focusEvent);
            if (!tunaMotor || barracudaSequenceTriggered)
            {
                return;
            }

            if (!monitoringHunger)
            {
                tunaMotor.HungerChanged += OnTunaHungerChanged;
                monitoringHunger = true;
            }

            TryStartBarracudaSequence();
        }

        private void StopMonitoringHunger()
        {
            if (tunaMotor && monitoringHunger)
            {
                tunaMotor.HungerChanged -= OnTunaHungerChanged;
            }

            monitoringHunger = false;
        }

        private void OnTunaHungerChanged(float currentHunger, float maxHunger)
        {
            TryStartBarracudaSequence();
        }

        private void TryStartBarracudaSequence()
        {
            if (!monitoringHunger || !tunaMotor || running || barracudaSequenceTriggered)
            {
                return;
            }

            if (tunaMotor.HungerPercent < barracudaTriggerHungerPercent)
            {
                return;
            }

            barracudaSequenceTriggered = true;
            StopMonitoringHunger();
            sequenceRoutine = StartCoroutine(RunBarracudaSequence(pendingFocusEvent));
        }

        private static void SetPriority(CinemachineCamera camera, int priority)
        {
            if (camera)
            {
                camera.Priority = priority;
            }
        }

        private void PrepareBarracudaSchool(Transform baitBall)
        {
            ResolveReferences();
            if (!barracudaSchool || !baitBall)
            {
                return;
            }

            DisableBarracudaPredatorStrike();
            barracudaSchool.SetPositionAndRotation(baitBall.position, baitBall.rotation);
            barracudaSchool.localScale = baitBall.localScale;

            if (!barracudaSchool.gameObject.activeSelf)
            {
                barracudaSchool.gameObject.SetActive(true);
            }
        }

        private void DisableBarracudaPredatorStrike()
        {
            ResolveBarracudaPredatorStrikeController();
            if (barracudaPredatorStrikeController)
            {
                barracudaPredatorStrikeController.enabled = false;
            }
        }

        private void EnableBarracudaPredatorStrike()
        {
            ResolveBarracudaPredatorStrikeController();
            if (!barracudaPredatorStrikeController)
            {
                return;
            }

            if (barracudaSchool && !barracudaSchool.gameObject.activeSelf)
            {
                barracudaSchool.gameObject.SetActive(true);
            }

            if (!barracudaPredatorStrikeController.gameObject.activeInHierarchy)
            {
                barracudaPredatorStrikeController.gameObject.SetActive(true);
            }

            barracudaPredatorStrikeController.enabled = true;
        }

        private float GetFocusTransitionDuration()
        {
            ResolveReferences();
            return eventBus ? eventBus.FocusTransitionDuration : 0f;
        }

        private float GetBarracudaCameraDuration()
        {
            ResolveReferences();
            return eventBus ? eventBus.BarracudaCameraDuration : 0f;
        }

        private float GetGTCameraDuration()
        {
            ResolveReferences();
            return eventBus ? eventBus.GTCameraDuration : 0f;
        }

        private void ResolveNamedCameras()
        {
            if (!barracudaCamera)
            {
                barracudaCamera = FindCinemachineCameraByName(barracudaCameraObjectName);
            }

            if (!gtCamera)
            {
                gtCamera = FindCinemachineCameraByName(gtCameraObjectName);
            }
        }

        private void ResolveBarracudaSchool()
        {
            if (barracudaSchool)
            {
                return;
            }

            barracudaSchool = FindTransformByName(barracudaSchoolObjectName);
            if (barracudaSchool)
            {
                return;
            }

            PredatorStrikeController strikeController = FindFirstObjectByType<PredatorStrikeController>(
                FindObjectsInactive.Include);
            if (strikeController)
            {
                barracudaSchool = strikeController.transform;
            }
        }

        private void ResolveBarracudaPredatorStrikeController()
        {
            if (barracudaPredatorStrikeController)
            {
                return;
            }

            if (barracudaSchool)
            {
                barracudaPredatorStrikeController = barracudaSchool.GetComponent<PredatorStrikeController>();
                if (!barracudaPredatorStrikeController)
                {
                    barracudaPredatorStrikeController =
                        barracudaSchool.GetComponentInChildren<PredatorStrikeController>(true);
                }
            }

            if (!barracudaPredatorStrikeController)
            {
                barracudaPredatorStrikeController = FindFirstObjectByType<PredatorStrikeController>(
                    FindObjectsInactive.Include);
            }
        }

        private static CinemachineCamera FindCinemachineCameraByName(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
            {
                return null;
            }

            CinemachineCamera[] cameras = FindObjectsByType<CinemachineCamera>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            foreach (CinemachineCamera camera in cameras)
            {
                if (camera && camera.name == objectName)
                {
                    return camera;
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
            foreach (Transform candidate in transforms)
            {
                if (candidate && candidate.name == objectName)
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}
