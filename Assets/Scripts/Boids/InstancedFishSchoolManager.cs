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

        [Header("Boids")]
        [SerializeField, HideInInspector] private float minSpeed = 2.8f;
        [SerializeField, HideInInspector] private float maxSpeed = 7f;
        [SerializeField, HideInInspector] private float maxTurnRate = 18f;
        [SerializeField] private float perceptionRadius = 4.2f;
        [SerializeField] private float separationRadius = 1.25f;
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

        private bool HasFish => fish.IsCreated && fish.Length > 0;

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
            minSpeed = 2.8f;
            maxSpeed = 7f;
            maxTurnRate = 18f;
            perceptionRadius = 4.2f;
            separationRadius = 1.25f;
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

        private void UpdateSimulation(float dt)
        {
            int count = fish.Length;
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
                BaitBallRadius = baitBallRadius,
                BaitBallCoreRatio = baitBallCoreRatio,
                CenteringWeight = centeringWeight,
                ToroidalFlowWeight = toroidalFlowWeight,
                ToroidalRollWeight = toroidalRollWeight,
                ToroidalAxisSpeed = toroidalAxisSpeed,
                BaitBallWidthScale = baitBallWidthScale,
                BaitBallHeightScale = baitBallHeightScale,
                BaitBallBottomDrop = baitBallBottomDrop,
                BaitBallBottomTaper = baitBallBottomTaper,
                BaitBallShapeAmount = baitBallShapeAmount,
                BaitBallShapeSpeed = baitBallShapeSpeed,
                PerceptionRadius = perceptionRadius,
                SeparationRadius = separationRadius,
                AlignWeight = alignWeight,
                CohesionWeight = cohesionWeight,
                BoundsRadius = boundsRadius,
                AvoidCollisionWeight = avoidCollisionWeight,
                CollisionAvoidDistance = collisionAvoidDistance,
                SphereSeparationMargin = sphereSeparationMargin,
                SphereSeparationWeight = sphereSeparationWeight,
                MaxBankAngleDegrees = maxBankAngleDegrees,
                BankTurnScale = bankTurnScale,
                BankResponse = bankResponse
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
            float radius = ComputeBaitBallTargetRadius(
                direction,
                ToQuaternion(transform.rotation),
                baitBallRadius,
                baitBallWidthScale,
                baitBallHeightScale,
                baitBallBottomDrop,
                baitBallBottomTaper,
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

        private static quaternion ToQuaternion(Quaternion value)
        {
            return new quaternion(value.x, value.y, value.z, value.w);
        }

        private static float SampleRange(Vector2 range, ref BoidRandom random)
        {
            return Mathf.Lerp(range.x, range.y, random.Next01());
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
            public float AlignWeight;
            public float CohesionWeight;
            public float BoundsRadius;
            public float AvoidCollisionWeight;
            public float CollisionAvoidDistance;
            public float SphereSeparationMargin;
            public float SphereSeparationWeight;
            public float MaxBankAngleDegrees;
            public float BankTurnScale;
            public float BankResponse;

            public void Execute(int index)
            {
                FishState current = Fish[index];
                float perceptionRadiusSq = PerceptionRadius * PerceptionRadius;
                float separationRadiusSq = SeparationRadius * SeparationRadius;
                float3 currentFlowAxis = ReadFlowAxis(ElapsedTime);
                float flowPhase = ElapsedTime * ToroidalAxisSpeed * 3.7f;

                float3 acceleration = float3.zero;
                float3 headingSum = float3.zero;
                float3 centerSum = float3.zero;
                float3 avoidanceSum = float3.zero;
                int neighborCount = 0;

                for (int i = 0; i < Fish.Length; i++)
                {
                    if (i == index)
                    {
                        continue;
                    }

                    FishState other = Fish[i];
                    float3 offset = other.Position - current.Position;
                    float distanceSq = math.lengthsq(offset);

                    if (distanceSq < perceptionRadiusSq)
                    {
                        neighborCount++;
                        headingSum += SafeNormalize(other.Velocity);
                        centerSum += other.Position;

                        if (distanceSq < separationRadiusSq)
                        {
                            float distance = math.sqrt(math.max(distanceSq, 0.0001f));
                            avoidanceSum += offset * (-1f / distance);
                        }
                    }
                }

                if (neighborCount > 0)
                {
                    centerSum /= neighborCount;
                    acceleration += SteerTowards(headingSum, current.Velocity, current.MaxSpeed, current.MaxSteerForce) * AlignWeight;
                    acceleration += SteerTowards(centerSum - current.Position, current.Velocity, current.MaxSpeed, current.MaxSteerForce) * CohesionWeight;
                    acceleration += SteerTowards(avoidanceSum, current.Velocity, current.MaxSpeed, current.MaxSteerForce) * current.SeparateWeight;
                }

                acceleration += SphericalEnvelopeForce(current.Position, current.Velocity, current.MaxSpeed, current.MaxSteerForce) * CenteringWeight;
                acceleration += ToroidalFlowForce(current.Position, current.Velocity, currentFlowAxis, flowPhase, current.MaxSpeed, current.MaxSteerForce) * ToroidalFlowWeight;
                acceleration += SphereObstacleSeparationForce(current.Position, current.Velocity, current.MaxSpeed, current.MaxSteerForce);

                float3 forward = SafeNormalize(current.Velocity);
                if (IsHeadingForCollision(current.Position, forward))
                {
                    float3 clearDirection = ObstacleRays(current.Position, forward, ref current);
                    acceleration += SteerTowards(clearDirection, current.Velocity, current.MaxSpeed, current.MaxSteerForce) * AvoidCollisionWeight;
                }
                else
                {
                    current.HasCollisionAvoidanceDirection = 0;
                }

                float3 desiredVelocity = current.Velocity + acceleration * Dt;
                float speed = math.clamp(math.length(desiredVelocity), current.MinSpeed, current.MaxSpeed);
                desiredVelocity = SafeNormalize(desiredVelocity) * speed;
                float3 velocity = LimitTurn(current.Velocity, desiredVelocity, Dt, current.MaxTurnRate);
                float3 nextPosition = current.Position + velocity * Dt;

                UpdateMotionState(ref current, velocity, Dt);
                current.Velocity = velocity;
                current.Position = nextPosition;

                NextFish[index] = current;
                NextVelocities[index] = velocity;
                NextPositions[index] = nextPosition;
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
