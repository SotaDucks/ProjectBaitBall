using System.Collections.Generic;
using UnityEngine;

namespace TestBoids.Gameplay.Lure
{
    [DisallowMultipleComponent]
    public sealed class AutomaticLureSpawner : MonoBehaviour
    {
        [Header("References")]
        [SerializeField, Tooltip("Collection root prefab containing an AutomaticLureMotor in its hierarchy.")]
        private GameObject lurePrefab;
        [SerializeField] private Transform tuna;
        [SerializeField] private Camera referenceCamera;
        [SerializeField] private Transform spawnParent;

        [Header("Automatic Spawning")]
        [SerializeField] private bool spawnAutomatically = true;
        [SerializeField, Min(0f)] private float initialSpawnDelay = 1f;
        [SerializeField] private Vector2 spawnIntervalRange = new(8f, 12f);
        [SerializeField, Min(1)] private int maximumActiveLures = 1;

        [Header("Hidden Spawn Area")]
        [SerializeField, Min(0.1f)] private float minimumSpawnDistance = 8f;
        [SerializeField, Min(0.1f)] private float maximumSpawnDistance = 14f;
        [SerializeField, Min(0f)] private float verticalSpawnRange = 3f;
        [SerializeField, Min(0f)] private float viewportMargin = 0.1f;
        [SerializeField, Min(1)] private int maximumSampleAttempts = 24;

        [Header("Water")]
        [SerializeField] private float waterSurfaceHeight;
        [SerializeField, Min(0f)] private float minimumSubmersionDepth = 0.5f;

        [Header("Obstacle Check")]
        [SerializeField] private LayerMask obstacleLayers;
        [SerializeField, Min(0f)] private float obstacleClearanceRadius = 0.5f;

        [Header("Pass By Tuna")]
        [SerializeField, Min(0f)] private float horizontalPassRadius = 2f;

        private readonly List<ActiveLure> activeLures = new();
        private float nextSpawnAt;

        private sealed class ActiveLure
        {
            public ActiveLure(GameObject root, AutomaticLureMotor motor)
            {
                Root = root;
                Motor = motor;
            }

            public GameObject Root { get; }
            public AutomaticLureMotor Motor { get; }
        }

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
            nextSpawnAt = Time.time + initialSpawnDelay;
        }

        private void OnValidate()
        {
            initialSpawnDelay = Mathf.Max(0f, initialSpawnDelay);
            spawnIntervalRange.x = Mathf.Max(0.05f, spawnIntervalRange.x);
            spawnIntervalRange.y = Mathf.Max(spawnIntervalRange.x, spawnIntervalRange.y);
            maximumActiveLures = Mathf.Max(1, maximumActiveLures);
            minimumSpawnDistance = Mathf.Max(0.1f, minimumSpawnDistance);
            maximumSpawnDistance = Mathf.Max(minimumSpawnDistance, maximumSpawnDistance);
            verticalSpawnRange = Mathf.Max(0f, verticalSpawnRange);
            viewportMargin = Mathf.Max(0f, viewportMargin);
            maximumSampleAttempts = Mathf.Max(1, maximumSampleAttempts);
            minimumSubmersionDepth = Mathf.Max(0f, minimumSubmersionDepth);
            obstacleClearanceRadius = Mathf.Max(0f, obstacleClearanceRadius);
            horizontalPassRadius = Mathf.Max(0f, horizontalPassRadius);
        }

        private void Update()
        {
            RemoveDestroyedLures();

            if (!spawnAutomatically || Time.time < nextSpawnAt)
            {
                return;
            }

            if (activeLures.Count < maximumActiveLures)
            {
                SpawnNow();
            }

            ScheduleNextSpawn();
        }

