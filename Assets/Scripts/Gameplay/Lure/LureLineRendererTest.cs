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
        [SerializeField] private Transform lureSearchRoot;
        [SerializeField] private Transform lineParent;

        [Header("Offsets")]
        [SerializeField] private Vector3 rodLocalOffset;
        [SerializeField] private Vector3 lureLocalOffset;

        private readonly Dictionary<AutomaticLureMotor, LineRenderer> activeLines = new();
        private readonly List<AutomaticLureMotor> removalBuffer = new();
        private bool warnedMissingReferences;

        private void Reset()
        {
            lureSearchRoot = transform;
            lineParent = transform;
        }

        private void LateUpdate()
        {
            if (!rodLineAnchor || !linePrefab)
            {
                WarnMissingReferencesOnce();
                ClearLines();
                return;
            }

            warnedMissingReferences = false;

            Transform searchRoot = lureSearchRoot ? lureSearchRoot : transform;
            AutomaticLureMotor[] lures = searchRoot.GetComponentsInChildren<AutomaticLureMotor>();

            for (int i = 0; i < lures.Length; i++)
            {
                AutomaticLureMotor lure = lures[i];
                if (!lure)
                {
                    continue;
                }

                LineRenderer line = GetOrCreateLine(lure);
                UpdateLine(line, lure);
            }

            RemoveLinesWithoutLures(lures);
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

        private void RemoveLinesWithoutLures(AutomaticLureMotor[] currentLures)
        {
            removalBuffer.Clear();

            foreach (KeyValuePair<AutomaticLureMotor, LineRenderer> pair in activeLines)
            {
                if (!pair.Key || !ContainsLure(currentLures, pair.Key))
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

        private static bool ContainsLure(AutomaticLureMotor[] lures, AutomaticLureMotor candidate)
        {
            if (!candidate)
            {
                return false;
            }

            for (int i = 0; i < lures.Length; i++)
            {
                if (lures[i] == candidate)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
