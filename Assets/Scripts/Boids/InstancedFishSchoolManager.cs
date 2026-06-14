using System;
using System.Collections.Generic;
using FishFlock.Utils;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace TestBoids.Boids
{
    public sealed class InstancedFishSchoolManager : MonoBehaviour
    {
        private const int MaxInstancesPerBatch = 1023;
        private const float MinCachedClearDirectionDot = 0.5f;
        private static readonly int FishAnimParamsId = Shader.PropertyToID("_FishAnimParams");
        private static readonly int FishTintId = Shader.PropertyToID("_FishTint");

        [Header("Render")]
        [SerializeField] private GameObject fishPrefab;
        [SerializeField] private Mesh instanceMesh;
        [SerializeField] private Material[] instanceMaterials = Array.Empty<Material>();
        [SerializeField] private Vector3 instanceScale = Vector3.one;
        [SerializeField, MinMax(0.5f, 1.5f)] private Vector2 scaleMultiplierRange = new(0.95f, 1.05f);
        [SerializeField] private Vector3 localForwardAxis = Vector3.forward;
        [SerializeField] private Vector3 localDorsalAxis = Vector3.up;
        [SerializeField] private bool applyBank = true;
        [SerializeField] private Color instanceTint = Color.white;
        [SerializeField] private ShadowCastingMode shadowCasting = ShadowCastingMode.On;
        [SerializeField] private bool receiveShadows = true;
        [SerializeField] private int layer;

        [Header("School")]
        [SerializeField, Min(0)] private int fishCount = 72;
        [SerializeField] private bool spawnOnStart = true;
        [SerializeField] private int seed = 42;

        [Header("Bait Ball")]
        [SerializeField] private float baitBallRadius = 3.3f;
        [SerializeField] private float baitBallCoreRatio = 0.34f;
        [SerializeField] private float centeringWeight = 1.4f;
        [SerializeField] private float toroidalFlowWeight = 1.9f;
        [SerializeField] private float toroidalRollWeight = 0.38f;
        [SerializeField] private float toroidalAxisSpeed = 0.42f;
        [SerializeField, Min(0.1f)] private float baitBallWidthScale = 1.45f;
        [SerializeField, Min(0.1f)] private float baitBallHeightScale = 0.72f;
        [SerializeField, Range(0f, 1f)] private float baitBallBottomDrop = 0.36f;
        [SerializeField, Range(0f, 0.9f)] private float baitBallBottomTaper = 0.42f;
        [SerializeField, Range(0f, 0.45f)] private float baitBallShapeAmount = 0.22f;
        [SerializeField, Min(0f)] private float baitBallShapeSpeed = 0.07f;
        [SerializeField] private bool baitBallMorphEnabled = true;
        [SerializeField, Min(0.25f)] private float baitBallMorphInterval = 8f;
        [SerializeField, Min(0f)] private float baitBallMorphResponse = 0.45f;
        [SerializeField, Range(0f, 1f)] private float baitBallMorphAmount = 0.65f;

        [Header("Boids")]
        [SerializeField, HideInInspector] private float minSpeed = 2.8f;
        [SerializeField, HideInInspector] private float maxSpeed = 7f;
        [SerializeField, HideInInspector] private float maxTurnRate = 18f;
        [SerializeField] private float perceptionRadius = 4.2f;
        [SerializeField] private float separationRadius = 1.25f;
        [SerializeField, Min(0), Tooltip("Maximum other fish sampled by each fish per simulation step. Set to 0 to scan every fish.")]
        public int neighborScanLimit = 256;
        [SerializeField, HideInInspector] private float maxSteerForce = 4.2f;
        [SerializeField] private float alignWeight = 0.5f;
        [SerializeField] private float cohesionWeight = 0.9f;
        [SerializeField, HideInInspector] private float separateWeight = 3f;

        [Header("Individual Randomization")]
        [SerializeField, MinMax(0f, 12f)] private Vector2 speedRange = new(2.8f, 7f);
        [SerializeField, MinMax(0f, 40f)] private Vector2 maxTurnRateRange = new(16.5f, 19.5f);
        [SerializeField, MinMax(0f, 10f)] private Vector2 maxSteerForceRange = new(3.8f, 4.6f);
        [SerializeField, MinMax(0f, 6f)] private Vector2 separateWeightRange = new(2.7f, 3.3f);
        [SerializeField, HideInInspector] private bool individualRandomRangesInitialized;

        [Header("Obstacle Avoidance")]
        [SerializeField] private float boundsRadius = 0.27f;
        [SerializeField] private float avoidCollisionWeight = 4f;
        [SerializeField] private float collisionAvoidDistance = 7f;
        [SerializeField] private float sphereSeparationMargin = 4f;
        [SerializeField] private float sphereSeparationWeight = 20f;
        [SerializeField, Min(8)] private int obstacleRayCount = 300;
        [SerializeField] private bool autoCollectObstacles = true;
        [SerializeField] private BoidObstacle[] obstacles = Array.Empty<BoidObstacle>();

        [Header("Panic Settings")]
        [SerializeField, Min(1f)] private float panicSpeedMultiplier = 1.8f;
        [SerializeField, Min(0f)] private float panicMinSpeedRatio = 1.15f;
        [SerializeField, Min(0f)] private float panicRiseRate = 8f;
        [SerializeField, Min(0f)] private float panicDecayRate = 2.4f;

        [Header("Pose")]
        [SerializeField] private float maxBankAngleDegrees = 12f;
        [SerializeField] private float bankTurnScale = 0.18f;
        [SerializeField] private float bankResponse = 8f;

        private readonly List<BoidObstacle> activeObstacles = new();

        private NativeArray<FishState> fish;
        private NativeArray<FishState> nextFish;
        private NativeArray<float3> nextVelocities;
        private NativeArray<float3> nextPositions;
        private NativeArray<float3> rayDirections;
        private NativeArray<ObstacleData> obstacleData;

        private MaterialPropertyBlock propertyBlock;
        private Material[] runtimeMaterials = Array.Empty<Material>();
        private Matrix4x4[] instanceMatrices = Array.Empty<Matrix4x4>();
        private float[] animationPhaseOffsets = Array.Empty<float>();
        private readonly Matrix4x4[] batchMatrices = new Matrix4x4[MaxInstancesPerBatch];
        private readonly Vector4[] batchAnimParams = new Vector4[MaxInstancesPerBatch];
        private readonly Vector4[] batchTints = new Vector4[MaxInstancesPerBatch];
        private Matrix4x4 prefabRenderLocalMatrix = Matrix4x4.identity;
        private Quaternion localAxisCorrection = Quaternion.identity;
        private float elapsedTime;
        private BoidRandom baitBallMorphRandom;
        private BaitBallShape currentBaitBallShape;
        private BaitBallShape targetBaitBallShape;
        private float baitBallMorphTimer;
        private bool baitBallMorphInitialized;
        private float focusMovementMultiplier = 1f;

        private bool HasFish => fish.IsCreated && fish.Length > 0;
        public int CurrentFishCount => fish.IsCreated ? fish.Length : 0;

        [Serializable]
        public struct FormationSettings
        {
            [Min(0.001f)] public float Radius;
            [Min(0f)] public float CoreRatio;
            [Min(0f)] public float CenteringWeight;
            [Min(0f)] public float ToroidalFlowWeight;
            [Min(0f)] public float ToroidalRollWeight;
            [Min(0f)] public float ToroidalAxisSpeed;
            [Min(0.1f)] public float WidthScale;
            [Min(0.1f)] public float HeightScale;
            [Range(0f, 1f)] public float BottomDrop;
            [Range(0f, 0.9f)] public float BottomTaper;
            [Range(0f, 0.45f)] public float ShapeAmount;
            [Min(0f)] public float ShapeSpeed;
            public bool MorphEnabled;
            [Min(0.25f)] public float MorphInterval;
            [Min(0f)] public float MorphResponse;
            [Range(0f, 1f)] public float MorphAmount;
            [Min(0f)] public float PerceptionRadius;
            [Min(0f)] public float SeparationRadius;
            [Min(0f)] public float AlignWeight;
            [Min(0f)] public float CohesionWeight;

            public static FormationSettings CreateDispersedDefault()
            {
                return new FormationSettings
                {
                    Radius = 8f,
                    CoreRatio = 0.08f,
                    CenteringWeight = 0.35f,
                    ToroidalFlowWeight = 0.6f,
                    ToroidalRollWeight = 0.18f,
                    ToroidalAxisSpeed = 0.2f,
                    WidthScale = 2.7f,
                    HeightScale = 1.05f,
                    BottomDrop = 0.12f,
                    BottomTaper = 0.18f,
                    ShapeAmount = 0.08f,
                    ShapeSpeed = 0.03f,
                    MorphEnabled = false,
                    MorphInterval = 8f,
                    MorphResponse = 0.45f,
                    MorphAmount = 0f,
                    PerceptionRadius = 4.2f,
                    SeparationRadius = 1.6f,
                    AlignWeight = 0.8f,
                    CohesionWeight = 0.25f
                };
            }

            public static FormationSettings Lerp(FormationSettings from, FormationSettings to, float t)
            {
                t = Mathf.Clamp01(t);
                return new FormationSettings
                {
                    Radius = Mathf.Lerp(from.Radius, to.Radius, t),
                    CoreRatio = Mathf.Lerp(from.CoreRatio, to.CoreRatio, t),
                    CenteringWeight = Mathf.Lerp(from.CenteringWeight, to.CenteringWeight, t),
                    ToroidalFlowWeight = Mathf.Lerp(from.ToroidalFlowWeight, to.ToroidalFlowWeight, t),
                    ToroidalRollWeight = Mathf.Lerp(from.ToroidalRollWeight, to.ToroidalRollWeight, t),
                    ToroidalAxisSpeed = Mathf.Lerp(from.ToroidalAxisSpeed, to.ToroidalAxisSpeed, t),
                    WidthScale = Mathf.Lerp(from.WidthScale, to.WidthScale, t),
                    HeightScale = Mathf.Lerp(from.HeightScale, to.HeightScale, t),
                    BottomDrop = Mathf.Lerp(from.BottomDrop, to.BottomDrop, t),
                    BottomTaper = Mathf.Lerp(from.BottomTaper, to.BottomTaper, t),
                    ShapeAmount = Mathf.Lerp(from.ShapeAmount, to.ShapeAmount, t),
                    ShapeSpeed = Mathf.Lerp(from.ShapeSpeed, to.ShapeSpeed, t),
                    MorphEnabled = t >= 1f ? to.MorphEnabled : from.MorphEnabled,
                    MorphInterval = Mathf.Lerp(from.MorphInterval, to.MorphInterval, t),
                    MorphResponse = Mathf.Lerp(from.MorphResponse, to.MorphResponse, t),
                    MorphAmount = Mathf.Lerp(from.MorphAmount, to.MorphAmount, t),
                    PerceptionRadius = Mathf.Lerp(from.PerceptionRadius, to.PerceptionRadius, t),
                    SeparationRadius = Mathf.Lerp(from.SeparationRadius, to.SeparationRadius, t),
                    AlignWeight = Mathf.Lerp(from.AlignWeight, to.AlignWeight, t),
                    CohesionWeight = Mathf.Lerp(from.CohesionWeight, to.CohesionWeight, t)
                };
            }
        }

        [ContextMenu("Apply Three.js Bait Ball Defaults")]
        private void ApplyBaitBallDefaults()
        {
            fishCount = 72;
            seed = 42;
            baitBallRadius = 3.3f;
            baitBallCoreRatio = 0.34f;
            centeringWeight = 1.4f;
            toroidalFlowWeight = 1.9f;
            toroidalRollWeight = 0.38f;
            toroidalAxisSpeed = 0.42f;
            baitBallWidthScale = 1.45f;
            baitBallHeightScale = 0.72f;
            baitBallBottomDrop = 0.36f;
            baitBallBottomTaper = 0.42f;
            baitBallShapeAmount = 0.22f;
            baitBallShapeSpeed = 0.07f;
            baitBallMorphEnabled = true;
            baitBallMorphInterval = 8f;
            baitBallMorphResponse = 0.45f;
            baitBallMorphAmount = 0.65f;
            minSpeed = 2.8f;
            maxSpeed = 7f;
            maxTurnRate = 18f;
            perceptionRadius = 4.2f;
            separationRadius = 1.25f;
            neighborScanLimit = 256;
            maxSteerForce = 4.2f;
            alignWeight = 0.5f;
            cohesionWeight = 0.9f;
            separateWeight = 3f;
            scaleMultiplierRange = new Vector2(0.95f, 1.05f);
            speedRange = new Vector2(2.8f, 7f);
            maxTurnRateRange = new Vector2(16.5f, 19.5f);
            maxSteerForceRange = new Vector2(3.8f, 4.6f);
            separateWeightRange = new Vector2(2.7f, 3.3f);
            boundsRadius = 0.27f;
            avoidCollisionWeight = 4f;
            collisionAvoidDistance = 7f;
            sphereSeparationMargin = 4f;
            sphereSeparationWeight = 20f;
            obstacleRayCount = 300;
            panicSpeedMultiplier = 1.8f;
            panicMinSpeedRatio = 1.15f;
            panicRiseRate = 8f;
            panicDecayRate = 2.4f;
            maxBankAngleDegrees = 12f;
            bankTurnScale = 0.18f;
            bankResponse = 8f;
            individualRandomRangesInitialized = true;
        }

        private void Awake()
        {
            InitializeIndividualRandomRangesFromLegacyValues();
            NormalizeIndividualRandomRanges();
            propertyBlock = new MaterialPropertyBlock();
            RefreshAxisCorrection();
            ResolveRenderResources();
        }

        private void OnValidate()
        {
            InitializeIndividualRandomRangesFromLegacyValues();
            NormalizeIndividualRandomRanges();
            if (!Application.isPlaying)
            {
                baitBallMorphInitialized = false;
            }

            neighborScanLimit = Mathf.Max(0, neighborScanLimit);
            RefreshAxisCorrection();
        }

        private void InitializeIndividualRandomRangesFromLegacyValues()
        {
            if (individualRandomRangesInitialized)
            {
                return;
            }

            speedRange = OrderedRange(minSpeed, maxSpeed);
            maxTurnRateRange = Around(maxTurnRate, 0.08f);
            maxSteerForceRange = Around(maxSteerForce, 0.10f);
            separateWeightRange = Around(separateWeight, 0.10f);
            individualRandomRangesInitialized = true;
        }

        private void NormalizeIndividualRandomRanges()
        {
            scaleMultiplierRange = OrderedRange(scaleMultiplierRange);
            speedRange = OrderedRange(speedRange);
            maxTurnRateRange = OrderedRange(maxTurnRateRange);
            maxSteerForceRange = OrderedRange(maxSteerForceRange);
            separateWeightRange = OrderedRange(separateWeightRange);
        }

        private void Start()
        {
            if (spawnOnStart)
            {
                ResetSchool(fishCount, seed);
            }
        }

        private void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 1f / 30f);
            if (dt <= 0f || !HasFish)
            {
                DrawInstances();
                return;
            }

            RefreshObstacles();
            WriteObstacleData();
            UpdateSimulation(dt);
            DrawInstances();
        }

        private void OnDisable()
        {
            DisposeNativeArrays();
            DestroyRuntimeMaterials();
        }

        public void ResetSchool(int count)
        {
            ResetSchool(count, seed);
        }

        public void ResetSchool(int count, int randomSeed)
        {
            count = Mathf.Max(0, count);
            fishCount = count;
            elapsedTime = 0f;
            InitializeBaitBallMorph(randomSeed);

            ResolveRenderResources();
            EnsureRayDirections();
            RefreshObstacles();
            AllocateFishArrays(count);

            if (count == 0)
            {
                return;
            }

            BoidRandom random = new((uint)randomSeed);
            float3 center = ToFloat3(transform.position);

            for (int i = 0; i < count; i++)
            {
                float3 position = CreateInitialPosition(ref random, center);
                float3 direction = CreateInitialDirection(position, center, ref random);
                float speed = Mathf.Max(0.001f, SampleRange(speedRange, ref random));
                fish[i] = new FishState
                {
                    Position = position,
                    Velocity = direction * speed,
                    CollisionAvoidanceDirection = float3.zero,
                    Bank = 0f,
                    ScaleMultiplier = Mathf.Max(0.001f, SampleRange(scaleMultiplierRange, ref random)),
                    MinSpeed = Mathf.Max(0.001f, speed * 0.88f),
                    MaxSpeed = Mathf.Max(0.001f, speed * 1.12f),
                    MaxTurnRate = Mathf.Max(0f, SampleRange(maxTurnRateRange, ref random)),
                    MaxSteerForce = Mathf.Max(0f, SampleRange(maxSteerForceRange, ref random)),
                    SeparateWeight = Mathf.Max(0f, SampleRange(separateWeightRange, ref random)),
                    HasCollisionAvoidanceDirection = 0
                };
                nextFish[i] = fish[i];
                animationPhaseOffsets[i] = random.Next01() * Mathf.PI * 2f;
            }

            UpdateInstanceMatrices();
        }

        public void SetCount(int count)
        {
            ResetSchool(count, seed);
        }

        public bool TryGetFishPose(int index, out Vector3 position, out Quaternion rotation, out Vector3 velocity)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            velocity = Vector3.zero;

            if (!fish.IsCreated || index < 0 || index >= fish.Length)
            {
                return false;
            }

            FishState state = fish[index];
            position = new Vector3(state.Position.x, state.Position.y, state.Position.z);
            rotation = CreatePoseRotation(state.Velocity, state.Bank);
            velocity = new Vector3(state.Velocity.x, state.Velocity.y, state.Velocity.z);
            return true;
        }

        public bool TryConsumeFish(
            Vector3 mouthPosition,
            Vector3 mouthForward,
            float radius,
            float angleDegrees,
            out Vector3 eatenPosition)
        {
            eatenPosition = Vector3.zero;
            if (!fish.IsCreated || fish.Length == 0 || radius <= 0f)
            {
                return false;
            }

            Vector3 forward = mouthForward.sqrMagnitude > 0.000001f
                ? mouthForward.normalized
                : transform.forward;
            float radiusSq = radius * radius;
            float halfAngle = Mathf.Clamp(angleDegrees, 0f, 360f) * 0.5f;
            float minForwardDot = halfAngle >= 180f ? -1f : Mathf.Cos(halfAngle * Mathf.Deg2Rad);
            int bestIndex = -1;
            float bestScore = float.PositiveInfinity;

            for (int i = 0; i < fish.Length; i++)
            {
                Vector3 position = ToVector3(fish[i].Position);
                Vector3 offset = position - mouthPosition;
                float distanceSq = offset.sqrMagnitude;
                if (distanceSq > radiusSq)
                {
                    continue;
                }

                float distance = Mathf.Sqrt(Mathf.Max(distanceSq, 0.000001f));
                float forwardDot = Vector3.Dot(forward, offset / distance);
                if (forwardDot < minForwardDot)
                {
                    continue;
                }

                float angularPenalty = 1f - forwardDot;
                float score = distanceSq + angularPenalty * radiusSq * 0.35f;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                    eatenPosition = position;
                }
            }

            return bestIndex >= 0 && RemoveFishAtIndex(bestIndex);
        }

        public int RemoveRandomFish(int count)
        {
            if (!fish.IsCreated || fish.Length == 0 || count <= 0)
            {
                return 0;
            }

            int oldCount = fish.Length;
            int removeCount = Mathf.Min(count, oldCount);
            int newCount = oldCount - removeCount;
            fishCount = newCount;
            if (newCount <= 0)
            {
                DisposeFishArrays();
                return removeCount;
            }

            int[] shuffledIndices = new int[oldCount];
            bool[] removedIndices = new bool[oldCount];
            for (int i = 0; i < oldCount; i++)
            {
                shuffledIndices[i] = i;
            }

            for (int i = 0; i < removeCount; i++)
            {
                int swapIndex = UnityEngine.Random.Range(i, oldCount);
                int removedIndex = shuffledIndices[swapIndex];
                shuffledIndices[swapIndex] = shuffledIndices[i];
                shuffledIndices[i] = removedIndex;
                removedIndices[removedIndex] = true;
            }

            NativeArray<FishState> oldFish = fish;
            NativeArray<FishState> oldNextFish = nextFish;
            NativeArray<float3> oldNextVelocities = nextVelocities;
            NativeArray<float3> oldNextPositions = nextPositions;
            float[] oldAnimationPhaseOffsets = animationPhaseOffsets ?? Array.Empty<float>();

            NativeArray<FishState> newFish = new(newCount, Allocator.Persistent);
            NativeArray<FishState> newNextFish = new(newCount, Allocator.Persistent);
            NativeArray<float3> newNextVelocities = new(newCount, Allocator.Persistent);
            NativeArray<float3> newNextPositions = new(newCount, Allocator.Persistent);
            float[] newAnimationPhaseOffsets = new float[newCount];
            int writeIndex = 0;

            for (int readIndex = 0; readIndex < oldCount; readIndex++)
            {
                if (removedIndices[readIndex])
                {
                    continue;
                }

                FishState state = oldFish[readIndex];
                newFish[writeIndex] = state;
                newNextFish[writeIndex] = state;
                if (readIndex < oldAnimationPhaseOffsets.Length)
                {
                    newAnimationPhaseOffsets[writeIndex] = oldAnimationPhaseOffsets[readIndex];
                }

                writeIndex++;
            }

            if (oldFish.IsCreated) oldFish.Dispose();
            if (oldNextFish.IsCreated) oldNextFish.Dispose();
            if (oldNextVelocities.IsCreated) oldNextVelocities.Dispose();
            if (oldNextPositions.IsCreated) oldNextPositions.Dispose();

            fish = newFish;
            nextFish = newNextFish;
            nextVelocities = newNextVelocities;
            nextPositions = newNextPositions;
            instanceMatrices = new Matrix4x4[newCount];
            animationPhaseOffsets = newAnimationPhaseOffsets;
            UpdateInstanceMatrices();
            return removeCount;
        }

        internal void SetFocusMovementMultiplier(float multiplier)
        {
            focusMovementMultiplier = Mathf.Max(1f, multiplier);
        }

        public void GetBehaviorWeights(out float currentToroidalFlowWeight, out float currentAlignWeight, out float currentCohesionWeight)
        {
            currentToroidalFlowWeight = toroidalFlowWeight;
            currentAlignWeight = alignWeight;
            currentCohesionWeight = cohesionWeight;
        }

        public void SetBehaviorWeights(float newToroidalFlowWeight, float newAlignWeight, float newCohesionWeight)
        {
            toroidalFlowWeight = Mathf.Max(0f, newToroidalFlowWeight);
            alignWeight = Mathf.Max(0f, newAlignWeight);
            cohesionWeight = Mathf.Max(0f, newCohesionWeight);
        }

        public FormationSettings GetFormationSettings()
        {
            return new FormationSettings
            {
                Radius = baitBallRadius,
                CoreRatio = baitBallCoreRatio,
                CenteringWeight = centeringWeight,
                ToroidalFlowWeight = toroidalFlowWeight,
                ToroidalRollWeight = toroidalRollWeight,
                ToroidalAxisSpeed = toroidalAxisSpeed,
                WidthScale = baitBallWidthScale,
                HeightScale = baitBallHeightScale,
                BottomDrop = baitBallBottomDrop,
                BottomTaper = baitBallBottomTaper,
                ShapeAmount = baitBallShapeAmount,
                ShapeSpeed = baitBallShapeSpeed,
                MorphEnabled = baitBallMorphEnabled,
                MorphInterval = baitBallMorphInterval,
                MorphResponse = baitBallMorphResponse,
                MorphAmount = baitBallMorphAmount,
                PerceptionRadius = perceptionRadius,
                SeparationRadius = separationRadius,
                AlignWeight = alignWeight,
                CohesionWeight = cohesionWeight
            };
        }

        public void ApplyFormationSettings(FormationSettings settings, bool resetMorph = false)
        {
            settings = SanitizeFormationSettings(settings);

            baitBallRadius = settings.Radius;
            baitBallCoreRatio = settings.CoreRatio;
            centeringWeight = settings.CenteringWeight;
            toroidalFlowWeight = settings.ToroidalFlowWeight;
            toroidalRollWeight = settings.ToroidalRollWeight;
            toroidalAxisSpeed = settings.ToroidalAxisSpeed;
            baitBallWidthScale = settings.WidthScale;
            baitBallHeightScale = settings.HeightScale;
            baitBallBottomDrop = settings.BottomDrop;
            baitBallBottomTaper = settings.BottomTaper;
            baitBallShapeAmount = settings.ShapeAmount;
            baitBallShapeSpeed = settings.ShapeSpeed;
            baitBallMorphEnabled = settings.MorphEnabled;
            baitBallMorphInterval = settings.MorphInterval;
            baitBallMorphResponse = settings.MorphResponse;
            baitBallMorphAmount = settings.MorphAmount;
            perceptionRadius = settings.PerceptionRadius;
            separationRadius = settings.SeparationRadius;
            alignWeight = settings.AlignWeight;
            cohesionWeight = settings.CohesionWeight;

            if (resetMorph)
            {
                InitializeBaitBallMorph(seed);
            }
        }

        private void UpdateSimulation(float dt)
        {
            int count = fish.Length;
            UpdateBaitBallMorph(dt);
            FishSimulationJob simulationJob = new()
            {
                Fish = fish,
                NextFish = nextFish,
                NextVelocities = nextVelocities,
                NextPositions = nextPositions,
                RayDirections = rayDirections,
                Obstacles = obstacleData,
                Center = ToFloat3(transform.position),
                TransformRotation = ToQuaternion(transform.rotation),
                Dt = dt,
                ElapsedTime = elapsedTime,
                BaitBallRadius = currentBaitBallShape.Radius,
                BaitBallCoreRatio = baitBallCoreRatio,
                CenteringWeight = centeringWeight,
                ToroidalFlowWeight = toroidalFlowWeight,
                ToroidalRollWeight = toroidalRollWeight,
                ToroidalAxisSpeed = toroidalAxisSpeed,
                BaitBallWidthScale = currentBaitBallShape.WidthScale,
                BaitBallHeightScale = currentBaitBallShape.HeightScale,
                BaitBallBottomDrop = currentBaitBallShape.BottomDrop,
                BaitBallBottomTaper = currentBaitBallShape.BottomTaper,
                BaitBallShapeAmount = baitBallShapeAmount,
                BaitBallShapeSpeed = baitBallShapeSpeed,
                PerceptionRadius = perceptionRadius,
                SeparationRadius = separationRadius,
                NeighborScanLimit = Mathf.Max(0, neighborScanLimit),
                AlignWeight = alignWeight,
                CohesionWeight = cohesionWeight,
                BoundsRadius = boundsRadius,
                AvoidCollisionWeight = avoidCollisionWeight,
                CollisionAvoidDistance = collisionAvoidDistance,
                SphereSeparationMargin = sphereSeparationMargin,
                SphereSeparationWeight = sphereSeparationWeight,
                PanicSpeedMultiplier = panicSpeedMultiplier,
                PanicMinSpeedRatio = panicMinSpeedRatio,
                PanicRiseRate = panicRiseRate,
                PanicDecayRate = panicDecayRate,
                MaxBankAngleDegrees = maxBankAngleDegrees,
                BankTurnScale = bankTurnScale,
                BankResponse = bankResponse,
                FocusMovementMultiplier = focusMovementMultiplier
            };

            JobHandle simulationHandle = simulationJob.Schedule(count, 32);
            simulationHandle.Complete();

            (fish, nextFish) = (nextFish, fish);
            elapsedTime += dt;
            UpdateInstanceMatrices();
        }

        private void DrawInstances()
        {
            if (!instanceMesh
                || runtimeMaterials == null
                || runtimeMaterials.Length == 0
                || instanceMatrices == null
                || instanceMatrices.Length == 0
                || !HasFish)
            {
                return;
            }

            int subMeshCount = Mathf.Max(1, instanceMesh.subMeshCount);
            for (int subMeshIndex = 0; subMeshIndex < subMeshCount; subMeshIndex++)
            {
                Material material = runtimeMaterials[Mathf.Min(subMeshIndex, runtimeMaterials.Length - 1)];
                if (!material)
                {
                    continue;
                }

                for (int start = 0; start < instanceMatrices.Length; start += MaxInstancesPerBatch)
                {
                    int batchCount = Mathf.Min(MaxInstancesPerBatch, instanceMatrices.Length - start);
                    FillBatchData(start, batchCount);
                    Graphics.DrawMeshInstanced(
                        instanceMesh,
                        subMeshIndex,
                        material,
                        batchMatrices,
                        batchCount,
                        propertyBlock,
                        shadowCasting,
                        receiveShadows,
                        layer,
                        null,
                        LightProbeUsage.Off);
                }
            }
        }

        private void ResolveRenderResources()
        {
            Renderer sourceRenderer = fishPrefab ? fishPrefab.GetComponentInChildren<Renderer>() : null;
            prefabRenderLocalMatrix = sourceRenderer && fishPrefab
                ? fishPrefab.transform.worldToLocalMatrix * sourceRenderer.transform.localToWorldMatrix
                : Matrix4x4.identity;

            if (fishPrefab && !instanceMesh)
            {
                MeshFilter meshFilter = fishPrefab.GetComponentInChildren<MeshFilter>();
                if (meshFilter && meshFilter.sharedMesh)
                {
                    instanceMesh = meshFilter.sharedMesh;
                }
                else
                {
                    SkinnedMeshRenderer skinnedRenderer = fishPrefab.GetComponentInChildren<SkinnedMeshRenderer>();
                    if (skinnedRenderer && skinnedRenderer.sharedMesh)
                    {
                        instanceMesh = skinnedRenderer.sharedMesh;
                    }
                }
            }

            if ((instanceMaterials == null || instanceMaterials.Length == 0) && sourceRenderer)
            {
                instanceMaterials = sourceRenderer.sharedMaterials;
            }

            if (instanceMaterials != null)
            {
                EnsureRuntimeMaterials();
                foreach (Material material in runtimeMaterials)
                {
                    if (material)
                    {
                        material.enableInstancing = true;
                    }
                }
            }

            if (fishPrefab && layer == 0)
            {
                layer = fishPrefab.layer;
            }
        }

        private void EnsureRuntimeMaterials()
        {
            if (instanceMaterials == null || instanceMaterials.Length == 0)
            {
                DestroyRuntimeMaterials();
                runtimeMaterials = Array.Empty<Material>();
                return;
            }

            if (runtimeMaterials != null && runtimeMaterials.Length == instanceMaterials.Length)
            {
                return;
            }

            DestroyRuntimeMaterials();
            runtimeMaterials = new Material[instanceMaterials.Length];
            for (int i = 0; i < instanceMaterials.Length; i++)
            {
                if (!instanceMaterials[i])
                {
                    continue;
                }

                runtimeMaterials[i] = new Material(instanceMaterials[i])
                {
                    enableInstancing = true
                };
            }
        }

        private void RefreshObstacles()
        {
            activeObstacles.Clear();
            if (obstacles != null)
            {
                foreach (BoidObstacle obstacle in obstacles)
                {
                    if (obstacle && obstacle.isActiveAndEnabled)
                    {
                        activeObstacles.Add(obstacle);
                    }
                }
            }

            if (!autoCollectObstacles)
            {
                return;
            }

            BoidObstacle[] found = FindObjectsByType<BoidObstacle>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (BoidObstacle obstacle in found)
            {
                if (obstacle && obstacle.isActiveAndEnabled && !activeObstacles.Contains(obstacle))
                {
                    activeObstacles.Add(obstacle);
                }
            }
        }

        private void WriteObstacleData()
        {
            if (!obstacleData.IsCreated || obstacleData.Length != activeObstacles.Count)
            {
                if (obstacleData.IsCreated)
                {
                    obstacleData.Dispose();
                }

                obstacleData = new NativeArray<ObstacleData>(activeObstacles.Count, Allocator.Persistent);
            }

            for (int i = 0; i < activeObstacles.Count; i++)
            {
                BoidObstacle obstacle = activeObstacles[i];
                obstacleData[i] = new ObstacleData
                {
                    Shape = (int)obstacle.Shape,
                    Position = ToFloat3(obstacle.Position),
                    Rotation = ToQuaternion(obstacle.Rotation),
                    Radius = obstacle.Radius,
                    Size = ToFloat3(obstacle.Size)
                };
            }
        }

        private void AllocateFishArrays(int count)
        {
            DisposeFishArrays();
            instanceMatrices = count > 0 ? new Matrix4x4[count] : Array.Empty<Matrix4x4>();
            animationPhaseOffsets = count > 0 ? new float[count] : Array.Empty<float>();

            if (count <= 0)
            {
                return;
            }

            fish = new NativeArray<FishState>(count, Allocator.Persistent);
            nextFish = new NativeArray<FishState>(count, Allocator.Persistent);
            nextVelocities = new NativeArray<float3>(count, Allocator.Persistent);
            nextPositions = new NativeArray<float3>(count, Allocator.Persistent);
        }

        private bool RemoveFishAtIndex(int index)
        {
            if (!fish.IsCreated || index < 0 || index >= fish.Length)
            {
                return false;
            }

            int newCount = fish.Length - 1;
            fishCount = newCount;
            if (newCount <= 0)
            {
                DisposeFishArrays();
                return true;
            }

            NativeArray<FishState> oldFish = fish;
            NativeArray<FishState> oldNextFish = nextFish;
            NativeArray<float3> oldNextVelocities = nextVelocities;
            NativeArray<float3> oldNextPositions = nextPositions;
            float[] oldAnimationPhaseOffsets = animationPhaseOffsets ?? Array.Empty<float>();

            NativeArray<FishState> newFish = new(newCount, Allocator.Persistent);
            NativeArray<FishState> newNextFish = new(newCount, Allocator.Persistent);
            NativeArray<float3> newNextVelocities = new(newCount, Allocator.Persistent);
            NativeArray<float3> newNextPositions = new(newCount, Allocator.Persistent);
            float[] newAnimationPhaseOffsets = new float[newCount];
            int writeIndex = 0;

            for (int readIndex = 0; readIndex < oldFish.Length; readIndex++)
            {
                if (readIndex == index)
                {
                    continue;
                }

                FishState state = oldFish[readIndex];
                newFish[writeIndex] = state;
                newNextFish[writeIndex] = state;
                if (readIndex < oldAnimationPhaseOffsets.Length)
                {
                    newAnimationPhaseOffsets[writeIndex] = oldAnimationPhaseOffsets[readIndex];
                }

                writeIndex++;
            }

            if (oldFish.IsCreated) oldFish.Dispose();
            if (oldNextFish.IsCreated) oldNextFish.Dispose();
            if (oldNextVelocities.IsCreated) oldNextVelocities.Dispose();
            if (oldNextPositions.IsCreated) oldNextPositions.Dispose();

            fish = newFish;
            nextFish = newNextFish;
            nextVelocities = newNextVelocities;
            nextPositions = newNextPositions;
            instanceMatrices = new Matrix4x4[newCount];
            animationPhaseOffsets = newAnimationPhaseOffsets;
            UpdateInstanceMatrices();
            return true;
        }

        private void UpdateInstanceMatrices()
        {
            if (!fish.IsCreated || instanceMatrices == null || instanceMatrices.Length != fish.Length)
            {
                return;
            }

            for (int i = 0; i < fish.Length; i++)
            {
                FishState state = fish[i];
                Quaternion rotation = CreatePoseRotation(state.Velocity, state.Bank);
                Matrix4x4 rootMatrix = Matrix4x4.TRS(
                    new Vector3(state.Position.x, state.Position.y, state.Position.z),
                    rotation,
                    GetInstanceScale(state.ScaleMultiplier));
                instanceMatrices[i] = rootMatrix * prefabRenderLocalMatrix;
            }
        }

        private Vector3 GetInstanceScale(float scaleMultiplier)
        {
            float multiplier = Mathf.Max(0.0001f, Mathf.Abs(scaleMultiplier));
            return new Vector3(
                Mathf.Max(0.0001f, Mathf.Abs(instanceScale.x)) * multiplier,
                Mathf.Max(0.0001f, Mathf.Abs(instanceScale.y)) * multiplier,
                Mathf.Max(0.0001f, Mathf.Abs(instanceScale.z)) * multiplier);
        }

        private void FillBatchData(int start, int count)
        {
            Vector4 tint = instanceTint;
            for (int i = 0; i < count; i++)
            {
                int sourceIndex = start + i;
                FishState state = fish[sourceIndex];
                float speed = math.length(state.Velocity);
                batchMatrices[i] = instanceMatrices[sourceIndex];
                batchAnimParams[i] = new Vector4(
                    animationPhaseOffsets[sourceIndex],
                    Mathf.Max(0f, speed / Mathf.Max(0.0001f, state.MaxSpeed)),
                    1f,
                    1f);
                batchTints[i] = tint;
            }

            if (propertyBlock == null)
            {
                propertyBlock = new MaterialPropertyBlock();
            }

            propertyBlock.Clear();
            propertyBlock.SetVectorArray(FishAnimParamsId, batchAnimParams);
            propertyBlock.SetVectorArray(FishTintId, batchTints);
        }

        private Quaternion CreatePoseRotation(float3 velocity, float bank)
        {
            Vector3 forward = new(velocity.x, velocity.y, velocity.z);
            if (forward.sqrMagnitude <= 0.000001f)
            {
                return transform.rotation * localAxisCorrection;
            }

            forward.Normalize();
            Vector3 dorsal = Vector3.up - forward * Vector3.Dot(Vector3.up, forward);
            if (dorsal.sqrMagnitude < 0.000001f)
            {
                Vector3 fallback = Mathf.Abs(Vector3.Dot(forward, Vector3.forward)) < 0.92f
                    ? Vector3.forward
                    : Vector3.right;
                dorsal = fallback - forward * Vector3.Dot(fallback, forward);
            }

            dorsal.Normalize();
            if (applyBank && Mathf.Abs(bank) > 0.000001f)
            {
                dorsal = Quaternion.AngleAxis(bank * Mathf.Rad2Deg, forward) * dorsal;
            }

            return Quaternion.LookRotation(forward, dorsal) * localAxisCorrection;
        }

        private void RefreshAxisCorrection()
        {
            Vector3 forward = NormalizeAxis(localForwardAxis, Vector3.forward);
            Vector3 dorsal = NormalizeAxis(localDorsalAxis, Vector3.up);

            dorsal -= forward * Vector3.Dot(dorsal, forward);
            if (dorsal.sqrMagnitude < 0.000001f)
            {
                dorsal = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) < 0.92f
                    ? Vector3.up
                    : Vector3.forward;
                dorsal -= forward * Vector3.Dot(dorsal, forward);
            }

            localAxisCorrection = Quaternion.Inverse(Quaternion.LookRotation(forward, dorsal.normalized));
        }

        private static Vector3 NormalizeAxis(Vector3 axis, Vector3 fallback)
        {
            return axis.sqrMagnitude > 0.000001f ? axis.normalized : fallback;
        }

        private void EnsureRayDirections()
        {
            int count = Mathf.Max(8, obstacleRayCount);
            if (rayDirections.IsCreated && rayDirections.Length == count)
            {
                return;
            }

            if (rayDirections.IsCreated)
            {
                rayDirections.Dispose();
            }

            rayDirections = new NativeArray<float3>(count, Allocator.Persistent);
            float goldenRatio = (1f + Mathf.Sqrt(5f)) * 0.5f;
            float angleIncrement = Mathf.PI * 2f * goldenRatio;

            for (int i = 0; i < count; i++)
            {
                float t = (float)i / count;
                float inclination = Mathf.Acos(1f - 2f * t);
                float azimuth = angleIncrement * i;
                rayDirections[i] = new float3(
                    Mathf.Sin(inclination) * Mathf.Cos(azimuth),
                    Mathf.Sin(inclination) * Mathf.Sin(azimuth),
                    Mathf.Cos(inclination));
            }
        }

        private float3 CreateInitialPosition(ref BoidRandom random, float3 center)
        {
            float3 direction = CreateRandomUnitVector(ref random);
            BaitBallShape shape = baitBallMorphInitialized ? currentBaitBallShape : ReadBaseBaitBallShape();
            float radius = ComputeBaitBallTargetRadius(
                direction,
                ToQuaternion(transform.rotation),
                shape.Radius,
                shape.WidthScale,
                shape.HeightScale,
                shape.BottomDrop,
                shape.BottomTaper,
                baitBallShapeAmount,
                0f,
                baitBallShapeSpeed);
            float shellBias = 0.32f + 0.68f * Mathf.Pow(random.Next01(), 1f / 3f);
            return center + direction * (radius * shellBias);
        }

        private float3 CreateInitialDirection(float3 position, float3 center, ref BoidRandom random)
        {
            float3 defaultFlowAxis = new(0f, 1f, 0f);
            float3 localPosition = position - center;
            float3 tangent = math.cross(defaultFlowAxis, localPosition);
            if (math.lengthsq(tangent) < 0.000001f)
            {
                tangent = math.cross(new float3(1f, 0f, 0f), localPosition);
            }

            tangent = math.normalize(tangent);
            float3 radial = SafeNormalize(localPosition);
            tangent += radial * ((random.Next01() * 2f - 1f) * 0.18f);
            return SafeNormalize(tangent);
        }

        private static float3 CreateRandomUnitVector(ref BoidRandom random)
        {
            float z = random.Next01() * 2f - 1f;
            float angle = random.Next01() * Mathf.PI * 2f;
            float radius = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));

            return new float3(Mathf.Cos(angle) * radius, z, Mathf.Sin(angle) * radius);
        }

        private void DisposeNativeArrays()
        {
            DisposeFishArrays();

            if (rayDirections.IsCreated) rayDirections.Dispose();
            if (obstacleData.IsCreated) obstacleData.Dispose();
        }

        private void DisposeFishArrays()
        {
            if (fish.IsCreated) fish.Dispose();
            if (nextFish.IsCreated) nextFish.Dispose();
            if (nextVelocities.IsCreated) nextVelocities.Dispose();
            if (nextPositions.IsCreated) nextPositions.Dispose();
            instanceMatrices = Array.Empty<Matrix4x4>();
            animationPhaseOffsets = Array.Empty<float>();
        }

        private void DestroyRuntimeMaterials()
        {
            if (runtimeMaterials == null)
            {
                return;
            }

            for (int i = 0; i < runtimeMaterials.Length; i++)
            {
                if (!runtimeMaterials[i])
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(runtimeMaterials[i]);
                }
                else
                {
                    DestroyImmediate(runtimeMaterials[i]);
                }
            }

            runtimeMaterials = Array.Empty<Material>();
        }

        private static float3 ToFloat3(Vector3 value)
        {
            return new float3(value.x, value.y, value.z);
        }

        private static Vector3 ToVector3(float3 value)
        {
            return new Vector3(value.x, value.y, value.z);
        }

        private static quaternion ToQuaternion(Quaternion value)
        {
            return new quaternion(value.x, value.y, value.z, value.w);
        }

        private static float SampleRange(Vector2 range, ref BoidRandom random)
        {
            return Mathf.Lerp(range.x, range.y, random.Next01());
        }

        private void InitializeBaitBallMorph(int randomSeed)
        {
            baitBallMorphRandom = new BoidRandom((uint)randomSeed ^ 0xA511E9B3u);
            currentBaitBallShape = ReadBaseBaitBallShape();
            targetBaitBallShape = SampleBaitBallMorphTarget(currentBaitBallShape, ref baitBallMorphRandom);
            baitBallMorphTimer = SampleMorphInterval(ref baitBallMorphRandom);
            baitBallMorphInitialized = true;
        }

        private void UpdateBaitBallMorph(float dt)
        {
            if (!baitBallMorphInitialized)
            {
                InitializeBaitBallMorph(seed);
            }

            BaitBallShape baseShape = ReadBaseBaitBallShape();
            if (!baitBallMorphEnabled || baitBallMorphAmount <= 0f)
            {
                currentBaitBallShape = baseShape;
                targetBaitBallShape = baseShape;
                baitBallMorphTimer = 0f;
                return;
            }

            baitBallMorphTimer -= dt;
            if (baitBallMorphTimer <= 0f)
            {
                targetBaitBallShape = SampleBaitBallMorphTarget(baseShape, ref baitBallMorphRandom);
                baitBallMorphTimer = SampleMorphInterval(ref baitBallMorphRandom);
            }

            float t = 1f - Mathf.Exp(-Mathf.Max(0f, baitBallMorphResponse) * dt);
            currentBaitBallShape = LerpBaitBallShape(currentBaitBallShape, targetBaitBallShape, t);
        }

        private BaitBallShape ReadBaseBaitBallShape()
        {
            return new BaitBallShape
            {
                Radius = Mathf.Max(0.001f, baitBallRadius),
                WidthScale = Mathf.Max(0.1f, baitBallWidthScale),
                HeightScale = Mathf.Max(0.1f, baitBallHeightScale),
                BottomDrop = Mathf.Clamp01(baitBallBottomDrop),
                BottomTaper = Mathf.Clamp(baitBallBottomTaper, 0f, 0.9f)
            };
        }

        private BaitBallShape SampleBaitBallMorphTarget(BaitBallShape baseShape, ref BoidRandom random)
        {
            float amount = Mathf.Clamp01(baitBallMorphAmount);
            return new BaitBallShape
            {
                Radius = SampleRelative(baseShape.Radius, 0.86f, 1.16f, amount, ref random),
                WidthScale = SampleRelative(baseShape.WidthScale, 0.86f, 1.14f, amount, ref random),
                HeightScale = SampleRelative(baseShape.HeightScale, 0.82f, 1.18f, amount, ref random),
                BottomDrop = SampleBlendedRange(baseShape.BottomDrop, 0.12f, 0.62f, amount, ref random),
                BottomTaper = SampleBlendedRange(baseShape.BottomTaper, 0.18f, 0.68f, amount, ref random)
            };
        }

        private float SampleMorphInterval(ref BoidRandom random)
        {
            return Mathf.Max(0.25f, baitBallMorphInterval) * Mathf.Lerp(0.75f, 1.25f, random.Next01());
        }

        private static float SampleRelative(float value, float minMultiplier, float maxMultiplier, float amount, ref BoidRandom random)
        {
            float target = value * Mathf.Lerp(minMultiplier, maxMultiplier, random.Next01());
            return Mathf.Lerp(value, target, amount);
        }

        private static float SampleBlendedRange(float value, float min, float max, float amount, ref BoidRandom random)
        {
            float target = Mathf.Lerp(min, max, random.Next01());
            return Mathf.Lerp(value, target, amount);
        }

        private static BaitBallShape LerpBaitBallShape(BaitBallShape from, BaitBallShape to, float t)
        {
            return new BaitBallShape
            {
                Radius = Mathf.Lerp(from.Radius, to.Radius, t),
                WidthScale = Mathf.Lerp(from.WidthScale, to.WidthScale, t),
                HeightScale = Mathf.Lerp(from.HeightScale, to.HeightScale, t),
                BottomDrop = Mathf.Lerp(from.BottomDrop, to.BottomDrop, t),
                BottomTaper = Mathf.Lerp(from.BottomTaper, to.BottomTaper, t)
            };
        }

        private struct BaitBallShape
        {
            public float Radius;
            public float WidthScale;
            public float HeightScale;
            public float BottomDrop;
            public float BottomTaper;
        }

        private static Vector2 Around(float value, float ratio)
        {
            float spread = Mathf.Abs(value) * Mathf.Max(0f, ratio);
            return OrderedRange(Mathf.Max(0f, value - spread), value + spread);
        }

        private static Vector2 OrderedRange(float x, float y)
        {
            return x <= y ? new Vector2(x, y) : new Vector2(y, x);
        }

        private static Vector2 OrderedRange(Vector2 range)
        {
            return OrderedRange(range.x, range.y);
        }

        private static FormationSettings SanitizeFormationSettings(FormationSettings settings)
        {
            settings.Radius = Mathf.Max(0.001f, settings.Radius);
            settings.CoreRatio = Mathf.Max(0f, settings.CoreRatio);
            settings.CenteringWeight = Mathf.Max(0f, settings.CenteringWeight);
            settings.ToroidalFlowWeight = Mathf.Max(0f, settings.ToroidalFlowWeight);
            settings.ToroidalRollWeight = Mathf.Max(0f, settings.ToroidalRollWeight);
            settings.ToroidalAxisSpeed = Mathf.Max(0f, settings.ToroidalAxisSpeed);
            settings.WidthScale = Mathf.Max(0.1f, settings.WidthScale);
            settings.HeightScale = Mathf.Max(0.1f, settings.HeightScale);
            settings.BottomDrop = Mathf.Clamp01(settings.BottomDrop);
            settings.BottomTaper = Mathf.Clamp(settings.BottomTaper, 0f, 0.9f);
            settings.ShapeAmount = Mathf.Clamp(settings.ShapeAmount, 0f, 0.45f);
            settings.ShapeSpeed = Mathf.Max(0f, settings.ShapeSpeed);
            settings.MorphInterval = Mathf.Max(0.25f, settings.MorphInterval);
            settings.MorphResponse = Mathf.Max(0f, settings.MorphResponse);
            settings.MorphAmount = Mathf.Clamp01(settings.MorphAmount);
            settings.PerceptionRadius = Mathf.Max(0f, settings.PerceptionRadius);
            settings.SeparationRadius = Mathf.Min(Mathf.Max(0f, settings.SeparationRadius), settings.PerceptionRadius);
            settings.AlignWeight = Mathf.Max(0f, settings.AlignWeight);
            settings.CohesionWeight = Mathf.Max(0f, settings.CohesionWeight);
            return settings;
        }

        private static float3 SafeNormalize(float3 vector)
        {
            return math.lengthsq(vector) > 0.000001f ? math.normalize(vector) : float3.zero;
        }

        private static float ComputeBaitBallTargetRadius(
            float3 radialDirection,
            quaternion rotation,
            float baseRadius,
            float widthScale,
            float heightScale,
            float bottomDrop,
            float bottomTaper,
            float shapeAmount,
            float elapsedTime,
            float shapeSpeed)
        {
            float radius = math.max(0.001f, baseRadius);
            float3 localDirection = SafeNormalize(math.mul(math.inverse(rotation), radialDirection));
            if (math.lengthsq(localDirection) < 0.000001f)
            {
                return radius;
            }

            float vertical = math.clamp(localDirection.y, -1f, 1f);
            float top = math.saturate(vertical);
            float bottom = math.saturate(-vertical);
            float bottomShape = bottom * bottom;
            float horizontalSq = math.max(0f, localDirection.x * localDirection.x + localDirection.z * localDirection.z);
            float horizontalScale = math.max(0.1f, widthScale) * (1f - math.clamp(bottomTaper, 0f, 0.9f) * bottomShape);
            float verticalScale = math.max(0.1f, heightScale)
                * (1f + math.saturate(bottomDrop) * bottom - top * top * 0.34f);
            verticalScale = math.max(0.1f, verticalScale);

            float shapeDistance = math.sqrt(
                horizontalSq / (horizontalScale * horizontalScale)
                + vertical * vertical / (verticalScale * verticalScale));
            float blobRadius = radius / math.max(0.001f, shapeDistance);

            float amount = math.clamp(shapeAmount, 0f, 0.75f);
            float time = elapsedTime * math.max(0f, shapeSpeed);
            float lump = noise.snoise(localDirection * 1.65f + new float3(time * 0.37f, time * 0.59f, time * 0.43f));
            float profile = 1f + lump * amount * 0.65f;

            return blobRadius * math.max(0.45f, profile);
        }

        private struct FishState
        {
            public float3 Position;
            public float3 Velocity;
            public float3 CollisionAvoidanceDirection;
            public float Bank;
            public float ScaleMultiplier;
            public float MinSpeed;
            public float MaxSpeed;
            public float MaxTurnRate;
            public float MaxSteerForce;
            public float SeparateWeight;
            public float Panic;
            public int HasCollisionAvoidanceDirection;
        }

        private struct ObstacleData
        {
            public int Shape;
            public float3 Position;
            public quaternion Rotation;
            public float Radius;
            public float3 Size;
        }

        [BurstCompile]
        private struct FishSimulationJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<FishState> Fish;
            [WriteOnly] public NativeArray<float3> NextVelocities;
            [WriteOnly] public NativeArray<float3> NextPositions;
            [ReadOnly] public NativeArray<float3> RayDirections;
            [ReadOnly] public NativeArray<ObstacleData> Obstacles;
            [WriteOnly] public NativeArray<FishState> NextFish;

            public float3 Center;
            public quaternion TransformRotation;
            public float Dt;
            public float ElapsedTime;
            public float BaitBallRadius;
            public float BaitBallCoreRatio;
            public float CenteringWeight;
            public float ToroidalFlowWeight;
            public float ToroidalRollWeight;
            public float ToroidalAxisSpeed;
            public float BaitBallWidthScale;
            public float BaitBallHeightScale;
            public float BaitBallBottomDrop;
            public float BaitBallBottomTaper;
            public float BaitBallShapeAmount;
            public float BaitBallShapeSpeed;
            public float PerceptionRadius;
            public float SeparationRadius;
            public int NeighborScanLimit;
            public float AlignWeight;
            public float CohesionWeight;
            public float BoundsRadius;
            public float AvoidCollisionWeight;
            public float CollisionAvoidDistance;
            public float SphereSeparationMargin;
            public float SphereSeparationWeight;
            public float PanicSpeedMultiplier;
            public float PanicMinSpeedRatio;
            public float PanicRiseRate;
            public float PanicDecayRate;
            public float MaxBankAngleDegrees;
            public float BankTurnScale;
            public float BankResponse;
            public float FocusMovementMultiplier;

            public void Execute(int index)
            {
                FishState current = Fish[index];
                float perceptionRadiusSq = PerceptionRadius * PerceptionRadius;
                float separationRadiusSq = SeparationRadius * SeparationRadius;
                float3 currentFlowAxis = ReadFlowAxis(ElapsedTime);
                float flowPhase = ElapsedTime * ToroidalAxisSpeed * 3.7f;
                float3 forward = SafeNormalize(current.Velocity);
                bool headingForCollision = IsHeadingForCollision(current.Position, forward);
                bool panicTriggered = headingForCollision || IsInsideSphereObstacleInfluence(current.Position);

                current.Panic = UpdatePanic(current.Panic, panicTriggered);
                float movementMultiplier = math.max(1f, FocusMovementMultiplier);
                float effectiveMaxSpeed = current.MaxSpeed * movementMultiplier * math.lerp(1f, math.max(1f, PanicSpeedMultiplier), current.Panic);
                float panicMinSpeed = math.max(current.MinSpeed, current.MaxSpeed * movementMultiplier * math.max(0f, PanicMinSpeedRatio));
                float effectiveMinSpeed = math.min(effectiveMaxSpeed, math.lerp(current.MinSpeed, panicMinSpeed, current.Panic));
                float effectiveMaxSteerForce = current.MaxSteerForce * movementMultiplier;
                float effectiveMaxTurnRate = current.MaxTurnRate * movementMultiplier;

                float3 acceleration = float3.zero;
                float3 headingSum = float3.zero;
                float3 centerSum = float3.zero;
                float3 avoidanceSum = float3.zero;
                int neighborCount = 0;

                if (NeighborScanLimit <= 0 || NeighborScanLimit >= Fish.Length - 1)
                {
                    for (int i = 0; i < Fish.Length; i++)
                    {
                        if (i == index)
                        {
                            continue;
                        }

                        AccumulateNeighbor(
                            i,
                            current,
                            perceptionRadiusSq,
                            separationRadiusSq,
                            ref headingSum,
                            ref centerSum,
                            ref avoidanceSum,
                            ref neighborCount);
                    }
                }
                else
                {
                    int sampleFrame = (int)math.floor(ElapsedTime * 8f);
                    int sampleCount = math.min(NeighborScanLimit, Fish.Length - 1);
                    for (int i = 0; i < sampleCount; i++)
                    {
                        int otherIndex = SampleNeighborIndex(index, i, Fish.Length, sampleFrame);
                        if (otherIndex == index)
                        {
                            otherIndex = (otherIndex + 1) % Fish.Length;
                        }

                        AccumulateNeighbor(
                            otherIndex,
                            current,
                            perceptionRadiusSq,
                            separationRadiusSq,
                            ref headingSum,
                            ref centerSum,
                            ref avoidanceSum,
                            ref neighborCount);
                    }
                }

                if (neighborCount > 0)
                {
                    centerSum /= neighborCount;
                    acceleration += SteerTowards(headingSum, current.Velocity, effectiveMaxSpeed, effectiveMaxSteerForce) * AlignWeight;
                    acceleration += SteerTowards(centerSum - current.Position, current.Velocity, effectiveMaxSpeed, effectiveMaxSteerForce) * CohesionWeight;
                    acceleration += SteerTowards(avoidanceSum, current.Velocity, effectiveMaxSpeed, effectiveMaxSteerForce) * current.SeparateWeight;
                }

                acceleration += SphericalEnvelopeForce(current.Position, current.Velocity, effectiveMaxSpeed, effectiveMaxSteerForce) * CenteringWeight;
                acceleration += ToroidalFlowForce(current.Position, current.Velocity, currentFlowAxis, flowPhase, effectiveMaxSpeed, effectiveMaxSteerForce) * ToroidalFlowWeight;
                acceleration += SphereObstacleSeparationForce(current.Position, current.Velocity, effectiveMaxSpeed, effectiveMaxSteerForce);

                if (headingForCollision)
                {
                    float3 clearDirection = ObstacleRays(current.Position, forward, ref current);
                    acceleration += SteerTowards(clearDirection, current.Velocity, effectiveMaxSpeed, effectiveMaxSteerForce) * AvoidCollisionWeight;
                }
                else
                {
                    current.HasCollisionAvoidanceDirection = 0;
                }

                float3 desiredVelocity = current.Velocity + acceleration * Dt;
                float speed = math.clamp(math.length(desiredVelocity), effectiveMinSpeed, effectiveMaxSpeed);
                desiredVelocity = SafeNormalize(desiredVelocity) * speed;
                float3 velocity = LimitTurn(current.Velocity, desiredVelocity, Dt, effectiveMaxTurnRate);
                float3 nextPosition = current.Position + velocity * Dt;

                UpdateMotionState(ref current, velocity, Dt);
                current.Velocity = velocity;
                current.Position = nextPosition;

                NextFish[index] = current;
                NextVelocities[index] = velocity;
                NextPositions[index] = nextPosition;
            }

            private void AccumulateNeighbor(
                int otherIndex,
                FishState current,
                float perceptionRadiusSq,
                float separationRadiusSq,
                ref float3 headingSum,
                ref float3 centerSum,
                ref float3 avoidanceSum,
                ref int neighborCount)
            {
                FishState other = Fish[otherIndex];
                float3 offset = other.Position - current.Position;
                float distanceSq = math.lengthsq(offset);

                if (distanceSq >= perceptionRadiusSq)
                {
                    return;
                }

                neighborCount++;
                headingSum += SafeNormalize(other.Velocity);
                centerSum += other.Position;

                if (distanceSq < separationRadiusSq)
                {
                    float distance = math.sqrt(math.max(distanceSq, 0.0001f));
                    avoidanceSum += offset * (-1f / distance);
                }
            }

            private static int SampleNeighborIndex(int fishIndex, int sampleIndex, int fishCount, int sampleFrame)
            {
                uint value = (uint)fishIndex * 747796405u
                    + (uint)sampleIndex * 2891336453u
                    + (uint)sampleFrame * 277803737u;
                value ^= value >> 16;
                value *= 2246822519u;
                value ^= value >> 13;
                return (int)(value % (uint)fishCount);
            }

            private float3 SteerTowards(float3 vector, float3 velocity, float maxSpeed, float maxSteerForce)
            {
                if (math.lengthsq(vector) < 0.000001f)
                {
                    return float3.zero;
                }

                float3 desired = math.normalize(vector) * math.max(0.001f, maxSpeed);
                return ClampMagnitude(desired - velocity, math.max(0f, maxSteerForce));
            }

            private float3 LimitTurn(float3 currentVelocity, float3 desiredVelocity, float dt, float maxTurnRate)
            {
                float3 currentDirection = SafeNormalize(currentVelocity);
                float3 desiredDirection = SafeNormalize(desiredVelocity);
                float dot = math.clamp(math.dot(currentDirection, desiredDirection), -1f, 1f);
                float angle = math.acos(dot);
                float maxAngle = math.max(0f, maxTurnRate) * dt;

                if (angle <= maxAngle || angle < 0.000001f)
                {
                    return desiredVelocity;
                }

                float t = maxAngle / angle;
                float3 direction = Slerp(currentDirection, desiredDirection, t);
                return direction * math.length(desiredVelocity);
            }

            private bool IsHeadingForCollision(float3 position, float3 forward)
            {
                return Obstacles.Length > 0 && RayHitsObstacle(position, forward, CollisionAvoidDistance);
            }

            private float UpdatePanic(float currentPanic, bool triggered)
            {
                float target = triggered ? 1f : 0f;
                float response = triggered ? PanicRiseRate : PanicDecayRate;
                float t = 1f - math.exp(-math.max(0f, response) * Dt);
                return math.saturate(math.lerp(math.saturate(currentPanic), target, t));
            }

            private float3 ObstacleRays(float3 position, float3 forward, ref FishState state)
            {
                if (state.HasCollisionAvoidanceDirection != 0
                    && math.dot(state.CollisionAvoidanceDirection, forward) > MinCachedClearDirectionDot
                    && IsDirectionClear(position, state.CollisionAvoidanceDirection, CollisionAvoidDistance))
                {
                    return state.CollisionAvoidanceDirection;
                }

                float3 result = FindClearObstacleDirection(position, forward);
                state.CollisionAvoidanceDirection = result;
                state.HasCollisionAvoidanceDirection = 1;
                return result;
            }

            private float3 FindClearObstacleDirection(float3 position, float3 forward)
            {
                float3 forwardDirection = SafeNormalize(forward);
                quaternion rotation = FromToRotation(new float3(0f, 0f, 1f), forwardDirection);
                for (int i = 0; i < RayDirections.Length; i++)
                {
                    float3 direction = SafeNormalize(math.mul(rotation, RayDirections[i]));
                    if (IsDirectionClear(position, direction, CollisionAvoidDistance))
                    {
                        return direction;
                    }
                }

                return forwardDirection;
            }

            private bool IsDirectionClear(float3 origin, float3 direction, float maxDistance)
            {
                return Obstacles.Length == 0 || !RayHitsObstacle(origin, direction, maxDistance);
            }

            private bool RayHitsObstacle(float3 origin, float3 direction, float maxDistance)
            {
                return math.isfinite(RayObstacleHitDistance(origin, direction, maxDistance));
            }

            private float RayObstacleHitDistance(float3 origin, float3 direction, float maxDistance)
            {
                for (int i = 0; i < Obstacles.Length; i++)
                {
                    ObstacleData obstacle = Obstacles[i];
                    if (obstacle.Shape == (int)BoidObstacleShape.Box || obstacle.Shape == (int)BoidObstacleShape.Plate)
                    {
                        float distance = RayBoxObstacleHitDistance(origin, direction, maxDistance, obstacle);
                        if (distance <= maxDistance) return distance;
                    }
                    else
                    {
                        float distance = RaySphereObstacleHitDistance(origin, direction, maxDistance, obstacle);
                        if (distance <= maxDistance) return distance;
                    }
                }

                return float.PositiveInfinity;
            }

            private float RaySphereObstacleHitDistance(float3 origin, float3 direction, float maxDistance, ObstacleData obstacle)
            {
                float radius = obstacle.Radius + BoundsRadius;
                float3 offset = origin - obstacle.Position;
                float b = math.dot(offset, direction);
                float c = math.lengthsq(offset) - radius * radius;
                float discriminant = b * b - c;

                if (discriminant < 0f)
                {
                    return float.PositiveInfinity;
                }

                float root = math.sqrt(discriminant);
                float near = -b - root;
                float far = -b + root;
                if (near >= 0f && near <= maxDistance)
                {
                    return near;
                }

                return far >= 0f && far <= maxDistance ? far : float.PositiveInfinity;
            }

            private bool IsInsideSphereObstacleInfluence(float3 position)
            {
                float margin = math.max(0f, SphereSeparationMargin);
                if (margin <= 0f || Obstacles.Length == 0)
                {
                    return false;
                }

                for (int i = 0; i < Obstacles.Length; i++)
                {
                    ObstacleData obstacle = Obstacles[i];
                    if (obstacle.Shape != (int)BoidObstacleShape.Sphere)
                    {
                        continue;
                    }

                    float influenceRadius = obstacle.Radius + margin;
                    float3 offset = position - obstacle.Position;
                    if (math.lengthsq(offset) < influenceRadius * influenceRadius)
                    {
                        return true;
                    }
                }

                return false;
            }

            private float RayBoxObstacleHitDistance(float3 origin, float3 direction, float maxDistance, ObstacleData obstacle)
            {
                quaternion inverseRotation = math.inverse(obstacle.Rotation);
                float3 localOrigin = math.mul(inverseRotation, origin - obstacle.Position);
                float3 localDirection = math.mul(inverseRotation, direction);
                float3 halfSize = obstacle.Size * 0.5f + new float3(BoundsRadius);

                return RayExpandedBoxHitDistance(localOrigin, localDirection, halfSize, maxDistance);
            }

            private static float RayExpandedBoxHitDistance(float3 origin, float3 direction, float3 halfSize, float maxDistance)
            {
                float near = 0f;
                float far = maxDistance;

                if (!ClipSlab(origin.x, direction.x, halfSize.x, ref near, ref far)) return float.PositiveInfinity;
                if (!ClipSlab(origin.y, direction.y, halfSize.y, ref near, ref far)) return float.PositiveInfinity;
                if (!ClipSlab(origin.z, direction.z, halfSize.z, ref near, ref far)) return float.PositiveInfinity;

                return far >= 0f && near <= maxDistance ? near : float.PositiveInfinity;
            }

            private static bool ClipSlab(float origin, float direction, float halfSize, ref float near, ref float far)
            {
                if (math.abs(direction) < 0.000001f)
                {
                    return origin >= -halfSize && origin <= halfSize;
                }

                float inverseDirection = 1f / direction;
                float axisNear = (-halfSize - origin) * inverseDirection;
                float axisFar = (halfSize - origin) * inverseDirection;
                if (axisNear > axisFar)
                {
                    float temp = axisNear;
                    axisNear = axisFar;
                    axisFar = temp;
                }

                near = math.max(near, axisNear);
                far = math.min(far, axisFar);
                return near <= far;
            }

            private float3 SphereObstacleSeparationForce(float3 position, float3 velocity, float maxSpeed, float maxSteerForce)
            {
                float margin = math.max(0f, SphereSeparationMargin);
                float weight = math.max(0f, SphereSeparationWeight);
                if (margin <= 0f || weight <= 0f || Obstacles.Length == 0)
                {
                    return float3.zero;
                }

                float3 away = float3.zero;
                float maxPressure = 0f;

                for (int i = 0; i < Obstacles.Length; i++)
                {
                    ObstacleData obstacle = Obstacles[i];
                    if (obstacle.Shape != (int)BoidObstacleShape.Sphere)
                    {
                        continue;
                    }

                    float radius = obstacle.Radius;
                    float influenceRadius = radius + margin;
                    float3 offset = position - obstacle.Position;
                    float distanceSq = math.lengthsq(offset);

                    if (distanceSq >= influenceRadius * influenceRadius)
                    {
                        continue;
                    }

                    float distance = math.sqrt(distanceSq);
                    if (distance < 0.000001f)
                    {
                        offset = -velocity;
                        if (math.lengthsq(offset) < 0.000001f)
                        {
                            offset = new float3(1f, 0f, 0f);
                        }

                        distance = math.length(offset);
                    }

                    float surfaceDistance = distance - radius;
                    float pressure = surfaceDistance >= 0f
                        ? 1f - surfaceDistance / margin
                        : 1f + math.min(1f, -surfaceDistance / math.max(radius, 0.000001f));

                    away += offset * (pressure / distance);
                    maxPressure = math.max(maxPressure, pressure);
                }

                if (math.lengthsq(away) < 0.000001f)
                {
                    return float3.zero;
                }

                return SteerTowards(away, velocity, maxSpeed, maxSteerForce) * (weight * maxPressure);
            }

            private float3 SphericalEnvelopeForce(float3 position, float3 velocity, float maxSpeed, float maxSteerForce)
            {
                float3 radial = position - Center;
                float distance = math.length(radial);

                if (distance < 0.000001f)
                {
                    return float3.zero;
                }

                float3 radialDirection = radial / distance;
                float targetRadius = ComputeBaitBallTargetRadius(
                    radialDirection,
                    TransformRotation,
                    BaitBallRadius,
                    BaitBallWidthScale,
                    BaitBallHeightScale,
                    BaitBallBottomDrop,
                    BaitBallBottomTaper,
                    BaitBallShapeAmount,
                    ElapsedTime,
                    BaitBallShapeSpeed);
                float coreRadius = targetRadius * math.max(0f, BaitBallCoreRatio);
                float3 force = float3.zero;
                float pressure = 1f;

                if (distance > targetRadius)
                {
                    float overshoot = math.max(0f, (distance - targetRadius) / targetRadius);
                    pressure = 1f + overshoot * 3f;
                    force += radialDirection * (-1f - overshoot);
                }
                else if (distance < coreRadius)
                {
                    float corePressure = 1f - distance / math.max(coreRadius, 0.000001f);
                    pressure = 0.5f + corePressure * 1.5f;
                    force += radialDirection * corePressure;
                }
                else
                {
                    float inwardBias = 0.28f * (distance / targetRadius);
                    pressure = 0.55f + inwardBias;
                    force += radialDirection * -inwardBias;
                }

                return SteerTowards(force, velocity, maxSpeed, maxSteerForce) * pressure;
            }

            private float3 ToroidalFlowForce(float3 position, float3 velocity, float3 axis, float phase, float maxSpeed, float maxSteerForce)
            {
                float3 radial = position - Center;
                if (math.lengthsq(radial) < 0.000001f)
                {
                    return float3.zero;
                }

                float axialOffset = math.dot(radial, axis);
                float3 ringRadial = radial - axis * axialOffset;
                if (math.lengthsq(ringRadial) < 0.000001f)
                {
                    ringRadial = math.cross(axis, velocity);
                    if (math.lengthsq(ringRadial) < 0.000001f)
                    {
                        ringRadial = math.cross(axis, new float3(1f, 0f, 0f));
                    }
                }

                float3 toroidal = SafeNormalize(math.cross(axis, ringRadial));
                float3 radialDirection = SafeNormalize(radial);
                float3 poloidal = SafeNormalize(math.cross(toroidal, radialDirection));
                float roll = math.sin(phase + axialOffset * 0.72f);
                float3 desiredDirection = toroidal + poloidal * (roll * ToroidalRollWeight);

                return SteerTowards(desiredDirection, velocity, maxSpeed, maxSteerForce);
            }

            private float3 ReadFlowAxis(float time)
            {
                float speed = ToroidalAxisSpeed;
                float3 flowAxis = new(
                    math.sin(time * speed * 0.83f) * 0.62f,
                    1f + math.sin(time * speed * 0.47f) * 0.22f,
                    math.cos(time * speed) * 0.62f);
                return math.mul(TransformRotation, SafeNormalize(flowAxis));
            }

            private void UpdateMotionState(ref FishState current, float3 nextVelocity, float dt)
            {
                float3 previousDirection = SafeNormalize(current.Velocity);
                float3 nextDirection = SafeNormalize(nextVelocity);

                if (math.lengthsq(previousDirection) <= 0.000001f || math.lengthsq(nextDirection) <= 0.000001f)
                {
                    current.Bank = DampAngle(current.Bank, 0f, BankResponse, dt);
                    return;
                }

                float dot = math.clamp(math.dot(previousDirection, nextDirection), -1f, 1f);
                float turnAngle = math.acos(dot);
                float3 turnAxis = math.cross(previousDirection, nextDirection);
                float turnSign = math.dot(turnAxis, new float3(0f, 1f, 0f));
                float turnRate = turnAngle / dt;
                float maxBankAngle = math.radians(MaxBankAngleDegrees);
                float targetBank = math.clamp(-turnSign * turnRate * BankTurnScale, -maxBankAngle, maxBankAngle);

                current.Bank = DampAngle(current.Bank, targetBank, BankResponse, dt);
            }

            private static float DampAngle(float current, float target, float response, float dt)
            {
                return math.lerp(current, target, 1f - math.exp(-math.max(0f, response) * dt));
            }

            private static float3 ClampMagnitude(float3 vector, float maxLength)
            {
                float lengthSq = math.lengthsq(vector);
                if (lengthSq <= maxLength * maxLength)
                {
                    return vector;
                }

                return vector * (maxLength / math.sqrt(lengthSq));
            }

            private static float3 Slerp(float3 from, float3 to, float t)
            {
                float dot = math.clamp(math.dot(from, to), -1f, 1f);
                float theta = math.acos(dot) * t;
                float3 relative = SafeNormalize(to - from * dot);
                return SafeNormalize(from * math.cos(theta) + relative * math.sin(theta));
            }

            private static quaternion FromToRotation(float3 from, float3 to)
            {
                float3 f = SafeNormalize(from);
                float3 t = SafeNormalize(to);
                float dot = math.dot(f, t);

                if (dot > 0.999999f)
                {
                    return quaternion.identity;
                }

                if (dot < -0.999999f)
                {
                    float3 axis = math.cross(new float3(1f, 0f, 0f), f);
                    if (math.lengthsq(axis) < 0.000001f)
                    {
                        axis = math.cross(new float3(0f, 1f, 0f), f);
                    }

                    return quaternion.AxisAngle(SafeNormalize(axis), math.PI);
                }

                float3 cross = math.cross(f, t);
                float s = math.sqrt((1f + dot) * 2f);
                float invS = 1f / s;
                return math.normalize(new quaternion(cross.x * invS, cross.y * invS, cross.z * invS, s * 0.5f));
            }

            private static float3 SafeNormalize(float3 vector)
            {
                return math.lengthsq(vector) > 0.000001f ? math.normalize(vector) : float3.zero;
            }
        }
    }
}