        public AutomaticLureMotor SpawnNow()
        {
            ResolveReferences();
            RemoveDestroyedLures();

            if (!lurePrefab || !tuna || !referenceCamera || activeLures.Count >= maximumActiveLures)
            {
                return null;
            }

            Vector3 spawnPosition = FindSpawnPosition();
            Vector3 passPoint = BuildPassPoint();
            Vector3 approachDirection = passPoint - spawnPosition;
            Quaternion spawnRotation = approachDirection.sqrMagnitude > 0.000001f
                ? Quaternion.LookRotation(approachDirection.normalized)
                : Quaternion.identity;

            GameObject lureRoot = Instantiate(lurePrefab, spawnPosition, spawnRotation, spawnParent);
            AutomaticLureMotor lure = lureRoot.GetComponentInChildren<AutomaticLureMotor>();
            if (!lure)
            {
                Debug.LogError(
                    $"The lure prefab '{lurePrefab.name}' does not contain an active {nameof(AutomaticLureMotor)}.",
                    lurePrefab);
                Destroy(lureRoot);
                return null;
            }

            lure.ConfigurePass(tuna, passPoint, waterSurfaceHeight);
            activeLures.Add(new ActiveLure(lureRoot, lure));
            return lure;
        }

        private Vector3 FindSpawnPosition()
        {
            for (int attempt = 0; attempt < maximumSampleAttempts; attempt++)
            {
                Vector3 candidate = BuildRandomCandidate();
                if (IsOutsideCameraView(candidate) && HasObstacleClearance(candidate))
                {
                    return candidate;
                }
            }

            Vector3 fallback = tuna.position
                - referenceCamera.transform.forward * maximumSpawnDistance
                + Vector3.up * Random.Range(-verticalSpawnRange, verticalSpawnRange);
            return KeepBelowWaterSurface(fallback);
        }

        private Vector3 BuildRandomCandidate()
        {
            Vector2 circle = Random.insideUnitCircle;
            if (circle.sqrMagnitude <= 0.000001f)
            {
                circle = Vector2.right;
            }

            circle.Normalize();
            float distance = Random.Range(minimumSpawnDistance, maximumSpawnDistance);
            Vector3 candidate = tuna.position
                + new Vector3(circle.x * distance, Random.Range(-verticalSpawnRange, verticalSpawnRange), circle.y * distance);
            return KeepBelowWaterSurface(candidate);
        }

        private Vector3 BuildPassPoint()
        {
            Vector2 circle = Random.insideUnitCircle * horizontalPassRadius;
            return tuna.position + new Vector3(circle.x, 0f, circle.y);
        }

        private bool IsOutsideCameraView(Vector3 worldPosition)
        {
            Vector3 viewport = referenceCamera.WorldToViewportPoint(worldPosition);
            return viewport.z <= 0f
                || viewport.x < -viewportMargin
                || viewport.x > 1f + viewportMargin
                || viewport.y < -viewportMargin
                || viewport.y > 1f + viewportMargin;
        }

        private bool HasObstacleClearance(Vector3 worldPosition)
        {
            return obstacleClearanceRadius <= 0f
                || !Physics.CheckSphere(
                    worldPosition,
                    obstacleClearanceRadius,
                    obstacleLayers,
                    QueryTriggerInteraction.Ignore);
        }

        private Vector3 KeepBelowWaterSurface(Vector3 worldPosition)
        {
            worldPosition.y = Mathf.Min(worldPosition.y, waterSurfaceHeight - minimumSubmersionDepth);
            return worldPosition;
        }

        private void RemoveDestroyedLures()
        {
            for (int index = activeLures.Count - 1; index >= 0; index--)
            {
                ActiveLure activeLure = activeLures[index];
                if (activeLure.Motor)
                {
                    continue;
                }

                if (activeLure.Root)
                {
                    Destroy(activeLure.Root);
                }

                activeLures.RemoveAt(index);
            }
        }

        private void ScheduleNextSpawn()
        {
            nextSpawnAt = Time.time + Random.Range(spawnIntervalRange.x, spawnIntervalRange.y);
        }

        private void ResolveReferences()
        {
            if (!referenceCamera)
            {
                referenceCamera = Camera.main;
            }
        }
    }
}
