using System.Collections.Generic;
using UnityEngine;

namespace TestBoids.Gameplay.Lure
{
    [DisallowMultipleComponent]
    public sealed class LureLineRendererTest : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform rodLineAnchor;
        [SerializeField] private LineRenderer linePrefab;
        [SerializeField] private Transform lineParent;
        [SerializeField] private GameStateManager stateManager;

        [Header("Offsets")]
        [SerializeField] private Vector3 rodLocalOffset;
        [SerializeField] private Vector3 lureLocalOffset;

        private readonly Dictionary<AutomaticLureMotor, LineRenderer> activeLines = new();
        private readonly List<AutomaticLureMotor> removalBuffer = new();
        private bool warnedMissingReferences;
        private bool subscribedToStateManager;

        private void Reset()
        {
            lineParent = transform;
            ResolveStateManager();
        }

        private void Awake()
        {
            ResolveStateManager();
        }

        private void OnEnable()
        {
            SubscribeToStateManager();
        }

        private void Start()
        {
            SubscribeToStateManager();
        }

        private void LateUpdate()
        {
            AutomaticLureMotor hookedLure = GetHookedLure();
            if (!hookedLure)
            {
                ClearLines();
                return;
            }

            if (!rodLineAnchor || !linePrefab)
            {
                WarnMissingReferencesOnce();
                ClearLines();
                return;
            }

            warnedMissingReferences = false;

            LineRenderer line = GetOrCreateLine(hookedLure);
            UpdateLine(line, hookedLure);
            RemoveLinesExcept(hookedLure);
        }

        private LineRenderer GetOrCreateLine(AutomaticLureMotor lure)
        {
            if (activeLines.TryGetValue(lure, out LineRenderer existingLine) && existingLine)
            {
                return existingLine;
            }

            Transform parent = lineParent ? lineParent : transform;
            LineRenderer line = Instantiate(linePrefab, parent);
            line.name = $"{lure.name}_FishingLine";
            line.useWorldSpace = true;
            line.positionCount = 2;
            activeLines[lure] = line;
            return line;
        }

        private void UpdateLine(LineRenderer line, AutomaticLureMotor lure)
        {
            if (!line || !lure)
            {
                return;
            }

            line.enabled = true;
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.SetPosition(0, rodLineAnchor.TransformPoint(rodLocalOffset));
            line.SetPosition(1, lure.transform.TransformPoint(lureLocalOffset));
        }

        private AutomaticLureMotor GetHookedLure()
        {
            ResolveStateManager();
            if (!stateManager || stateManager.CurrentState != GameState.OnHook)
            {
                return null;
            }

            if (stateManager.HookedLureMotor)
            {
                return stateManager.HookedLureMotor;
            }

            return ResolveLureMotor(stateManager.HookedLure);
        }

        private void RemoveLinesExcept(AutomaticLureMotor hookedLure)
        {
            removalBuffer.Clear();

            foreach (KeyValuePair<AutomaticLureMotor, LineRenderer> pair in activeLines)
            {
                if (!pair.Key || pair.Key != hookedLure)
                {
                    if (pair.Value)
                    {
                        Destroy(pair.Value.gameObject);
                    }

                    removalBuffer.Add(pair.Key);
                }
            }

            for (int i = 0; i < removalBuffer.Count; i++)
            {
                activeLines.Remove(removalBuffer[i]);
            }
        }

        private void OnStateChanged(GameState previousState, GameState nextState)
        {
            if (nextState != GameState.OnHook)
            {
                ClearLines();
            }
        }

        private void SubscribeToStateManager()
        {
            if (subscribedToStateManager)
            {
                return;
            }

            ResolveStateManager();
            if (!stateManager)
            {
                return;
            }

            stateManager.StateChanged += OnStateChanged;
            subscribedToStateManager = true;
        }

        private void UnsubscribeFromStateManager()
        {
            if (stateManager && subscribedToStateManager)
            {
                stateManager.StateChanged -= OnStateChanged;
            }

            subscribedToStateManager = false;
        }

        private void ResolveStateManager()
        {
            if (!stateManager)
            {
                stateManager = GameStateManager.Instance;
            }

            if (!stateManager)
            {
                stateManager = FindFirstObjectByType<GameStateManager>(FindObjectsInactive.Include);
            }
        }

        private static AutomaticLureMotor ResolveLureMotor(Transform lureTransform)
        {
            if (!lureTransform)
            {
                return null;
            }

            AutomaticLureMotor lure = lureTransform.GetComponent<AutomaticLureMotor>();
            if (lure)
            {
                return lure;
            }

            lure = lureTransform.GetComponentInParent<AutomaticLureMotor>();
            if (lure)
            {
                return lure;
            }

            return lureTransform.GetComponentInChildren<AutomaticLureMotor>();
        }

        private void ClearLines()
        {
            foreach (KeyValuePair<AutomaticLureMotor, LineRenderer> pair in activeLines)
            {
                if (pair.Value)
                {
                    Destroy(pair.Value.gameObject);
                }
            }

            activeLines.Clear();
            removalBuffer.Clear();
        }

        private void OnDisable()
        {
            UnsubscribeFromStateManager();
            ClearLines();
        }

        private void OnDestroy()
        {
            ClearLines();
        }

        private void WarnMissingReferencesOnce()
        {
            if (warnedMissingReferences)
            {
                return;
            }

            Debug.LogWarning(
                $"{nameof(LureLineRendererTest)} on {name} needs a rod line anchor and a LineRenderer prefab.",
                this);
            warnedMissingReferences = true;
        }
    }
}
