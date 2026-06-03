using System;
using UnityEngine;

namespace TestBoids.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class GameplayEventBus : MonoBehaviour
    {
        public static GameplayEventBus Instance { get; private set; }

        public event Action<TunaSchoolFocusEvent> TunaSchoolFocusTriggered;
        public event Action<SardineSchoolGatheredEvent> SardineSchoolGathered;

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

        public void RaiseTunaSchoolFocusTriggered(TunaSchoolFocusEvent focusEvent)
        {
            TunaSchoolFocusTriggered?.Invoke(focusEvent);
        }

        public void RaiseSardineSchoolGathered(SardineSchoolGatheredEvent gatheredEvent)
        {
            SardineSchoolGathered?.Invoke(gatheredEvent);
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
}
