using System;
using UnityEngine;

namespace TestBoids.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class GameplayEventBus : MonoBehaviour
    {
        [Header("Tuna School Focus Sequence")]
        [SerializeField, Min(0f)] private float focusTransitionDuration = 4f;
        [SerializeField, Min(0f)] private float barracudaCameraDuration = 3f;
        [SerializeField, Min(0f)] private float gtCameraDuration = 3f;

        public static GameplayEventBus Instance { get; private set; }

        public event Action<TunaSchoolFocusEvent> TunaSchoolFocusTriggered;
        public event Action<SardineSchoolGatheredEvent> SardineSchoolGathered;
        public event Action<LureBittenEvent> LureBitten;

        public float FocusTransitionDuration => focusTransitionDuration;
        public float BarracudaCameraDuration => barracudaCameraDuration;
        public float GTCameraDuration => gtCameraDuration;

        private void Awake()
        {
            if (Instance && Instance != this)
            {
                Debug.LogWarning($"{nameof(GameplayEventBus)} already exists. Replacing the static instance with {name}.", this);
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void OnValidate()
        {
            focusTransitionDuration = Mathf.Max(0f, focusTransitionDuration);
            barracudaCameraDuration = Mathf.Max(0f, barracudaCameraDuration);
            gtCameraDuration = Mathf.Max(0f, gtCameraDuration);
        }

        public void RaiseTunaSchoolFocusTriggered(TunaSchoolFocusEvent focusEvent)
        {
            TunaSchoolFocusTriggered?.Invoke(focusEvent);
        }

        public void RaiseSardineSchoolGathered(SardineSchoolGatheredEvent gatheredEvent)
        {
            SardineSchoolGathered?.Invoke(gatheredEvent);
        }

        public void RaiseLureBitten(LureBittenEvent bittenEvent)
        {
            LureBitten?.Invoke(bittenEvent);
        }
    }

    public readonly struct TunaSchoolFocusEvent
    {
        public TunaSchoolFocusEvent(Transform tuna, Transform fishSchool, float distance)
        {
            Tuna = tuna;
            FishSchool = fishSchool;
            Distance = distance;
        }

        public Transform Tuna { get; }
        public Transform FishSchool { get; }
        public float Distance { get; }
    }

    public readonly struct SardineSchoolGatheredEvent
    {
        public SardineSchoolGatheredEvent(Transform tuna, Transform fishSchool, float triggerDistance)
        {
            Tuna = tuna;
            FishSchool = fishSchool;
            TriggerDistance = triggerDistance;
        }

        public Transform Tuna { get; }
        public Transform FishSchool { get; }
        public float TriggerDistance { get; }
    }

    public readonly struct LureBittenEvent
    {
        public LureBittenEvent(Transform tuna, Transform lure, Vector3 lureForward = default)
        {
            Tuna = tuna;
            Lure = lure;
            LureForward = lureForward;
        }

        public Transform Tuna { get; }
        public Transform Lure { get; }
        public Vector3 LureForward { get; }
    }
}
